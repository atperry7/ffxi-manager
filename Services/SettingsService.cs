using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simple, reliable settings service without complex dependencies
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private const string SETTINGS_FILE = "FFXIManagerSettings.json";
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly string _settingsPath;
        
        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "FFXIManager");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SETTINGS_FILE);
        }
        
        public ApplicationSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<ApplicationSettings>(json);
                    return settings ?? new ApplicationSettings();
                }
            }
            catch (Exception)
            {
                // If there's any error loading settings, return defaults
            }
            
            return new ApplicationSettings();
        }
        
        public void SaveSettings(ApplicationSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, SerializerOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception)
            {
                // Silently fail - settings are not critical
            }
        }
    }
    
    /// <summary>
    /// Simple application settings model
    /// </summary>
        public class ApplicationSettings
        {
            public string PlayOnlineDirectory { get; set; } = @"C:\\Program Files (x86)\\PlayOnline\\SquareEnix\\PlayOnlineViewer\\usr\\all";
            public bool AutoRefreshOnStartup { get; set; } = true;
            public bool ConfirmDeleteOperations { get; set; } = true;
            public bool CreateAutoBackups { get; set; } = true;
            public int MaxAutoBackups { get; set; } = 5;
            public bool ShowAutoBackupsInList { get; set; }
            public bool EnableSmartBackupDeduplication { get; set; } = true;
            
            // Profile tracking
            public string LastActiveProfileName { get; set; } = string.Empty;
            public string LastUsedProfile { get; set; } = string.Empty;
            
            // External applications persistence
            public List<ExternalApplicationData> ExternalApplications { get; set; } = new();

            // Process discovery configuration (drives discovery filters)
            public ProcessDiscoverySettings ProcessDiscovery { get; set; } = new ProcessDiscoverySettings();
            
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

        public class DiagnosticsOptions
        {
            // Master toggle for diagnostics. When off, only Warning and Error are persisted
            public bool EnableDiagnostics { get; set; }
            // When diagnostics are enabled, include Debug-level events
            public bool VerboseLogging { get; set; }
            // Upper bound for in-memory log buffer and recent persisted entries to prevent flooding
            public int MaxLogEntries { get; set; } = 1000;
        }

    public class ProcessDiscoverySettings
    {
        // Simple, expressive filters: wildcard-capable include/exclude; case-insensitive
        public List<string> IncludeNames { get; set; } = new List<string> { "pol", "ffxi", "PlayOnlineViewer", "Windower", "POLProxy", "Silmaril" };
        public List<string> ExcludeNames { get; set; } = new List<string>();
        // Window title ignore prefixes (in addition to sensible defaults)
        public List<string> IgnoredWindowTitlePrefixes { get; set; } = new List<string>();
    }
    
    /// <summary>
    /// Simplified external application data for persistence
    /// </summary>
    public class ExternalApplicationData
    {
        public string Name { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool AllowMultipleInstances { get; set; }
    }
}