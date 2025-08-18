using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// High-performance hotkey-to-character mapping service with pre-validation and caching.
    /// Provides sub-millisecond hotkey resolution for gaming scenarios.
    /// </summary>
    public interface IHotkeyMappingService
    {
        /// <summary>
        /// Gets a character by hotkey ID with O(1) performance.
        /// </summary>
        Task<PlayOnlineCharacter?> GetCharacterByHotkeyAsync(int hotkeyId);
        
        /// <summary>
        /// Refreshes all hotkey mappings from current settings and character data.
        /// </summary>
        Task RefreshMappingsAsync();
        
        /// <summary>
        /// Gets current mapping statistics for diagnostics.
        /// </summary>
        HotkeyMappingStatistics GetStatistics();
        
        /// <summary>
        /// Event raised when hotkey mappings are updated.
        /// </summary>
        event EventHandler<HotkeyMappingsUpdatedEventArgs>? MappingsUpdated;
    }

    /// <summary>
    /// High-performance implementation of hotkey-to-character mapping.
    /// </summary>
    public class HotkeyMappingService : IHotkeyMappingService, IDisposable
    {
        private readonly ICharacterOrderingService _characterOrdering;
        private readonly ISettingsService _settingsService;
        private readonly ILoggingService _loggingService;
        
        // **GAMING OPTIMIZATION**: O(1) hotkey lookups
        private volatile ConcurrentDictionary<int, MappedCharacter> _hotkeyMappings = new();
        private DateTime _lastMappingUpdate = DateTime.MinValue;
        private readonly SemaphoreSlim _mappingUpdateSemaphore = new(1, 1);
        private readonly Stopwatch _performanceStopwatch = Stopwatch.StartNew();
        
        // Performance counters
        private int _lookupHitCount;
        private int _lookupMissCount;
        private int _mappingRefreshCount;
        
        // Settings subscription
        private bool _disposed;
        
        public event EventHandler<HotkeyMappingsUpdatedEventArgs>? MappingsUpdated;

        public HotkeyMappingService(
            ICharacterOrderingService characterOrdering, 
            ISettingsService settingsService,
            ILoggingService loggingService)
        {
            _characterOrdering = characterOrdering ?? throw new ArgumentNullException(nameof(characterOrdering));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            
            // Subscribe to cache updates for automatic refresh
            _characterOrdering.CharacterCacheUpdated += OnCharacterCacheUpdated;
            
            _ = _loggingService.LogInfoAsync("HotkeyMappingService initialized", "HotkeyMappingService");
        }

        /// <summary>
        /// Gets a character by hotkey ID with O(1) performance.
        /// </summary>
        public async Task<PlayOnlineCharacter?> GetCharacterByHotkeyAsync(int hotkeyId)
        {
            var startTime = _performanceStopwatch.Elapsed;
            
            try
            {
                // **FAST PATH**: O(1) lookup in pre-validated mappings
                if (_hotkeyMappings.TryGetValue(hotkeyId, out var mappedCharacter))
                {
                    Interlocked.Increment(ref _lookupHitCount);
                    
                    var lookupTime = (_performanceStopwatch.Elapsed - startTime).TotalMicroseconds;
                    await _loggingService.LogDebugAsync($"Hotkey {hotkeyId} → {mappedCharacter.Character.DisplayName} ({lookupTime:F1}μs)", "HotkeyMappingService");
                    
                    return mappedCharacter.Character;
                }

                // **CACHE MISS**: Mapping might be stale, try refreshing
                Interlocked.Increment(ref _lookupMissCount);
                await _loggingService.LogDebugAsync($"Hotkey {hotkeyId} mapping miss, refreshing...", "HotkeyMappingService");
                
                // Refresh mappings and try again
                await RefreshMappingsAsync();
                
                if (_hotkeyMappings.TryGetValue(hotkeyId, out mappedCharacter))
                {
                    var totalTime = (_performanceStopwatch.Elapsed - startTime).TotalMilliseconds;
                    await _loggingService.LogDebugAsync($"Hotkey {hotkeyId} resolved after refresh → {mappedCharacter.Character.DisplayName} ({totalTime:F1}ms)", "HotkeyMappingService");
                    return mappedCharacter.Character;
                }
                
                // **FINAL FALLBACK**: Hotkey ID not found
                await _loggingService.LogWarningAsync($"Hotkey {hotkeyId} has no valid character mapping", "HotkeyMappingService");
                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error resolving hotkey {hotkeyId}", ex, "HotkeyMappingService");
                return null;
            }
        }

        /// <summary>
        /// Refreshes all hotkey mappings from current settings and character data.
        /// </summary>
        public async Task RefreshMappingsAsync()
        {
            if (_disposed) return;
            
            // **THREAD SAFETY**: Only allow one mapping refresh at a time
            if (!await _mappingUpdateSemaphore.WaitAsync(200))
            {
                await _loggingService.LogDebugAsync("Mapping refresh already in progress", "HotkeyMappingService");
                return;
            }
            
            try
            {
                var refreshStart = _performanceStopwatch.Elapsed;
                
                // Get current settings and characters
                var settings = _settingsService.LoadSettings();
                var characters = await _characterOrdering.GetOrderedCharactersAsync();
                
                var newMappings = new ConcurrentDictionary<int, MappedCharacter>();
                int validMappings = 0;
                int invalidMappings = 0;
                
                // Create mappings for all enabled shortcuts
                foreach (var shortcut in settings.CharacterSwitchShortcuts.Where(s => s.IsEnabled))
                {
                    var slotIndex = KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(shortcut.HotkeyId);
                    
                    if (slotIndex >= 0 && slotIndex < characters.Count)
                    {
                        var character = characters[slotIndex];
                        newMappings[shortcut.HotkeyId] = new MappedCharacter
                        {
                            HotkeyId = shortcut.HotkeyId,
                            SlotIndex = slotIndex,
                            Character = character,
                            ShortcutConfig = shortcut,
                            MappedAt = DateTime.UtcNow
                        };
                        validMappings++;
                    }
                    else
                    {
                        await _loggingService.LogWarningAsync($"Hotkey {shortcut.HotkeyId} slot {slotIndex} out of range (0-{characters.Count - 1})", "HotkeyMappingService");
                        invalidMappings++;
                    }
                }
                
                // **ATOMIC UPDATE**: Replace mappings atomically
                _hotkeyMappings = newMappings;
                _lastMappingUpdate = DateTime.UtcNow;
                Interlocked.Increment(ref _mappingRefreshCount);
                
                var refreshTime = (_performanceStopwatch.Elapsed - refreshStart).TotalMilliseconds;
                
                await _loggingService.LogInfoAsync($"Hotkey mappings refreshed: {validMappings} valid, {invalidMappings} invalid ({refreshTime:F1}ms)", "HotkeyMappingService");
                
                // Notify subscribers
                MappingsUpdated?.Invoke(this, new HotkeyMappingsUpdatedEventArgs
                {
                    ValidMappings = validMappings,
                    InvalidMappings = invalidMappings,
                    RefreshTime = TimeSpan.FromMilliseconds(refreshTime),
                    UpdateTime = _lastMappingUpdate
                });
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error refreshing hotkey mappings", ex, "HotkeyMappingService");
            }
            finally
            {
                _mappingUpdateSemaphore.Release();
            }
        }

        /// <summary>
        /// Gets current mapping statistics for diagnostics.
        /// </summary>
        public HotkeyMappingStatistics GetStatistics()
        {
            var totalLookups = _lookupHitCount + _lookupMissCount;
            var hitRate = totalLookups > 0 ? (_lookupHitCount / (double)totalLookups) * 100 : 0;
            
            return new HotkeyMappingStatistics
            {
                ActiveMappings = _hotkeyMappings.Count,
                LookupHitCount = _lookupHitCount,
                LookupMissCount = _lookupMissCount,
                LookupHitRate = hitRate,
                RefreshCount = _mappingRefreshCount,
                LastUpdateTime = _lastMappingUpdate
            };
        }

        /// <summary>
        /// Handles character cache updates to keep mappings synchronized.
        /// </summary>
        private void OnCharacterCacheUpdated(object? sender, CharacterCacheUpdatedEventArgs e)
        {
            // **PERFORMANCE**: Refresh mappings in background when character data changes
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshMappingsAsync();
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync("Error during automatic mapping refresh", ex, "HotkeyMappingService");
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try
            {
                _characterOrdering.CharacterCacheUpdated -= OnCharacterCacheUpdated;
                _mappingUpdateSemaphore.Dispose();
                _ = _loggingService.LogInfoAsync("HotkeyMappingService disposed", "HotkeyMappingService");
            }
            catch
            {
                // Ignore disposal errors
            }
            
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents a pre-validated hotkey-to-character mapping.
    /// </summary>
    public class MappedCharacter
    {
        public int HotkeyId { get; init; }
        public int SlotIndex { get; init; }
        public PlayOnlineCharacter Character { get; init; } = new();
        public KeyboardShortcutConfig ShortcutConfig { get; init; } = new();
        public DateTime MappedAt { get; init; }
    }

    /// <summary>
    /// Statistics for hotkey mapping performance.
    /// </summary>
    public class HotkeyMappingStatistics
    {
        public int ActiveMappings { get; init; }
        public int LookupHitCount { get; init; }
        public int LookupMissCount { get; init; }
        public double LookupHitRate { get; init; }
        public int RefreshCount { get; init; }
        public DateTime LastUpdateTime { get; init; }
    }

    /// <summary>
    /// Event arguments for hotkey mapping updates.
    /// </summary>
    public class HotkeyMappingsUpdatedEventArgs : EventArgs
    {
        public int ValidMappings { get; init; }
        public int InvalidMappings { get; init; }
        public TimeSpan RefreshTime { get; init; }
        public DateTime UpdateTime { get; init; }
    }
}