using FFXIManager.Infrastructure;
using System.Windows;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for user notifications
    /// </summary>
    public interface INotificationService
    {
        Task ShowSuccessAsync(string message, string? title = null);
        Task ShowWarningAsync(string message, string? title = null);
        Task ShowErrorAsync(string message, string? title = null);
        Task ShowInfoAsync(string message, string? title = null);
        Task<bool> ShowConfirmationAsync(string message, string? title = null);
        void ShowToast(string message, NotificationType type = NotificationType.Info);
    }

    /// <summary>
    /// Types of notifications
    /// </summary>
    public enum NotificationType
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Service for showing user notifications and confirmations
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly ILoggingService _loggingService;
        private readonly IUiDispatcher _uiDispatcher;

        public NotificationService(ILoggingService loggingService, IUiDispatcher uiDispatcher)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public async Task ShowSuccessAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Success notification: {message}", "NotificationService");
            await _uiDispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public async Task ShowWarningAsync(string message, string? title = null)
        {
            await _loggingService.LogWarningAsync($"Warning notification: {message}", "NotificationService");
            await _uiDispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        public async Task ShowErrorAsync(string message, string? title = null)
        {
            await _loggingService.LogErrorAsync($"Error notification: {message}", null, "NotificationService");
            await _uiDispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Info notification: {message}", "NotificationService");
            await _uiDispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public async Task<bool> ShowConfirmationAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Confirmation requested: {message}", "NotificationService");
            return await _uiDispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(message, title ?? "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                var confirmed = result == MessageBoxResult.Yes;
                _loggingService.LogInfoAsync($"Confirmation result: {confirmed}", "NotificationService");
                return confirmed;
            });
        }

        public void ShowToast(string message, NotificationType type = NotificationType.Info)
        {
            // For now, just log the toast message
            // In a real application, this could show a Windows toast notification
            _loggingService.LogInfoAsync($"Toast notification ({type}): {message}", "NotificationService");
        }
    }
}
