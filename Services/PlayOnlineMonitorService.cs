using System;
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
        
        // Rapid switching protection with character-aware logic
        private readonly SemaphoreSlim _activationSemaphore = new(1, 1);
        private readonly Timer _activationDebounceTimer;
        private CancellationTokenSource _currentActivationCts = new();
        private PlayOnlineCharacter? _pendingActivation;
        private DateTime _lastActivationAttempt = DateTime.MinValue;
        private int _lastActivatedSlotIndex = -1; // Track last activated character slot for smart debouncing
        
        // Gaming-optimized timing values (loaded from settings)
        private int _activationDebounceMs = 50;    // Fast debounce for gaming
        private int _minActivationIntervalMs = 100; // Only applies to same character
        private int _activationTimeoutMs = 3000;   // Reduced timeout for responsiveness
        
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
        
        public async Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character, CancellationToken cancellationToken = default)
        {
            if (character == null || character.WindowHandle == IntPtr.Zero)
            {
                await _logging.LogWarningAsync("Cannot activate character: invalid window handle", "PlayOnlineMonitorService");
                return false;
            }
            
            // **GAMING OPTIMIZATION**: Smart character-aware rate limiting
            var currentSlotIndex = await GetCharacterSlotIndexAsync(character);
            var timeSinceLastAttempt = DateTime.UtcNow - _lastActivationAttempt;
            
            // Only apply rate limiting if switching to the SAME character
            bool isSameCharacter = (currentSlotIndex == _lastActivatedSlotIndex && currentSlotIndex != -1);
            bool tooFrequent = timeSinceLastAttempt.TotalMilliseconds < _minActivationIntervalMs;
            
            if (isSameCharacter && tooFrequent)
            {
                await _logging.LogDebugAsync($"Rate limiting same-character activation for {character.DisplayName} (slot {currentSlotIndex}, {timeSinceLastAttempt.TotalMilliseconds:F0}ms ago)", "PlayOnlineMonitorService");
                
                // Use debounced activation for same character
                RequestDebouncedActivation(character);
                return true; // Return true to indicate request was accepted (will be processed)
            }
            else if (!isSameCharacter)
            {
                // Different character - allow immediate activation (gaming-optimized)
                await _logging.LogDebugAsync($"Fast character switch: {_lastActivatedSlotIndex} → {currentSlotIndex} for {character.DisplayName}", "PlayOnlineMonitorService");
                _lastActivatedSlotIndex = currentSlotIndex;
                return await PerformImmediateActivationAsync(character, cancellationToken);
            }
            
            // Same character but enough time has passed - proceed
            _lastActivatedSlotIndex = currentSlotIndex;
            return await PerformImmediateActivationAsync(character, cancellationToken);
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
            // **OPTIMIZATION**: Use semaphore to ensure only one activation at a time
            if (!await _activationSemaphore.WaitAsync(100, cancellationToken))
            {
                await _logging.LogWarningAsync($"Activation already in progress, skipping {character.DisplayName}", "PlayOnlineMonitorService");
                return false;
            }
            
            try
            {
                _lastActivationAttempt = DateTime.UtcNow;
                
                // Create timeout cancellation token
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_activationTimeoutMs);
                
                await _logging.LogInfoAsync($"Activating window for {character.DisplayName}...", "PlayOnlineMonitorService");
                
                // **IMPROVED**: Use injected dependency with ServiceLocator fallback
                var processManagement = _processManagement ?? ServiceLocator.ProcessManagementService;
                var success = await processManagement.ActivateWindowAsync(character.WindowHandle, _activationTimeoutMs);
                
                if (success)
                {
                    await _logging.LogInfoAsync($"Successfully activated window for {character.DisplayName}", "PlayOnlineMonitorService");
                }
                else
                {
                    await _logging.LogWarningAsync($"Failed to activate window for {character.DisplayName}", "PlayOnlineMonitorService");
                }
                
                return success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await _logging.LogInfoAsync($"Activation cancelled for {character.DisplayName}", "PlayOnlineMonitorService");
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
            return new PlayOnlineCharacter
            {
                ProcessId = process.ProcessId,
                WindowHandle = window?.Handle ?? IntPtr.Zero,
                WindowTitle = window?.Title ?? process.ProcessName,
                CharacterName = ExtractCharacterName(window?.Title),
                ServerName = ExtractServerName(window?.Title),
                ProcessName = process.ProcessName,
                LastSeen = process.LastSeen,
                IsActive = window?.IsActive ?? false // Use the IsActive flag from the window
            };
        }
        
        private static string ExtractCharacterName(string? windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                return string.Empty;
            
            // Common FFXI window title patterns:
            // "CharacterName" (simple)
            // "CharacterName - ServerName" (with server)
            // "Final Fantasy XI" (login screen)
            
            // Skip non-character titles
            if (windowTitle.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                windowTitle.Equals("Final Fantasy XI", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            
            // Extract character name (before dash if present)
            var dashIndex = windowTitle.IndexOf('-');
            if (dashIndex > 0)
            {
                return windowTitle.Substring(0, dashIndex).Trim();
            }
            
            // If no dash, the whole title might be the character name
            return windowTitle.Trim();
        }
        
        private static string ExtractServerName(string? windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                return string.Empty;
            
            // Look for pattern "CharacterName - ServerName"
            var dashIndex = windowTitle.IndexOf('-');
            if (dashIndex > 0 && dashIndex < windowTitle.Length - 1)
            {
                return windowTitle.Substring(dashIndex + 1).Trim();
            }
            
            return string.Empty;
        }
        
        private void OnProcessDetected(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId || _disposed) return;
            
            try
            {
                // Convert and fire events for each window
                foreach (var window in e.Process.Windows)
                {
                    var character = ConvertToCharacter(e.Process, window);
                    SafeDispatchEvent(() => CharacterDetected?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
                }
                
                // If no windows yet, fire event for the process itself
                if (e.Process.Windows.Count == 0)
                {
                    var character = ConvertToCharacter(e.Process, null);
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
                    _ = SafeLogInfoAsync($"[PlayOnline] Firing CharacterUpdated for window: '{window.Title}' (Handle: 0x{window.Handle.ToInt64():X})");
                    
                    var character = ConvertToCharacter(e.Process, window);
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
        /// Gets the character slot index from the Characters collection for character-aware debouncing
        /// </summary>
        /// <param name="character">The character to find</param>
        /// <returns>Slot index or -1 if not found</returns>
        private async Task<int> GetCharacterSlotIndexAsync(PlayOnlineCharacter character)
        {
            try
            {
                var characters = await GetCharactersAsync();
                for (int i = 0; i < characters.Count; i++)
                {
                    if (characters[i].ProcessId == character.ProcessId && 
                        characters[i].WindowHandle == character.WindowHandle)
                    {
                        return i;
                    }
                }
            }
            catch (Exception ex)
            {
                _ = SafeLogErrorAsync($"Error getting character slot index for {character.DisplayName}", ex);
            }
            
            return -1; // Not found or error
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
        
        #endregion
    }
}
