using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using FFXIManager.Infrastructure;

namespace FFXIManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly Uri LightThemeUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkThemeUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Load the initial theme from settings
            try
            {
                var settingsService = ServiceLocator.SettingsService;
                var settings = settingsService.LoadSettings();
                ApplyTheme(settings.IsDarkTheme);

                // Centralize global hotkey registration at app startup so it works regardless of UI windows
                Services.GlobalHotkeyManager.Instance.RegisterHotkeysFromSettings();

                // Ensure PlayOnline monitoring is started regardless of UI windows
                ServiceLocator.PlayOnlineMonitorService.StartMonitoring();

                // Handle hotkeys at the service layer so it works even if no VM is constructed
                Services.GlobalHotkeyManager.Instance.HotkeyPressed += async (_, e) =>
                {
                    try
                    {
                        // Map hotkey ID -> slot index
                        int slotIndex = Models.Settings.KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(e.HotkeyId);
                        if (slotIndex < 0) 
                        {
                            _ = ServiceLocator.LoggingService.LogWarningAsync($"Invalid hotkey slot index: {slotIndex} from hotkey ID {e.HotkeyId}", "App");
                            return;
                        }

                        // **ARCHITECTURAL FIX**: Use CharacterOrderingService to get properly ordered characters
                        // This ensures hotkey slot positions match the visual order in the UI
                        var characterOrderingService = ServiceLocator.CharacterOrderingService;
                        var characters = await characterOrderingService.GetOrderedCharactersAsync();
                        
                        // **GAMING CRITICAL**: Robust bounds checking with race condition protection
                        if (characters == null || characters.Count == 0)
                        {
                            _ = ServiceLocator.LoggingService.LogInfoAsync($"No characters available for hotkey slot {slotIndex + 1}", "App");
                            return;
                        }
                        
                        if (slotIndex >= characters.Count)
                        {
                            _ = ServiceLocator.LoggingService.LogInfoAsync($"Hotkey slot {slotIndex + 1} is out of range (have {characters.Count} characters)", "App");
                            return;
                        }

                        // Safe array access with additional null check
                        var character = characters.ElementAtOrDefault(slotIndex);
                        if (character != null)
                        {
                            var monitor = ServiceLocator.PlayOnlineMonitorService;
                            await monitor.ActivateCharacterWindowAsync(character);
                            _ = ServiceLocator.LoggingService.LogInfoAsync($"Activated character '{character.DisplayName}' via hotkey slot {slotIndex + 1}", "App");
                        }
                        else
                        {
                            _ = ServiceLocator.LoggingService.LogWarningAsync($"Character at slot {slotIndex + 1} is null", "App");
                        }
                    }
                    catch (Exception ex)
                    {
                        // **CRITICAL**: Replace silent failure with proper error handling
                        var logging = ServiceLocator.LoggingService;
                        await logging.LogErrorAsync($"Hotkey activation failed for slot {Models.Settings.KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(e.HotkeyId) + 1}", ex, "App");
                        
                        // Don't show notifications for common/expected errors to avoid spam
                        if (!(ex is ArgumentOutOfRangeException || ex is NullReferenceException))
                        {
                            _ = ServiceLocator.NotificationService?.ShowErrorAsync($"Hotkey failed: {ex.Message}");
                        }
                    }
                };

                // Refresh hotkeys when settings change
                ViewModels.DiscoverySettingsViewModel.HotkeySettingsChanged += (_, __) =>
                {
                    try
                    {
                        Services.GlobalHotkeyManager.Instance.RefreshHotkeys();
                    }
                    catch { }
                };
            }
            catch
            {
                // Default to dark theme if settings can't be loaded
                ApplyTheme(true);
            }
        }

        public static void ApplyTheme(bool isDarkTheme)
        {
            var app = Application.Current;
            if (app == null) return;

            var themeUri = isDarkTheme ? DarkThemeUri : LightThemeUri;

            // Find and replace the theme dictionary - it should always be at index 0
            ResourceDictionary? oldTheme = null;
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                var firstDict = app.Resources.MergedDictionaries[0];
                if (firstDict.Source != null &&
                    (firstDict.Source.OriginalString.Contains("LightTheme.xaml") ||
                     firstDict.Source.OriginalString.Contains("DarkTheme.xaml")))
                {
                    oldTheme = firstDict;
                }
            }

            var newTheme = new ResourceDictionary { Source = themeUri };

            if (oldTheme != null)
            {
                // Replace the first dictionary (theme)
                app.Resources.MergedDictionaries[0] = newTheme;
            }
            else
            {
                // Insert at the beginning
                app.Resources.MergedDictionaries.Insert(0, newTheme);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Unregister global hotkeys on exit to avoid leaving hooks active
                Services.GlobalHotkeyManager.Instance.UnregisterAllHotkeys();
            }
            catch { }

            // Properly dispose of all services before exiting
            ServiceLocator.DisposeAll();
            base.OnExit(e);
        }
    }
}
