using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for persisting and loading queue state
    /// </summary>
    public interface IQueuePersistenceService
    {
        #region Configuration

        /// <summary>
        /// Gets or sets whether to auto-save queue state
        /// </summary>
        bool AutoSaveQueueState { get; set; }

        #endregion

        #region Persistence Operations

        /// <summary>
        /// Saves the current queue state to settings
        /// </summary>
        /// <param name="queueItems">Current queue items</param>
        /// <param name="isExecuting">Whether queue is currently executing</param>
        /// <param name="isPaused">Whether queue is currently paused</param>
        /// <param name="originalProfilePath">Original profile path before queue started</param>
        /// <param name="statistics">Current queue statistics</param>
        Task SaveQueueStateAsync(IEnumerable<AutoLoginQueueItem> queueItems,
            bool isExecuting,
            bool isPaused,
            string? originalProfilePath,
            QueueExecutionStatistics statistics);

        /// <summary>
        /// Loads queue state from settings and returns restored items
        /// </summary>
        /// <returns>Tuple containing restored items and original profile path</returns>
        Task<(IList<AutoLoginQueueItem> items, string? originalProfilePath)> LoadQueueStateAsync();

        /// <summary>
        /// Clears saved queue state from settings
        /// </summary>
        Task ClearSavedStateAsync();

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds an account by ID within a specific profile
        /// </summary>
        /// <param name="accountId">Account GUID</param>
        /// <param name="profilePath">Profile file path</param>
        /// <returns>The found account or null</returns>
        Task<PlayOnlineMemberAccount?> FindAccountByIdAsync(Guid accountId, string profilePath);

        /// <summary>
        /// Finds a profile by its file path
        /// </summary>
        /// <param name="profilePath">Profile file path</param>
        /// <returns>The found profile or null</returns>
        Task<ProfileInfo?> FindProfileByPathAsync(string profilePath);

        #endregion
    }
}