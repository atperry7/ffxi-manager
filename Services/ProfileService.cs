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
                
                // Sort profiles: User profiles first, then auto-backups, all by name
                profiles.Sort((x, y) => 
                {
                    var xIsAuto = x.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
                    var yIsAuto = y.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
                    
                    // If one is auto-backup and other is not, user profiles come first
                    if (xIsAuto != yIsAuto)
                        return xIsAuto.CompareTo(yIsAuto);
                    
                    // Within same category, sort by name (or date for auto-backups)
                    if (xIsAuto && yIsAuto)
                        return y.LastModified.CompareTo(x.LastModified); // Newest auto-backups first
                    else
                        return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); // User profiles alphabetically
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading profiles from directory: {ex.Message}", ex);
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Gets user-created profiles only (excludes auto-backups)
        /// </summary>
        public async Task<List<ProfileInfo>> GetUserProfilesAsync()
        {
            var allProfiles = await GetProfilesAsync();
            return allProfiles.Where(p => !p.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        
        /// <summary>
        /// Gets auto-backup profiles only
        /// </summary>
        public async Task<List<ProfileInfo>> GetAutoBackupsAsync()
        {
            var allProfiles = await GetProfilesAsync();
            return allProfiles.Where(p => p.Name.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(p => p.LastModified)
                              .ToList();
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
                // Create backup of current active file if it exists and settings allow
                if (File.Exists(activeLoginPath))
                {
                    await CreateSmartBackupAsync(activeLoginPath);
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
        /// Creates a smart backup that avoids duplicates and manages auto-backup storage efficiently
        /// </summary>
        private async Task CreateSmartBackupAsync(string activeLoginPath)
        {
            try
            {
                // Check if we should create auto-backups (user setting)
                if (!ShouldCreateAutoBackup())
                    return;
                
                var currentContent = File.ReadAllBytes(activeLoginPath);
                var currentHash = ComputeFileHash(currentContent);
                
                // Check if we already have a recent backup with identical content
                var existingBackups = Directory.GetFiles(PlayOnlineDirectory, "backup_*.bin")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(5) // Only check the 5 most recent backups
                    .ToList();
                
                foreach (var backup in existingBackups)
                {
                    try
                    {
                        var backupContent = File.ReadAllBytes(backup.FullName);
                        var backupHash = ComputeFileHash(backupContent);
                        
                        // If we find identical content, just update the timestamp instead of creating new backup
                        if (currentHash.SequenceEqual(backupHash))
                        {
                            File.SetLastWriteTime(backup.FullName, DateTime.Now);
                            return; // Don't create a new backup
                        }
                    }
                    catch
                    {
                        // If we can't read a backup file, ignore it and continue
                        continue;
                    }
                }
                
                // Create new backup only if content is different
                var backupPath = Path.Combine(PlayOnlineDirectory, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.bin");
                File.Copy(activeLoginPath, backupPath, true);
                
                // Clean up old auto-backups
                await CleanupAutoBackupsAsync();
            }
            catch (Exception)
            {
                // If backup creation fails, don't stop the swap operation
                // Auto-backup is a convenience feature, not critical
            }
        }
        
        /// <summary>
        /// Computes a simple hash for file content comparison
        /// </summary>
        private static byte[] ComputeFileHash(byte[] content)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            return sha1.ComputeHash(content);
        }
        
        /// <summary>
        /// Determines if auto-backup should be created based on settings
        /// </summary>
        private static bool ShouldCreateAutoBackup()
        {
            // This could be expanded to check user settings
            // For now, always create backups but with smart deduplication
            return true;
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
        public async Task CleanupAutoBackupsAsync()
        {
            try
            {
                var maxBackups = GetMaxAutoBackupsFromSettings(); // Default to 5 instead of 10
                
                var backupFiles = Directory.GetFiles(PlayOnlineDirectory, "backup_*.bin")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Skip(maxBackups) // Keep the most recent backups based on settings
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
        
        /// <summary>
        /// Gets the maximum number of auto-backups to keep from settings
        /// </summary>
        private static int GetMaxAutoBackupsFromSettings()
        {
            // This could be enhanced to actually read from settings
            // For now, return a sensible default
            return 5;
        }
    }
}