using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FFXIManager.Controls;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Enhanced notification service with non-blocking toasts and smart batching
    /// </summary>
    public class NotificationServiceEnhanced : INotificationServiceEnhanced
    {
        private readonly ILoggingService _loggingService;
        private readonly ConcurrentQueue<QueuedNotification> _notificationQueue = new();
        private readonly DispatcherTimer _batchTimer;
        private ToastNotification? _currentToast;

        public NotificationServiceEnhanced(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            // Setup batch timer to flush queued notifications
            _batchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _batchTimer.Tick += (s, e) => _ = FlushNotificationQueueAsync();
        }

        #region INotificationService Implementation (Delegate to Toast)

        public async Task ShowSuccessAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Success);
            await _loggingService.LogInfoAsync($"Success notification: {message}", "NotificationService");
        }

        public async Task ShowWarningAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Warning);
            await _loggingService.LogWarningAsync($"Warning notification: {message}", "NotificationService");
        }

        public async Task ShowErrorAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Error);
            await _loggingService.LogErrorAsync($"Error notification: {message}", null, "NotificationService");
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Info);
            await _loggingService.LogInfoAsync($"Info notification: {message}", "NotificationService");
        }

        public async Task<bool> ShowConfirmationAsync(string message, string? title = null)
        {
            // Confirmations still use MessageBox since they need user interaction
            await _loggingService.LogInfoAsync($"Confirmation requested: {message}", "NotificationService");
            return await ServiceLocator.UiDispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(message, title ?? "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                var confirmed = result == MessageBoxResult.Yes;
                _ = _loggingService.LogInfoAsync($"Confirmation result: {confirmed}", "NotificationService");
                return confirmed;
            });
        }

        public void ShowToast(string message, NotificationType type = NotificationType.Info)
        {
            _ = ShowToastAsync(message, type);
        }

        #endregion

        #region INotificationServiceEnhanced Implementation

        public async Task ShowToastAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 3000)
        {
            await ServiceLocator.UiDispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Find main window to host toast
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow == null) return;

                    // Create or reuse toast control
                    if (_currentToast == null)
                    {
                        _currentToast = new ToastNotification();
                        
                        // Add to main window's grid (assume main window has a Grid)
                        if (mainWindow.Content is System.Windows.Controls.Grid mainGrid)
                        {
                            _currentToast.HorizontalAlignment = HorizontalAlignment.Right;
                            _currentToast.VerticalAlignment = VerticalAlignment.Top;
                            _currentToast.Margin = new Thickness(0, 60, 20, 0); // Below title bar
                            System.Windows.Controls.Grid.SetColumnSpan(_currentToast, int.MaxValue);
                            System.Windows.Controls.Grid.SetRowSpan(_currentToast, int.MaxValue);
                            System.Windows.Controls.Panel.SetZIndex(_currentToast, 1000);
                            
                            mainGrid.Children.Add(_currentToast);
                        }
                    }

                    // Show the toast
                    _currentToast.ShowToast(message, type, durationMs: durationMs);
                    
                    _ = _loggingService.LogDebugAsync($"Toast shown: ({type}) {message}", "NotificationServiceEnhanced");
                }
                catch (Exception ex)
                {
                    _ = _loggingService.LogErrorAsync($"Error showing toast: {ex.Message}", ex, "NotificationServiceEnhanced");
                }
            });
        }

        public async Task ShowActivationFailureAsync(WindowActivationResult result, string characterName)
        {
            var message = result.FailureReason switch
            {
                WindowActivationFailureReason.WindowHung => $"{characterName}: Game not responding",
                WindowActivationFailureReason.AccessDenied => $"{characterName}: Access denied - try running as admin",
                WindowActivationFailureReason.ElevationMismatch => $"{characterName}: UAC mismatch - run as administrator",
                WindowActivationFailureReason.FullScreenBlocking => $"{characterName}: Blocked by fullscreen app",
                WindowActivationFailureReason.InvalidHandle => $"{characterName}: Window was closed",
                WindowActivationFailureReason.FocusStealingPrevention => $"{characterName}: Windows blocked focus change",
                WindowActivationFailureReason.Timeout => $"{characterName}: Activation timed out",
                _ => $"{characterName}: Activation failed"
            };

            await ShowToastAsync(message, NotificationType.Error, 5000);
        }

        public void UpdateStatusBar(string message, NotificationType type = NotificationType.Info)
        {
            // Delegate to existing StatusMessageService for status bar updates
            var duration = type == NotificationType.Error ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);
            ServiceLocator.StatusMessageService.SetTemporaryMessage(message, duration);
        }

        public void QueueNotification(string message, NotificationType type)
        {
            _notificationQueue.Enqueue(new QueuedNotification(message, type, DateTime.UtcNow));
            
            // Start batch timer if not already running
            if (!_batchTimer.IsEnabled)
            {
                _batchTimer.Start();
            }
        }

        public async Task FlushNotificationQueueAsync()
        {
            _batchTimer.Stop();
            
            if (_notificationQueue.IsEmpty) return;

            var notifications = new List<QueuedNotification>();
            while (_notificationQueue.TryDequeue(out var notification))
            {
                notifications.Add(notification);
            }

            if (notifications.Count == 0) return;

            // Group by type for summary
            var grouped = notifications.GroupBy(n => n.Type).ToList();
            
            if (grouped.Count == 1 && notifications.Count == 1)
            {
                // Single notification - show as-is
                var single = notifications[0];
                await ShowToastAsync(single.Message, single.Type);
            }
            else
            {
                // Multiple notifications - create summary
                var summary = string.Join(" | ", grouped.Select(g => 
                    g.Count() == 1 ? g.First().Message : $"{g.Count()} {g.Key.ToString().ToLower()} notifications"));
                    
                var worstType = grouped.Select(g => g.Key)
                    .OrderByDescending(t => (int)t)
                    .First();
                    
                await ShowToastAsync(summary, worstType, 4000);
            }
        }

        #endregion

        private sealed record QueuedNotification(string Message, NotificationType Type, DateTime Timestamp);
    }
}