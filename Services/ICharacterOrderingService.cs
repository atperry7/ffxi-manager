using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for providing consistently ordered character lists to both UI and hotkey systems.
    /// This ensures that hotkey slot positions match the visual order in the UI.
    /// </summary>
    public interface ICharacterOrderingService
    {
        /// <summary>
        /// Gets the current ordered list of characters. This list respects user-defined ordering
        /// preferences and handles dynamic scenarios (new characters, missing characters, etc.).
        /// </summary>
        /// <returns>List of characters in user-preferred order, or empty list if no provider is registered</returns>
        Task<List<PlayOnlineCharacter>> GetOrderedCharactersAsync();

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
    }
}
