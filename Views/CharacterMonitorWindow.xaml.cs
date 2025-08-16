using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Infrastructure;
using FFXIManager.Models.Settings;
using FFXIManager.Services;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Standalone character monitor window with "always on top" functionality.
    /// Implements proper disposal of resources including hotkey services.
    /// </summary>
    public sealed partial class CharacterMonitorWindow : Window, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly LowLevelHotkeyService _globalHotkeyService;
        private readonly ILoggingService _loggingService;
        private volatile bool _disposed;
        
        // Hotkey debouncing to prevent rapid-fire issues
        private readonly Dictionary<int, DateTime> _lastHotkeyPress = new();
        private readonly TimeSpan _hotkeyDebounceInterval = TimeSpan.FromMilliseconds(500); // 500ms debounce
        
        public CharacterMonitorWindow(PlayOnlineMonitorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Get services
            _settingsService = ServiceLocator.SettingsService;
            _loggingService = ServiceLocator.LoggingService;
            
            // Initialize low-level keyboard hook service (bypasses Windower/FFXI interception)
            _globalHotkeyService = new LowLevelHotkeyService();
            _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
            
            _loggingService.LogInfoAsync("🔥 Using LOW-LEVEL keyboard hooks to bypass Windower/FFXI interception", "CharacterMonitorWindow");
            
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
            
            // Register hotkeys after window is loaded
            Loaded += OnWindowLoaded;
            
            // Subscribe to settings changes to refresh hotkeys
            DiscoverySettingsViewModel.HotkeySettingsChanged += OnHotkeySettingsChanged;
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
            Dispose();
        }
        
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Register keyboard shortcuts after window is fully loaded
            RegisterKeyboardShortcuts();
        }
        
        private void RegisterKeyboardShortcuts()
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                
                // If no shortcuts configured, create defaults
                if (settings.CharacterSwitchShortcuts.Count == 0)
                {
                    settings.CharacterSwitchShortcuts = ApplicationSettings.GetDefaultShortcuts();
                    _settingsService.SaveSettings(settings);
                    _loggingService.LogInfoAsync("Created default keyboard shortcuts (Win+F1-F9)", "CharacterMonitorWindow");
                }
                
                var registeredCount = 0;
                var failedCount = 0;
                var registeredShortcuts = new List<string>();
                
                // Register each shortcut
                foreach (var shortcut in settings.CharacterSwitchShortcuts.Where(s => s.IsEnabled))
                {
                    bool success = _globalHotkeyService.RegisterHotkey(shortcut.HotkeyId, shortcut.Modifiers, shortcut.Key);
                    
                    if (success)
                    {
                        registeredCount++;
                        registeredShortcuts.Add($"{shortcut.DisplayText} → Slot {shortcut.SlotIndex + 1}");
                        _loggingService.LogInfoAsync($"✓ Registered global hotkey: {shortcut.DisplayText} for slot {shortcut.SlotIndex + 1}", "CharacterMonitorWindow");
                    }
                    else
                    {
                        failedCount++;
                        _loggingService.LogWarningAsync($"✗ Failed to register global hotkey: {shortcut.DisplayText} (may be in use by another application)", "CharacterMonitorWindow");
                    }
                }
                
                _loggingService.LogInfoAsync($"Hotkey registration complete: {registeredCount} registered, {failedCount} failed", "CharacterMonitorWindow");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error registering keyboard shortcuts", ex, "CharacterMonitorWindow");
            }
        }
        
        /// <summary>
        /// Refreshes keyboard shortcuts by unregistering all current shortcuts and re-registering based on current settings.
        /// Call this method after hotkey settings have been changed.
        /// </summary>
        public void RefreshKeyboardShortcuts()
        {
            if (_disposed) return;
            
            try
            {
                _loggingService.LogInfoAsync("🔄 Refreshing keyboard shortcuts due to settings change", "CharacterMonitorWindow");
                
                // Unregister all current hotkeys
                _globalHotkeyService.UnregisterAll();
                
                // Re-register based on current settings
                RegisterKeyboardShortcuts();
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error refreshing keyboard shortcuts", ex, "CharacterMonitorWindow");
            }
        }
        
        private void OnHotkeySettingsChanged(object? sender, EventArgs e)
        {
            // This event can be called from any thread, so we need to marshal to the UI thread
            Dispatcher.BeginInvoke(RefreshKeyboardShortcuts);
        }
        
        private void OnGlobalHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
            try
            {
                // Convert hotkey ID back to slot index
                int slotIndex = e.HotkeyId - 1000; // Subtract the offset we added
                
                // Debouncing: check if this hotkey was pressed recently
                var now = DateTime.UtcNow;
                if (_lastHotkeyPress.TryGetValue(e.HotkeyId, out var lastPress))
                {
                    var timeSinceLastPress = now - lastPress;
                    if (timeSinceLastPress < _hotkeyDebounceInterval)
                    {
                        // Ignore this press - too soon after the last one
                        _loggingService.LogInfoAsync($"⏰ Ignoring rapid hotkey press: {e.Modifiers}+{e.Key} (debounced - {timeSinceLastPress.TotalMilliseconds:F0}ms since last press)", "CharacterMonitorWindow");
                        return;
                    }
                }
                
                // Update the last press time for this hotkey
                _lastHotkeyPress[e.HotkeyId] = now;
                
                _loggingService.LogInfoAsync($"🎮 Global hotkey pressed: {e.Modifiers}+{e.Key} (slot {slotIndex + 1})", "CharacterMonitorWindow");
                
                // Execute the switch command on the UI thread (use BeginInvoke for better responsiveness)
                Dispatcher.BeginInvoke(() =>
                {
                    var viewModel = DataContext as PlayOnlineMonitorViewModel;
                    
                    if (viewModel != null)
                    {
                        if (viewModel.SwitchToSlotCommand.CanExecute(slotIndex))
                        {
                            viewModel.SwitchToSlotCommand.Execute(slotIndex);
                            
                            // Get character name for feedback
                            var character = viewModel.Characters.ElementAtOrDefault(slotIndex);
                            var characterName = character?.DisplayName ?? $"Slot {slotIndex + 1}";
                            
                            _loggingService.LogInfoAsync($"✓ Switched to character: {characterName}", "CharacterMonitorWindow");
                        }
                        else
                        {
                            _loggingService.LogWarningAsync($"⚠ Cannot switch to slot {slotIndex + 1} - slot empty or character not responding", "CharacterMonitorWindow");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Error handling hotkey press", ex, "CharacterMonitorWindow");
            }
        }
        
        
        /// <summary>
        /// Releases all resources used by the CharacterMonitorWindow.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            
            try
            {
                // Unsubscribe from settings change events
                DiscoverySettingsViewModel.HotkeySettingsChanged -= OnHotkeySettingsChanged;
                
                // Cleanup global hotkeys
                _globalHotkeyService?.Dispose();
                
                // Save the opacity setting for next time
                try
                {
                    var settings = _settingsService.LoadSettings();
                    settings.CharacterMonitorOpacity = MainBorder?.Opacity ?? 0.95;
                    _settingsService.SaveSettings(settings);
                }
                catch
                {
                    // Ignore any errors saving settings during disposal
                }
            }
            catch (Exception ex)
            {
                // Log disposal errors but don't throw
                _loggingService?.LogErrorAsync("Error during CharacterMonitorWindow disposal", ex, "CharacterMonitorWindow");
            }
            
            GC.SuppressFinalize(this);
        }
    }
}
