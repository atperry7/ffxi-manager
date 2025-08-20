using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly Uri LightThemeUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkThemeUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        protected override async void OnStartup(StartupEventArgs e)
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
                
                // Connect the character ordering service to the monitor and wait for completion
                if (ServiceLocator.CharacterOrderingService is CharacterOrderingService orderingService)
                {
                    try
                    {
                        await orderingService.ConnectToMonitorAsync(ServiceLocator.PlayOnlineMonitorService);
                    }
                    catch (Exception ex)
                    {
                        _ = ServiceLocator.LoggingService.LogErrorAsync("Error connecting character ordering service to monitor", ex, "App");
                    }
                }

                // **GAMING OPTIMIZATION**: Ultra-fast hotkey processing via unified service
                Services.GlobalHotkeyManager.Instance.HotkeyPressed += async (_, e) =>
                {
                    // **UNIFIED PIPELINE**: All hotkey activation through optimized service
                    var result = await ServiceLocator.HotkeyActivationService.ActivateCharacterByHotkeyAsync(e.HotkeyId);
                    
                    if (!result.Success && IsUnexpectedHotkeyError(result.ErrorMessage))
                    {
                        _ = ServiceLocator.NotificationServiceEnhanced?.ShowToastAsync($"Hotkey failed: {result.ErrorMessage}", NotificationType.Error);
                    }
                };
                
                // **PERFORMANCE**: Initialize hotkey mappings at startup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ServiceLocator.HotkeyMappingService.RefreshMappingsAsync();
                    }
                    catch (Exception ex)
                    {
                        _ = ServiceLocator.LoggingService.LogErrorAsync("Error initializing hotkey mappings", ex, "App");
                    }
                });
                
                // Refresh hotkeys and mappings when settings change
                ViewModels.DiscoverySettingsViewModel.HotkeySettingsChanged += (_, __) =>
                {
                    try
                    {
                        Services.GlobalHotkeyManager.Instance.RefreshHotkeys();
                        _ = Task.Run(() => ServiceLocator.HotkeyMappingService.RefreshMappingsAsync());
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
        

        /// <summary>
        /// Determines if a hotkey error is unexpected and should be shown to the user.
        /// </summary>
        private static bool IsUnexpectedHotkeyError(string? errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage)) return false;
            
            // Don't show notifications for expected/common errors
            return !errorMessage.Contains("No character mapped") &&
                   !errorMessage.Contains("Invalid window handle") &&
                   !errorMessage.Contains("Access denied") &&
                   !errorMessage.Contains("out of range");
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
