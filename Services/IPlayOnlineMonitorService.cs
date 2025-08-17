using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for monitoring PlayOnline processes and character information
    /// </summary>
    public interface IPlayOnlineMonitorService
    {
        /// <summary>
        /// Event raised when character information changes
        /// </summary>
        event EventHandler<PlayOnlineCharacterEventArgs>? CharacterDetected;

        /// <summary>
        /// Event raised when a character is updated
        /// </summary>
        event EventHandler<PlayOnlineCharacterEventArgs>? CharacterUpdated;

        /// <summary>
        /// Event raised when a character is removed
        /// </summary>
        event EventHandler<PlayOnlineCharacterEventArgs>? CharacterRemoved;

        /// <summary>
        /// Gets all currently detected PlayOnline characters
        /// </summary>
        Task<List<PlayOnlineCharacter>> GetCharactersAsync();

        /// <summary>
        /// Refreshes character information for all processes
        /// </summary>
        Task RefreshCharactersAsync();

        /// <summary>
        /// Activates the window for a specific character
        /// </summary>
        Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts monitoring for PlayOnline processes
        /// </summary>
        void StartMonitoring();

        /// <summary>
        /// Stops monitoring for PlayOnline processes
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Gets whether monitoring is currently active
        /// </summary>
        bool IsMonitoring { get; }
    }

    /// <summary>
    /// Event arguments for PlayOnline character events
    /// </summary>
    public class PlayOnlineCharacterEventArgs : EventArgs
    {
        /// <summary>
        /// The character that triggered the event
        /// </summary>
        public PlayOnlineCharacter Character { get; }

        /// <summary>
        /// Creates new character event arguments
        /// </summary>
        public PlayOnlineCharacterEventArgs(PlayOnlineCharacter character)
        {
            Character = character ?? throw new ArgumentNullException(nameof(character));
        }
    }
}
