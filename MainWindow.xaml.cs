using System;
using System.ComponentModel;
using System.Windows;
using FFXIManager.Infrastructure;
using FFXIManager.ViewModels;

namespace FFXIManager
{
    /// <summary>
    /// MainWindow with window state persistence
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isClosing = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            
            // Load and apply saved window state
            LoadWindowState();
            
            // Save window state when closing
            Closing += MainWindow_Closing;
            SizeChanged += MainWindow_SizeChanged;
            LocationChanged += MainWindow_LocationChanged;
            StateChanged += MainWindow_StateChanged;
        }

        private void LoadWindowState()
        {
            try
            {
                var settings = ServiceLocator.SettingsService.LoadSettings();
                
                if (settings.RememberWindowPosition)
                {
                    // Apply saved size
                    Width = Math.Max(settings.MainWindowWidth, MinWidth);
                    Height = Math.Max(settings.MainWindowHeight, MinHeight);
                    
                    // Apply saved position if valid
                    if (!double.IsNaN(settings.MainWindowLeft) && !double.IsNaN(settings.MainWindowTop))
                    {
                        // Ensure window is on screen
                        var left = Math.Max(0, Math.Min(settings.MainWindowLeft, SystemParameters.VirtualScreenWidth - Width));
                        var top = Math.Max(0, Math.Min(settings.MainWindowTop, SystemParameters.VirtualScreenHeight - Height));
                        
                        Left = left;
                        Top = top;
                        WindowStartupLocation = WindowStartupLocation.Manual;
                    }
                    
                    // Apply saved window state
                    if (settings.MainWindowMaximized)
                    {
                        WindowState = WindowState.Maximized;
                    }
                }
            }
            catch (Exception ex)
            {
                // If loading window state fails, use defaults
                System.Diagnostics.Debug.WriteLine($"Error loading window state: {ex.Message}");
            }
        }

        private void SaveWindowState()
        {
            if (_isClosing) return;

            try
            {
                var settings = ServiceLocator.SettingsService.LoadSettings();
                
                if (settings.RememberWindowPosition)
                {
                    // Save current state
                    settings.MainWindowMaximized = WindowState == WindowState.Maximized;
                    
                    // Save size and position (use RestoreBounds when maximized)
                    if (WindowState == WindowState.Maximized)
                    {
                        settings.MainWindowWidth = RestoreBounds.Width;
                        settings.MainWindowHeight = RestoreBounds.Height;
                        settings.MainWindowLeft = RestoreBounds.Left;
                        settings.MainWindowTop = RestoreBounds.Top;
                    }
                    else
                    {
                        settings.MainWindowWidth = Width;
                        settings.MainWindowHeight = Height;
                        settings.MainWindowLeft = Left;
                        settings.MainWindowTop = Top;
                    }
                    
                    ServiceLocator.SettingsService.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - window state saving is not critical
                System.Diagnostics.Debug.WriteLine($"Error saving window state: {ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;
            SaveWindowState();
        }

        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            SaveWindowState();
        }

        private void MainWindow_LocationChanged(object? sender, EventArgs e)
        {
            SaveWindowState();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            SaveWindowState();
        }
    }
}