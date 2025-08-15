
using System.Collections.Generic;
using System.Windows.Input;

namespace FFXIManager.Models.Settings
{
    /// <summary>
    /// Simple application settings model
    /// </summary>
    public class ApplicationSettings
    {
        public string PlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        public bool AutoRefreshOnStartup { get; set; } = true;
        public bool ConfirmDeleteOperations { get; set; } = true;
        public bool CreateAutoBackups { get; set; } = true;
        public int MaxAutoBackups { get; set; } = 5;
        public bool ShowAutoBackupsInList { get; set; }
        public bool EnableSmartBackupDeduplication { get; set; } = true;

        // Theme and personalization
        public bool IsDarkTheme { get; set; } = true; // Default to dark theme
        public double CharacterMonitorOpacity { get; set; } = 0.95; // Default to 95% opacity

        // Profile tracking
        public string LastActiveProfileName { get; set; } = string.Empty;
        public string LastUsedProfile { get; set; } = string.Empty;

        // External applications persistence
        public List<ExternalApplicationData> ExternalApplications { get; set; } = new();

        // Diagnostics and logging options
        public DiagnosticsOptions Diagnostics { get; set; } = new DiagnosticsOptions();

        // Keyboard shortcuts for character switching
        public List<KeyboardShortcutConfig> CharacterSwitchShortcuts { get; set; } = new();

        // Window state persistence
        public double MainWindowWidth { get; set; } = 1200; // Increased default width
        public double MainWindowHeight { get; set; } = 700; // Increased default height
        public double MainWindowLeft { get; set; } = double.NaN; // NaN = center on screen
        public double MainWindowTop { get; set; } = double.NaN; // NaN = center on screen
        public bool MainWindowMaximized { get; set; }
        public bool RememberWindowPosition { get; set; } = true;

        /// <summary>
        /// Gets the default keyboard shortcuts for character switching (Ctrl+Shift+1 through Ctrl+Shift+9)
        /// </summary>
        public static List<KeyboardShortcutConfig> GetDefaultShortcuts()
        {
            var shortcuts = new List<KeyboardShortcutConfig>();
            for (int i = 0; i < 9; i++)
            {
                var key = (Key)(Key.D1 + i); // D1, D2, D3... D9
                shortcuts.Add(new KeyboardShortcutConfig(i, ModifierKeys.Control | ModifierKeys.Shift, key));
            }
            return shortcuts;
        }
    }
}

