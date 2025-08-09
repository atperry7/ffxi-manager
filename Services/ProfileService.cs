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
        /// Settings service for accessing user preferences and tracking
        /// </summary>
        public SettingsService? SettingsService { get; set; }
        
        /// <summary>
        /// Gets all available profile backup files with smart active detection
        /// </summary>
        public async Task<List<ProfileInfo>> GetProfilesAsync()
        {
            var profiles = new List<ProfileInfo>();
            
            if (!Directory.Exists(PlayOnlineDirectory))
                return profiles;
            
            try
            {
                // Get current active profile info for smart tracking
                string? trackedActiveProfile = null;
                bool useContentDetection = true;
                
                if (SettingsService != null)
                {
                    var settings = SettingsService.LoadSettings();
                    var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
                    
                    if (File.Exists(activeLoginPath) && !string.IsNullOrEmpty(settings.CurrentActiveProfile))
                    {
                        var activeContent = File.ReadAllBytes(activeLoginPath);
                        var currentHash = Convert.ToBase64String(ComputeFileHash(activeContent));
                        
                        // If hash matches, trust the user's tracked choice
                        if (settings.ActiveProfileHash == currentHash)
                        {
                            trackedActiveProfile = settings.CurrentActiveProfile;
                            useContentDetection = false;
                        }
                    }
                }
                
                // Get active login content for fallback content detection
                byte[]? activeHash = null;
                if (useContentDetection)
                {
                    var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
                    if (File.Exists(activeLoginPath))
                    {
                        try
                        {
                            var activeContent = File.ReadAllBytes(activeLoginPath);
                            activeHash = ComputeFileHash(activeContent);
                        }
                        catch
                        {
                            // If we can't read active file, continue without comparison
                        }
                    }
                }
                
                var files = Directory.GetFiles(PlayOnlineDirectory, $"*{BACKUP_EXTENSION}");
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = fileInfo.Name;
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                    
                    // Skip system files that shouldn't be managed as profiles
                    if (EXCLUDED_FILES.Contains(fileName))
                        continue;
                    
                    // Determine if this profile is currently active
                    bool isCurrentlyActive = false;
                    
                    if (trackedActiveProfile != null)
                    {
                        // Use tracked active profile (more reliable)
                        // Only mark as active if it exactly matches the tracked profile name
                        isCurrentlyActive = fileNameWithoutExtension.Equals(trackedActiveProfile, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (activeHash != null)
                    {
                        // Fallback to content detection, but prefer user profiles over auto-backups
                        try
                        {
                            var backupContent = File.ReadAllBytes(file);
                            var backupHash = ComputeFileHash(backupContent);
                            bool contentMatches = activeHash.SequenceEqual(backupHash);
                            
                            if (contentMatches)
                            {
                                // If content matches, prefer user-created profiles over auto-backups
                                var isAutoBackup = fileNameWithoutExtension.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
                                
                                if (!isAutoBackup)
                                {
                                    // User-created profile gets priority
                                    isCurrentlyActive = true;
                                }
                                else
                                {
                                    // Auto-backup: only mark as active if no user profile matches
                                    var userProfiles = files
                                        .Where(f => !Path.GetFileNameWithoutExtension(f).StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                                        .Where(f => !EXCLUDED_FILES.Contains(Path.GetFileName(f)));
                                    
                                    bool hasMatchingUserProfile = false;
                                    foreach (var userFile in userProfiles)
                                    {
                                        try
                                        {
                                            var userContent = File.ReadAllBytes(userFile);
                                            var userHash = ComputeFileHash(userContent);
                                            if (activeHash.SequenceEqual(userHash))
                                            {
                                                hasMatchingUserProfile = true;
                                                break;
                                            }
                                        }
                                        catch { }
                                    }
                                    
                                    isCurrentlyActive = !hasMatchingUserProfile;
                                }
                            }
                        }
                        catch
                        {
                            // If we can't read backup file, assume it's not active
                        }
                    }
                    
                    var profile = new ProfileInfo
                    {
                        Name = fileNameWithoutExtension,
                        FilePath = file,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length,
                        IsActive = false, // Backup profiles are never marked as "active" in the traditional sense
                        IsCurrentlyActive = isCurrentlyActive,
                        Description = GetProfileDescription(fileNameWithoutExtension, isCurrentlyActive)
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
                var activeContent = File.ReadAllBytes(activeLoginPath);
                var currentHash = Convert.ToBase64String(ComputeFileHash(activeContent));
                
                string? displayProfile = null;
                string description;
                bool wasChangedExternally = false;
                
                if (SettingsService != null)
                {
                    var settings = SettingsService.LoadSettings();
                    
                    // Check if we have a tracked active profile
                    if (!string.IsNullOrEmpty(settings.CurrentActiveProfile))
                    {
                        // Check if the file hash matches what we expect
                        if (settings.ActiveProfileHash == currentHash)
                        {
                            // File hasn't changed since we set it - user's choice is still valid
                            displayProfile = settings.CurrentActiveProfile;
                            description = $"Currently active login file - set to '{displayProfile}' by user";
                        }
                        else
                        {
                            // File was changed externally - we need to detect or ask
                            wasChangedExternally = true;
                            var detectedProfile = await FindMatchingBackupProfileAsync(ComputeFileHash(activeContent));
                            
                            if (detectedProfile != null)
                            {
                                displayProfile = detectedProfile;
                                description = $"Currently active login file - detected as '{detectedProfile}' (changed externally)";
                            }
                            else
                            {
                                displayProfile = "Unknown";
                                description = "Currently active login file - changed externally, source unknown";
                            }
                        }
                    }
                    else
                    {
                        // First time or no tracked profile - try to detect
                        var detectedProfile = await FindMatchingBackupProfileAsync(ComputeFileHash(activeContent));
                        
                        if (detectedProfile != null)
                        {
                            displayProfile = detectedProfile;
                            description = $"Currently active login file - detected as '{detectedProfile}' (not explicitly set)";
                        }
                        else
                        {
                            displayProfile = "Unknown";
                            description = "Currently active login file - no matching backup profile found";
                        }
                    }
                }
                else
                {
                    // Fallback to content detection if no settings service
                    var detectedProfile = await FindMatchingBackupProfileAsync(ComputeFileHash(activeContent));
                    displayProfile = detectedProfile ?? "Unknown";
                    description = detectedProfile != null 
                        ? $"Currently active login file - detected as '{detectedProfile}'"
                        : "Currently active login file - no matching backup profile found";
                }
                
                var displayName = displayProfile != null && displayProfile != "Unknown"
                    ? $"login_w (Currently: {displayProfile})"
                    : "login_w (Active - Unknown Source)";
                
                if (wasChangedExternally)
                {
                    displayName += " [EXT]";
                    description += " - File was modified outside this application";
                }
                
                return new ProfileInfo
                {
                    Name = displayName,
                    FilePath = activeLoginPath,
                    LastModified = fileInfo.LastWriteTime,
                    FileSize = fileInfo.Length,
                    IsActive = true,
                    Description = description
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error reading active login file: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Finds which backup profile matches the given content hash (prioritizes user profiles)
        /// </summary>
        private async Task<string?> FindMatchingBackupProfileAsync(byte[] activeHash)
        {
            try
            {
                var allBackupFiles = Directory.GetFiles(PlayOnlineDirectory, "*.bin")
                    .Where(f => !EXCLUDED_FILES.Contains(Path.GetFileName(f)))
                    .ToList();
                
                // First pass: Look for user-created profiles (non-auto-backups)
                foreach (var backupFile in allBackupFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(backupFile);
                    
                    // Skip auto-backups in first pass
                    if (fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    try
                    {
                        var backupContent = File.ReadAllBytes(backupFile);
                        var backupHash = ComputeFileHash(backupContent);
                        
                        if (activeHash.SequenceEqual(backupHash))
                        {
                            return fileName; // Return the user profile name
                        }
                    }
                    catch
                    {
                        // If we can't read a backup file, skip it
                        continue;
                    }
                }
                
                // Second pass: If no user profile matches, check auto-backups
                foreach (var backupFile in allBackupFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(backupFile);
                    
                    // Only check auto-backups in second pass
                    if (!fileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    try
                    {
                        var backupContent = File.ReadAllBytes(backupFile);
                        var backupHash = ComputeFileHash(backupContent);
                        
                        if (activeHash.SequenceEqual(backupHash))
                        {
                            return fileName; // Return the auto-backup name
                        }
                    }
                    catch
                    {
                        // If we can't read a backup file, skip it
                        continue;
                    }
                }
                
                return null; // No matching profile found
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Gets a description for a profile based on its name pattern and active status
        /// </summary>
        private static string GetProfileDescription(string profileName, bool isCurrentlyActive = false)
        {
            var isAutoBackup = profileName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase);
            var baseDescription = isAutoBackup
                ? "Automatic backup created during profile swap"
                : "User-created profile backup";
            
            if (isCurrentlyActive)
            {
                if (isAutoBackup)
                {
                    return $"? {baseDescription} (Currently matches active file)";
                }
                else
                {
                    return $"? {baseDescription} (Your selected active profile)";
                }
            }
            
            return baseDescription;
        }
        
        /// <summary>
        /// Swaps the active login file with a backup profile and tracks user choice
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
                
                // Record the user's explicit choice for smart tracking
                await RecordActiveProfileChoiceAsync(targetProfile.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error swapping profile: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Records the user's explicit active profile choice for smart tracking
        /// </summary>
        private async Task RecordActiveProfileChoiceAsync(string profileName)
        {
            if (SettingsService == null) return;
            
            try
            {
                var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
                if (!File.Exists(activeLoginPath)) return;
                
                var activeContent = File.ReadAllBytes(activeLoginPath);
                var currentHash = Convert.ToBase64String(ComputeFileHash(activeContent));
                
                var settings = SettingsService.LoadSettings();
                settings.CurrentActiveProfile = profileName;
                settings.ActiveProfileHash = currentHash;
                settings.ActiveProfileSetTime = DateTime.Now;
                
                SettingsService.SaveSettings(settings);
            }
            catch
            {
                // If we can't record the choice, don't fail the swap operation
                // This is just for UX improvement
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
        
        /// <summary>
        /// Checks if the active login file was changed externally and updates tracking
        /// </summary>
        public async Task<bool> DetectExternalChangesAsync()
        {
            if (SettingsService == null) return false;
            
            try
            {
                var activeLoginPath = Path.Combine(PlayOnlineDirectory, DEFAULT_LOGIN_FILE);
                if (!File.Exists(activeLoginPath)) return false;
                
                var settings = SettingsService.LoadSettings();
                if (string.IsNullOrEmpty(settings.CurrentActiveProfile)) return false;
                
                var activeContent = File.ReadAllBytes(activeLoginPath);
                var currentHash = Convert.ToBase64String(ComputeFileHash(activeContent));
                
                // If hash doesn't match, file was changed externally
                if (settings.ActiveProfileHash != currentHash)
                {
                    // Try to detect what it was changed to
                    var detectedProfile = await FindMatchingBackupProfileAsync(ComputeFileHash(activeContent));
                    
                    // Update tracking with detected or unknown state
                    settings.CurrentActiveProfile = detectedProfile ?? "Unknown";
                    settings.ActiveProfileHash = currentHash;
                    settings.ActiveProfileSetTime = DateTime.Now;
                    
                    SettingsService.SaveSettings(settings);
                    return true; // External change detected
                }
                
                return false; // No external change
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Clears the active profile tracking (useful when user wants to reset)
        /// </summary>
        public async Task ClearActiveProfileTrackingAsync()
        {
            if (SettingsService == null) return;
            
            try
            {
                var settings = SettingsService.LoadSettings();
                settings.CurrentActiveProfile = string.Empty;
                settings.ActiveProfileHash = string.Empty;
                settings.ActiveProfileSetTime = DateTime.MinValue;
                
                SettingsService.SaveSettings(settings);
            }
            catch
            {
                // Ignore errors when clearing tracking
            }
        }
        
        /// <summary>
        /// Renames a profile file
        /// </summary>
        public async Task RenameProfileAsync(ProfileInfo profile, string newName)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name cannot be empty", nameof(newName));
            
            if (profile.IsActive)
                throw new InvalidOperationException("Cannot rename the active login file");
            
            // Sanitize the new name
            var sanitizedName = SanitizeFileName(newName);
            var newFilePath = Path.Combine(PlayOnlineDirectory, $"{sanitizedName}.bin");
            
            // Check if target file already exists
            if (File.Exists(newFilePath))
            {
                throw new InvalidOperationException($"A profile with the name '{sanitizedName}' already exists");
            }
            
            // Additional safety check - don't allow renaming system files
            var fileName = Path.GetFileName(profile.FilePath);
            if (EXCLUDED_FILES.Contains(fileName))
                throw new InvalidOperationException("Cannot rename system files");
            
            if (!File.Exists(profile.FilePath))
                throw new FileNotFoundException($"Profile file not found: {profile.FilePath}");
            
            try
            {
                // Rename the file
                File.Move(profile.FilePath, newFilePath);
                
                // Update settings if this was the currently tracked active profile
                if (SettingsService != null)
                {
                    var settings = SettingsService.LoadSettings();
                    if (settings.CurrentActiveProfile == profile.Name)
                    {
                        settings.CurrentActiveProfile = sanitizedName;
                        SettingsService.SaveSettings(settings);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error renaming profile: {ex.Message}", ex);
            }
        }
    }
}