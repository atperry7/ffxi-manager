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
        private readonly List<ToastNotification> _activeToasts = new();
        private const int MAX_CONCURRENT_TOASTS = 5;
        private const int TOAST_VERTICAL_SPACING = 5;

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

        public async Task ShowToastAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 8000)
        {
            await ServiceLocator.UiDispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Find main window to host toast
                    var mainWindow = Application.Current.MainWindow;
                    if (mainWindow == null || mainWindow.Content is not System.Windows.Controls.Grid mainGrid) 
                        return;

                    // Remove old toasts if we've hit the limit
                    if (_activeToasts.Count >= MAX_CONCURRENT_TOASTS)
                    {
                        RemoveOldestToast();
                    }

                    // Create new toast and calculate position BEFORE adding to list
                    var toast = new ToastNotification();
                    var stackPosition = _activeToasts.Count; // Position in stack (0, 1, 2, etc.)
                    ConfigureToastPosition(toast, mainGrid, stackPosition);
                    
                    // Add to active toasts list AFTER positioning
                    _activeToasts.Add(toast);
                    
                    // Setup removal when toast completes
                    var removalTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(durationMs + 300) // Animation buffer
                    };
                    removalTimer.Tick += (s, e) =>
                    {
                        removalTimer.Stop();
                        RemoveToast(toast);
                    };
                    removalTimer.Start();

                    // Show the toast
                    toast.ShowToast(message, type, durationMs: durationMs);
                    
                    _ = _loggingService.LogDebugAsync($"Toast shown: ({type}) {message} - Active: {_activeToasts.Count} - Position: {stackPosition}", "NotificationServiceEnhanced");
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

            await ShowToastAsync(message, NotificationType.Error, 8000);
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
                    
                await ShowToastAsync(summary, worstType, 8000);
            }
        }

        #endregion

        #region Toast Management

        /// <summary>
        /// Configures the position of a new toast in the stack
        /// </summary>
        private void ConfigureToastPosition(ToastNotification toast, System.Windows.Controls.Grid mainGrid, int stackPosition)
        {
            // Calculate vertical position based on stack position
            var topOffset = 5.0; // Very close to title bar for first toast
            var toastHeight = 80.0; // Actual toast height (from XAML design height)
            
            // Each toast gets positioned with true 5px visual gaps
            // First toast at 5px, second at 5+80+5=90px, third at 5+80+5+80+5=175px, etc.
            topOffset += stackPosition * (toastHeight + TOAST_VERTICAL_SPACING);

            // Use Canvas positioning for more precise control
            var canvas = new System.Windows.Controls.Canvas();
            System.Windows.Controls.Canvas.SetTop(toast, topOffset);
            System.Windows.Controls.Canvas.SetRight(toast, 20);
            
            toast.HorizontalAlignment = HorizontalAlignment.Center;
            toast.VerticalAlignment = VerticalAlignment.Center;
            toast.Width = 350; // Fixed width to prevent sizing issues
            
            System.Windows.Controls.Panel.SetZIndex(toast, 1000 + stackPosition);
            
            // Add canvas to main grid, then toast to canvas
            System.Windows.Controls.Grid.SetColumnSpan(canvas, int.MaxValue);
            System.Windows.Controls.Grid.SetRowSpan(canvas, int.MaxValue);
            canvas.Children.Add(toast);
            mainGrid.Children.Add(canvas);
            
            _ = _loggingService.LogDebugAsync($"Toast positioned at: Top={topOffset}, Right=20, StackPos={stackPosition}", "NotificationServiceEnhanced");
        }

        /// <summary>
        /// Removes the oldest toast to make room for new ones
        /// </summary>
        private void RemoveOldestToast()
        {
            if (_activeToasts.Count > 0)
            {
                var oldestToast = _activeToasts[0];
                RemoveToast(oldestToast);
            }
        }

        /// <summary>
        /// Removes a specific toast and repositions remaining toasts
        /// </summary>
        private void RemoveToast(ToastNotification toast)
        {
            if (!_activeToasts.Contains(toast)) return;

            try
            {
                // Remove from UI - handle Canvas wrapper
                if (toast.Parent is System.Windows.Controls.Canvas canvas)
                {
                    canvas.Children.Remove(toast);
                    // Remove the canvas from its parent
                    if (canvas.Parent is System.Windows.Controls.Panel canvasParent)
                    {
                        canvasParent.Children.Remove(canvas);
                    }
                }
                else if (toast.Parent is System.Windows.Controls.Panel directParent)
                {
                    directParent.Children.Remove(toast);
                }

                // Remove from active list
                _activeToasts.Remove(toast);

                // Reposition remaining toasts
                RepositionToasts();
                
                _ = _loggingService.LogDebugAsync($"Toast removed - Remaining: {_activeToasts.Count}", "NotificationServiceEnhanced");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync($"Error removing toast: {ex.Message}", ex, "NotificationServiceEnhanced");
            }
        }

        /// <summary>
        /// Repositions all active toasts after one is removed
        /// </summary>
        private void RepositionToasts()
        {
            var topOffset = 5.0; // Very close to title bar
            var toastHeight = 80.0; // Actual toast height (from XAML design height)

            for (int i = 0; i < _activeToasts.Count; i++)
            {
                var toast = _activeToasts[i];
                
                // Calculate position for this toast with true 5px visual gaps  
                var currentOffset = topOffset + (i * (toastHeight + TOAST_VERTICAL_SPACING));
                
                // Update Canvas position if using Canvas wrapper
                if (toast.Parent is System.Windows.Controls.Canvas canvas)
                {
                    System.Windows.Controls.Canvas.SetTop(toast, currentOffset);
                    System.Windows.Controls.Panel.SetZIndex(toast, 1000 + i);
                }
                else
                {
                    // Fallback to margin positioning
                    toast.Margin = new Thickness(0, currentOffset, 20, 0);
                    System.Windows.Controls.Panel.SetZIndex(toast, 1000 + i);
                }
            }
        }

        #endregion

        private sealed record QueuedNotification(string Message, NotificationType Type, DateTime Timestamp);
    }
}