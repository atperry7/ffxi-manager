using System;
using System.Configuration;
using System.Data;
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
            // Properly dispose of all services before exiting
            ServiceLocator.DisposeAll();
            base.OnExit(e);
        }
    }
}
