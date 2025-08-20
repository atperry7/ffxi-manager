using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simple, maintainable character ordering service.
    /// Manages the user's preferred character order while monitors handle character health/detection.
    /// Clear separation of concerns: ORDER vs HEALTH.
    /// </summary>
    public class CharacterOrderingService : ICharacterOrderingService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly List<PlayOnlineCharacter> _orderedCharacters = new();
        private readonly object _lock = new object();
        
        private bool _disposed;
        
        public event EventHandler<CharacterCacheUpdatedEventArgs>? CharacterCacheUpdated;
        
        #pragma warning disable CS0067 // Event is never used - keeping for interface compatibility
        public event EventHandler? CharacterOrderProviderChanged;
        #pragma warning restore CS0067

        public CharacterOrderingService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            _ = _loggingService.LogInfoAsync("CharacterOrderingService initialized - waiting for monitor to connect", "CharacterOrderingService");
        }
        
        /// <summary>
        /// Called by the monitor service to establish the connection
        /// </summary>
        public async Task ConnectToMonitorAsync(IPlayOnlineMonitorService monitorService)
        {
            if (monitorService == null || _disposed) return;
            
            try
            {
                // Subscribe to monitor events
                monitorService.CharacterDetected += OnCharacterDetected;
                monitorService.CharacterUpdated += OnCharacterUpdated;
                monitorService.CharacterRemoved += OnCharacterRemoved;
                
                await _loggingService.LogInfoAsync("CharacterOrderingService connected to monitor", "CharacterOrderingService");
                
                // Load any existing characters synchronously to ensure UI gets them
                try
                {
                    var existingCharacters = await monitorService.GetCharactersAsync();
                    if (existingCharacters?.Count > 0)
                    {
                        await _loggingService.LogInfoAsync($"Loading {existingCharacters.Count} existing characters", "CharacterOrderingService");
                        
                        lock (_lock)
                        {
                            foreach (var character in existingCharacters)
                            {
                                if (!_orderedCharacters.Any(c => c.ProcessId == character.ProcessId))
                                {
                                    _orderedCharacters.Add(character);
                                }
                            }
                        }
                        
                        NotifyOrderChanged();
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error loading existing characters", ex, "CharacterOrderingService");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error connecting to monitor", ex, "CharacterOrderingService");
            }
        }
        

        /// <summary>
        /// Gets the ordered list of characters. This is THE source of truth for character order.
        /// </summary>
        public Task<List<PlayOnlineCharacter>> GetOrderedCharactersAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(_orderedCharacters.ToList()); // Return copy to prevent external modification
            }
        }

        /// <summary>
        /// Gets a character by slot index (position-based hotkey mapping).
        /// </summary>
        public Task<PlayOnlineCharacter?> GetCharacterBySlotAsync(int slotIndex)
        {
            lock (_lock)
            {
                if (slotIndex < 0 || slotIndex >= _orderedCharacters.Count)
                    return Task.FromResult<PlayOnlineCharacter?>(null);
                    
                return Task.FromResult<PlayOnlineCharacter?>(_orderedCharacters[slotIndex]);
            }
        }

        /// <summary>
        /// Moves a character to a new slot position (for user reordering).
        /// </summary>
        public Task<bool> MoveCharacterToSlotAsync(PlayOnlineCharacter character, int newSlotIndex)
        {
            if (character == null) return Task.FromResult(false);
            
            lock (_lock)
            {
                // Find current position by PID
                var currentIndex = _orderedCharacters.FindIndex(c => c.ProcessId == character.ProcessId);
                if (currentIndex < 0) return Task.FromResult(false);
                
                // Validate new position
                if (newSlotIndex < 0 || newSlotIndex >= _orderedCharacters.Count)
                    return Task.FromResult(false);
                
                if (currentIndex == newSlotIndex)
                    return Task.FromResult(true); // Already in position
                
                // Move the character
                var movingCharacter = _orderedCharacters[currentIndex];
                _orderedCharacters.RemoveAt(currentIndex);
                _orderedCharacters.Insert(newSlotIndex, movingCharacter);
                
                _ = _loggingService.LogInfoAsync($"Moved {character.DisplayName} from slot {currentIndex} to {newSlotIndex}", "CharacterOrderingService");
            }
            
            // Notify after releasing lock
            NotifyOrderChanged();
            return Task.FromResult(true);
        }

        /// <summary>
        /// Forces refresh of character order (for interface compatibility).
        /// </summary>
        public Task InvalidateCacheAsync()
        {
            // In this simple architecture, we don't cache - we ARE the source
            NotifyOrderChanged();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Get statistics (for interface compatibility).
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            lock (_lock)
            {
                return new CacheStatistics
                {
                    HitCount = 0,
                    MissCount = 0,
                    HitRate = 100, // Always "hits" since we're the source
                    LastUpdateTime = DateTime.UtcNow,
                    CachedItemCount = _orderedCharacters.Count,
                    CacheValidityPeriod = TimeSpan.Zero
                };
            }
        }

        /// <summary>
        /// Legacy provider registration - not used in clean architecture.
        /// </summary>
        public void RegisterCharacterOrderProvider(Func<Task<List<PlayOnlineCharacter>>> provider)
        {
            _ = _loggingService.LogDebugAsync("Provider registration ignored - this service IS the source of truth", "CharacterOrderingService");
        }

        /// <summary>
        /// Legacy provider unregistration - not used in clean architecture.
        /// </summary>
        public void UnregisterCharacterOrderProvider()
        {
            // Not used in clean architecture
        }

        #region Monitor Event Handlers (Health Updates)

        /// <summary>
        /// Handle new character detection - add to end of ordered list.
        /// </summary>
        private void OnCharacterDetected(object? sender, PlayOnlineCharacterEventArgs e)
        {
            if (e?.Character == null) return;
            
            bool added = false;
            
            lock (_lock)
            {
                // Check if already exists by PID
                if (_orderedCharacters.Any(c => c.ProcessId == e.Character.ProcessId))
                    return;
                
                // Add new character to end of ordered list
                _orderedCharacters.Add(e.Character);
                added = true;
                
                _ = _loggingService.LogInfoAsync($"Added {e.Character.DisplayName} to slot {_orderedCharacters.Count - 1}", "CharacterOrderingService");
            }
            
            if (added)
            {
                NotifyOrderChanged();
            }
        }

        /// <summary>
        /// Handle character health/status updates - update in-place by PID (preserves order).
        /// </summary>
        private void OnCharacterUpdated(object? sender, PlayOnlineCharacterEventArgs e)
        {
            if (e?.Character == null) return;
            
            lock (_lock)
            {
                // Find and update character by PID, preserving position
                var index = _orderedCharacters.FindIndex(c => c.ProcessId == e.Character.ProcessId);
                if (index >= 0)
                {
                    _orderedCharacters[index] = e.Character; // Update health/status in-place
                }
            }
            
            // Note: No order change notification needed for health updates
        }

        /// <summary>
        /// Handle character removal - remove from ordered list.
        /// </summary>
        private void OnCharacterRemoved(object? sender, PlayOnlineCharacterEventArgs e)
        {
            if (e?.Character == null) return;
            
            bool removed = false;
            
            lock (_lock)
            {
                var index = _orderedCharacters.FindIndex(c => c.ProcessId == e.Character.ProcessId);
                if (index >= 0)
                {
                    _orderedCharacters.RemoveAt(index);
                    removed = true;
                    
                    _ = _loggingService.LogInfoAsync($"Removed {e.Character.DisplayName} from slot {index}", "CharacterOrderingService");
                }
            }
            
            if (removed)
            {
                NotifyOrderChanged();
            }
        }

        #endregion

        /// <summary>
        /// Notify subscribers that character order has changed.
        /// </summary>
        private void NotifyOrderChanged()
        {
            CharacterCacheUpdated?.Invoke(this, new CharacterCacheUpdatedEventArgs
            {
                CharacterCount = _orderedCharacters.Count,
                RefreshTime = TimeSpan.Zero,
                UpdateTime = DateTime.UtcNow
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                // Unsubscribe from monitor events
                var monitorService = ServiceLocator.PlayOnlineMonitorService;
                if (monitorService != null)
                {
                    monitorService.CharacterDetected -= OnCharacterDetected;
                    monitorService.CharacterUpdated -= OnCharacterUpdated;
                    monitorService.CharacterRemoved -= OnCharacterRemoved;
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw during dispose
                _ = _loggingService.LogErrorAsync("Error during CharacterOrderingService disposal", ex, "CharacterOrderingService");
            }
            
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Event arguments for character order changes.
    /// </summary>
    public class CharacterCacheUpdatedEventArgs : EventArgs
    {
        public int CharacterCount { get; init; }
        public TimeSpan RefreshTime { get; init; }
        public DateTime UpdateTime { get; init; }
    }

    /// <summary>
    /// Cache statistics for monitoring.
    /// </summary>
    public class CacheStatistics
    {
        public int HitCount { get; init; }
        public int MissCount { get; init; }
        public double HitRate { get; init; }
        public DateTime LastUpdateTime { get; init; }
        public int CachedItemCount { get; init; }
        public TimeSpan CacheValidityPeriod { get; init; }
    }
}