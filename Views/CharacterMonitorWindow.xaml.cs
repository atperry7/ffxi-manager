using System;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Services;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Standalone character monitor window with "always on top" functionality
    /// </summary>
    public partial class CharacterMonitorWindow : Window
    {
        private readonly ISettingsService _settingsService;
        
        public CharacterMonitorWindow(PlayOnlineMonitorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Get settings service
            _settingsService = ServiceLocator.SettingsService;
            
            // Set initial window properties
            ShowInTaskbar = true;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            // Load opacity from settings
            if (MainBorder != null)
            {
                var settings = _settingsService.LoadSettings();
                MainBorder.Opacity = settings.CharacterMonitorOpacity;
                
                // Also set the slider value
                if (OpacitySlider != null)
                {
                    OpacitySlider.Value = settings.CharacterMonitorOpacity;
                }
            }
        }

        private void AlwaysOnTopToggle_Checked(object sender, RoutedEventArgs e)
        {
            Topmost = true;
        }

        private void AlwaysOnTopToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            Topmost = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window by the custom title bar
            if (e.ClickCount == 2)
            {
                // Double-click to toggle between normal and maximized
                if (WindowState == WindowState.Normal)
                    WindowState = WindowState.Maximized;
                else
                    WindowState = WindowState.Normal;
            }
            else
            {
                // Single click to drag
                DragMove();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Ensure the window can be resized properly with custom chrome
            // The resize grip should work automatically with ResizeMode="CanResizeWithGrip"
        }
        
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Save the opacity setting for next time
            try
            {
                var settings = _settingsService.LoadSettings();
                settings.CharacterMonitorOpacity = MainBorder?.Opacity ?? 0.95;
                _settingsService.SaveSettings(settings);
            }
            catch
            {
                // Ignore any errors saving settings
            }
        }
    }
}