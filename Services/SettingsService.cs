using System;
using System.IO;
using System.Text.Json;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing application settings
    /// </summary>
    public class SettingsService
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
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception)
            {
                // Silently fail - settings are not critical
            }
        }
    }
    
    /// <summary>
    /// Application settings model
    /// </summary>
    public class ApplicationSettings
    {
        public string PlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        public bool AutoRefreshOnStartup { get; set; } = true;
        public bool ConfirmDeleteOperations { get; set; } = true;
        public bool CreateAutoBackups { get; set; } = true;
        public int MaxAutoBackups { get; set; } = 5;
        public string LastUsedProfile { get; set; } = string.Empty;
        public bool ShowAutoBackupsInList { get; set; } = false;
        public bool EnableSmartBackupDeduplication { get; set; } = true;
        
        /// <summary>
        /// The profile name that the user explicitly selected to be active
        /// </summary>
        public string CurrentActiveProfile { get; set; } = string.Empty;
        
        /// <summary>
        /// Hash of the login_w.bin file when CurrentActiveProfile was set
        /// Used to detect if file was changed externally
        /// </summary>
        public string ActiveProfileHash { get; set; } = string.Empty;
        
        /// <summary>
        /// Timestamp when the active profile was last set by user
        /// </summary>
        public DateTime ActiveProfileSetTime { get; set; } = DateTime.MinValue;
    }
}