using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIManager.Configuration;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simple service for managing application configuration
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private readonly string _configurationPath;
        private ApplicationConfiguration _configuration = null!;

        public ProfileConfiguration ProfileConfig => _configuration.Profile;
        public UIConfiguration UIConfig => _configuration.UI;
        public FileSystemConfiguration FileSystemConfig => _configuration.FileSystem;
        public BackupConfiguration BackupConfig => _configuration.Backup;
        public ValidationConfiguration ValidationConfig => _configuration.Validation;

        public ConfigurationService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configDirectory = Path.Combine(appDataPath, "FFXIManager");
            Directory.CreateDirectory(configDirectory);
            _configurationPath = Path.Combine(configDirectory, "FFXIManagerConfig.json");
            
            LoadConfiguration();
        }

        public void ReloadConfiguration()
        {
            LoadConfiguration();
        }

        public void SaveConfiguration()
        {
            try
            {
                var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter() }
                });
                
                File.WriteAllText(_configurationPath, json);
            }
            catch (Exception ex)
            {
                // Log error but don't throw - configuration is not critical
                System.Diagnostics.Debug.WriteLine($"Failed to save configuration: {ex.Message}");
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configurationPath))
                {
                    var json = File.ReadAllText(_configurationPath);
                    var config = JsonSerializer.Deserialize<ApplicationConfiguration>(json, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter() }
                    });
                    
                    _configuration = config ?? CreateDefaultConfiguration();
                }
                else
                {
                    _configuration = CreateDefaultConfiguration();
                    SaveConfiguration(); // Create default config file
                }
            }
            catch (Exception ex)
            {
                // If there's any error loading configuration, use defaults
                System.Diagnostics.Debug.WriteLine($"Failed to load configuration: {ex.Message}");
                _configuration = CreateDefaultConfiguration();
            }
        }

        private static ApplicationConfiguration CreateDefaultConfiguration()
        {
            return new ApplicationConfiguration
            {
                Profile = new ProfileConfiguration(),
                UI = new UIConfiguration(),
                FileSystem = new FileSystemConfiguration(),
                Backup = new BackupConfiguration(),
                Validation = new ValidationConfiguration()
            };
        }
    }

    /// <summary>
    /// Root configuration container
    /// </summary>
    internal class ApplicationConfiguration
    {
        public ProfileConfiguration Profile { get; set; } = new();
        public UIConfiguration UI { get; set; } = new();
        public FileSystemConfiguration FileSystem { get; set; } = new();
        public BackupConfiguration Backup { get; set; } = new();
        public ValidationConfiguration Validation { get; set; } = new();
    }
}