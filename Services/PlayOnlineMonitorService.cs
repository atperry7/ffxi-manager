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

        // **POL-SPECIFIC**: Timer to check POL processes for title changes (since Win32 events don't work)
        private readonly Timer _polTitleCheckTimer;
        private readonly Dictionary<IntPtr, string> _lastPolTitles = new();
        private DateTime _lastActivationAttempt = DateTime.MinValue;

        // Gaming-optimized timing values (loaded from settings)
        private int _activationDebounceMs = 50;    // Fast debounce for gaming
        private int _minActivationIntervalMs = 100; // Only applies to same character
        private int _activationTimeoutMs = 150;    // **OPTIMIZED**: Reduced to 150ms for fast gaming response

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

        public async Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            var activationStopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Validation
            if (!IsCharacterValidForActivation(character))
            {
                await _logging.LogWarningAsync("Cannot activate character: invalid window handle", "PlayOnlineMonitorService");
                return false;
            }

            // **FIRE AND FORGET**: No debouncing - activation cancellation handled at HotkeyActivationService level
            var result = await PerformImmediateActivationAsync(character, cancellationToken);

            // Performance logging
            activationStopwatch.Stop();
            if (activationStopwatch.ElapsedMilliseconds > 100)
            {
                await _logging.LogWarningAsync("[PERFORMANCE] Character activation took {ElapsedMs}ms for {CharacterName}", "PlayOnlineMonitorService", activationStopwatch.ElapsedMilliseconds, character.DisplayName);
            }

            return result;
        }

        /// <summary>
        /// Validates character is suitable for window activation
        /// </summary>
        private bool IsCharacterValidForActivation(PlayOnlineCharacter character)
        {
            if (character == null || character.WindowHandle == IntPtr.Zero)
                return false;

            if (!_processUtility.IsWindowValid(character.WindowHandle))
            {
                _ = _logging.LogWarningAsync("Window handle 0x{WindowHandle:X} for {CharacterName} is no longer valid", "PlayOnlineMonitorService", character.WindowHandle.ToInt64(), character.DisplayName);
                _ = RefreshCharactersAsync();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs the actual window activation with proper synchronization
        /// </summary>
        private async Task<bool> PerformImmediateActivationAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            try
            {
                _lastActivationAttempt = DateTime.UtcNow;

                // **PERFORMANCE**: Use shorter timeout for faster response
                var fastTimeoutMs = Math.Min(_activationTimeoutMs, 150); // Cap at 150ms for gaming

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
                    if (preferredChar != null && preferredChar.WindowHandle != IntPtr.Zero && _processUtility.IsWindowValid(preferredChar.WindowHandle))
                    {
                        await _logging.LogInfoAsync($"[POL-WINDOW] Using preferred PID {preferredProcessId}: Handle 0x{preferredChar.WindowHandle.ToInt64():X}", "PlayOnlineMonitorService");
                        return preferredChar.WindowHandle;
                    }

                    // If preferred character has no window yet, try to find one
                    if (preferredChar != null)
                    {
                        var windowHandle = await GetMainWindowHandleAsync(preferredProcessId.Value);
                        if (windowHandle != IntPtr.Zero)
                        {
                            await _logging.LogInfoAsync($"[POL-WINDOW] Found new window for preferred PID {preferredProcessId}: Handle 0x{windowHandle.ToInt64():X}", "PlayOnlineMonitorService");
                            return windowHandle;
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
                    var windowHandle = await GetMainWindowHandleAsync(character.ProcessId);
                    if (windowHandle != IntPtr.Zero)
                    {
                        var title = GetWindowTitleSafe(windowHandle);
                        await _logging.LogInfoAsync($"[POL-WINDOW] Found NEW window for PID {character.ProcessId}: Handle 0x{windowHandle.ToInt64():X}, Title: '{title}'", "PlayOnlineMonitorService");
                        return windowHandle;
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

        /// <summary>
        /// Fires character event on UI thread with cache update
        /// </summary>
        private void RaiseCharacterEvent(PlayOnlineCharacter character, EventHandler<PlayOnlineCharacterEventArgs>? eventHandler)
        {
            UpdateCharacterCache(character);
            _uiDispatcher.BeginInvoke(() => eventHandler?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
        }

        private void OnProcessDetected(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                if (e.Process.Windows.Count > 0)
                {
                    foreach (var window in e.Process.Windows)
                        RaiseCharacterEvent(ConvertToCharacter(e.Process, window), CharacterDetected);
                }
                else
                {
                    RaiseCharacterEvent(ConvertToCharacter(e.Process, null), CharacterDetected);
                }

                _ = _logging.LogInfoAsync("PlayOnline process detected: {ProcessName} (PID: {ProcessId})", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId);
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("Error in OnProcessDetected", "PlayOnlineMonitorService", ex);
            }
        }

        private void OnProcessUpdated(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                _ = _logging.LogInfoAsync("[PlayOnline] Process updated: {ProcessName} (PID: {ProcessId}) with {WindowCount} windows", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId, e.Process.Windows.Count);

                if (e.Process.Windows.Count > 0)
                {
                    foreach (var window in e.Process.Windows)
                    {
                        _ = _logging.LogInfoAsync("[PlayOnline] Window title updated: '{WindowTitle}' (Handle: 0x{WindowHandle:X})", "PlayOnlineMonitorService", window.Title, window.Handle.ToInt64());
                        RaiseCharacterEvent(ConvertToCharacter(e.Process, window), CharacterUpdated);
                    }
                }
                else
                {
                    RaiseCharacterEvent(ConvertToCharacter(e.Process, null), CharacterUpdated);
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("Error in OnProcessUpdated", "PlayOnlineMonitorService", ex);
            }
        }

        private void OnProcessRemoved(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;

            try
            {
                RemoveFromCharacterCache(e.Process.ProcessId);
                var character = new PlayOnlineCharacter
                {
                    ProcessId = e.Process.ProcessId,
                    ProcessName = e.Process.ProcessName
                };
                _uiDispatcher.BeginInvoke(() => CharacterRemoved?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                _ = _logging.LogInfoAsync("PlayOnline process removed: {ProcessName} (PID: {ProcessId})", "PlayOnlineMonitorService", e.Process.ProcessName, e.Process.ProcessId);
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("Error in OnProcessRemoved", "PlayOnlineMonitorService", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                StopMonitoring();

                // Dispose timer
                _polTitleCheckTimer?.Dispose();

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
                _ = _logging.LogErrorAsync("Error during PlayOnlineMonitorService disposal", "PlayOnlineMonitorService", ex);
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
                _ = _logging.LogErrorAsync("Error getting cached character slot index for {CharacterName}", "PlayOnlineMonitorService", ex, character.DisplayName);
            }

            return -1;
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
                    _characterCache[cacheKey] = new CachedCharacterInfo
                    {
                        ProcessId = character.ProcessId,
                        WindowHandle = character.WindowHandle,
                        CharacterName = character.CharacterName,
                        WindowTitle = character.WindowTitle,
                        LastUpdated = DateTime.UtcNow,
                        IsValid = true
                    };
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("Error updating character cache for {CharacterName}", "PlayOnlineMonitorService", ex, character.DisplayName);
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
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync("Error removing character from cache (PID: {ProcessId})", "PlayOnlineMonitorService", ex, processId);
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

        #region Win32 API and Helper Methods

        /// <summary>
        /// Retrieves window title with comprehensive error handling and validation
        /// </summary>
        private string GetWindowTitleSafe(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return string.Empty;

            try
            {
                const int maxLength = 512;
                var buffer = new char[maxLength];
                int length = GetWindowText(windowHandle, buffer, maxLength);

                if (length <= 0)
                    return string.Empty;

                var title = new string(buffer, 0, length).Trim('\0').Trim();

                // Filter out invalid titles
                if (string.IsNullOrWhiteSpace(title) || title.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                return title;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets main window handle for a process, preferring main window over other visible windows
        /// </summary>
        private async Task<IntPtr> GetMainWindowHandleAsync(int processId)
        {
            var windows = await _processUtility.GetProcessWindowsAsync(processId);
            var mainWindow = windows.FirstOrDefault(w => w.IsMainWindow) ?? windows.FirstOrDefault();
            return mainWindow?.Handle ?? IntPtr.Zero;
        }

        /// <summary>
        /// Creates updated character with new window information
        /// </summary>
        private PlayOnlineCharacter UpdateCharacterWindowInfo(PlayOnlineCharacter source, IntPtr windowHandle, string windowTitle)
        {
            return new PlayOnlineCharacter
            {
                ProcessId = source.ProcessId,
                ProcessName = source.ProcessName,
                WindowHandle = windowHandle,
                WindowTitle = windowTitle,
                CharacterName = windowTitle,
                ServerName = string.Empty,
                LastSeen = DateTime.UtcNow
            };
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
                            var windowHandle = await GetMainWindowHandleAsync(character.ProcessId);

                            if (windowHandle != IntPtr.Zero)
                            {
                                var title = GetWindowTitleSafe(windowHandle);
                                await _logging.LogInfoAsync("🔍 POL Title Check: Found window for process {ProcessId}: Handle 0x{WindowHandle:X}, Title: '{WindowTitle}'", "PlayOnlineMonitorService", character.ProcessId, windowHandle.ToInt64(), title);

                                var updatedCharacter = UpdateCharacterWindowInfo(character, windowHandle, title);
                                UpdateCharacterCache(updatedCharacter);
                                _uiDispatcher.BeginInvoke(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(updatedCharacter)));
                                continue;
                            }
                            else
                            {
                                await _logging.LogDebugAsync("🔍 POL Title Check: No windows found for process {ProcessId}", "PlayOnlineMonitorService", character.ProcessId);
                                continue;
                            }
                        }

                        // Get current window title
                        var currentTitle = GetWindowTitleSafe(character.WindowHandle);
                        if (string.IsNullOrEmpty(currentTitle))
                            continue;

                        // Check if title has changed
                        if (!_lastPolTitles.TryGetValue(character.WindowHandle, out var lastTitle) || lastTitle != currentTitle)
                        {
                            await _logging.LogInfoAsync("📊 POL TITLE CHANGE: PID {ProcessId}, Handle 0x{WindowHandle:X}, '{OldTitle}' → '{NewTitle}'", "PlayOnlineMonitorService", character.ProcessId, character.WindowHandle.ToInt64(), lastTitle ?? "<unknown>", currentTitle);

                            _lastPolTitles[character.WindowHandle] = currentTitle;
                            var updatedCharacter = UpdateCharacterWindowInfo(character, character.WindowHandle, currentTitle);
                            UpdateCharacterCache(updatedCharacter);
                            _uiDispatcher.BeginInvoke(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(updatedCharacter)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogErrorAsync("Error in POL title checking", "PlayOnlineMonitorService", ex);
                }
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        #endregion
    }
}


