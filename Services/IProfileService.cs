using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for profile management operations
    /// </summary>
    public interface IProfileService
    {
        string PlayOnlineDirectory { get; set; }

        Task<List<ProfileInfo>> GetProfilesAsync();
        Task<List<ProfileInfo>> GetUserProfilesAsync();
        Task<List<ProfileInfo>> GetAutoBackupsAsync();
        Task<ProfileInfo?> GetActiveLoginInfoAsync();

        Task SwapProfileAsync(ProfileInfo targetProfile);
        Task<ProfileInfo> CreateBackupAsync(string backupName);
        Task DeleteProfileAsync(ProfileInfo profile);
        Task RenameProfileAsync(ProfileInfo profile, string newName);
        Task CleanupAutoBackupsAsync();
        Task ClearActiveProfileTrackingAsync();

        bool ValidatePlayOnlineDirectory();
    }
}
