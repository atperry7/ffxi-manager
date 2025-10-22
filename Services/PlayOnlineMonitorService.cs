using FFXIManager.Infrastructure;
using FFXIManager.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

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
        private readonly IProcessUtilityService _processUtility;
        private readonly ISettingsService _settingsService;

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
        private readonly string[] _targetProcessNames = { "pol", "ffxi", "ffximain", "PlayOnlineViewer" };

        // Injected dependency for better testability (fallback to ServiceLocator if not provided)
        private readonly IProcessManagementService? _processManagement;

        public PlayOnlineMonitorService(
            IUnifiedMonitoringService unifiedMonitoring,
            ILoggingService logging,
            IUiDispatcher uiDispatcher,
            IProcessManagementService? processManagement,
            IProcessUtilityService processUtility,
            ISettingsService settingsService)
        {
            _unifiedMonitoring = unifiedMonitoring ?? throw new ArgumentNullException(nameof(unifiedMonitoring));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _processManagement = processManagement; // null = use ServiceLocator fallback
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

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
            _ = _logging.LogInfoAsync("Registered PlayOnline monitoring profile (ID: {MonitorId})", "PlayOnlineMonitorService", _monitorId);
        }

        public bool IsMonitoring => _isMonitoring;

        public async Task<List<PlayOnlineCharacter>> GetCharactersAsync()
        {
            var processes = await _unifiedMonitoring.GetProcessesAsync(_monitorId);
            var characters = new List<PlayOnlineCharacter>();

            await _logging.LogInfoAsync($"[DEBUG_UNIFIED] GetCharactersAsync: Found {processes.Count} processes from UnifiedMonitoringService", "PlayOnlineMonitorService");

            foreach (var process in processes)
            {
                await _logging.LogInfoAsync($"[DEBUG_UNIFIED] Process PID {process.ProcessId}, Name: '{process.ProcessName}', Windows: {process.Windows.Count}", "PlayOnlineMonitorService");

                // Convert each window to a character
                foreach (var window in process.Windows)
                {
                    await _logging.LogInfoAsync($"[DEBUG_UNIFIED] - Window Handle: 0x{window.Handle.ToInt64():X}, Title: '{window.Title}'", "PlayOnlineMonitorService");
                    characters.Add(ConvertToCharacter(process, window));
                }

                // If no windows, create one character for the process
                if (process.Windows.Count == 0)
                {
                    await _logging.LogInfoAsync($"[DEBUG_UNIFIED] - No windows found for process, creating fallback character", "PlayOnlineMonitorService");
                    characters.Add(ConvertToCharacter(process, null));
                }
            }

            await _logging.LogInfoAsync($"[DEBUG_UNIFIED] GetCharactersAsync: Returning {characters.Count} characters total", "PlayOnlineMonitorService");
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

                _ = _logging.LogWarningAsync("**EMERGENCY THROTTLE ACTIVATED**: {ActivationCount} activations/sec exceeded limit ({MaxActivationsPerSecond})", "PlayOnlineMonitorService", currentCount, MAX_ACTIVATIONS_PER_SECOND);
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
            if (!_processUtility.IsWindowValid(character.WindowHandle))
            {
                await _logging.LogWarningAsync("Cannot activate {CharacterName}: window handle 0x{WindowHandle:X} is no longer valid (process may have updated window title)", "PlayOnlineMonitorService", character.DisplayName, character.WindowHandle.ToInt64());

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
                await _logging.LogWarningAsync("[PERFORMANCE] Character activation took {ElapsedMs}ms for {CharacterName}", "PlayOnlineMonitorService", activationStopwatch.ElapsedMilliseconds, character.DisplayName);
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

            _ = _logging.LogDebugAsync("Queued debounced activation for {CharacterName} (debounce: {DebounceMs}ms)", "PlayOnlineMonitorService", character.DisplayName, _activationDebounceMs);
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
                await SafeLogErrorAsync("Error in debounced activation for {CharacterName}", characterToActivate.DisplayName, ex);
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
                var result = await _processUtility.ActivateWindowEnhancedAsync(character.WindowHandle, fastTimeoutMs);

                if (result.Success)
                {
                    // Only log if it took too long
                    if (result.Duration.TotalMilliseconds > 100)
                    {
                        await _logging.LogInfoAsync("Successfully activated {CharacterName} in {DurationMs:F0}ms (attempts: {AttemptsRequired})", "PlayOnlineMonitorService", character.DisplayName, result.Duration.TotalMilliseconds, result.AttemptsRequired);
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
                        WindowActivationFailureReason.Timeout => "Activation timed out after " + fastTimeoutMs + "ms",
                        _ => result.DiagnosticInfo ?? "Unknown failure reason"
                    };

                    await _logging.LogWarningAsync("Failed to activate window for {CharacterName}: {Diagnostic}", "PlayOnlineMonitorService", character.DisplayName, diagnostic);
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
                await _logging.LogErrorAsync("Error activating window for {CharacterName}", "PlayOnlineMonitorService", ex, character.DisplayName);
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

            await _logging.LogInfoAsync("Refreshed character data: {CharacterCount} character(s) found", "PlayOnlineMonitorService", characters.Count);
        }

        /// <summary>
        /// Gets a valid PlayOnline window handle with optional PID preference.
        /// </summary>
        /// <param name="preferredProcessId">Optional PID hint from launch step</param>
        /// <returns>Valid window handle or IntPtr.Zero if none found</returns>
        public async Task<IntPtr> GetValidPlayOnlineWindowAsync(int? preferredProcessId = null)
        {
            try
            {
                await _logging.LogDebugAsync($"[POL-WINDOW] Looking for valid POL window (Preferred PID: {preferredProcessId?.ToString() ?? "none"})", "PlayOnlineMonitorService");

                // Get current live characters
                var characters = await GetCharactersAsync();
                
                // If we have a preferred PID, try it first
                if (preferredProcessId.HasValue)
                {
                    var preferredChar = characters.FirstOrDefault(c => c.ProcessId == preferredProcessId.Value);
                    if (preferredChar?.WindowHandle != IntPtr.Zero && _processUtility.IsWindowValid(preferredChar.WindowHandle))
                    {
                        await _logging.LogInfoAsync($"[POL-WINDOW] Using preferred PID {preferredProcessId}: Handle 0x{preferredChar.WindowHandle.ToInt64():X}", "PlayOnlineMonitorService");
                        return preferredChar.WindowHandle;
                    }
                    
                    // If preferred character has no window yet, try to find one
                    if (preferredChar != null)
                    {
                        var windows = await _processUtility.GetProcessWindowsAsync(preferredProcessId.Value);
                        var mainWindow = windows.FirstOrDefault(w => w.IsMainWindow) ?? windows.FirstOrDefault();
                        if (mainWindow != null)
                        {
                            await _logging.LogInfoAsync($"[POL-WINDOW] Found new window for preferred PID {preferredProcessId}: Handle 0x{mainWindow.Handle.ToInt64():X}", "PlayOnlineMonitorService");
                            return mainWindow.Handle;
                        }
                        else
                        {
                            await _logging.LogDebugAsync($"[POL-WINDOW] Preferred PID {preferredProcessId} has no windows yet", "PlayOnlineMonitorService");
                        }
                    }
                }

                // **FIX**: Split characters into those with windows vs those without
                var charactersWithWindows = characters
                    .Where(c => c.WindowHandle != IntPtr.Zero && _processUtility.IsWindowValid(c.WindowHandle))
                    .OrderByDescending(c => c.ProcessId) // Higher PIDs = newer processes
                    .ToList();

                var charactersWithoutWindows = characters
                    .Where(c => c.WindowHandle == IntPtr.Zero)
                    .OrderByDescending(c => c.ProcessId) // Higher PIDs = newer processes  
                    .ToList();

                await _logging.LogDebugAsync($"[POL-WINDOW] Found {charactersWithWindows.Count} characters with windows, {charactersWithoutWindows.Count} without windows", "PlayOnlineMonitorService");

                // **STRATEGY 1**: Try characters without windows first (these are likely the newest instances)
                foreach (var character in charactersWithoutWindows)
                {
                    await _logging.LogDebugAsync($"[POL-WINDOW] Checking PID {character.ProcessId} for new windows", "PlayOnlineMonitorService");
                    
                    var windows = await _processUtility.GetProcessWindowsAsync(character.ProcessId);
                    var mainWindow = windows.FirstOrDefault(w => w.IsMainWindow) ?? windows.FirstOrDefault();
                    if (mainWindow != null)
                    {
                        await _logging.LogInfoAsync($"[POL-WINDOW] Found NEW window for PID {character.ProcessId}: Handle 0x{mainWindow.Handle.ToInt64():X}, Title: '{mainWindow.Title}'", "PlayOnlineMonitorService");
                        return mainWindow.Handle;
                    }
                }

                await _logging.LogWarningAsync("[POL-WINDOW] No valid PlayOnline windows found", "PlayOnlineMonitorService");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync("[POL-WINDOW] Error getting valid PlayOnline window", "PlayOnlineMonitorService", ex);
                return IntPtr.Zero;
            }
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

        private PlayOnlineCharacter ConvertToCharacter(MonitoredProcess process, MonitoredWindow? window)
        {
            // **FIX**: Window title IS the character name - no extraction needed
            var windowTitle = window?.Title ?? process.ProcessName;

            _ = _logging.LogDebugAsync("ConvertToCharacter: Process {ProcessId} ({ProcessName}), Window Title: '{WindowTitle}', Handle: 0x{WindowHandle:X}", "PlayOnlineMonitorService", process.ProcessId, process.ProcessName, windowTitle, window?.Handle.ToInt64() ?? 0);

            // **FIX**: Additional protection against null/empty/invalid titles
            if (string.IsNullOrWhiteSpace(windowTitle) || windowTitle.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                windowTitle = "FFXI Process " + process.ProcessId;
                _ = _logging.LogDebugAsync("ConvertToCharacter: Using fallback title '{WindowTitle}' for process {ProcessId}", "PlayOnlineMonitorService", windowTitle, process.ProcessId);
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

                _ = _logging.LogInfoAsync("PlayOnline process detected: {ProcessName} (PID: {ProcessId})", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId);
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
                _ = _logging.LogInfoAsync("[PlayOnline] Process updated: {ProcessName} (PID: {ProcessId}) with {WindowCount} windows", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId, e.Process.Windows.Count);

                // Fire update events for windows with title changes
                foreach (var window in e.Process.Windows)
                {
                    _ = _logging.LogInfoAsync("[PlayOnline] Window title updated: '{WindowTitle}' (Handle: 0x{WindowHandle:X})", "PlayOnlineMonitorService", window.Title, window.Handle.ToInt64());

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

                _ = _logging.LogInfoAsync("PlayOnline process removed: {ProcessName} (PID: {ProcessId})", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId);
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
                var settings = _settingsService.LoadSettings();

                _activationDebounceMs = settings.ActivationDebounceIntervalMs;
                _minActivationIntervalMs = settings.MinActivationIntervalMs;
                _activationTimeoutMs = settings.ActivationTimeoutMs;

                _ = _logging.LogInfoAsync("Loaded gaming-optimized settings: ActivationDebounce={DebounceMs}ms, MinInterval={MinIntervalMs}ms, Timeout={TimeoutMs}ms", "PlayOnlineMonitorService", _activationDebounceMs, _minActivationIntervalMs, _activationTimeoutMs);
            }
            catch (Exception ex)
            {
                _ = _logging.LogWarningAsync("Failed to load performance settings, using defaults: {ErrorMessage}", "PlayOnlineMonitorService", ex.Message);
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
                _ = SafeLogErrorAsync("Error getting cached character slot index for {CharacterName}", character.DisplayName, ex);
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
                    _ = SafeLogInfoAsync("Updated character cache: {CharacterName} (PID: {ProcessId})", character.CharacterName, character.ProcessId);
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync("Error updating character cache for {CharacterName}", character.DisplayName, ex);
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
                    _ = SafeLogInfoAsync("Removed character from cache (PID: {ProcessId})", processId);
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync("Error removing character from cache (PID: {ProcessId})", processId.ToString(), ex);
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
        /// Safe structured logging that won't throw exceptions
        /// </summary>
        private async Task SafeLogInfoAsync(string messageTemplate, params object[] args)
        {
            try
            {
                await _logging.LogInfoAsync(messageTemplate, "PlayOnlineMonitorService", args);
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
        /// Safe structured error logging that won't throw exceptions
        /// </summary>
        private async Task SafeLogErrorAsync(string messageTemplate, string arg, Exception ex)
        {
            try
            {
                await _logging.LogErrorAsync(messageTemplate, "PlayOnlineMonitorService", ex, arg);
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
                    {
                        await _logging.LogDebugAsync("🔍 POL Title Check: No POL processes found", "PlayOnlineMonitorService");
                        return;
                    }

                    await _logging.LogDebugAsync("🔍 POL Title Check: Checking {ProcessCount} POL processes for title changes", "PlayOnlineMonitorService", polCharacters.Count);

                    foreach (var character in polCharacters)
                    {
                        if (!character.IsRunning)
                        {
                            await _logging.LogDebugAsync("🔍 POL Title Check: Skipping non-running process {ProcessId}", "PlayOnlineMonitorService", character.ProcessId);
                            continue;
                        }

                        // If we don't have a window handle, try to get windows for this process
                        if (character.WindowHandle == IntPtr.Zero)
                        {
                            await _logging.LogDebugAsync("🔍 POL Title Check: Process {ProcessId} has no window handle, trying to find windows", "PlayOnlineMonitorService", character.ProcessId);

                            // Get windows directly from ProcessUtilityService
                            var windows = await _processUtility.GetProcessWindowsAsync(character.ProcessId);

                            if (windows.Count > 0)
                            {
                                var mainWindow = windows.FirstOrDefault(w => w.IsMainWindow) ?? windows.First();
                                await _logging.LogInfoAsync("🔍 POL Title Check: Found window for process {ProcessId}: Handle 0x{WindowHandle:X}, Title: '{WindowTitle}'", "PlayOnlineMonitorService", character.ProcessId, mainWindow.Handle.ToInt64(), mainWindow.Title);

                                // Update the character with the found window
                                character.WindowHandle = mainWindow.Handle;
                                character.WindowTitle = mainWindow.Title;
                                character.CharacterName = mainWindow.Title;

                                // Fire update event
                                UpdateCharacterCache(character);
                                SafeDispatchEvent(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                                continue;
                            }
                            else
                            {
                                await _logging.LogDebugAsync("🔍 POL Title Check: No windows found for process {ProcessId}", "PlayOnlineMonitorService", character.ProcessId);
                                continue;
                            }
                        }

                        // Get current window title
                        var currentTitle = GetWindowTitle(character.WindowHandle);
                        if (string.IsNullOrEmpty(currentTitle))
                            continue;

                        // Check if title has changed
                        if (!_lastPolTitles.TryGetValue(character.WindowHandle, out var lastTitle) || lastTitle != currentTitle)
                        {
                            await _logging.LogInfoAsync("📊 POL TITLE CHANGE: PID {ProcessId}, Handle 0x{WindowHandle:X}, '{OldTitle}' → '{NewTitle}'", "PlayOnlineMonitorService", character.ProcessId, character.WindowHandle.ToInt64(), lastTitle ?? "<unknown>", currentTitle);

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
        /// Get window title using Win32 API with enhanced error handling
        /// </summary>
        private string GetWindowTitle(IntPtr windowHandle)
        {
            try
            {
                if (windowHandle == IntPtr.Zero)
                {
                    _ = _logging.LogDebugAsync("GetWindowTitle: Invalid window handle (IntPtr.Zero)", "PlayOnlineMonitorService");
                    return string.Empty;
                }

                const int maxLength = 512; // Increased buffer size
                var buffer = new char[maxLength];
                int length = GetWindowText(windowHandle, buffer, maxLength);

                if (length <= 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    _ = _logging.LogDebugAsync("GetWindowTitle: GetWindowText returned {Length} for handle 0x{WindowHandle:X}, Win32 Error: {ErrorCode}", "PlayOnlineMonitorService", length, windowHandle.ToInt64(), error);
                    return string.Empty;
                }

                // **FIX**: Handle null terminators and clean up the string
                var title = new string(buffer, 0, length).Trim('\0').Trim();

                // **FIX**: If title is literally "NULL" or empty, return empty string
                if (string.IsNullOrWhiteSpace(title) || title.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    _ = _logging.LogDebugAsync("GetWindowTitle: Filtered out invalid title '{Title}' for handle 0x{WindowHandle:X}", "PlayOnlineMonitorService", title, windowHandle.ToInt64());
                    return string.Empty;
                }

                _ = _logging.LogDebugAsync("GetWindowTitle: Successfully retrieved '{Title}' for handle 0x{WindowHandle:X}", "PlayOnlineMonitorService", title, windowHandle.ToInt64());
                return title;
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("GetWindowTitle: Exception for handle 0x{WindowHandle:X}", "PlayOnlineMonitorService", ex, windowHandle.ToInt64());
                return string.Empty;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        #endregion
    }
}


