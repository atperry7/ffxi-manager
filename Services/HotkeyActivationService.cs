using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Unified service for all character activation operations.
    /// Consolidates hotkey and UI activation paths for consistency and performance.
    /// </summary>
    public interface IHotkeyActivationService
    {
        /// <summary>
        /// Activates a character by hotkey ID using the optimized pipeline.
        /// </summary>
        Task<HotkeyActivationResult> ActivateCharacterByHotkeyAsync(int hotkeyId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Activates a character directly using the optimized pipeline.
        /// Performs reverse lookup to find hotkey mapping if available.
        /// </summary>
        Task<HotkeyActivationResult> ActivateCharacterDirectAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets the hotkey ID associated with a character (reverse lookup).
        /// </summary>
        Task<int?> GetHotkeyIdForCharacterAsync(PlayOnlineCharacter character);
        
        /// <summary>
        /// Refreshes all hotkey mappings from current settings.
        /// </summary>
        Task RefreshMappingsAsync();
        
        /// <summary>
        /// Gets current performance statistics for all activation operations.
        /// </summary>
        HotkeyPerformanceStats GetPerformanceStats();
        
        /// <summary>
        /// Event raised when any character activation completes (success or failure).
        /// </summary>
        event EventHandler<HotkeyActivationResult>? CharacterActivated;
    }

    /// <summary>
    /// Result of a character activation operation with comprehensive metrics.
    /// </summary>
    public class HotkeyActivationResult
    {
        public int? HotkeyId { get; init; }
        public PlayOnlineCharacter? Character { get; init; }
        public bool Success { get; init; }
        public TimeSpan Duration { get; init; }
        public int RetryCount { get; init; }
        public string? ErrorMessage { get; init; }
        public ActivationSource Source { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        
        public static HotkeyActivationResult NotMapped(int hotkeyId) => new()
        {
            HotkeyId = hotkeyId,
            Success = false,
            ErrorMessage = "No character mapped to hotkey",
            Source = ActivationSource.Hotkey
        };
        
        public static HotkeyActivationResult Failed(PlayOnlineCharacter character, string error, ActivationSource source) => new()
        {
            Character = character,
            Success = false,
            ErrorMessage = error,
            Source = source
        };
        
        /// <summary>
        /// Converts to HotkeyActivationMetrics for performance monitoring.
        /// </summary>
        public HotkeyActivationMetrics ToMetrics() => new()
        {
            HotkeyId = HotkeyId ?? 0,
            CharacterName = Character?.DisplayName ?? "Unknown",
            Success = Success,
            TotalTimeMs = Duration.TotalMilliseconds,
            CharacterLookupTimeMs = Source == ActivationSource.Hotkey ? 1 : 0, // Hotkeys use fast lookup
            WindowActivationTimeMs = Duration.TotalMilliseconds,
            RetryCount = RetryCount,
            RetryTimeMs = RetryCount > 0 ? Duration.TotalMilliseconds * 0.3 : 0, // Estimate retry portion
            ErrorMessage = ErrorMessage,
            Timestamp = Timestamp
        };
    }

    /// <summary>
    /// Source of character activation request.
    /// </summary>
    public enum ActivationSource
    {
        Hotkey,
        UI,
        API
    }

    /// <summary>
    /// Unified implementation of character activation service.
    /// </summary>
    public class HotkeyActivationService : IHotkeyActivationService, IDisposable
    {
        private readonly IHotkeyMappingService _mappingService;
        private readonly IPlayOnlineMonitorService _monitorService;
        private readonly IHotkeyPerformanceMonitor _performanceMonitor;
        private readonly ILoggingService _loggingService;
        private readonly INotificationServiceEnhanced _notificationService;
        
        private bool _disposed;
        
        // **SPAM PREVENTION**: Track last activation times to prevent rapid-fire hotkeys
        private readonly ConcurrentDictionary<int, DateTime> _lastActivationTimes = new();
        
        public event EventHandler<HotkeyActivationResult>? CharacterActivated;

        public HotkeyActivationService(
            IHotkeyMappingService mappingService,
            IPlayOnlineMonitorService monitorService,
            IHotkeyPerformanceMonitor performanceMonitor,
            ILoggingService loggingService,
            INotificationServiceEnhanced notificationService)
        {
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            
            _ = _loggingService.LogInfoAsync("HotkeyActivationService initialized", "HotkeyActivationService");
        }

        /// <summary>
        /// Activates a character by hotkey ID using the optimized pipeline.
        /// </summary>
        public async Task<HotkeyActivationResult> ActivateCharacterByHotkeyAsync(int hotkeyId, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // **SPAM PREVENTION**: Check cooldown to prevent rapid-fire hotkey spam
                var now = DateTime.UtcNow;
                if (_lastActivationTimes.TryGetValue(hotkeyId, out var lastActivation))
                {
                    var settings = ServiceLocator.SettingsService.LoadSettings();
                    var timeSinceLastMs = (now - lastActivation).TotalMilliseconds;
                    
                    if (timeSinceLastMs < settings.HotkeySpamCooldownMs)
                    {
                        var cooldownResult = new HotkeyActivationResult
                        {
                            HotkeyId = hotkeyId,
                            Success = false,
                            Duration = stopwatch.Elapsed,
                            ErrorMessage = $"Hotkey on cooldown ({settings.HotkeySpamCooldownMs - timeSinceLastMs:F0}ms remaining)",
                            Source = ActivationSource.Hotkey
                        };
                        
                        // Don't log this as it would be spam, just return
                        return cooldownResult;
                    }
                }
                
                // **FAST PATH**: O(1) character lookup from pre-validated mappings
                var character = await _mappingService.GetCharacterByHotkeyAsync(hotkeyId);
                
                if (character == null)
                {
                    var notMappedResult = HotkeyActivationResult.NotMapped(hotkeyId);
                    await _loggingService.LogDebugAsync($"No character mapped to hotkey {hotkeyId}", "HotkeyActivationService");
                    
                    // Record metrics and fire event
                    _performanceMonitor.RecordActivation(notMappedResult.ToMetrics());
                    CharacterActivated?.Invoke(this, notMappedResult);
                    
                    return notMappedResult;
                }
                
                // **SPAM PREVENTION**: Update last activation time for successful lookup
                _lastActivationTimes.AddOrUpdate(hotkeyId, now, (key, oldValue) => now);

                // Perform activation with smart retry logic
                var result = await PerformActivationWithMetrics(character, hotkeyId, ActivationSource.Hotkey, stopwatch, cancellationToken);
                
                // Show toast notification for activation result
                await ShowActivationToastAsync(result);
                
                await _loggingService.LogInfoAsync($"Hotkey {hotkeyId} → {character.DisplayName}: {(result.Success ? "✓" : "✗")} ({result.Duration.TotalMilliseconds:F0}ms)", "HotkeyActivationService");
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorResult = new HotkeyActivationResult
                {
                    HotkeyId = hotkeyId,
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = ex.Message,
                    Source = ActivationSource.Hotkey
                };
                
                await _loggingService.LogErrorAsync($"Error activating hotkey {hotkeyId}", ex, "HotkeyActivationService");
                
                _performanceMonitor.RecordActivation(errorResult.ToMetrics());
                CharacterActivated?.Invoke(this, errorResult);
                
                return errorResult;
            }
        }

        /// <summary>
        /// Activates a character directly using the optimized pipeline.
        /// </summary>
        public async Task<HotkeyActivationResult> ActivateCharacterDirectAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // **OPTIMIZATION**: Check if character has a hotkey mapping for unified metrics
                var hotkeyId = await GetHotkeyIdForCharacterAsync(character);
                
                if (hotkeyId.HasValue)
                {
                    // Use hotkey pipeline for consistency
                    return await ActivateCharacterByHotkeyAsync(hotkeyId.Value, cancellationToken);
                }
                
                // **FALLBACK**: Direct activation for characters without hotkey mappings
                var result = await PerformActivationWithMetrics(character, null, ActivationSource.UI, stopwatch, cancellationToken);
                
                // Show toast for UI activation (less prominent)
                if (!result.Success)
                {
                    await ShowActivationToastAsync(result);
                }
                
                await _loggingService.LogInfoAsync($"Direct activation: {character.DisplayName}: {(result.Success ? "✓" : "✗")} ({result.Duration.TotalMilliseconds:F0}ms)", "HotkeyActivationService");
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorResult = new HotkeyActivationResult
                {
                    Character = character,
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = ex.Message,
                    Source = ActivationSource.UI
                };
                
                await _loggingService.LogErrorAsync($"Error activating character {character.DisplayName}", ex, "HotkeyActivationService");
                
                _performanceMonitor.RecordActivation(errorResult.ToMetrics());
                CharacterActivated?.Invoke(this, errorResult);
                
                return errorResult;
            }
        }

        /// <summary>
        /// Gets the hotkey ID associated with a character (reverse lookup).
        /// </summary>
        public async Task<int?> GetHotkeyIdForCharacterAsync(PlayOnlineCharacter character)
        {
            if (character == null) return null;
            
            try
            {
                var stats = _mappingService.GetStatistics();
                // This is a simplified reverse lookup - in a full implementation,
                // we'd add a reverse mapping cache to HotkeyMappingService
                
                // For now, we'll use the character's position in the ordered list
                var characterOrdering = ServiceLocator.CharacterOrderingService;
                var characters = await characterOrdering.GetOrderedCharactersAsync();
                
                for (int i = 0; i < characters.Count; i++)
                {
                    if (characters[i].ProcessId == character.ProcessId)
                    {
                        // Convert slot index to hotkey ID using the same logic as hotkey registration
                        var settings = ServiceLocator.SettingsService.LoadSettings();
                        var hotkeyMapping = settings.CharacterSwitchShortcuts.FirstOrDefault(s => 
                            Models.Settings.KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(s.HotkeyId) == i && s.IsEnabled);
                        
                        return hotkeyMapping?.HotkeyId;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error in reverse hotkey lookup for {character.DisplayName}", ex, "HotkeyActivationService");
                return null;
            }
        }

        /// <summary>
        /// Refreshes all hotkey mappings from current settings.
        /// </summary>
        public async Task RefreshMappingsAsync()
        {
            try
            {
                await _mappingService.RefreshMappingsAsync();
                await _loggingService.LogInfoAsync("Hotkey mappings refreshed", "HotkeyActivationService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error refreshing hotkey mappings", ex, "HotkeyActivationService");
            }
        }

        /// <summary>
        /// Gets current performance statistics for all activation operations.
        /// </summary>
        public HotkeyPerformanceStats GetPerformanceStats()
        {
            return _performanceMonitor.GetStatistics();
        }

        /// <summary>
        /// Performs character activation with comprehensive metrics collection.
        /// </summary>
        private async Task<HotkeyActivationResult> PerformActivationWithMetrics(
            PlayOnlineCharacter character, 
            int? hotkeyId, 
            ActivationSource source,
            Stopwatch totalStopwatch, 
            CancellationToken cancellationToken)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 25;
            
            int retryCount = 0;
            Exception? lastException = null;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var success = await _monitorService.ActivateCharacterWindowAsync(character, cancellationToken);
                    
                    totalStopwatch.Stop();
                    
                    // **NEW FEATURE**: Track last activation time for successful activations
                    if (success)
                    {
                        character.MarkAsActivated();
                    }
                    
                    var result = new HotkeyActivationResult
                    {
                        HotkeyId = hotkeyId,
                        Character = character,
                        Success = success,
                        Duration = totalStopwatch.Elapsed,
                        RetryCount = retryCount,
                        Source = source
                    };
                    
                    // Record metrics and fire event
                    _performanceMonitor.RecordActivation(result.ToMetrics());
                    CharacterActivated?.Invoke(this, result);
                    
                    return result;
                }
                catch (ArgumentException ex)
                {
                    // Invalid window handle - don't retry
                    await _loggingService.LogWarningAsync($"Invalid window handle for {character.DisplayName} - not retrying", "HotkeyActivationService");
                    lastException = ex;
                    break;
                }
                catch (System.ComponentModel.Win32Exception win32Ex) when (win32Ex.NativeErrorCode == 5)
                {
                    // Access denied - don't retry
                    await _loggingService.LogWarningAsync($"Access denied activating {character.DisplayName} - not retrying", "HotkeyActivationService");
                    lastException = win32Ex;
                    break;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    // Transient error - retry with exponential backoff
                    retryCount++;
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                    await _loggingService.LogDebugAsync($"Activation attempt {attempt} failed for {character.DisplayName}, retrying in {delay}ms: {ex.Message}", "HotkeyActivationService");
                    await Task.Delay(delay, cancellationToken);
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    // Final attempt failed
                    lastException = ex;
                    retryCount++;
                    break;
                }
            }
            
            // All attempts failed
            totalStopwatch.Stop();
            
            var failedResult = new HotkeyActivationResult
            {
                HotkeyId = hotkeyId,
                Character = character,
                Success = false,
                Duration = totalStopwatch.Elapsed,
                RetryCount = retryCount,
                ErrorMessage = lastException?.Message ?? "Activation failed after all retries",
                Source = source
            };
            
            _performanceMonitor.RecordActivation(failedResult.ToMetrics());
            CharacterActivated?.Invoke(this, failedResult);
            
            return failedResult;
        }

        /// <summary>
        /// Shows appropriate toast notification for activation result
        /// </summary>
        private async Task ShowActivationToastAsync(HotkeyActivationResult result)
        {
            try
            {
                var characterName = result.Character?.DisplayName ?? "Character";
                
                if (result.Success)
                {
                    // Success toast with performance feedback
                    var durationMs = result.Duration.TotalMilliseconds;
                    var message = $"{characterName} activated ({durationMs:F0}ms)";
                    
                    // Color-code by performance
                    var notificationType = durationMs switch
                    {
                        < 25 => NotificationType.Success, // Excellent performance
                        < 100 => NotificationType.Info,   // Good performance  
                        _ => NotificationType.Warning     // Slow but working
                    };
                    
                    // Only show success toasts for slow activations or errors
                    if (durationMs > 50 || result.Source == ActivationSource.Hotkey)
                    {
                        await _notificationService.ShowToastAsync(message, notificationType);
                    }
                }
                else
                {
                    // Error toast with helpful message
                    var errorMessage = result.ErrorMessage ?? "Activation failed";
                    await _notificationService.ShowToastAsync($"{characterName}: {errorMessage}", NotificationType.Error);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error showing activation toast: {ex.Message}", ex, "HotkeyActivationService");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            CharacterActivated = null;
            _ = _loggingService?.LogInfoAsync("HotkeyActivationService disposed", "HotkeyActivationService");
            
            GC.SuppressFinalize(this);
        }
    }
}

