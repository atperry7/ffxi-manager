using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// High-level service for coordinating profile operations with validation
    /// </summary>
    public interface IProfileOperationsService
    {
        Task<List<ProfileInfo>> LoadProfilesAsync(bool includeAutoBackups);
        Task<ProfileInfo?> GetActiveLoginInfoAsync();
        Task<(bool Success, string Message)> SwapProfileAsync(ProfileInfo profile);
        Task<(bool Success, string Message, ProfileInfo? NewProfile)> CreateBackupAsync(string name);
        Task<(bool Success, string Message)> DeleteProfileAsync(ProfileInfo profile, bool confirmDelete = true);
        Task<(bool Success, string Message)> RenameProfileAsync(ProfileInfo profile, string newName);
        Task<(bool Success, string Message, int DeletedCount)> CleanupAutoBackupsAsync();
        Task<(bool Success, string Message)> ResetTrackingAsync();
    }
    
    public class ProfileOperationsService : IProfileOperationsService
    {
        private readonly IProfileService _profileService;
        private readonly ISettingsService _settingsService;
        
        public ProfileOperationsService(IProfileService profileService, ISettingsService settingsService)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }
        
        public async Task<List<ProfileInfo>> LoadProfilesAsync(bool includeAutoBackups)
        {
            return includeAutoBackups 
                ? await _profileService.GetProfilesAsync()
                : await _profileService.GetUserProfilesAsync();
        }
        
        public async Task<ProfileInfo?> GetActiveLoginInfoAsync()
        {
            return await _profileService.GetActiveLoginInfoAsync();
        }
        
        public async Task<(bool Success, string Message)> SwapProfileAsync(ProfileInfo profile)
        {
            try
            {
                if (profile == null)
                    return (false, "Profile cannot be null");

                if (profile.IsSystemFile)
                    return (false, "Cannot swap with the system login file");

                await _profileService.SwapProfileAsync(profile);
                
                // Update settings
                var settings = _settingsService.LoadSettings();
                settings.LastActiveProfileName = profile.Name;
                _settingsService.SaveSettings(settings);
                
                return (true, $"Successfully swapped to profile: {profile.Name}");
            }
            catch (Exception ex)
            {
                return (false, $"Error swapping profile: {ex.Message}");
            }
        }
        
        public async Task<(bool Success, string Message, ProfileInfo? NewProfile)> CreateBackupAsync(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name))
                    return (false, "Backup name cannot be empty", null);

                var newProfile = await _profileService.CreateBackupAsync(name);
                return (true, $"Successfully created backup: {newProfile.Name}", newProfile);
            }
            catch (Exception ex)
            {
                return (false, $"Error creating backup: {ex.Message}", null);
            }
        }
        
        public async Task<(bool Success, string Message)> DeleteProfileAsync(ProfileInfo profile, bool confirmDelete = true)
        {
            try
            {
                if (profile == null)
                    return (false, "Profile cannot be null");

                if (profile.IsSystemFile)
                    return (false, "Cannot delete system files");

                if (confirmDelete)
                {
                    var settings = _settingsService.LoadSettings();
                    if (settings.ConfirmDeleteOperations)
                    {
                        var result = System.Windows.MessageBox.Show(
                            $"Are you sure you want to delete profile '{profile.Name}'?",
                            "Confirm Delete",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);
                        
                        if (result != System.Windows.MessageBoxResult.Yes)
                            return (false, "Delete cancelled by user");
                    }
                }
                
                await _profileService.DeleteProfileAsync(profile);
                return (true, $"Profile '{profile.Name}' deleted successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error deleting profile: {ex.Message}");
            }
        }
        
        public async Task<(bool Success, string Message)> RenameProfileAsync(ProfileInfo profile, string newName)
        {
            try
            {
                if (profile == null)
                    return (false, "Profile cannot be null");

                if (string.IsNullOrWhiteSpace(newName))
                    return (false, "New name cannot be empty");

                if (profile.IsSystemFile)
                    return (false, "Cannot rename the system login file");

                await _profileService.RenameProfileAsync(profile, newName);
                return (true, $"Profile renamed successfully to '{newName}'");
            }
            catch (Exception ex)
            {
                return (false, $"Error renaming profile: {ex.Message}");
            }
        }
        
        public async Task<(bool Success, string Message, int DeletedCount)> CleanupAutoBackupsAsync()
        {
            try
            {
                var autoBackupsBefore = await _profileService.GetAutoBackupsAsync();
                var countBefore = autoBackupsBefore.Count;
                
                await _profileService.CleanupAutoBackupsAsync();
                
                var autoBackupsAfter = await _profileService.GetAutoBackupsAsync();
                var countAfter = autoBackupsAfter.Count;
                var deletedCount = countBefore - countAfter;
                
                return (true, $"Cleaned up {deletedCount} old auto-backup files", deletedCount);
            }
            catch (Exception ex)
            {
                return (false, $"Error cleaning up auto-backups: {ex.Message}", 0);
            }
        }
        
        public async Task<(bool Success, string Message)> ResetTrackingAsync()
        {
            try
            {
                await _profileService.ClearActiveProfileTrackingAsync();
                return (true, "User profile choice cleared successfully");
            }
            catch (Exception ex)
            {
                return (false, $"Error resetting user choice: {ex.Message}");
            }
        }
    }
}