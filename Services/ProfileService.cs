using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing FFXI login profile files
    /// </summary>
    public class ProfileService
    {
        private const string DEFAULT_LOGIN_FILE = "login_w.bin";
        private const string BACKUP_EXTENSION = ".bin";
        
        // System files that should be excluded from profile management
        private static readonly HashSet<string> EXCLUDED_FILES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "login_w.bin",    // Active login file - managed separately
            "inet_w.bin",     // Network configuration file
            "noramim.bin"     // System configuration file
        };
        
        public string PlayOnlineDirectory { get; set; } = @"C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all";
        
        /// <summary>
        /// Gets all available profile backup files (excludes system files)
        /// </summary>
        public async Task<List<ProfileInfo>> GetProfilesAsync()
        {
            var profiles = new List<ProfileInfo>();
            
            if (!Directory.Exists(PlayOnlineDirectory))
                return profiles;
            
            try
            {
                var files = Directory.GetFiles(PlayOnlineDirectory, $"*{BACKUP_EXTENSION}");
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = fileInfo.Name;
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                    
                    // Skip system files that shouldn't be managed as profiles
                    if (EXCLUDED_FILES.Contains(fileName))
                        continue;
                    
                    var profile = new ProfileInfo
                    {
                        Name = fileNameWithoutExtension,
                        FilePath = file,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length,
                        IsActive = false, // Backup profiles are never "active"
                        Description = GetProfileDescription(fileNameWithoutExtension)
                    };
                    
                    profiles.Add(profile);
                }
                
                // Sort profiles by name for consistent display
                profiles.Sort((x, y) => string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading profiles from directory: {ex.Message}", ex);
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Gets the current active login file information
        /// </summary>
        public async Task<ProfileInfo?> GetActiveLoginInfoAsync()
        {
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
            
            if (!File.Exists(activeLoginPath))
                return null;
            
            try
            {
                var fileInfo = new FileInfo(activeLoginPath);
                return new ProfileInfo
                {
                    Name = "login_w (Active)",
                    FilePath = activeLoginPath,
                    LastModified = fileInfo.LastWriteTime,
                    FileSize = fileInfo.Length,
                    IsActive = true,
                    Description = "Currently active login file used by PlayOnline"
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading active login file: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Gets a description for a profile based on its name pattern
        /// </summary>
        private static string GetProfileDescription(string profileName)
        {
            if (profileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                return "Automatic backup created during profile swap";
            
            return "User-created profile backup";
        }
        
        /// <summary>
        /// Swaps the active login file with a backup profile
        /// </summary>
        public async Task SwapProfileAsync(ProfileInfo targetProfile)
        {
            if (targetProfile == null)
                throw new ArgumentNullException(nameof(targetProfile));
            
            if (targetProfile.IsActive)
                throw new InvalidOperationException("Cannot swap with the active login file");
            
            // Additional safety check - don't allow swapping system files
            var fileName = Path.GetFileName(targetProfile.FilePath);
            if (EXCLUDED_FILES.Contains(fileName))
                throw new InvalidOperationException("Cannot swap system files");
            
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
            
            if (!File.Exists(targetProfile.FilePath))
                throw new FileNotFoundException($"Profile file not found: {targetProfile.FilePath}");
            
            try
            {
                // Create backup of current active file if it exists
                if (File.Exists(activeLoginPath))
                {
                    var backupPath = Path.Combine(PlayOnlineDirectory, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                    File.Copy(activeLoginPath, backupPath, true);
                    
                    // Clean up old auto-backups
                    await CleanupAutoBackupsAsync();
                }
                
                // Copy target profile to active location
                File.Copy(targetProfile.FilePath, activeLoginPath, true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error swapping profile: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Creates a backup of the current active login file
        /// </summary>
        public async Task<ProfileInfo> CreateBackupAsync(string backupName)
        {
            if (string.IsNullOrWhiteSpace(backupName))
                throw new ArgumentException("Backup name cannot be empty", nameof(backupName));
            
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
            
            if (!File.Exists(activeLoginPath))
                throw new FileNotFoundException("Active login file not found");
            
            // Sanitize backup name
            var sanitizedName = SanitizeFileName(backupName);
            var backupPath = Path.Combine(PlayOnlineDirectory, $"{sanitizedName}.bin");
            
            if (File.Exists(backupPath))
                throw new InvalidOperationException($"Backup file already exists: {sanitizedName}.bin");
            
            try
            {
                File.Copy(activeLoginPath, backupPath);
                
                var fileInfo = new FileInfo(backupPath);
                return new ProfileInfo
                {
                    Name = sanitizedName,
                    FilePath = backupPath,
                    LastModified = fileInfo.LastWriteTime,
                    FileSize = fileInfo.Length,
                    IsActive = false
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error creating backup: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Deletes a backup profile file
        /// </summary>
        public async Task DeleteProfileAsync(ProfileInfo profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            
            if (profile.IsActive)
                throw new InvalidOperationException("Cannot delete the active login file");
            
            // Additional safety check - don't allow deletion of system files
            var fileName = Path.GetFileName(profile.FilePath);
            if (EXCLUDED_FILES.Contains(fileName))
                throw new InvalidOperationException("Cannot delete system files");
            
            if (!File.Exists(profile.FilePath))
                throw new FileNotFoundException($"Profile file not found: {profile.FilePath}");
            
            try
            {
                File.Delete(profile.FilePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error deleting profile: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Validates if the PlayOnline directory exists and is accessible
        /// </summary>
        public bool ValidatePlayOnlineDirectory()
        {
            return Directory.Exists(PlayOnlineDirectory);
        }
        
        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = fileName;
            
            foreach (var invalidChar in invalidChars)
            {
                sanitized = sanitized.Replace(invalidChar, '_');
            }
            
            return sanitized.Trim();
        }
        
        /// <summary>
        /// Cleans up old automatic backup files, keeping only the most recent ones
        /// </summary>
        private async Task CleanupAutoBackupsAsync()
        {
            try
            {
                var backupFiles = Directory.GetFiles(PlayOnlineDirectory, "backup_*.bin")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(10) // Keep the 10 most recent backups
                    .ToList();
                
                foreach (var file in backupFiles)
                {
                    File.Delete(file.FullName);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors - not critical
            }
        }
    }
}