using FFXIManager.Models.Settings;
using System.Windows.Input;

namespace FFXIManager.Services
{
    /// <summary>
    /// Centralized global hotkey management. Handles keyboard and controller inputs.
    /// </summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private readonly IGlobalHotkeyService _hotkeyService;
        private readonly ControllerInputService _controllerService;
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;

        private readonly Dictionary<int, DateTime> _lastHotkeyPress = new();
        private TimeSpan _hotkeyDebounceInterval = TimeSpan.FromMilliseconds(50);
        private bool _disposed;

        // **EMERGENCY PROTECTION**: Circuit breaker for hotkey flooding
        private static int _hotkeyPressCount;
        private static DateTime _lastHotkeyReset = DateTime.UtcNow;
        private static volatile bool _hotkeyFloodProtection;
        private const int MAX_HOTKEYS_PER_SECOND = 15;
        private const int FLOOD_PROTECTION_DURATION_MS = 2000;

        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        public GlobalHotkeyManager(
            IGlobalHotkeyService hotkeyService,
            ControllerInputService controllerService,
            ILoggingService loggingService,
            ISettingsService settingsService)
        {
            _hotkeyService = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));
            _controllerService = controllerService ?? throw new ArgumentNullException(nameof(controllerService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            _hotkeyService.HotkeyPressed += OnLowLevelHotkeyPressed;
            _controllerService.ButtonPressed += OnControllerButtonPressed;

            _ = _loggingService.LogInfoAsync("GlobalHotkeyManager initialized", "GlobalHotkeyManager");
        }

        public void RegisterHotkeysFromSettings()
        {
            if (_disposed) return;

            try
            {
                var settings = _settingsService.LoadSettings();

                var debounceMs = Math.Clamp(settings.HotkeyDebounceIntervalMs, 1, 1000);
                if (debounceMs != settings.HotkeyDebounceIntervalMs)
                {
                    _ = _loggingService.LogWarningAsync($"Clamped invalid hotkey debounce value {settings.HotkeyDebounceIntervalMs}ms to {debounceMs}ms", "GlobalHotkeyManager");
                }
                _hotkeyDebounceInterval = TimeSpan.FromMilliseconds(debounceMs);

                if (settings.CharacterSwitchShortcuts.Count == 0)
                {
                    settings.CharacterSwitchShortcuts = ApplicationSettings.GetDefaultShortcuts();
                    _settingsService.SaveSettings(settings);
                    _ = _loggingService.LogInfoAsync("Created default keyboard shortcuts (Win+F1-F11)", "GlobalHotkeyManager");
                }

                if (settings.CycleHotkey == null)
                {
                    settings.CycleHotkey = ApplicationSettings.GetDefaultCycleHotkey();
                    _settingsService.SaveSettings(settings);
                    _ = _loggingService.LogInfoAsync("Created default cycle hotkey (Win+F12)", "GlobalHotkeyManager");
                }

                var registeredCount = 0;
                var failedCount = 0;

                var seenSlots = new HashSet<int>();
                foreach (var shortcut in settings.CharacterSwitchShortcuts.Where(s => s.IsEnabled))
                {
                    if (!seenSlots.Add(shortcut.SlotIndex))
                    {
                        _ = _loggingService.LogWarningAsync($"Duplicate shortcut for slot {shortcut.SlotIndex + 1} ignored during registration", "GlobalHotkeyManager");
                        continue;
                    }

                    var keyboardRegistered = false;
                    var controllerRegistered = false;

                    if (shortcut.Key != Key.None)
                    {
                        keyboardRegistered = _hotkeyService.RegisterHotkey(shortcut.HotkeyId, shortcut.Modifiers, shortcut.Key);
                        if (keyboardRegistered)
                        {
                            registeredCount++;
                            _ = _loggingService.LogInfoAsync($"Registered keyboard hotkey: {shortcut.GetKeyboardDisplayText()} for slot {shortcut.SlotIndex + 1}", "GlobalHotkeyManager");
                        }
                        else
                        {
                            failedCount++;
                            _ = _loggingService.LogWarningAsync($"Failed to register keyboard hotkey: {shortcut.GetKeyboardDisplayText()} (may be in use)", "GlobalHotkeyManager");
                        }
                    }

                    if (shortcut.ControllerButton != Models.Settings.ControllerButton.None)
                    {
                        controllerRegistered = _controllerService.RegisterButton(shortcut.HotkeyId, shortcut.ControllerButton);
                        if (controllerRegistered)
                        {
                            registeredCount++;
                            _ = _loggingService.LogInfoAsync($"Registered controller button: {shortcut.GetControllerDisplayText()} for slot {shortcut.SlotIndex + 1}", "GlobalHotkeyManager");
                        }
                        else
                        {
                            failedCount++;
                            _ = _loggingService.LogWarningAsync($"Failed to register controller button: {shortcut.GetControllerDisplayText()}", "GlobalHotkeyManager");
                        }
                    }

                    if (!keyboardRegistered && !controllerRegistered)
                    {
                        _ = _loggingService.LogWarningAsync($"No inputs registered for slot {shortcut.SlotIndex + 1}: {shortcut.DisplayText}", "GlobalHotkeyManager");
                    }
                }

                if (settings.CycleHotkey != null && settings.CycleHotkey.IsEnabled)
                {
                    if (settings.CycleHotkey.Key != Key.None)
                    {
                        if (_hotkeyService.RegisterHotkey(HotkeyActivationService.CycleHotkeyId, settings.CycleHotkey.Modifiers, settings.CycleHotkey.Key))
                        {
                            registeredCount++;
                            _ = _loggingService.LogInfoAsync($"Registered cycle keyboard hotkey: {settings.CycleHotkey.GetKeyboardDisplayText()}", "GlobalHotkeyManager");
                        }
                        else
                        {
                            failedCount++;
                            _ = _loggingService.LogWarningAsync($"Failed to register cycle keyboard hotkey: {settings.CycleHotkey.GetKeyboardDisplayText()}", "GlobalHotkeyManager");
                        }
                    }

                    if (settings.CycleHotkey.ControllerButton != Models.Settings.ControllerButton.None)
                    {
                        if (_controllerService.RegisterButton(HotkeyActivationService.CycleHotkeyId, settings.CycleHotkey.ControllerButton))
                        {
                            registeredCount++;
                            _ = _loggingService.LogInfoAsync($"Registered cycle controller button: {settings.CycleHotkey.GetControllerDisplayText()}", "GlobalHotkeyManager");
                        }
                        else
                        {
                            failedCount++;
                            _ = _loggingService.LogWarningAsync($"Failed to register cycle controller button: {settings.CycleHotkey.GetControllerDisplayText()}", "GlobalHotkeyManager");
                        }
                    }
                }

                _ = _loggingService.LogInfoAsync($"Hotkey registration complete: {registeredCount} registered, {failedCount} failed", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error registering keyboard shortcuts", ex, "GlobalHotkeyManager");
            }
        }

        public void RefreshHotkeys()
        {
            if (_disposed) return;
            try
            {
                _ = _loggingService.LogInfoAsync("Refreshing keyboard shortcuts due to settings change", "GlobalHotkeyManager");
                _hotkeyService.UnregisterAll();
                _controllerService.UnregisterAll();
                RegisterHotkeysFromSettings();
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error refreshing keyboard shortcuts", ex, "GlobalHotkeyManager");
            }
        }

        public void UnregisterAllHotkeys()
        {
            if (_disposed) return;
            try
            {
                _hotkeyService.UnregisterAll();
                _controllerService.UnregisterAll();
                _ = _loggingService.LogInfoAsync("All hotkeys and controller buttons unregistered", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error unregistering hotkeys", ex, "GlobalHotkeyManager");
            }
        }

        private void OnLowLevelHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
            try
            {
                // **EMERGENCY PROTECTION**: Check for hotkey flood
                if (IsHotkeyFloodProtectionActive())
                {
                    return;
                }

                var now = DateTime.UtcNow;
                if (_lastHotkeyPress.TryGetValue(e.HotkeyId, out var lastPress))
                {
                    var timeSinceLastPress = now - lastPress;
                    if (timeSinceLastPress < _hotkeyDebounceInterval)
                    {
                        _ = _loggingService.LogInfoAsync($"Ignoring rapid hotkey press: {e.Modifiers}+{e.Key} ({timeSinceLastPress.TotalMilliseconds:F0}ms)", "GlobalHotkeyManager");
                        return;
                    }
                }
                _lastHotkeyPress[e.HotkeyId] = now;

                HotkeyPressed?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error handling hotkey press", ex, "GlobalHotkeyManager");
            }
        }

        /// <summary>
        /// Checks if flood protection is active and updates counters.
        /// Returns true if flood protection is blocking hotkeys.
        /// </summary>
        private bool IsHotkeyFloodProtectionActive()
        {
            var now = DateTime.UtcNow;

            // If flood protection is active, check if cooldown has expired
            if (_hotkeyFloodProtection)
            {
                var timeSinceReset = (now - _lastHotkeyReset).TotalMilliseconds;
                if (timeSinceReset > FLOOD_PROTECTION_DURATION_MS)
                {
                    _hotkeyFloodProtection = false;
                    _hotkeyPressCount = 0;
                    _lastHotkeyReset = now;
                    System.Diagnostics.Debug.WriteLine("Hotkey flood protection deactivated - cooldown expired");
                }
                else
                {
                    return true; // Still in cooldown
                }
            }

            // Reset counter every second
            var timeSinceLastReset = (now - _lastHotkeyReset).TotalSeconds;
            if (timeSinceLastReset >= 1.0)
            {
                _hotkeyPressCount = 0;
                _lastHotkeyReset = now;
            }

            // Increment counter and check threshold
            _hotkeyPressCount++;
            if (_hotkeyPressCount > MAX_HOTKEYS_PER_SECOND)
            {
                _hotkeyFloodProtection = true;
                _lastHotkeyReset = now;
                _ = _loggingService.LogWarningAsync($"HOTKEY FLOOD PROTECTION ACTIVATED: {_hotkeyPressCount} presses/sec exceeded limit of {MAX_HOTKEYS_PER_SECOND}. Blocking for {FLOOD_PROTECTION_DURATION_MS}ms", "GlobalHotkeyManager");
                return true;
            }

            return false;
        }

        private void OnControllerButtonPressed(object? sender, ControllerButtonPressedEventArgs e)
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_lastHotkeyPress.TryGetValue(e.HotkeyId, out var lastPress))
                {
                    var timeSinceLastPress = now - lastPress;
                    if (timeSinceLastPress < _hotkeyDebounceInterval)
                    {
                        _ = _loggingService.LogInfoAsync($"Ignoring rapid controller press: {e.Button} ({timeSinceLastPress.TotalMilliseconds:F0}ms)", "GlobalHotkeyManager");
                        return;
                    }
                }
                _lastHotkeyPress[e.HotkeyId] = now;

                var hotkeyArgs = new HotkeyPressedEventArgs(e.HotkeyId, ModifierKeys.None, Key.None);
                HotkeyPressed?.Invoke(this, hotkeyArgs);
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error handling controller button press", ex, "GlobalHotkeyManager");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _hotkeyService?.Dispose();
                _controllerService?.Dispose();
                _ = _loggingService.LogInfoAsync("GlobalHotkeyManager disposed", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error during GlobalHotkeyManager disposal", ex, "GlobalHotkeyManager");
            }
            GC.SuppressFinalize(this);
        }
    }
}

