using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Configuration;

namespace FFXIManager.Services
{
    /// <summary>
    /// Performance-optimized service for managing FFXI login profile files with caching and logging
    /// </summary>
    public class ProfileService : IProfileService
    {
        private readonly IConfigurationService _configService;
        private readonly ICachingService _cachingService;
        private readonly ILoggingService _loggingService;
        
        public string PlayOnlineDirectory { get; set; }
        public ISettingsService? SettingsService { get; set; }
        
        public ProfileService(
            IConfigurationService configService, 
            ICachingService cachingService, 
            ILoggingService loggingService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _cachingService = cachingService ?? throw new ArgumentNullException(nameof(cachingService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            PlayOnlineDirectory = _configService.ProfileConfig.DefaultPlayOnlineDirectory;
        }

        /// <summary>
        /// Gets profiles with optional filtering and caching
        /// </summary>
        public async Task<List<ProfileInfo>> GetProfilesAsync()
        {
            await _loggingService.LogDebugAsync("Getting all profiles", "ProfileService");
            
            return await _cachingService.GetOrSetAsync(
                CacheKeys.ProfilesList,
                async () => await Task.Run(() => GetProfiles(includeAutoBackups: true)),
                TimeSpan.FromMinutes(5));
        }
        
        /// <summary>
        /// Gets user-created profiles only (excludes auto-backups) with caching
        /// </summary>
        public async Task<List<ProfileInfo>> GetUserProfilesAsync()
        {
            await _loggingService.LogDebugAsync("Getting user profiles", "ProfileService");
            
            return await _cachingService.GetOrSetAsync(
                CacheKeys.UserProfilesList,
                async () => await Task.Run(() => GetProfiles(includeAutoBackups: false)),
                TimeSpan.FromMinutes(5));
        }
        
        /// <summary>
        /// Gets auto-backup profiles only with caching
        /// </summary>
        public async Task<List<ProfileInfo>> GetAutoBackupsAsync()
        {
            await _loggingService.LogDebugAsync("Getting auto-backup profiles", "ProfileService");
            
            return await _cachingService.GetOrSetAsync(
                CacheKeys.AutoBackupsList,
                async () => await Task.Run(() => GetProfiles(includeAutoBackups: true)
                    .Where(p => p.Name.StartsWith(_configService.ProfileConfig.AutoBackupPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.LastModified)
                    .ToList()),
                TimeSpan.FromMinutes(2)); // Shorter cache for auto-backups as they change more frequently
        }
        
        /// <summary>
        /// Core method to get profiles with filtering and performance optimizations
        /// </summary>
        private List<ProfileInfo> GetProfiles(bool includeAutoBackups)
        {
            if (!Directory.Exists(PlayOnlineDirectory))
            {
                _loggingService.LogWarningAsync($"PlayOnline directory does not exist: {PlayOnlineDirectory}", "ProfileService");
                return new List<ProfileInfo>();
            }
            
            var profiles = new List<ProfileInfo>();
            var settings = SettingsService?.LoadSettings();
            var config = _configService.ProfileConfig;
            
            try
            {
                var searchPattern = $"*{config.BackupFileExtension}";
                var files = Directory.EnumerateFiles(PlayOnlineDirectory, searchPattern, SearchOption.TopDirectoryOnly)
                    .Where(file => !config.ExcludedFiles.Contains(Path.GetFileName(file)))
                    .ToArray(); // Materialize to avoid multiple enumerations
                
                // Pre-allocate list capacity for better performance
                profiles = new List<ProfileInfo>(files.Length);
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var name = Path.GetFileNameWithoutExtension(file);
                    var isAutoBackup = name.StartsWith(config.AutoBackupPrefix, StringComparison.OrdinalIgnoreCase);
                    
                    // Filter auto-backups if not requested
                    if (isAutoBackup && !includeAutoBackups) continue;
                    
                    var profile = new ProfileInfo
                    {
                        Name = name,
                        FilePath = file,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length,
                        Description = GetProfileDescription(name),
                        IsLastUserChoice = settings?.LastActiveProfileName.Equals(name, StringComparison.OrdinalIgnoreCase) == true
                    };
                    
                    profiles.Add(profile);
                }
                
                // Efficient sorting with pre-computed values
                profiles.Sort((x, y) => 
                {
                    var xIsAuto = x.Name.StartsWith(config.AutoBackupPrefix, StringComparison.OrdinalIgnoreCase);
                    var yIsAuto = y.Name.StartsWith(config.AutoBackupPrefix, StringComparison.OrdinalIgnoreCase);
                    
                    if (xIsAuto != yIsAuto) return xIsAuto.CompareTo(yIsAuto);
                    
                    return xIsAuto 
                        ? y.LastModified.CompareTo(x.LastModified) // Newest auto-backups first
                        : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); // User profiles alphabetically
                });

                _loggingService.LogInfoAsync($"Successfully loaded {profiles.Count} profiles (includeAutoBackups: {includeAutoBackups})", "ProfileService");
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Error reading profiles from {PlayOnlineDirectory}", ex, "ProfileService");
                throw new InvalidOperationException($"Error reading profiles: {ex.Message}", ex);
            }
            
            return profiles;
        }
        
        /// <summary>
        /// Gets the current active login file information with caching
        /// </summary>
        public async Task<ProfileInfo?> GetActiveLoginInfoAsync()
        {
            await _loggingService.LogDebugAsync("Getting active login info", "ProfileService");
            
            // Handle nullable return type properly for caching
            var result = await _cachingService.GetAsync<ProfileInfo>(CacheKeys.ActiveLoginInfo);
            if (result != null)
                return result;
                
            var activeInfo = GetActiveLoginInfoInternal();
            if (activeInfo != null)
            {
                await _cachingService.SetAsync(CacheKeys.ActiveLoginInfo, activeInfo, TimeSpan.FromMinutes(1));
            }
            
            return activeInfo;
        }
        
        private ProfileInfo? GetActiveLoginInfoInternal()
        {
            var config = _configService.ProfileConfig;
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, config.DefaultLoginFileName);
            if (!File.Exists(activeLoginPath)) return null;
            
            try
            {
                var fileInfo = new FileInfo(activeLoginPath);
                var settings = SettingsService?.LoadSettings();
                
                var (displayName, description) = !string.IsNullOrEmpty(settings?.LastActiveProfileName)
                    ? ($"{config.DefaultLoginFileName} (Last Set: {settings.LastActiveProfileName})", 
                       $"System login file - last set to '{settings.LastActiveProfileName}' by user")
                    : ($"{config.DefaultLoginFileName} (System File)", 
                       _configService.BackupConfig.ProfileDescriptions["SystemFile"]);
                
                return new ProfileInfo
                {
                    Name = displayName,
                    FilePath = activeLoginPath,
                    LastModified = fileInfo.LastWriteTime,
                    FileSize = fileInfo.Length,
                    Description = description
                };
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"Error reading active login file: {activeLoginPath}", ex, "ProfileService");
                throw new InvalidOperationException($"Error reading active login file: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Swaps the active login file with a backup profile
        /// </summary>
        public async Task SwapProfileAsync(ProfileInfo targetProfile)
        {
            ArgumentNullException.ThrowIfNull(targetProfile);
            
            await _loggingService.LogInfoAsync($"Starting profile swap to: {targetProfile.Name}", "ProfileService");
            
            if (targetProfile.IsSystemFile)
            {
                var error = "Cannot swap with the system login file";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new InvalidOperationException(error);
            }
            
            var config = _configService.ProfileConfig;
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, config.DefaultLoginFileName);
            
            if (!File.Exists(targetProfile.FilePath))
            {
                var error = $"Profile file not found: {targetProfile.FilePath}";
                await _loggingService.LogErrorAsync(error, null, "ProfileService");
                throw new FileNotFoundException(error);
            }
            
            try
            {
                // Create auto-backup of current file
                if (File.Exists(activeLoginPath) && _configService.BackupConfig.CreateAutoBackupsOnSwap)
                {
                    var backupPath = Path.Combine(PlayOnlineDirectory, 
                        $"{config.AutoBackupPrefix}{DateTime.Now.ToString(config.AutoBackupDateTimeFormat)}{config.BackupFileExtension}");
                    
                    await _loggingService.LogDebugAsync($"Creating auto-backup: {backupPath}", "ProfileService");
                    File.Copy(activeLoginPath, backupPath, true);
                    
                    // Clean up old backups asynchronously
                    _ = Task.Run(async () => await CleanupAutoBackupsAsync());
                }
                
                // Copy target profile to active location
                await _loggingService.LogDebugAsync($"Copying profile from {targetProfile.FilePath} to {activeLoginPath}", "ProfileService");
                File.Copy(targetProfile.FilePath, activeLoginPath, true);
                
                // Remember user's choice
                UpdateLastActiveProfile(targetProfile.Name);
                
                // Invalidate relevant caches
                await InvalidateProfileCaches();
                
                await _loggingService.LogInfoAsync($"Successfully swapped to profile: {targetProfile.Name}", "ProfileService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error swapping to profile: {targetProfile.Name}", ex, "ProfileService");
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
            
            await _loggingService.LogInfoAsync($"Creating backup: {backupName}", "ProfileService");
            
            var config = _configService.ProfileConfig;
            var activeLoginPath = Path.Combine(PlayOnlineDirectory, config.DefaultLoginFileName);
            if (!File.Exists(activeLoginPath))
            {
                var error = "Active login file not found";
                await _loggingService.LogErrorAsync(error, null, "ProfileService");
                throw new FileNotFoundException(error);
            }
            
            var sanitizedName = SanitizeFileName(backupName);
            var backupPath = Path.Combine(PlayOnlineDirectory, $"{sanitizedName}{config.BackupFileExtension}");
            
            if (File.Exists(backupPath))
            {
                var error = $"Backup file already exists: {sanitizedName}{config.BackupFileExtension}";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new InvalidOperationException(error);
            }
            
            return await Task.Run(async () =>
            {
                try
                {
                    File.Copy(activeLoginPath, backupPath);
                    var fileInfo = new FileInfo(backupPath);
                    
                    var profile = new ProfileInfo
                    {
                        Name = sanitizedName,
                        FilePath = backupPath,
                        LastModified = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length,
                        Description = _configService.BackupConfig.ProfileDescriptions["UserProfile"]
                    };
                    
                    // Invalidate caches since we added a new profile
                    await InvalidateProfileCaches();
                    
                    await _loggingService.LogInfoAsync($"Successfully created backup: {sanitizedName}", "ProfileService");
                    return profile;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Error creating backup: {backupName}", ex, "ProfileService");
                    throw new InvalidOperationException($"Error creating backup: {ex.Message}", ex);
                }
            });
        }
        
        /// <summary>
        /// Deletes a backup profile file
        /// </summary>
        public async Task DeleteProfileAsync(ProfileInfo profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            
            await _loggingService.LogInfoAsync($"Deleting profile: {profile.Name}", "ProfileService");
            
            var config = _configService.ProfileConfig;
            if (profile.IsSystemFile || config.ExcludedFiles.Contains(Path.GetFileName(profile.FilePath)))
            {
                var error = "Cannot delete system files";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new InvalidOperationException(error);
            }
            
            if (!File.Exists(profile.FilePath))
            {
                var error = $"Profile file not found: {profile.FilePath}";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new FileNotFoundException(error);
            }
            
            await Task.Run(async () =>
            {
                try
                {
                    File.Delete(profile.FilePath);
                    
                    // Invalidate caches since we removed a profile
                    await InvalidateProfileCaches();
                    
                    await _loggingService.LogInfoAsync($"Successfully deleted profile: {profile.Name}", "ProfileService");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Error deleting profile: {profile.Name}", ex, "ProfileService");
                    throw new InvalidOperationException($"Error deleting profile: {ex.Message}", ex);
                }
            });
        }
        
        /// <summary>
        /// Renames a profile file
        /// </summary>
        public async Task RenameProfileAsync(ProfileInfo profile, string newName)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name cannot be empty", nameof(newName));
            
            await _loggingService.LogInfoAsync($"Renaming profile from '{profile.Name}' to '{newName}'", "ProfileService");
            
            if (profile.IsSystemFile)
            {
                var error = "Cannot rename the system login file";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new InvalidOperationException(error);
            }
            
            var config = _configService.ProfileConfig;
            var sanitizedName = SanitizeFileName(newName);
            var newFilePath = Path.Combine(PlayOnlineDirectory, $"{sanitizedName}{config.BackupFileExtension}");
            
            if (File.Exists(newFilePath))
            {
                var error = $"A profile with the name '{sanitizedName}' already exists";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new InvalidOperationException(error);
            }
            
            if (!File.Exists(profile.FilePath))
            {
                var error = $"Profile file not found: {profile.FilePath}";
                await _loggingService.LogWarningAsync(error, "ProfileService");
                throw new FileNotFoundException(error);
            }
            
            await Task.Run(async () =>
            {
                try
                {
                    File.Move(profile.FilePath, newFilePath);
                    
                    // Update settings if this was the user's last active profile
                    var settings = SettingsService?.LoadSettings();
                    if (settings?.LastActiveProfileName == profile.Name)
                    {
                        settings.LastActiveProfileName = sanitizedName;
                        SettingsService?.SaveSettings(settings);
                    }
                    
                    // Invalidate caches since we changed a profile
                    await InvalidateProfileCaches();
                    
                    await _loggingService.LogInfoAsync($"Successfully renamed profile to: {sanitizedName}", "ProfileService");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Error renaming profile from '{profile.Name}' to '{newName}'", ex, "ProfileService");
                    throw new InvalidOperationException($"Error renaming profile: {ex.Message}", ex);
                }
            });
        }
        
        /// <summary>
        /// Cleans up old automatic backup files, keeping only the most recent ones
        /// </summary>
        public async Task CleanupAutoBackupsAsync()
        {
            await _loggingService.LogDebugAsync("Starting auto-backup cleanup", "ProfileService");
            
            await Task.Run(async () =>
            {
                try
                {
                    var config = _configService.ProfileConfig;
                    var settings = SettingsService?.LoadSettings();
                    var maxBackups = settings?.MaxAutoBackups ?? _configService.BackupConfig.DefaultMaxAutoBackups;
                    
                    var backupFiles = Directory.GetFiles(PlayOnlineDirectory, $"{config.AutoBackupPrefix}*{config.BackupFileExtension}")
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToArray();
                    
                    var filesToDelete = backupFiles.Skip(maxBackups);
                    var deletedCount = 0;
                    
                    foreach (var file in filesToDelete)
                    {
                        try
                        {
                            file.Delete();
                            deletedCount++;
                        }
                        catch
                        {
                            await _loggingService.LogWarningAsync($"Failed to delete auto-backup: {file.Name}", "ProfileService");
                        }
                    }
                    
                    if (deletedCount > 0)
                    {
                        // Invalidate auto-backup cache
                        await _cachingService.RemoveAsync(CacheKeys.AutoBackupsList);
                        await _loggingService.LogInfoAsync($"Cleaned up {deletedCount} old auto-backup files", "ProfileService");
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error during auto-backup cleanup", ex, "ProfileService");
                    // Don't throw - cleanup errors are not critical
                }
            });
        }
        
        /// <summary>
        /// Clears the user's profile choice
        /// </summary>
        public async Task ClearActiveProfileTrackingAsync()
        {
            await _loggingService.LogInfoAsync("Clearing active profile tracking", "ProfileService");
            await Task.Run(() => UpdateLastActiveProfile(string.Empty));
            await _cachingService.RemoveAsync(CacheKeys.ActiveLoginInfo);
        }
        
        /// <summary>
        /// Always returns false - external change detection removed for simplicity
        /// </summary>
        public async Task<bool> DetectExternalChangesAsync() => await Task.FromResult(false);
        
        /// <summary>
        /// Validates if the PlayOnline directory exists with caching
        /// </summary>
        public bool ValidatePlayOnlineDirectory()
        {
            var cacheKey = string.Format(CacheKeys.DirectoryValidation, PlayOnlineDirectory);
            
            // Use synchronous check for validation - can be cached if needed
            var exists = Directory.Exists(PlayOnlineDirectory);
            
            if (!exists)
            {
                _loggingService.LogWarningAsync($"PlayOnline directory validation failed: {PlayOnlineDirectory}", "ProfileService");
            }
            
            return exists;
        }
        
        // Helper methods
        private string GetProfileDescription(string profileName)
        {
            var config = _configService.BackupConfig;
            var isAutoBackup = profileName.StartsWith(_configService.ProfileConfig.AutoBackupPrefix, StringComparison.OrdinalIgnoreCase);
            return isAutoBackup ? config.ProfileDescriptions["AutoBackup"] : config.ProfileDescriptions["UserProfile"];
        }
        
        private void UpdateLastActiveProfile(string profileName)
        {
            try
            {
                var settings = SettingsService?.LoadSettings();
                if (settings != null)
                {
                    settings.LastActiveProfileName = profileName;
                    SettingsService?.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarningAsync($"Failed to update last active profile: {ex.Message}", "ProfileService");
            }
        }
        
        private string SanitizeFileName(string fileName)
        {
            var config = _configService.FileSystemConfig;
            var invalidChars = Path.GetInvalidFileNameChars()
                .Concat(config.InvalidFileNameChars.Select(s => s[0]))
                .Distinct();
                
            return invalidChars
                .Aggregate(fileName, (current, invalidChar) => current.Replace(invalidChar, '_'))
                .Trim();
        }
        
        private async Task InvalidateProfileCaches()
        {
            await _cachingService.RemoveAsync(CacheKeys.ProfilesList);
            await _cachingService.RemoveAsync(CacheKeys.UserProfilesList);
            await _cachingService.RemoveAsync(CacheKeys.AutoBackupsList);
            await _cachingService.RemoveAsync(CacheKeys.ActiveLoginInfo);
        }
    }
}