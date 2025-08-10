using System;
using System.Threading.Tasks;
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

        public NotificationService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task ShowSuccessAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Success notification: {message}", "NotificationService");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public async Task ShowWarningAsync(string message, string? title = null)
        {
            await _loggingService.LogWarningAsync($"Warning notification: {message}", "NotificationService");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        public async Task ShowErrorAsync(string message, string? title = null)
        {
            await _loggingService.LogErrorAsync($"Error notification: {message}", null, "NotificationService");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        public async Task ShowInfoAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Info notification: {message}", "NotificationService");
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(message, title ?? "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public async Task<bool> ShowConfirmationAsync(string message, string? title = null)
        {
            await _loggingService.LogInfoAsync($"Confirmation requested: {message}", "NotificationService");
            
            return await Application.Current.Dispatcher.InvokeAsync(() =>
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
            
            // Could implement Windows 10/11 toast notifications here using
            // Microsoft.Toolkit.Win32.UI.Controls or similar
        }
    }
}