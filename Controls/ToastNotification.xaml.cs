using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FFXIManager.Services;

namespace FFXIManager.Controls
{
    /// <summary>
    /// Non-blocking toast notification control
    /// </summary>
    public partial class ToastNotification : UserControl
    {
        private DispatcherTimer? _autoHideTimer;
        
        public ToastNotification()
        {
            InitializeComponent();
        }
        
        public void ShowToast(string message, NotificationType type = NotificationType.Info, string? title = null, int durationMs = 3000)
        {
            // Set content
            MessageText.Text = message;
            
            if (!string.IsNullOrEmpty(title))
            {
                TitleText.Text = title;
                TitleText.Visibility = Visibility.Visible;
            }
            
            // Set icon and colors based on type
            switch (type)
            {
                case NotificationType.Success:
                    IconText.Text = "✅";
                    ToastBorder.Background = FindResource("SuccessBrush") as System.Windows.Media.Brush ?? 
                                           new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                    break;
                case NotificationType.Warning:
                    IconText.Text = "⚠️";
                    ToastBorder.Background = FindResource("WarningBrush") as System.Windows.Media.Brush ?? 
                                           new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                    break;
                case NotificationType.Error:
                    IconText.Text = "❌";
                    ToastBorder.Background = FindResource("DangerBrush") as System.Windows.Media.Brush ?? 
                                           new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 113, 113));
                    break;
                default: // Info
                    IconText.Text = "ℹ️";
                    ToastBorder.Background = FindResource("InfoBrush") as System.Windows.Media.Brush ?? 
                                           new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(167, 139, 250));
                    break;
            }
            
            // Show animation
            Visibility = Visibility.Visible;
            var showStoryboard = FindResource("ShowToast") as Storyboard;
            showStoryboard?.Begin(this);
            
            // Setup auto-hide
            _autoHideTimer?.Stop();
            _autoHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _autoHideTimer.Tick += (s, e) => HideToast();
            _autoHideTimer.Start();
        }
        
        private void HideToast()
        {
            _autoHideTimer?.Stop();
            
            var hideStoryboard = FindResource("HideToast") as Storyboard;
            if (hideStoryboard != null)
            {
                hideStoryboard.Completed += (s, e) => Visibility = Visibility.Collapsed;
                hideStoryboard.Begin(this);
            }
            else
            {
                Visibility = Visibility.Collapsed;
            }
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideToast();
        }
    }
}