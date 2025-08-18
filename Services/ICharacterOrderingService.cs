using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// High-performance service for providing consistently ordered character lists with intelligent caching.
    /// Optimized for gaming scenarios with sub-5ms character lookups for hotkey activation.
    /// </summary>
    public interface ICharacterOrderingService
    {
        /// <summary>
        /// Gets the current ordered list of characters with high-performance caching.
        /// Uses cached data for sub-5ms response times during gaming scenarios.
        /// </summary>
        /// <returns>List of characters in user-preferred order, or empty list if no provider is registered</returns>
        Task<List<PlayOnlineCharacter>> GetOrderedCharactersAsync();

        /// <summary>
        /// Gets a character by slot index with O(1) performance.
        /// Optimized for hotkey activation scenarios.
        /// </summary>
        /// <param name="slotIndex">Zero-based slot index</param>
        /// <returns>Character at the specified slot, or null if index is out of range</returns>
        Task<PlayOnlineCharacter?> GetCharacterBySlotAsync(int slotIndex);

        /// <summary>
        /// Forces immediate cache refresh. Useful after settings changes or process updates.
        /// </summary>
        Task InvalidateCacheAsync();

        /// <summary>
        /// Gets cache performance statistics for monitoring and diagnostics.
        /// </summary>
        CacheStatistics GetCacheStatistics();

        /// <summary>
        /// Registers a provider function that supplies the ordered character list.
        /// Typically called by the PlayOnlineMonitorViewModel to provide its ordered Characters collection.
        /// </summary>
        /// <param name="provider">Function that returns the ordered character list</param>
        void RegisterCharacterOrderProvider(Func<Task<List<PlayOnlineCharacter>>> provider);

        /// <summary>
        /// Unregisters the current character order provider.
        /// Useful when the ViewModel is disposed or when switching contexts.
        /// </summary>
        void UnregisterCharacterOrderProvider();

        /// <summary>
        /// Event raised when the character order provider is changed.
        /// Allows subscribers to refresh their character lists when the source changes.
        /// </summary>
        event EventHandler? CharacterOrderProviderChanged;

        /// <summary>
        /// Event raised when the character cache is updated.
        /// Provides performance metrics and refresh timing information.
        /// </summary>
        event EventHandler<CharacterCacheUpdatedEventArgs>? CharacterCacheUpdated;
    }
}
