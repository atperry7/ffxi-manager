using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Centralized global hotkey management service that provides singleton access to hotkey functionality.
    /// This ensures hotkeys work regardless of which UI windows are open.
    /// </summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private static readonly Lazy<GlobalHotkeyManager> _instance = new(() => new GlobalHotkeyManager());

        /// <summary>
        /// Gets the singleton instance of the GlobalHotkeyManager.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the instance has been disposed.</exception>
        public static GlobalHotkeyManager Instance
        {
            get
            {
                if (_instance.IsValueCreated && _instance.Value._disposed)
                    throw new ObjectDisposedException(nameof(GlobalHotkeyManager), "The singleton instance has been disposed and cannot be accessed.");
                return _instance.Value;
            }
        }

        private readonly LowLevelHotkeyService _hotkeyService;
        private readonly ILoggingService _loggingService;
        private readonly Dictionary<int, DateTime> _lastHotkeyPress = new();
        private TimeSpan _hotkeyDebounceInterval = TimeSpan.FromMilliseconds(50); // **INCREASED** from 5ms to prevent system overload
        private bool _disposed;
        
        // **EMERGENCY PROTECTION**: Circuit breaker for hotkey flooding
        private static int _hotkeyPressCount;
        private static DateTime _lastHotkeyReset = DateTime.UtcNow;
        private static volatile bool _hotkeyFloodProtection;
        private const int MAX_HOTKEYS_PER_SECOND = 15;
        private const int FLOOD_PROTECTION_DURATION_MS = 2000;

        /// <summary>
        /// Event fired when a registered hotkey is pressed
        /// </summary>
        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        private GlobalHotkeyManager()
        {
            _loggingService = ServiceLocator.LoggingService;
            _hotkeyService = new LowLevelHotkeyService();
            _hotkeyService.HotkeyPressed += OnLowLevelHotkeyPressed;

            _loggingService.LogInfoAsync("🔥 GlobalHotkeyManager initialized with LOW-LEVEL keyboard hooks", "GlobalHotkeyManager");
        }

        /// <summary>
        /// Registers hotkeys based on current application settings
        /// </summary>
        public void RegisterHotkeysFromSettings()
        {
            if (_disposed) return;

            try
            {
                var settingsService = ServiceLocator.SettingsService;
                var settings = settingsService.LoadSettings();

                // **GAMING CRITICAL**: Validate and clamp debounce interval to prevent crashes from malformed settings
                var debounceMs = Math.Max(1, Math.Min(settings.HotkeyDebounceIntervalMs, 1000)); // Clamp 1-1000ms
                if (debounceMs != settings.HotkeyDebounceIntervalMs)
                {
                    _loggingService.LogWarningAsync($"Clamped invalid hotkey debounce value {settings.HotkeyDebounceIntervalMs}ms to {debounceMs}ms", "GlobalHotkeyManager");
                }
                _hotkeyDebounceInterval = TimeSpan.FromMilliseconds(debounceMs);

                // If no shortcuts configured, create defaults
                if (settings.CharacterSwitchShortcuts.Count == 0)
                {
                    settings.CharacterSwitchShortcuts = ApplicationSettings.GetDefaultShortcuts();
                    settingsService.SaveSettings(settings);
                    _loggingService.LogInfoAsync("Created default keyboard shortcuts (Win+F1-F9)", "GlobalHotkeyManager");
                }

                var registeredCount = 0;
                var failedCount = 0;

                // Register each enabled shortcut (de-duplicated by SlotIndex to avoid double registration)
                var seenSlots = new HashSet<int>();
                foreach (var shortcut in settings.CharacterSwitchShortcuts.Where(s => s.IsEnabled))
                {
                    if (!seenSlots.Add(shortcut.SlotIndex))
                    {
                        _loggingService.LogWarningAsync($"⚠ Duplicate shortcut for slot {shortcut.SlotIndex + 1} ignored during registration", "GlobalHotkeyManager");
                        continue;
                    }

                    bool success = _hotkeyService.RegisterHotkey(shortcut.HotkeyId, shortcut.Modifiers, shortcut.Key);

                    if (success)
                    {
                        registeredCount++;
                        _loggingService.LogInfoAsync($"✓ Registered global hotkey: {shortcut.DisplayText} for slot {shortcut.SlotIndex + 1}", "GlobalHotkeyManager");
                    }
                    else
                    {
                        failedCount++;
                        _loggingService.LogWarningAsync($"✗ Failed to register global hotkey: {shortcut.DisplayText} (may be in use by another application)", "GlobalHotkeyManager");
                    }
                }

                _loggingService.LogInfoAsync($"Hotkey registration complete: {registeredCount} registered, {failedCount} failed", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error registering keyboard shortcuts", ex, "GlobalHotkeyManager");
            }
        }

        /// <summary>
        /// Refreshes hotkey registration based on current settings
        /// </summary>
        public void RefreshHotkeys()
        {
            if (_disposed) return;

            try
            {
                _loggingService.LogInfoAsync("🔄 Refreshing keyboard shortcuts due to settings change", "GlobalHotkeyManager");

                // Unregister all current hotkeys
                _hotkeyService.UnregisterAll();

                // Re-register based on current settings
                RegisterHotkeysFromSettings();
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error refreshing keyboard shortcuts", ex, "GlobalHotkeyManager");
            }
        }

        /// <summary>
        /// Unregisters all hotkeys
        /// </summary>
        public void UnregisterAllHotkeys()
        {
            if (_disposed) return;

            try
            {
                _hotkeyService.UnregisterAll();
                _loggingService.LogInfoAsync("All hotkeys unregistered", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error unregistering hotkeys", ex, "GlobalHotkeyManager");
            }
        }

        private void OnLowLevelHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
        {
            try
            {
                // **EMERGENCY FLOOD PROTECTION**: Check global hotkey rate
                if (IsHotkeyFloodProtectionActive())
                {
                    _loggingService.LogWarningAsync($"Hotkey press ignored: flood protection active ({e.Modifiers}+{e.Key})", "GlobalHotkeyManager");
                    return;
                }
                
                // Debouncing: check if this hotkey was pressed recently
                var now = DateTime.UtcNow;
                if (_lastHotkeyPress.TryGetValue(e.HotkeyId, out var lastPress))
                {
                    var timeSinceLastPress = now - lastPress;
                    if (timeSinceLastPress < _hotkeyDebounceInterval)
                    {
                        // Ignore this press - too soon after the last one
                        _loggingService.LogInfoAsync($"⏰ Ignoring rapid hotkey press: {e.Modifiers}+{e.Key} (debounced - {timeSinceLastPress.TotalMilliseconds:F0}ms since last press)", "GlobalHotkeyManager");
                        return;
                    }
                }

                // Update the last press time for this hotkey
                _lastHotkeyPress[e.HotkeyId] = now;

                // Convert hotkey ID back to slot index
                int slotIndex = KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(e.HotkeyId);
                _loggingService.LogInfoAsync($"🎮 Global hotkey pressed: {e.Modifiers}+{e.Key} (slot {slotIndex + 1})", "GlobalHotkeyManager");

                // Forward the event to any listeners
                HotkeyPressed?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync("Error handling hotkey press", ex, "GlobalHotkeyManager");
            }
        }
        
        /// <summary>
        /// **EMERGENCY PROTECTION**: Manages hotkey flood protection to prevent system overload
        /// </summary>
        private bool IsHotkeyFloodProtectionActive()
        {
            var now = DateTime.UtcNow;
            
            // Reset counter every second
            if ((now - _lastHotkeyReset).TotalMilliseconds >= 1000)
            {
                Interlocked.Exchange(ref _hotkeyPressCount, 0);
                _lastHotkeyReset = now;
                _hotkeyFloodProtection = false;
            }
            
            // Check if we're over the limit
            var currentCount = Interlocked.Increment(ref _hotkeyPressCount);
            
            if (currentCount > MAX_HOTKEYS_PER_SECOND && !_hotkeyFloodProtection)
            {
                _hotkeyFloodProtection = true;
                
                // Schedule flood protection reset
                Task.Run(async () => 
                {
                    await Task.Delay(FLOOD_PROTECTION_DURATION_MS);
                    _hotkeyFloodProtection = false;
                    Interlocked.Exchange(ref _hotkeyPressCount, 0);
                    await _loggingService.LogInfoAsync("Hotkey flood protection deactivated - normal operation resumed", "GlobalHotkeyManager");
                });
                
                _loggingService.LogWarningAsync($"**HOTKEY FLOOD PROTECTION ACTIVATED**: {currentCount} presses/sec exceeded limit ({MAX_HOTKEYS_PER_SECOND})", "GlobalHotkeyManager");
                return true;
            }
            
            return _hotkeyFloodProtection;
        }

        /// <summary>
        /// Releases all resources used by the GlobalHotkeyManager
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _hotkeyService?.Dispose();
                _loggingService.LogInfoAsync("GlobalHotkeyManager disposed", "GlobalHotkeyManager");
            }
            catch (Exception ex)
            {
                _loggingService?.LogErrorAsync("Error during GlobalHotkeyManager disposal", ex, "GlobalHotkeyManager");
            }

            GC.SuppressFinalize(this);
        }
    }
}
