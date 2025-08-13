
using System.Collections.Generic;

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

        // Window state persistence
        public double MainWindowWidth { get; set; } = 1200; // Increased default width
        public double MainWindowHeight { get; set; } = 700; // Increased default height
        public double MainWindowLeft { get; set; } = double.NaN; // NaN = center on screen
        public double MainWindowTop { get; set; } = double.NaN; // NaN = center on screen
        public bool MainWindowMaximized { get; set; }
        public bool RememberWindowPosition { get; set; } = true;
    }
}

