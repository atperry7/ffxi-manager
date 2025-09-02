using System;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Enhanced notification service interface with non-blocking notifications
    /// </summary>
    public interface INotificationServiceEnhanced : INotificationService
    {
        /// <summary>
        /// Shows a non-blocking toast notification that auto-dismisses
        /// </summary>
        Task ShowToastAsync(string message, NotificationType type = NotificationType.Info, int durationMs = 8000);
        
        /// <summary>
        /// Shows a detailed window activation failure notification
        /// </summary>
        Task ShowActivationFailureAsync(WindowActivationResult result, string characterName);
        
        /// <summary>
        /// Updates status bar with activation result
        /// </summary>
        void UpdateStatusBar(string message, NotificationType type = NotificationType.Info);
        
        /// <summary>
        /// Batches multiple notifications to prevent spam
        /// </summary>
        void QueueNotification(string message, NotificationType type);
        
        /// <summary>
        /// Flushes queued notifications as a single summary
        /// </summary>
        Task FlushNotificationQueueAsync();
    }
    
    /// <summary>
    /// Non-blocking notification options
    /// </summary>
    public class NotificationOptions
    {
        public bool ShowToast { get; set; } = true;
        public bool UpdateStatusBar { get; set; } = true;
        public bool LogOnly { get; set; }
        public int ToastDurationMs { get; set; } = 3000;
        public bool PlaySound { get; set; }
    }
}