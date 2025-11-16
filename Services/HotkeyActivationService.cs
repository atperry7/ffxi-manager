using FFXIManager.Models;
using System.Collections.Concurrent;
using System.Diagnostics;

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
        /// Cycles to the next active character.
        /// </summary>
        Task<HotkeyActivationResult> CycleToNextCharacterAsync(CancellationToken cancellationToken = default);

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
        private readonly ISettingsService _settingsService;
        private readonly ICharacterOrderingService _characterOrderingService;

        private bool _disposed;

        // **FIRE AND FORGET - LAST WINS**: Track and cancel in-flight activations
        private CancellationTokenSource? _currentActivationCts;
        private readonly object _activationLock = new();
        private DateTime _lastActivationStart = DateTime.MinValue;
        private const int MIN_ACTIVATION_INTERVAL_MS = 150; // Minimum time between activation starts (Windows API stability)

        // **CYCLE TRACKING**: Track current position for character cycling with cancellation support
        private CancellationTokenSource? _currentCycleCts;
        private int _currentCycleIndex = -1;
        private DateTime _lastCycleTime = DateTime.MinValue;
        private DateTime _lastCycleActivationStart = DateTime.MinValue;
        private readonly object _cycleLock = new();

        // **CYCLE CONSTANTS**: Configuration for cycle behavior
        private const int CYCLE_TIMEOUT_SECONDS = 30;
        public const int CycleHotkeyId = 999;

        public event EventHandler<HotkeyActivationResult>? CharacterActivated;

        public HotkeyActivationService(
            IHotkeyMappingService mappingService,
            IPlayOnlineMonitorService monitorService,
            IHotkeyPerformanceMonitor performanceMonitor,
            ILoggingService loggingService,
            INotificationServiceEnhanced notificationService,
            ISettingsService settingsService,
            ICharacterOrderingService characterOrderingService)
        {
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _monitorService = monitorService ?? throw new ArgumentNullException(nameof(monitorService));
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _characterOrderingService = characterOrderingService ?? throw new ArgumentNullException(nameof(characterOrderingService));

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
                // **FIRE AND FORGET - LAST WINS**: Cancel any in-flight activation and start new one
                CancellationTokenSource linkedCts;
                int waitMs = 0;

                lock (_activationLock)
                {
                    // Cancel previous activation if any
                    _currentActivationCts?.Cancel();
                    _currentActivationCts?.Dispose();

                    // Calculate minimum interval wait time (outside lock for await)
                    var now = DateTime.UtcNow;
                    var timeSinceLastStart = (now - _lastActivationStart).TotalMilliseconds;
                    if (timeSinceLastStart < MIN_ACTIVATION_INTERVAL_MS)
                    {
                        waitMs = (int)(MIN_ACTIVATION_INTERVAL_MS - timeSinceLastStart);
                    }
                    _lastActivationStart = DateTime.UtcNow;

                    // Create new cancellation token for this activation
                    _currentActivationCts = new CancellationTokenSource();
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_currentActivationCts.Token, cancellationToken);
                }

                // Enforce minimum interval outside lock
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, cancellationToken);
                }

                // **FAST PATH**: O(1) character lookup from pre-validated mappings
                var character = await _mappingService.GetCharacterByHotkeyAsync(hotkeyId);

                if (character == null)
                {
                    var notMappedResult = HotkeyActivationResult.NotMapped(hotkeyId);
                    await _loggingService.LogDebugAsync("No character mapped to hotkey {HotkeyId}", "HotkeyActivationService", hotkeyId);

                    // Record metrics and fire event
                    _performanceMonitor.RecordActivation(notMappedResult.ToMetrics());
                    CharacterActivated?.Invoke(this, notMappedResult);

                    return notMappedResult;
                }

                // Perform activation with smart retry logic using linked cancellation token
                var result = await PerformActivationWithMetrics(character, hotkeyId, ActivationSource.Hotkey, stopwatch, linkedCts.Token);

                // Show toast notification for activation result
                await ShowActivationToastAsync(result);

                await _loggingService.LogInfoAsync("Hotkey {HotkeyId} → {CharacterName}: {Result} ({DurationMs:F0}ms)", "HotkeyActivationService", hotkeyId, character.DisplayName, result.Success ? "✓" : "✗", result.Duration.TotalMilliseconds);

                return result;
            }
            catch (OperationCanceledException)
            {
                // Activation was cancelled (replaced by newer activation) - this is normal for "last wins"
                stopwatch.Stop();
                var cancelledResult = new HotkeyActivationResult
                {
                    HotkeyId = hotkeyId,
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Cancelled by newer activation",
                    Source = ActivationSource.Hotkey
                };

                await _loggingService.LogDebugAsync("Hotkey {HotkeyId} activation cancelled (replaced by newer activation)", "HotkeyActivationService", hotkeyId);

                // Don't record cancelled activations as failures
                return cancelledResult;
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

                await _loggingService.LogErrorAsync("Error activating hotkey {HotkeyId}", "HotkeyActivationService", ex, hotkeyId);

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

                await _loggingService.LogInfoAsync("Direct activation: {CharacterName}: {Result} ({DurationMs:F0}ms)", "HotkeyActivationService", character.DisplayName, result.Success ? "✓" : "✗", result.Duration.TotalMilliseconds);

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

                await _loggingService.LogErrorAsync("Error activating character {CharacterName}", "HotkeyActivationService", ex, character.DisplayName);

                _performanceMonitor.RecordActivation(errorResult.ToMetrics());
                CharacterActivated?.Invoke(this, errorResult);

                return errorResult;
            }
        }

        /// <summary>
        /// Cycles to the next active character.
        /// **FIRE AND FORGET - LAST WINS**: Cancels any in-flight cycle activation
        /// </summary>
        public async Task<HotkeyActivationResult> CycleToNextCharacterAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // **FIRE AND FORGET - LAST WINS**: Cancel any in-flight cycle activation
                CancellationTokenSource linkedCts;
                int waitMs = 0;

                lock (_cycleLock)
                {
                    // Cancel previous cycle activation if any
                    _currentCycleCts?.Cancel();
                    _currentCycleCts?.Dispose();

                    // Calculate minimum interval wait time
                    var now = DateTime.UtcNow;
                    var timeSinceLastStart = (now - _lastCycleActivationStart).TotalMilliseconds;
                    if (timeSinceLastStart < MIN_ACTIVATION_INTERVAL_MS)
                    {
                        waitMs = (int)(MIN_ACTIVATION_INTERVAL_MS - timeSinceLastStart);
                    }
                    _lastCycleActivationStart = DateTime.UtcNow;

                    // Create new cancellation token for this cycle activation
                    _currentCycleCts = new CancellationTokenSource();
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_currentCycleCts.Token, cancellationToken);
                }

                // Enforce minimum interval outside lock
                if (waitMs > 0)
                {
                    await Task.Delay(waitMs, linkedCts.Token);
                }

                // Get characters in user-defined order
                var characterOrdering = _characterOrderingService;
                var orderedCharacters = await characterOrdering.GetOrderedCharactersAsync();

                if (orderedCharacters == null || orderedCharacters.Count == 0)
                {
                    var noCharactersResult = new HotkeyActivationResult
                    {
                        Success = false,
                        Duration = stopwatch.Elapsed,
                        ErrorMessage = "No active characters to cycle through",
                        Source = ActivationSource.Hotkey
                    };

                    await _loggingService.LogDebugAsync("Cycle hotkey pressed but no active characters found", "HotkeyActivationService");
                    await _notificationService.ShowToastAsync("No active characters to cycle", NotificationType.Warning);
                    return noCharactersResult;
                }

                if (orderedCharacters.Count == 1)
                {
                    // Only one character, just activate it
                    return await ActivateCharacterDirectAsync(orderedCharacters[0], linkedCts.Token);
                }

                // Determine next character to cycle to
                PlayOnlineCharacter targetCharacter;
                bool cycleReset = false;
                bool isFirstUse = (_lastCycleTime == DateTime.MinValue);
                double timeSinceLastCycle;
                int cycleIndex;
                int totalCount = orderedCharacters.Count;

                lock (_cycleLock)
                {
                    // Check if we need to reset the cycle (timeout or first use)
                    timeSinceLastCycle = isFirstUse ? 0 : (DateTime.UtcNow - _lastCycleTime).TotalSeconds;

                    if (_currentCycleIndex == -1 || (!isFirstUse && timeSinceLastCycle > CYCLE_TIMEOUT_SECONDS))
                    {
                        // Reset cycle - find the currently active character to start from
                        var currentActiveIndex = -1;
                        PlayOnlineCharacter? lastActivatedChar = null;
                        DateTime mostRecentActivation = DateTime.MinValue;

                        for (int i = 0; i < orderedCharacters.Count; i++)
                        {
                            var char_ = orderedCharacters[i];
                            if (char_.LastActivated.HasValue && char_.LastActivated.Value > mostRecentActivation)
                            {
                                mostRecentActivation = char_.LastActivated.Value;
                                lastActivatedChar = char_;
                                currentActiveIndex = i;
                            }
                        }

                        // If we found a last activated character, start from the next one
                        // Otherwise start from the beginning
                        if (currentActiveIndex >= 0)
                        {
                            _currentCycleIndex = (currentActiveIndex + 1) % orderedCharacters.Count;
                        }
                        else
                        {
                            _currentCycleIndex = 0;
                        }

                        cycleReset = true;
                    }
                    else
                    {
                        // Continue cycling - move to next character
                        _currentCycleIndex = (_currentCycleIndex + 1) % orderedCharacters.Count;
                    }

                    _lastCycleTime = DateTime.UtcNow;
                    cycleIndex = _currentCycleIndex;
                    targetCharacter = orderedCharacters[_currentCycleIndex];
                }

                // **FIRE AND FORGET**: Perform activation with cancellation support
                var result = await ActivateCharacterDirectAsync(targetCharacter, linkedCts.Token);

                // Show which character we cycled to
                var positionText = $"Character {cycleIndex + 1}/{totalCount}: {targetCharacter.DisplayName}";

                // Only show [Reset] if it was an actual timeout reset, not first use
                if (cycleReset && !isFirstUse && timeSinceLastCycle > CYCLE_TIMEOUT_SECONDS)
                {
                    positionText = $"[Reset] {positionText}";
                    _ = _notificationService.ShowToastAsync("Cycle reset - timeout exceeded", NotificationType.Info);
                }

                await _notificationService.ShowToastAsync(positionText, NotificationType.Success);
                await _loggingService.LogInfoAsync("Cycled to {PositionText}", "HotkeyActivationService", positionText);

                stopwatch.Stop();
                return new HotkeyActivationResult
                {
                    Character = targetCharacter,
                    Success = result.Success,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = result.ErrorMessage,
                    Source = ActivationSource.Hotkey
                };
            }
            catch (OperationCanceledException)
            {
                // Cycle activation was cancelled (replaced by newer cycle) - this is normal for "last wins"
                stopwatch.Stop();
                var cancelledResult = new HotkeyActivationResult
                {
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Cancelled by newer cycle activation",
                    Source = ActivationSource.Hotkey
                };

                await _loggingService.LogDebugAsync("Cycle activation cancelled (replaced by newer cycle)", "HotkeyActivationService");

                // Don't record cancelled activations as failures
                return cancelledResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorResult = new HotkeyActivationResult
                {
                    Success = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = $"Error cycling characters: {ex.Message}",
                    Source = ActivationSource.Hotkey
                };

                await _loggingService.LogErrorAsync("Error cycling to next character", ex, "HotkeyActivationService");
                await _notificationService.ShowToastAsync($"Cycle error: {ex.Message}", NotificationType.Error);

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
                var characterOrdering = _characterOrderingService;
                var characters = await characterOrdering.GetOrderedCharactersAsync();

                for (int i = 0; i < characters.Count; i++)
                {
                    if (characters[i].ProcessId == character.ProcessId)
                    {
                        // Convert slot index to hotkey ID using the same logic as hotkey registration
                        var settings = _settingsService.LoadSettings();
                        var hotkeyMapping = settings.CharacterSwitchShortcuts.FirstOrDefault(s =>
                            Models.Settings.KeyboardShortcutConfig.GetSlotIndexFromHotkeyId(s.HotkeyId) == i && s.IsEnabled);

                        return hotkeyMapping?.HotkeyId;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error in reverse hotkey lookup for {CharacterName}", ex, "HotkeyActivationService", character.DisplayName);
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
        /// **OPTIMIZED**: Reduced retries (2 max) and faster retry delays (10ms base) for gaming performance
        /// </summary>
        private async Task<HotkeyActivationResult> PerformActivationWithMetrics(
            PlayOnlineCharacter character,
            int? hotkeyId,
            ActivationSource source,
            Stopwatch totalStopwatch,
            CancellationToken cancellationToken)
        {
            const int maxRetries = 2;     // Reduced from 3 for faster response
            const int baseDelayMs = 10;   // Reduced from 25ms for faster retries

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
                    await _loggingService.LogWarningAsync("Invalid window handle for {CharacterName} - not retrying", "HotkeyActivationService", character.DisplayName);
                    lastException = ex;
                    break;
                }
                catch (System.ComponentModel.Win32Exception win32Ex) when (win32Ex.NativeErrorCode == 5)
                {
                    // Access denied - don't retry
                    await _loggingService.LogWarningAsync("Access denied activating {CharacterName} - not retrying", "HotkeyActivationService", character.DisplayName);
                    lastException = win32Ex;
                    break;
                }
                catch (Exception ex) when (attempt < maxRetries)
                {
                    // Transient error - retry with exponential backoff
                    retryCount++;
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                    await _loggingService.LogDebugAsync("Activation attempt {Attempt} failed for {CharacterName}, retrying in {DelayMs}ms: {ErrorMessage}", "HotkeyActivationService", attempt, character.DisplayName, delay, ex.Message);
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
                await _loggingService.LogErrorAsync("Error showing activation toast: {ErrorMessage}", ex, "HotkeyActivationService", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel and dispose any in-flight activation
            lock (_activationLock)
            {
                _currentActivationCts?.Cancel();
                _currentActivationCts?.Dispose();
                _currentActivationCts = null;
            }

            // Cancel and dispose any in-flight cycle activation
            lock (_cycleLock)
            {
                _currentCycleCts?.Cancel();
                _currentCycleCts?.Dispose();
                _currentCycleCts = null;
            }

            CharacterActivated = null;
            _ = _loggingService?.LogInfoAsync("HotkeyActivationService disposed", "HotkeyActivationService");

            GC.SuppressFinalize(this);
        }
    }
}


