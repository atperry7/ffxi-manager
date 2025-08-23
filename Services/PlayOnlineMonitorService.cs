using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Simplified PlayOnline monitoring service using UnifiedMonitoringService
    /// </summary>
    public class PlayOnlineMonitorService : IPlayOnlineMonitorService, IDisposable
    {
        private readonly IUnifiedMonitoringService _unifiedMonitoring;
        private readonly ILoggingService _logging;
        private readonly IUiDispatcher _uiDispatcher;

        private Guid _monitorId;
        private bool _isMonitoring;
        private bool _disposed;

        // **EMERGENCY PROTECTION**: Rapid switching protection with character-aware logic and system safeguards
        private readonly SemaphoreSlim _activationSemaphore = new(1, 1);
        private readonly Timer _activationDebounceTimer;
        private CancellationTokenSource _currentActivationCts = new();
        private PlayOnlineCharacter? _pendingActivation;

        // **POL-SPECIFIC**: Timer to check POL processes for title changes (since Win32 events don't work)
        private readonly Timer _polTitleCheckTimer;
        private readonly Dictionary<IntPtr, string> _lastPolTitles = new();
        private DateTime _lastActivationAttempt = DateTime.MinValue;
        private int _lastActivatedCharacterSlotIndex = -1; // Track last activated character slot for smart debouncing
        
        // **EMERGENCY CIRCUIT BREAKER**: Prevents system lockup during excessive switching
        private static int _globalActivationCount;
        private static DateTime _lastGlobalReset = DateTime.UtcNow;
        private static volatile bool _emergencyThrottleActive;
        private const int MAX_ACTIVATIONS_PER_SECOND = 20;
        private const int EMERGENCY_THROTTLE_DURATION_MS = 3000;

        // Gaming-optimized timing values (loaded from settings) with emergency limits
        private int _activationDebounceMs = 50;    // Fast debounce for gaming
        private int _minActivationIntervalMs = 100; // Only applies to same character
        private int _activationTimeoutMs = 1500;   // **REDUCED** timeout to prevent deadlocks (was 3000)

        // **GAMING OPTIMIZATION**: Predictive character window caching
        private readonly ConcurrentDictionary<int, CachedCharacterInfo> _characterCache = new();
        private readonly object _cacheLock = new object();

        /// <summary>
        /// Cached character information for fast window handle lookups
        /// </summary>
        private sealed class CachedCharacterInfo
        {
            public int ProcessId { get; set; }
            public IntPtr WindowHandle { get; set; }
            public string CharacterName { get; set; } = string.Empty;
            public string WindowTitle { get; set; } = string.Empty;
            public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
            public bool IsValid { get; set; } = true;

            public PlayOnlineCharacter ToCharacter()
            {
                return new PlayOnlineCharacter
                {
                    ProcessId = ProcessId,
                    WindowHandle = WindowHandle,
                    CharacterName = CharacterName,
                    WindowTitle = WindowTitle,
                    LastSeen = LastUpdated,
                    // LastActivated will be set by HotkeyActivationService when character is activated
                };
            }
        }

        // Events
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterDetected;
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterUpdated;
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterRemoved;

        // Target process names for PlayOnline/FFXI
        private readonly string[] _targetProcessNames = { "pol", "ffxi", "PlayOnlineViewer" };

        // Injected dependency for better testability (fallback to ServiceLocator if not provided)
        private readonly IProcessManagementService? _processManagement;

        public PlayOnlineMonitorService(
            IUnifiedMonitoringService unifiedMonitoring,
            ILoggingService logging,
            IUiDispatcher uiDispatcher,
            IProcessManagementService? processManagement = null)
        {
            _unifiedMonitoring = unifiedMonitoring ?? throw new ArgumentNullException(nameof(unifiedMonitoring));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _processManagement = processManagement; // null = use ServiceLocator fallback

            // Load gaming-optimized settings
            LoadPerformanceSettings();

            // Initialize activation debounce timer (initially disabled)
            _activationDebounceTimer = new Timer(DebouncedActivationCallback, null, Timeout.Infinite, Timeout.Infinite);
            
            // **POL-SPECIFIC**: Initialize POL title checking timer (every 10 seconds - reduced to avoid Windows protection)
            _polTitleCheckTimer = new Timer(CheckPolTitlesCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

            // Register our monitoring profile
            RegisterMonitoringProfile();

            // Subscribe to unified monitoring events
            _unifiedMonitoring.ProcessDetected += OnProcessDetected;
            _unifiedMonitoring.ProcessUpdated += OnProcessUpdated;
            _unifiedMonitoring.ProcessRemoved += OnProcessRemoved;
        }

        private void RegisterMonitoringProfile()
        {
            var profile = new MonitoringProfile
            {
                Name = "PlayOnline Character Monitor",
                ProcessNames = _targetProcessNames,
                TrackWindows = true,           // We need windows
                TrackWindowTitles = true,       // We need titles for character names
                IncludeProcessPath = false      // Don't need full paths
            };

            _monitorId = _unifiedMonitoring.RegisterMonitor(profile);
            _ = _logging.LogInfoAsync($"Registered PlayOnline monitoring profile (ID: {_monitorId})", "PlayOnlineMonitorService");
        }

        public bool IsMonitoring => _isMonitoring;

        public async Task<List<PlayOnlineCharacter>> GetCharactersAsync()
        {
            var processes = await _unifiedMonitoring.GetProcessesAsync(_monitorId);
            var characters = new List<PlayOnlineCharacter>();

            foreach (var process in processes)
            {
                // Convert each window to a character
                foreach (var window in process.Windows)
                {
                    characters.Add(ConvertToCharacter(process, window));
                }

                // If no windows, create one character for the process
                if (process.Windows.Count == 0)
                {
                    characters.Add(ConvertToCharacter(process, null));
                }
            }

            return characters;
        }
        
        /// <summary>
        /// **EMERGENCY PROTECTION**: Checks and manages global activation rate limiting
        /// </summary>
        private bool IsEmergencyThrottleActive()
        {
            var now = DateTime.UtcNow;
            
            // Reset counter every second
            if ((now - _lastGlobalReset).TotalMilliseconds >= 1000)
            {
                Interlocked.Exchange(ref _globalActivationCount, 0);
                _lastGlobalReset = now;
                _emergencyThrottleActive = false;
            }
            
            // Check if we're over the limit
            var currentCount = Interlocked.Increment(ref _globalActivationCount);
            
            if (currentCount > MAX_ACTIVATIONS_PER_SECOND && !_emergencyThrottleActive)
            {
                _emergencyThrottleActive = true;
                
                // Schedule throttle reset
                Task.Delay(EMERGENCY_THROTTLE_DURATION_MS).ContinueWith(_ => 
                {
                    _emergencyThrottleActive = false;
                    Interlocked.Exchange(ref _globalActivationCount, 0);
                    _ = _logging.LogInfoAsync("Emergency throttle deactivated - normal switching resumed", "PlayOnlineMonitorService");
                });
                
                _ = _logging.LogWarningAsync($"**EMERGENCY THROTTLE ACTIVATED**: {currentCount} activations/sec exceeded limit ({MAX_ACTIVATIONS_PER_SECOND})", "PlayOnlineMonitorService");
                return true;
            }
            
            return _emergencyThrottleActive;
        }

        public async Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            // **PERFORMANCE**: Start timing immediately
            var activationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // **EMERGENCY CIRCUIT BREAKER**: Check global activation throttle
            if (IsEmergencyThrottleActive())
            {
                await _logging.LogWarningAsync("Character activation blocked: emergency throttle active", "PlayOnlineMonitorService");
                return false;
            }
            
            if (character == null || character.WindowHandle == IntPtr.Zero)
            {
                await _logging.LogWarningAsync("Cannot activate character: invalid window handle", "PlayOnlineMonitorService");
                return false;
            }

            // **CRITICAL FIX**: Validate window handle is still valid
            var processUtility = ServiceLocator.ProcessUtilityService;
            if (!processUtility.IsWindowValid(character.WindowHandle))
            {
                await _logging.LogWarningAsync($"Cannot activate {character.DisplayName}: window handle 0x{character.WindowHandle.ToInt64():X} is no longer valid (process may have updated window title)", "PlayOnlineMonitorService");
                
                // Try to refresh character data to get updated window handle
                await RefreshCharactersAsync();
                return false;
            }

            // **PERFORMANCE OPTIMIZATION**: Skip rate limiting for fast switching
            var currentSlotIndex = GetCharacterSlotIndexFast(character);
            var timeSinceLastAttempt = DateTime.UtcNow - _lastActivationAttempt;

            // Only apply rate limiting if switching to the SAME character within 50ms
            bool isSameCharacter = (currentSlotIndex == _lastActivatedCharacterSlotIndex && currentSlotIndex != -1);
            bool tooFrequent = timeSinceLastAttempt.TotalMilliseconds < 50; // Reduced from _minActivationIntervalMs

            if (isSameCharacter && tooFrequent)
            {
                // Skip logging for performance
                RequestDebouncedActivation(character);
                return true;
            }
            
            // **PERFORMANCE**: Log after decision to avoid delays
            if (!isSameCharacter && activationStopwatch.ElapsedMilliseconds > 10)
            {
                System.Diagnostics.Debug.WriteLine($"[PERF WARNING] Pre-activation took {activationStopwatch.ElapsedMilliseconds}ms");
            }
            
            _lastActivatedCharacterSlotIndex = currentSlotIndex;
            var result = await PerformImmediateActivationAsync(character, cancellationToken);
            
            // **PERFORMANCE**: Log total time
            activationStopwatch.Stop();
            if (activationStopwatch.ElapsedMilliseconds > 100)
            {
                await _logging.LogWarningAsync($"[PERFORMANCE] Character activation took {activationStopwatch.ElapsedMilliseconds}ms for {character.DisplayName}", "PlayOnlineMonitorService");
            }
            
            return result;
        }

        /// <summary>
        /// Requests a debounced character activation to prevent rapid-fire switching
        /// </summary>
        private void RequestDebouncedActivation(PlayOnlineCharacter character)
        {
            // Store the character for debounced activation
            _pendingActivation = character;

            // Cancel previous activation if still pending
            _currentActivationCts.Cancel();
            _currentActivationCts.Dispose();
            _currentActivationCts = new CancellationTokenSource();

            // Reset debounce timer
            _activationDebounceTimer.Change(_activationDebounceMs, Timeout.Infinite);

            _ = _logging.LogDebugAsync($"Queued debounced activation for {character.DisplayName} (debounce: {_activationDebounceMs}ms)", "PlayOnlineMonitorService");
        }

        /// <summary>
        /// Timer callback for debounced activation
        /// </summary>
        private void DebouncedActivationCallback(object? state)
        {
            // **FIXED**: Convert async void to fire-and-forget Task to prevent crashes
            _ = DebouncedActivationCallbackAsync();
        }

        /// <summary>
        /// Async implementation of debounced activation with proper exception handling
        /// </summary>
        private async Task DebouncedActivationCallbackAsync()
        {
            var characterToActivate = _pendingActivation;
            if (characterToActivate == null || _disposed) return;

            try
            {
                await PerformImmediateActivationAsync(characterToActivate, _currentActivationCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected cancellation - ignore
            }
            catch (Exception ex)
            {
                // **IMPROVED**: Use safe logging that won't throw
                await SafeLogErrorAsync($"Error in debounced activation for {characterToActivate.DisplayName}", ex);
            }
        }

        /// <summary>
        /// Performs the actual window activation with proper synchronization
        /// </summary>
        private async Task<bool> PerformImmediateActivationAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            // **PERFORMANCE**: Reduce semaphore wait to 100ms
            if (!await _activationSemaphore.WaitAsync(100, cancellationToken))
            {
                // Don't log for performance
                return false;
            }

            try
            {
                _lastActivationAttempt = DateTime.UtcNow;

                // **PERFORMANCE**: Use shorter timeout for faster response
                var fastTimeoutMs = Math.Min(_activationTimeoutMs, 500); // Cap at 500ms
                
                // **PERFORMANCE**: Skip info logging to reduce overhead
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION] Starting for {character.DisplayName}");

                // **ENHANCED**: Use ProcessUtilityService with detailed failure detection
                var processUtility = ServiceLocator.ProcessUtilityService;
                var result = await processUtility.ActivateWindowEnhancedAsync(character.WindowHandle, fastTimeoutMs);

                if (result.Success)
                {
                    // Only log if it took too long
                    if (result.Duration.TotalMilliseconds > 100)
                    {
                        await _logging.LogInfoAsync($"Successfully activated {character.DisplayName} in {result.Duration.TotalMilliseconds:F0}ms (attempts: {result.AttemptsRequired})", "PlayOnlineMonitorService");
                    }
                }
                else
                {
                    // **IMPROVED DIAGNOSTICS**: Log specific failure reason
                    var diagnostic = result.FailureReason switch
                    {
                        WindowActivationFailureReason.WindowHung => "Window is not responding - game may be frozen",
                        WindowActivationFailureReason.ElevationMismatch => "UAC elevation mismatch - try running as administrator",
                        WindowActivationFailureReason.FullScreenBlocking => "Another application is in fullscreen mode",
                        WindowActivationFailureReason.FocusStealingPrevention => "Windows focus stealing prevention is blocking activation",
                        WindowActivationFailureReason.InvalidHandle => "Window handle is no longer valid",
                        WindowActivationFailureReason.WindowDestroyed => "Window has been destroyed",
                        WindowActivationFailureReason.AccessDenied => "Access denied to window",
                        WindowActivationFailureReason.Timeout => $"Activation timed out after {fastTimeoutMs}ms",
                        _ => result.DiagnosticInfo ?? "Unknown failure reason"
                    };
                    
                    await _logging.LogWarningAsync($"Failed to activate window for {character.DisplayName}: {diagnostic}", "PlayOnlineMonitorService");
                }

                return result.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Skip logging for performance
                return false;
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error activating window for {character.DisplayName}", ex, "PlayOnlineMonitorService");
                return false;
            }
            finally
            {
                _activationSemaphore.Release();
            }
        }

        public async Task RefreshCharactersAsync()
        {
            // Simply get the latest data - the UnifiedMonitoringService handles all updates
            var characters = await GetCharactersAsync();

            await _logging.LogInfoAsync($"Refreshed character data: {characters.Count} character(s) found", "PlayOnlineMonitorService");
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;

            // Start the unified monitoring if not already started
            if (!_unifiedMonitoring.IsMonitoring)
            {
                _unifiedMonitoring.StartMonitoring();
            }

            _ = _logging.LogInfoAsync("Started PlayOnline character monitoring", "PlayOnlineMonitorService");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;

            // We don't stop the unified monitoring as other services might be using it
            // The unified monitoring will handle its own lifecycle

            _ = _logging.LogInfoAsync("Stopped PlayOnline character monitoring", "PlayOnlineMonitorService");
        }

        private static PlayOnlineCharacter ConvertToCharacter(MonitoredProcess process, MonitoredWindow? window)
        {
            // **FIX**: Window title IS the character name - no extraction needed
            var windowTitle = window?.Title ?? process.ProcessName;
            
            // **FIX**: Additional protection against null/empty/invalid titles
            if (string.IsNullOrWhiteSpace(windowTitle) || windowTitle.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                windowTitle = $"FFXI Process {process.ProcessId}";
            }
            
            return new PlayOnlineCharacter
            {
                ProcessId = process.ProcessId,
                WindowHandle = window?.Handle ?? IntPtr.Zero,
                WindowTitle = windowTitle,
                CharacterName = windowTitle,  // Window title IS the character name
                ServerName = string.Empty,     // Server info not needed
                ProcessName = process.ProcessName,
                LastSeen = process.LastSeen,
                // LastActivated will be set by HotkeyActivationService when character is activated
            };
        }

        // **REMOVED**: Character name extraction methods no longer needed
        // Window title IS the character name - no extraction required

        private void OnProcessDetected(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                // Convert and fire events for each window
                foreach (var window in e.Process.Windows)
                {
                    var character = ConvertToCharacter(e.Process, window);

                    // **GAMING OPTIMIZATION**: Update character cache for fast lookups
                    UpdateCharacterCache(character);

                    SafeDispatchEvent(() => CharacterDetected?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                }

                // If no windows yet, fire event for the process itself
                if (e.Process.Windows.Count == 0)
                {
                    var character = ConvertToCharacter(e.Process, null);

                    // **GAMING OPTIMIZATION**: Update character cache for fast lookups
                    UpdateCharacterCache(character);

                    SafeDispatchEvent(() => CharacterDetected?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                }

                _ = SafeLogInfoAsync($"PlayOnline process detected: {e.Process.ProcessName} (PID: {e.Process.ProcessId})");
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync("Error in OnProcessDetected", ex);
            }
        }

        private void OnProcessUpdated(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                _ = SafeLogInfoAsync($"[PlayOnline] Process updated: {e.Process.ProcessName} (PID: {e.Process.ProcessId}) with {e.Process.Windows.Count} windows");

                // Fire update events for windows with title changes
                foreach (var window in e.Process.Windows)
                {
                    _ = SafeLogInfoAsync($"[PlayOnline] Window title updated: '{window.Title}' (Handle: 0x{window.Handle.ToInt64():X})");

                    var character = ConvertToCharacter(e.Process, window);

                    // **GAMING OPTIMIZATION**: Update character cache with latest information
                    UpdateCharacterCache(character);

                    // **FIX**: Fire the update event with the updated character data
                    // The CharacterCollectionViewModel will handle updating the existing character
                    SafeDispatchEvent(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                }
                
                // If no windows, still fire update for the process
                if (e.Process.Windows.Count == 0)
                {
                    var character = ConvertToCharacter(e.Process, null);
                    UpdateCharacterCache(character);
                    SafeDispatchEvent(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync("Error in OnProcessUpdated", ex);
            }
        }

        private void OnProcessRemoved(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                // **GAMING OPTIMIZATION**: Remove character from cache when process dies
                RemoveFromCharacterCache(e.Process.ProcessId);

                // Need to create a character object for removal
                var character = new PlayOnlineCharacter
                {
                    ProcessId = e.Process.ProcessId,
                    ProcessName = e.Process.ProcessName
                };
                SafeDispatchEvent(() => CharacterRemoved?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));

                _ = SafeLogInfoAsync($"PlayOnline process removed: {e.Process.ProcessName} (PID: {e.Process.ProcessId})");
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync("Error in OnProcessRemoved", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                StopMonitoring();

                // **IMPROVED**: Wait briefly for any pending activation to complete
                if (_activationSemaphore.CurrentCount == 0)
                {
                    // Activation in progress - wait up to 500ms for completion
                    _activationSemaphore.Wait(500);
                }

                // Cancel any pending activations
                _currentActivationCts?.Cancel();
                _currentActivationCts?.Dispose();

                // Dispose timers and synchronization objects
                _activationDebounceTimer?.Dispose();
                _polTitleCheckTimer?.Dispose();
                _activationSemaphore?.Dispose();

                // Unregister from unified monitoring
                _unifiedMonitoring.UnregisterMonitor(_monitorId);

                // Unsubscribe from events
                _unifiedMonitoring.ProcessDetected -= OnProcessDetected;
                _unifiedMonitoring.ProcessUpdated -= OnProcessUpdated;
                _unifiedMonitoring.ProcessRemoved -= OnProcessRemoved;
            }
            catch (Exception ex)
            {
                // Log disposal errors but don't throw
                _ = SafeLogErrorAsync("Error during PlayOnlineMonitorService disposal", ex);
            }

            GC.SuppressFinalize(this);
        }

        #region Performance Settings

        /// <summary>
        /// Loads gaming-optimized performance settings from application configuration
        /// </summary>
        private void LoadPerformanceSettings()
        {
            try
            {
                var settingsService = ServiceLocator.SettingsService;
                var settings = settingsService.LoadSettings();

                _activationDebounceMs = settings.ActivationDebounceIntervalMs;
                _minActivationIntervalMs = settings.MinActivationIntervalMs;
                _activationTimeoutMs = settings.ActivationTimeoutMs;

                _ = _logging.LogInfoAsync($"Loaded gaming-optimized settings: ActivationDebounce={_activationDebounceMs}ms, MinInterval={_minActivationIntervalMs}ms, Timeout={_activationTimeoutMs}ms", "PlayOnlineMonitorService");
            }
            catch (Exception ex)
            {
                _ = _logging.LogWarningAsync($"Failed to load performance settings, using defaults: {ex.Message}", "PlayOnlineMonitorService");
                // Keep the default values already set
            }
        }

        /// <summary>
        /// Gets the character slot index using cached lookups - GAMING OPTIMIZED
        /// </summary>
        /// <param name="character">The character to find</param>
        /// <returns>Slot index or -1 if not found</returns>
        private int GetCharacterSlotIndexFast(PlayOnlineCharacter character)
        {
            try
            {
                // **GAMING OPTIMIZATION**: Use cached character lookup instead of expensive GetCharactersAsync()
                lock (_cacheLock)
                {
                    var cachedChars = _characterCache.Values.Where(c => c.IsValid).OrderBy(c => c.ProcessId).ToList();
                    for (int i = 0; i < cachedChars.Count; i++)
                    {
                        if (cachedChars[i].ProcessId == character.ProcessId &&
                            cachedChars[i].WindowHandle == character.WindowHandle)
                        {
                            return i;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync($"Error getting cached character slot index for {character.DisplayName}", ex);
            }

            return -1; // Not found or error
        }

        /// <summary>
        /// Updates the character cache with new/changed character information
        /// </summary>
        private void UpdateCharacterCache(PlayOnlineCharacter character)
        {
            try
            {
                lock (_cacheLock)
                {
                    var cacheKey = GetCharacterCacheKey(character.ProcessId, character.WindowHandle);
                    var cached = new CachedCharacterInfo
                    {
                        ProcessId = character.ProcessId,
                        WindowHandle = character.WindowHandle,
                        CharacterName = character.CharacterName,
                        WindowTitle = character.WindowTitle,
                        LastUpdated = DateTime.UtcNow,
                        IsValid = true
                    };

                    _characterCache[cacheKey] = cached;
                    _ = SafeLogInfoAsync($"Updated character cache: {character.CharacterName} (PID: {character.ProcessId})");
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync($"Error updating character cache for {character.DisplayName}", ex);
            }
        }

        /// <summary>
        /// Removes a character from the cache when it's no longer available
        /// </summary>
        private void RemoveFromCharacterCache(int processId)
        {
            try
            {
                lock (_cacheLock)
                {
                    var keysToRemove = _characterCache.Where(kvp => kvp.Value.ProcessId == processId)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (var key in keysToRemove)
                    {
                        _characterCache.TryRemove(key, out _);
                    }
                    _ = SafeLogInfoAsync($"Removed character from cache (PID: {processId})");
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync($"Error removing character from cache (PID: {processId})", ex);
            }
        }

        /// <summary>
        /// Generates a unique cache key for character identification
        /// Uses both 32-bit processId and full 64-bit windowHandle to avoid hash collisions
        /// </summary>
        private static int GetCharacterCacheKey(int processId, IntPtr windowHandle)
        {
            // Handle both 32-bit and 64-bit window handles properly
            // On 64-bit systems, use both upper and lower 32 bits of the window handle
            if (IntPtr.Size == 8) // 64-bit system
            {
                var handleValue = windowHandle.ToInt64();
                var lowerHandle = (int)(handleValue & 0xFFFFFFFF);
                var upperHandle = (int)((handleValue >> 32) & 0xFFFFFFFF);
                return HashCode.Combine(processId, lowerHandle, upperHandle);
            }
            else // 32-bit system
            {
                return HashCode.Combine(processId, windowHandle.ToInt32());
            }
        }

        #endregion

        #region Safe Helper Methods

        /// <summary>
        /// Safely dispatches an event to the UI thread with exception handling
        /// </summary>
        private void SafeDispatchEvent(Action eventHandler)
        {
            try
            {
                if (!_disposed)
                {
                    _uiDispatcher.BeginInvoke(eventHandler);
                }
            }
            catch (Exception ex)
            {
                // If dispatcher fails, log but don't crash
                _ = SafeLogErrorAsync("Error dispatching event to UI thread", ex);
            }
        }

        /// <summary>
        /// Safe logging that won't throw exceptions
        /// </summary>
        private async Task SafeLogInfoAsync(string message)
        {
            try
            {
                await _logging.LogInfoAsync(message, "PlayOnlineMonitorService");
            }
            catch
            {
                // Ignore logging errors to prevent cascading failures
            }
        }

        /// <summary>
        /// Safe error logging that won't throw exceptions
        /// </summary>
        private async Task SafeLogErrorAsync(string message, Exception ex)
        {
            try
            {
                await _logging.LogErrorAsync(message, ex, "PlayOnlineMonitorService");
            }
            catch
            {
                // Last resort - could write to Debug output or Event Log
                // But for now, fail silently to prevent crashes
            }
        }

        /// <summary>
        /// **POL-SPECIFIC**: Check POL processes for window title changes since Win32 events don't work for them
        /// </summary>
        private void CheckPolTitlesCallback(object? state)
        {
            if (!_isMonitoring || _disposed)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var characters = await GetCharactersAsync();
                    var polCharacters = characters.Where(c => c.ProcessName.Contains("pol", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (polCharacters.Count == 0)
                        return;

                    await _logging.LogDebugAsync($"🔍 POL Title Check: Checking {polCharacters.Count} POL processes for title changes", "PlayOnlineMonitorService");

                    foreach (var character in polCharacters)
                    {
                        if (!character.IsRunning || character.WindowHandle == IntPtr.Zero)
                            continue;

                        // Get current window title
                        var currentTitle = GetWindowTitle(character.WindowHandle);
                        if (string.IsNullOrEmpty(currentTitle))
                            continue;

                        // Check if title has changed
                        if (!_lastPolTitles.TryGetValue(character.WindowHandle, out var lastTitle) || lastTitle != currentTitle)
                        {
                            await _logging.LogInfoAsync($"📊 POL TITLE CHANGE: PID {character.ProcessId}, Handle 0x{character.WindowHandle.ToInt64():X}, '{lastTitle}' → '{currentTitle}'", "PlayOnlineMonitorService");
                            
                            _lastPolTitles[character.WindowHandle] = currentTitle;

                            // Create updated character with new title
                            // **FIX**: Window title IS the character name - no extraction needed
                            var updatedCharacter = new PlayOnlineCharacter
                            {
                                ProcessId = character.ProcessId,
                                ProcessName = character.ProcessName,
                                WindowHandle = character.WindowHandle,
                                WindowTitle = currentTitle,
                                CharacterName = currentTitle,  // Window title IS the character name
                                ServerName = string.Empty,      // Server info not needed
                                LastSeen = DateTime.UtcNow
                            };

                            // Update cache and fire event
                            UpdateCharacterCache(updatedCharacter);
                            SafeDispatchEvent(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(updatedCharacter)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogErrorAsync("Error in POL title checking", ex, "PlayOnlineMonitorService");
                }
            });
        }

        /// <summary>
        /// Get window title using Win32 API
        /// </summary>
        private static string GetWindowTitle(IntPtr windowHandle)
        {
            try
            {
                if (windowHandle == IntPtr.Zero)
                    return string.Empty;

                const int maxLength = 512; // Increased buffer size
                var buffer = new char[maxLength];
                int length = GetWindowText(windowHandle, buffer, maxLength);
                
                if (length <= 0)
                    return string.Empty;
                
                // **FIX**: Handle null terminators and clean up the string
                var title = new string(buffer, 0, length).Trim('\0').Trim();
                
                // **FIX**: If title is literally "NULL" or empty, return empty string
                if (string.IsNullOrWhiteSpace(title) || title.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
                
                return title;
            }
            catch
            {
                return string.Empty;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        #endregion
    }
}
