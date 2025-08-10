using System;
using System.Collections.Generic;

namespace FFXIManager.Configuration
{
    /// <summary>
    /// Interface for application configuration management
    /// </summary>
    public interface IConfigurationService
    {
        ProfileConfiguration ProfileConfig { get; }
        UIConfiguration UIConfig { get; }
        FileSystemConfiguration FileSystemConfig { get; }
        BackupConfiguration BackupConfig { get; }
        ValidationConfiguration ValidationConfig { get; }
        
        void ReloadConfiguration();
        void SaveConfiguration();
    }

    /// <summary>
    /// Configuration for profile-related settings
    /// </summary>
    public class ProfileConfiguration
    {
        public string DefaultLoginFileName { get; set; } = "login_w.bin";
        public string BackupFileExtension { get; set; } = ".bin";
        public string AutoBackupPrefix { get; set; } = "backup_";
        public IReadOnlySet<string> ExcludedFiles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "login_w.bin", "inet_w.bin", "noramim.bin"
        };
        public string DefaultPlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        public string AutoBackupDateTimeFormat { get; set; } = "yyyyMMdd_HHmmss";
    }

    /// <summary>
    /// Configuration for UI-related settings
    /// </summary>
    public class UIConfiguration
    {
        public TimeSpan DefaultStatusMessageDuration { get; set; } = TimeSpan.FromSeconds(3);
        public TimeSpan LoadingFeedbackDelay { get; set; } = TimeSpan.FromMilliseconds(300);
        public string ApplicationTitle { get; set; } = "FFXI - Multi-Box Manager";
        public Dictionary<string, string> StatusMessages { get; set; } = new()
        {
            ["LoadingProfiles"] = "Loading profiles...",
            ["RefreshingAfterCleanup"] = "Refreshing profiles after cleanup...",
            ["RefreshingAfterReset"] = "Refreshing profiles after reset...",
            ["SystemFileRenameError"] = "Cannot rename the system login file (login_w.bin).",
            ["EmptyNameError"] = "Profile name cannot be empty",
            ["CleaningUpBackups"] = "Cleaning up auto-backups...",
            ["ResettingTracking"] = "Resetting user profile choice..."
        };
    }

    /// <summary>
    /// Configuration for file system operations
    /// </summary>
    public class FileSystemConfiguration
    {
        public string SettingsDirectory { get; set; } = "FFXIManager";
        public string SettingsFileName { get; set; } = "FFXIManagerSettings.json";
        public string ConfigurationFileName { get; set; } = "FFXIManagerConfig.json";
        public bool CreateDirectoryIfNotExists { get; set; } = true;
        public string[] InvalidFileNameChars { get; set; } = new[] { "<", ">", ":", "\"", "|", "?", "*" };
    }

    /// <summary>
    /// Configuration for backup operations
    /// </summary>
    public class BackupConfiguration
    {
        public int DefaultMaxAutoBackups { get; set; } = 5;
        public bool EnableSmartDeduplication { get; set; } = true;
        public bool CreateAutoBackupsOnSwap { get; set; } = true;
        public bool ConfirmDeleteOperations { get; set; } = true;
        public Dictionary<string, string> ProfileDescriptions { get; set; } = new()
        {
            ["AutoBackup"] = "Automatic backup created during profile swap",
            ["UserProfile"] = "User-created profile backup",
            ["SystemFile"] = "System login file used by PlayOnline"
        };
    }

    /// <summary>
    /// Configuration for validation rules
    /// </summary>
    public class ValidationConfiguration
    {
        public int MaxProfileNameLength { get; set; } = 255;
        public int MinProfileNameLength { get; set; } = 1;
        public string[] ReservedProfileNames { get; set; } = new[] { "CON", "PRN", "AUX", "NUL" };
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB
        public bool AllowUnicodeInNames { get; set; } = true;
        public Dictionary<string, string> ValidationMessages { get; set; } = new()
        {
            ["NameTooLong"] = "Profile name is too long (maximum {0} characters)",
            ["NameTooShort"] = "Profile name cannot be empty",
            ["InvalidCharacters"] = "Profile name contains invalid characters",
            ["ReservedName"] = "'{0}' is a reserved name and cannot be used",
            ["FileTooLarge"] = "File size exceeds maximum allowed size ({0})"
        };
    }
}