using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Standalone character monitor window with "always on top" functionality.
    /// </summary>
    public sealed partial class CharacterMonitorWindow : Window
    {
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;

        public CharacterMonitorWindow(PlayOnlineMonitorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Get services
            _settingsService = ServiceLocator.SettingsService;
            _loggingService = ServiceLocator.LoggingService;

            _loggingService.LogInfoAsync("CharacterMonitorWindow using centralized GlobalHotkeyManager", "CharacterMonitorWindow");

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

            // No need to register hotkeys here - they are handled by PlayOnlineMonitorViewModel via GlobalHotkeyManager
            
            // Set up auto-hide functionality
            SetupAutoHideFunctionality();
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
                // Single click to drag with edge snapping
                _isDragging = true;
                try
                {
                    DragMove();
                }
                finally
                {
                    _isDragging = false;
                }
            }
        }

        private void TitleBar_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Show system menu on right-click
            SystemCommands.ShowSystemMenu(this, GetMousePosition());
            e.Handled = true;
        }

        private Point GetMousePosition()
        {
            var position = Mouse.GetPosition(this);
            return PointToScreen(position);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Ensure the window can be resized properly with custom chrome
            // The resize grip should work automatically with ResizeMode="CanResizeWithGrip"
        }

        #region **GAMING PRESET SIZES**

        private void TinySize_Click(object sender, RoutedEventArgs e)
        {
            SetPresetSize(120, 200, "Tiny");
        }

        private void SmallSize_Click(object sender, RoutedEventArgs e)
        {
            SetPresetSize(160, 300, "Small");
        }

        private void MediumSize_Click(object sender, RoutedEventArgs e)
        {
            SetPresetSize(200, 400, "Medium");
        }

        private void LargeSize_Click(object sender, RoutedEventArgs e)
        {
            SetPresetSize(250, 500, "Large");
        }

        private void WideSize_Click(object sender, RoutedEventArgs e)
        {
            SetPresetSize(400, 150, "Wide");
        }

        private void SetPresetSize(double width, double height, string sizeName)
        {
            try
            {
                // Animate to new size for smooth gaming experience
                var widthAnimation = new System.Windows.Media.Animation.DoubleAnimation(Width, width, TimeSpan.FromMilliseconds(200));
                var heightAnimation = new System.Windows.Media.Animation.DoubleAnimation(Height, height, TimeSpan.FromMilliseconds(200));
                
                BeginAnimation(WidthProperty, widthAnimation);
                BeginAnimation(HeightProperty, heightAnimation);
                
                _loggingService?.LogInfoAsync($"Character monitor resized to {sizeName} ({width}x{height})", "CharacterMonitorWindow");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync($"Error resizing to {sizeName}", ex, "CharacterMonitorWindow");
            }
        }

        #endregion

        #region **SCREEN EDGE SNAPPING**

        private const int SnapDistance = 20; // Pixels from edge to trigger snap
        private bool _isDragging;

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            
            if (_isDragging)
            {
                SnapToScreenEdges();
            }
        }

        private void SnapToScreenEdges()
        {
            try
            {
                var screenBounds = SystemParameters.WorkArea;

                var left = Left;
                var top = Top;
                var right = Left + Width;
                var bottom = Top + Height;

                // Snap to left edge
                if (Math.Abs(left - screenBounds.Left) < SnapDistance)
                {
                    Left = screenBounds.Left;
                }
                // Snap to right edge
                else if (Math.Abs(right - screenBounds.Right) < SnapDistance)
                {
                    Left = screenBounds.Right - Width;
                }

                // Snap to top edge
                if (Math.Abs(top - screenBounds.Top) < SnapDistance)
                {
                    Top = screenBounds.Top;
                }
                // Snap to bottom edge
                else if (Math.Abs(bottom - screenBounds.Bottom) < SnapDistance)
                {
                    Top = screenBounds.Bottom - Height;
                }

                _loggingService?.LogDebugAsync($"Window snapped to screen position ({Left}, {Top})", "CharacterMonitorWindow");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync("Error during screen edge snapping", ex, "CharacterMonitorWindow");
            }
        }

        #endregion

        #region **AUTO-DOCK FUNCTIONALITY**

        private void DockTopLeft_Click(object sender, RoutedEventArgs e)
        {
            DockToPosition(DockPosition.TopLeft);
        }

        private void DockTopRight_Click(object sender, RoutedEventArgs e)
        {
            DockToPosition(DockPosition.TopRight);
        }

        private void DockBottomLeft_Click(object sender, RoutedEventArgs e)
        {
            DockToPosition(DockPosition.BottomLeft);
        }

        private void DockBottomRight_Click(object sender, RoutedEventArgs e)
        {
            DockToPosition(DockPosition.BottomRight);
        }

        private enum DockPosition
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private void DockToPosition(DockPosition position)
        {
            try
            {
                var screenBounds = SystemParameters.WorkArea;
                
                const int margin = 10; // Small margin from screen edges

                switch (position)
                {
                    case DockPosition.TopLeft:
                        Left = screenBounds.Left + margin;
                        Top = screenBounds.Top + margin;
                        break;
                        
                    case DockPosition.TopRight:
                        Left = screenBounds.Right - Width - margin;
                        Top = screenBounds.Top + margin;
                        break;
                        
                    case DockPosition.BottomLeft:
                        Left = screenBounds.Left + margin;
                        Top = screenBounds.Bottom - Height - margin;
                        break;
                        
                    case DockPosition.BottomRight:
                        Left = screenBounds.Right - Width - margin;
                        Top = screenBounds.Bottom - Height - margin;
                        break;
                }

                _loggingService?.LogInfoAsync($"Window docked to {position} at ({Left}, {Top})", "CharacterMonitorWindow");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync($"Error docking window to {position}", ex, "CharacterMonitorWindow");
            }
        }

        #endregion

        #region **CLICK-TO-SWITCH FUNCTIONALITY**

        private void CharacterCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only handle click-to-switch if the mode is enabled
            if (ClickToSwitchMenuItem?.IsChecked == true && sender is Border border)
            {
                // Get the character from the data context
                if (border.DataContext is PlayOnlineCharacter character)
                {
                    // Get the view model and execute the activate command
                    if (DataContext is PlayOnlineMonitorViewModel viewModel)
                    {
                        try
                        {
                            if (viewModel.ActivateCharacterCommand.CanExecute(character))
                            {
                                _loggingService?.LogInfoAsync($"Click-to-switch: Attempting to activate {character.DisplayName} (PID: {character.ProcessId}, Handle: 0x{character.WindowHandle.ToInt64():X})", "CharacterMonitorWindow");
                                viewModel.ActivateCharacterCommand.Execute(character);
                                _loggingService?.LogInfoAsync($"Character {character.DisplayName} activated via click-to-switch", "CharacterMonitorWindow");
                            }
                            else
                            {
                                _loggingService?.LogWarningAsync($"Click-to-switch: ActivateCharacterCommand.CanExecute returned false for {character.DisplayName} (PID: {character.ProcessId})", "CharacterMonitorWindow");
                            }
                        }
                        catch (Exception ex)
                        {
                            _loggingService?.LogErrorAsync($"Error in click-to-switch for character {character.DisplayName} (PID: {character.ProcessId}, Handle: 0x{character.WindowHandle.ToInt64():X})", ex, "CharacterMonitorWindow");
                        }
                    }
                }
                
                // Mark event as handled to prevent bubbling
                e.Handled = true;
            }
        }

        #endregion

        #region **AUTO-HIDE FUNCTIONALITY**

        private void SetupAutoHideFunctionality()
        {
            // Subscribe to character count changes if we have a view model
            if (DataContext is PlayOnlineMonitorViewModel viewModel)
            {
                // Listen for property changes to react to character count changes
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayOnlineMonitorViewModel.CharacterCount))
            {
                CheckAutoHide();
            }
        }

        private void CheckAutoHide()
        {
            try
            {
                // Only proceed if auto-hide is enabled
                if (AutoHideMenuItem?.IsChecked != true) return;

                if (DataContext is PlayOnlineMonitorViewModel viewModel)
                {
                    // If no characters are running, minimize the window
                    if (viewModel.CharacterCount == 0)
                    {
                        if (WindowState != WindowState.Minimized)
                        {
                            WindowState = WindowState.Minimized;
                            _loggingService?.LogInfoAsync("Character monitor auto-hidden (no characters running)", "CharacterMonitorWindow");
                        }
                    }
                    else
                    {
                        // If characters are detected and window is minimized, restore it
                        if (WindowState == WindowState.Minimized)
                        {
                            WindowState = WindowState.Normal;
                            _loggingService?.LogInfoAsync($"Character monitor auto-restored ({viewModel.CharacterCount} characters detected)", "CharacterMonitorWindow");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync("Error in auto-hide functionality", ex, "CharacterMonitorWindow");
            }
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Unsubscribe from view model events
                if (DataContext is PlayOnlineMonitorViewModel viewModel)
                {
                    viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                }
                
                // Save the opacity setting for next time
                try
                {
                    var settings = _settingsService.LoadSettings();
                    settings.CharacterMonitorOpacity = MainBorder?.Opacity ?? 0.95;
                    _settingsService.SaveSettings(settings);
                }
                catch
                {
                    // Ignore any errors saving settings during window close
                }
            }
            catch (Exception ex)
            {
                // Log cleanup errors but don't throw
                _loggingService?.LogErrorAsync("Error during CharacterMonitorWindow cleanup", ex, "CharacterMonitorWindow");
            }

            base.OnClosed(e);
        }
    }

    /// <summary>
    /// **GAMING FEATURE**: MultiValueConverter to show character index as hotkey number (F1, F2, etc.)
    /// </summary>
    public class CharacterIndexMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && 
                values[0] is PlayOnlineCharacter character && 
                values[1] is System.Collections.IList characters)
            {
                var index = characters.IndexOf(character);
                if (index >= 0 && index < 9) // F1-F9
                {
                    return $"{index + 1}"; // Just the number for compact display
                }
            }
            return "?";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// **RESPONSIVE LAYOUT**: Converter for character count comparisons
    /// </summary>
    public class GreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && parameter is string threshold && int.TryParse(threshold, out int thresholdValue))
            {
                return count > thresholdValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for less than comparisons (for responsive menu)
    /// </summary>
    public class LessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double actualValue && parameter is string threshold && double.TryParse(threshold, out double thresholdValue))
            {
                return actualValue < thresholdValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// **CLICK-TO-SWITCH**: Inverted boolean to visibility converter
    /// </summary>
    public class InvertedBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
