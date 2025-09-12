using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Enhanced notification service without on-screen toasts. Routes feedback
    /// to the status bar with batching and logs important events.
    /// </summary>
    public class NotificationServiceEnhanced : INotificationServiceEnhanced
    {
        private readonly ILoggingService _loggingService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly IStatusMessageService _statusService;
        private readonly ConcurrentQueue<QueuedNotification> _notificationQueue = new();
        private readonly DispatcherTimer _batchTimer;

        public NotificationServiceEnhanced(ILoggingService loggingService, IUiDispatcher uiDispatcher, IStatusMessageService statusService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));

            // Batch timer to flush queued notifications
            _batchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _batchTimer.Tick += (s, e) => _ = FlushNotificationQueueAsync();
        }

        #region INotificationService Implementation

        public async Task ShowSuccessAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Success);
            await _loggingService.LogInfoAsync("Success notification: {Message}", "NotificationService", message);
        }

        public async Task ShowWarningAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Warning);
            await _loggingService.LogWarningAsync("Warning notification: {Message}", "NotificationService", message);
        }

        public async Task ShowErrorAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Error);
            await _loggingService.LogErrorAsync("Error notification: {Message}", null, "NotificationService", message);
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            await ShowToastAsync(message, NotificationType.Info);
            await _loggingService.LogInfoAsync("Info notification: {Message}", "NotificationService", message);
        }

        public async Task<bool> ShowConfirmationAsync(string message, string? title = null)
        {
            // Confirmations still use MessageBox since they need user interaction
            await _loggingService.LogInfoAsync("Confirmation requested: {Message}", "NotificationService", message);
            return await _uiDispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(message, title ?? "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                var confirmed = result == MessageBoxResult.Yes;
                _ = _loggingService.LogInfoAsync("Confirmation result: {Confirmed}", "NotificationService", confirmed);
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
            // No on-screen toasts: update the status bar and log instead
            try
            {
                _statusService.Enqueue(message, type);
                var duration = type == NotificationType.Error ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(3);
                _statusService.SetTemporaryMessage(message, duration);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error routing notification to status bar: {ErrorMessage}", ex, "NotificationServiceEnhanced", ex.Message);
            }
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
            var duration = type == NotificationType.Error ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);
            _statusService.Enqueue(message, type);
            _statusService.SetTemporaryMessage(message, duration);
        }

        public void QueueNotification(string message, NotificationType type)
        {
            _notificationQueue.Enqueue(new QueuedNotification(message, type, DateTime.UtcNow));

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

            var grouped = notifications.GroupBy(n => n.Type).ToList();

            if (grouped.Count == 1 && notifications.Count == 1)
            {
                var single = notifications[0];
                await ShowToastAsync(single.Message, single.Type);
            }
            else
            {
                var summary = string.Join(" | ", grouped.Select(g =>
                    g.Count() == 1 ? g.First().Message : $"{g.Count()} {g.Key.ToString().ToLower()} notifications"));

                var worstType = grouped.Select(g => g.Key)
                    .OrderByDescending(t => (int)t)
                    .First();

                await ShowToastAsync(summary, worstType, 8000);
            }
        }

        #endregion

        private sealed record QueuedNotification(string Message, NotificationType Type, DateTime Timestamp);
    }
}

