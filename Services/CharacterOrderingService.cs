using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service implementation for providing consistently ordered character lists.
    /// Acts as a bridge between the UI ordering logic and the hotkey system.
    /// </summary>
    public class CharacterOrderingService : ICharacterOrderingService
    {
        private Func<Task<List<PlayOnlineCharacter>>>? _characterOrderProvider;
        private readonly ILoggingService _loggingService;

        /// <summary>
        /// Event raised when the character order provider is changed
        /// </summary>
        public event EventHandler? CharacterOrderProviderChanged;

        public CharacterOrderingService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        /// <summary>
        /// Gets the current ordered list of characters from the registered provider
        /// </summary>
        public async Task<List<PlayOnlineCharacter>> GetOrderedCharactersAsync()
        {
            try
            {
                if (_characterOrderProvider == null)
                {
                    await _loggingService.LogInfoAsync("No character order provider registered, returning empty list", "CharacterOrderingService");
                    return new List<PlayOnlineCharacter>();
                }

                var characters = await _characterOrderProvider();
                await _loggingService.LogDebugAsync($"Retrieved {characters?.Count ?? 0} ordered characters from provider", "CharacterOrderingService");
                
                return characters ?? new List<PlayOnlineCharacter>();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error getting ordered characters from provider", ex, "CharacterOrderingService");
                return new List<PlayOnlineCharacter>();
            }
        }

        /// <summary>
        /// Registers a provider function that supplies the ordered character list
        /// </summary>
        public void RegisterCharacterOrderProvider(Func<Task<List<PlayOnlineCharacter>>> provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            _characterOrderProvider = provider;
            _ = _loggingService.LogInfoAsync("Character order provider registered successfully", "CharacterOrderingService");
            
            // Notify subscribers that the provider has changed
            CharacterOrderProviderChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Unregisters the current character order provider
        /// </summary>
        public void UnregisterCharacterOrderProvider()
        {
            if (_characterOrderProvider != null)
            {
                _characterOrderProvider = null;
                _ = _loggingService.LogInfoAsync("Character order provider unregistered", "CharacterOrderingService");
                
                // Notify subscribers that the provider has been removed
                CharacterOrderProviderChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
