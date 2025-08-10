using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private const string SETTINGS_FILE = "FFXIManagerSettings.json";
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
                    
                    // DEBUG: Show what was loaded
                    System.Diagnostics.Debug.WriteLine($"SETTINGS LOADED from: {_settingsPath}");
                    System.Diagnostics.Debug.WriteLine($"   - LastUsedProfile: '{settings?.LastUsedProfile ?? "null"}'");
                    System.Diagnostics.Debug.WriteLine($"   - ExternalApplications count: {settings?.ExternalApplications?.Count ?? 0}");
                    
                    return settings ?? new ApplicationSettings();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"SETTINGS FILE NOT FOUND: {_settingsPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR LOADING SETTINGS: {ex.Message}");
                // If there's any error loading settings, return defaults
            }
            
            return new ApplicationSettings();
        }
        
        public void SaveSettings(ApplicationSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_settingsPath, json);
                
                // DEBUG: Verify what was saved
                System.Diagnostics.Debug.WriteLine($"SETTINGS SAVED to: {_settingsPath}");
                System.Diagnostics.Debug.WriteLine($"   - LastUsedProfile: '{settings.LastUsedProfile}'");
                System.Diagnostics.Debug.WriteLine($"   - ExternalApplications count: {settings.ExternalApplications?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR SAVING SETTINGS: {ex.Message}");
                // Silently fail - settings are not critical
            }
        }
    }
    
    /// <summary>
    /// Application settings model with external applications support
    /// </summary>
    public class ApplicationSettings
    {
        public string PlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        public bool AutoRefreshOnStartup { get; set; } = true;
        public bool ConfirmDeleteOperations { get; set; } = true;
        public bool CreateAutoBackups { get; set; } = true;
        public int MaxAutoBackups { get; set; } = 5;
        public bool ShowAutoBackupsInList { get; set; } = false;
        public bool EnableSmartBackupDeduplication { get; set; } = true;
        
        // Profile tracking
        public string LastActiveProfileName { get; set; } = string.Empty;
        public string LastUsedProfile { get; set; } = string.Empty;
        
        // External applications persistence
        public List<ExternalApplicationData> ExternalApplications { get; set; } = new();
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
        public bool AllowMultipleInstances { get; set; } = false;
    }
}