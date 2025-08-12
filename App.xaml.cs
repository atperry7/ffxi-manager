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
            
            // Find and replace the theme dictionary
            ResourceDictionary? oldTheme = null;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source?.OriginalString.Contains("Theme.xaml") == true)
                {
                    oldTheme = dict;
                    break;
                }
            }

            var newTheme = new ResourceDictionary { Source = themeUri };
            
            if (oldTheme != null)
            {
                var index = app.Resources.MergedDictionaries.IndexOf(oldTheme);
                app.Resources.MergedDictionaries.RemoveAt(index);
                app.Resources.MergedDictionaries.Insert(index, newTheme);
            }
            else
            {
                // Insert at the beginning to ensure theme resources have lowest priority
                app.Resources.MergedDictionaries.Insert(0, newTheme);
            }
        }
    }
}
