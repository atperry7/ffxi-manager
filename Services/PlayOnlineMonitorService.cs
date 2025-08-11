using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for monitoring PlayOnline character instances
    /// </summary>
    public interface IPlayOnlineMonitorService
    {
        Task<List<PlayOnlineCharacter>> GetRunningCharactersAsync();
        Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character);
        Task RefreshCharacterDataAsync();
        void StartMonitoring();
        void StopMonitoring();
        event EventHandler<PlayOnlineCharacter>? CharacterDetected;
        event EventHandler<PlayOnlineCharacter>? CharacterUpdated;
        event EventHandler<int>? CharacterRemoved; // ProcessId
    }

    /// <summary>
    /// Enhanced service for monitoring and managing PlayOnline character instances
    /// Uses shared ProcessManagementService for unified process handling
    /// </summary>
    public class PlayOnlineMonitorService : IPlayOnlineMonitorService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly IProcessManagementService _processManagementService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly List<PlayOnlineCharacter> _characters = new();
        private readonly object _lockObject = new();
        private static readonly TimeSpan TitleRefreshThrottle = TimeSpan.FromSeconds(1);
        private bool _isMonitoring;
        private bool _disposed;

        // Target process names for PlayOnline/FFXI monitoring
        private readonly string[] _targetProcessNames = { "pol", "ffxi", "PlayOnlineViewer" };
        private Guid? _discoveryWatchId;

        public event EventHandler<PlayOnlineCharacter>? CharacterDetected;
        public event EventHandler<PlayOnlineCharacter>? CharacterUpdated;
        public event EventHandler<int>? CharacterRemoved;

        public PlayOnlineMonitorService(ILoggingService loggingService, IProcessManagementService processManagementService, IUiDispatcher uiDispatcher)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _processManagementService = processManagementService ?? throw new ArgumentNullException(nameof(processManagementService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            
            // Subscribe to global process events
            _processManagementService.ProcessDetected += OnProcessDetected;
            _processManagementService.ProcessTerminated += OnProcessTerminated;
            _processManagementService.ProcessUpdated += OnProcessUpdated;
            _processManagementService.WindowTitleChanged += OnWindowTitleChanged;
        }

        public async Task<List<PlayOnlineCharacter>> GetRunningCharactersAsync()
        {
            await RefreshCharacterDataAsync();
            lock (_lockObject)
            {
                return new List<PlayOnlineCharacter>(_characters);
            }
        }

        public async Task<bool> ActivateCharacterWindowAsync(PlayOnlineCharacter character)
        {
            if (character == null || character.WindowHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync($"Cannot activate character: null character or invalid window handle", "PlayOnlineMonitorService");
                return false;
            }

            try
            {
                await _loggingService.LogInfoAsync($"Attempting to activate window for character: {character.DisplayName} (Handle: {character.WindowHandle:X8}, PID: {character.ProcessId})", "PlayOnlineMonitorService");

                // Use shared process management service for activation
                var success = await _processManagementService.ActivateWindowAsync(character.WindowHandle, 5000);

                if (success)
                {
                    // Update active status for all characters
                    lock (_lockObject)
                    {
                        foreach (var ch in _characters)
                        {
                            ch.IsActive = ch.ProcessId == character.ProcessId;
                        }
                    }
                    
                    await _loggingService.LogInfoAsync($"Successfully activated window for {character.DisplayName}", "PlayOnlineMonitorService");
                }
                else
                {
                    await _loggingService.LogWarningAsync($"Failed to activate window for {character.DisplayName}", "PlayOnlineMonitorService");
                }

                return success;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error activating window for {character.DisplayName}", ex, "PlayOnlineMonitorService");
                return false;
            }
        }

        public async Task RefreshCharacterDataAsync()
        {
            if (_disposed) return;

            try
            {
                // Get PlayOnline/FFXI processes using shared service
                var processes = await _processManagementService.GetProcessesByNamesAsync(_targetProcessNames);
                var currentCharacters = new List<PlayOnlineCharacter>();

                foreach (var processInfo in processes)
                {
                    try
                    {
                        // Create characters from process windows
                        foreach (var window in processInfo.Windows)
                        {
                            var character = CreateCharacterFromWindow(window, processInfo);
                            if (character != null)
                            {
                                currentCharacters.Add(character);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogDebugAsync($"Error processing {processInfo.ProcessName}: {ex.Message}", "PlayOnlineMonitorService");
                    }
                }

                // Update character list
                await UpdateCharacterListAsync(currentCharacters);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error refreshing character data", ex, "PlayOnlineMonitorService");
            }
        }

        public void StartMonitoring()
        {
            if (!_isMonitoring)
            {
                _isMonitoring = true;
                
                // Register discovery for PlayOnline/FFXI processes
                try
                {
                    _discoveryWatchId = _processManagementService.RegisterDiscoveryWatch(new DiscoveryFilter
                    {
                        IncludeNames = _targetProcessNames
                    });
                }
                catch { }
                
                // Start global monitoring if not already started
                _processManagementService.StartGlobalMonitoring(TimeSpan.FromSeconds(3));

                // Start lightweight title refresh timer
                // Title changes will be driven by WindowTitleChanged event; fallback timer not required.
                
                _loggingService.LogInfoAsync("Started PlayOnline character monitoring", "PlayOnlineMonitorService");
            }
        }

        public void StopMonitoring()
        {
            if (_isMonitoring)
            {
                _isMonitoring = false;
                try
                {
                    if (_discoveryWatchId.HasValue)
                    {
                        _processManagementService.UnregisterDiscoveryWatch(_discoveryWatchId.Value);
                        _discoveryWatchId = null;
                    }
                }
                catch { }
                // No timer cleanup needed if not used
                _loggingService.LogInfoAsync("Stopped PlayOnline character monitoring", "PlayOnlineMonitorService");
            }
        }

        #region Private Methods

        private void RaiseCharacterUpdated(PlayOnlineCharacter character)
        {
            _uiDispatcher.BeginInvoke(() => CharacterUpdated?.Invoke(this, character));
        }

        private bool UpdateTitleIfChanged(PlayOnlineCharacter character, string newTitle)
        {
            if (!string.Equals(character.WindowTitle, newTitle, StringComparison.Ordinal))
            {
                character.WindowTitle = newTitle;
                RaiseCharacterUpdated(character);
                return true;
            }
            return false;
        }

        private void OnProcessDetected(object? sender, ProcessInfo processInfo)
        {
            if (!_isMonitoring || !IsTargetProcess(processInfo)) return;

            // Explicitly track this PID for faster updates
            try { _processManagementService.TrackPid(processInfo.ProcessId); } catch { }

            Task.Run(async () =>
            {
                try
                {
                    await AddOrUpdateCharactersForProcessAsync(processInfo.ProcessId);
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Error handling process detection: {ex.Message}", "PlayOnlineMonitorService");
                }
            });
        }

        private void OnProcessTerminated(object? sender, ProcessInfo processInfo)
        {
            if (!IsTargetProcess(processInfo)) return;

            try { _processManagementService.UntrackPid(processInfo.ProcessId); } catch { }

            lock (_lockObject)
            {
                var charactersToRemove = _characters
                    .Where(c => c.ProcessId == processInfo.ProcessId)
                    .ToList();

                foreach (var character in charactersToRemove)
                {
                    _characters.Remove(character);
                    CharacterRemoved?.Invoke(this, character.ProcessId);
                }
            }
        }

        private void OnProcessUpdated(object? sender, ProcessInfo processInfo)
        {
            if (!_isMonitoring || !IsTargetProcess(processInfo)) return;

            // Use the periodic process update as a safe fallback to refresh titles
            _uiDispatcher.BeginInvoke(() =>
            {
                lock (_lockObject)
                {
                    var existingCharacters = _characters
                        .Where(c => c.ProcessId == processInfo.ProcessId)
                        .ToList();

                    foreach (var character in existingCharacters)
                    {
                        character.LastSeen = processInfo.LastSeen;

                        var matchingWindow = processInfo.Windows.FirstOrDefault(w => w.Handle == character.WindowHandle);
                        if (matchingWindow != null)
                        {
                            UpdateTitleIfChanged(character, matchingWindow.Title);
                        }
                    }
                }
            });
        }

        private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
        {
            if (!_isMonitoring) return;
            // Update on UI thread to ensure WPF binding receives notifications safely
            _uiDispatcher.BeginInvoke(async () =>
            {
                PlayOnlineCharacter? updated = null;
                lock (_lockObject)
                {
                    // First try exact window handle match
                    updated = _characters.FirstOrDefault(c => c.ProcessId == e.ProcessId && c.WindowHandle == e.WindowHandle);
                    
                    if (updated != null)
                    {
                        UpdateTitleIfChanged(updated, e.NewTitle);
                        return;
                    }

                    // No exact handle match. Try to map by PID if there's a single character for this PID
                    var byPid = _characters.Where(c => c.ProcessId == e.ProcessId).ToList();
                    if (byPid.Count == 1)
                    {
                        updated = byPid[0];
                        if (updated.WindowHandle != e.WindowHandle || !string.Equals(updated.WindowTitle, e.NewTitle, StringComparison.Ordinal))
                        {
                            updated.WindowHandle = e.WindowHandle;
                            UpdateTitleIfChanged(updated, e.NewTitle);
                        }
                        return;
                    }
                }

                // Throttled fallback: refresh characters for this PID to align handles/titles
                if (FFXIManager.Utilities.Throttle.ShouldRun($"pid:{e.ProcessId}", TitleRefreshThrottle))
                {
                    try { await AddOrUpdateCharactersForProcessAsync(e.ProcessId); } catch { }
                }
            });
        }

        private bool IsTargetProcess(ProcessInfo processInfo)
        {
            return FFXIManager.Utilities.ProcessFilters.MatchesProcessName(processInfo.ProcessName, _targetProcessNames);
        }

        private async Task AddOrUpdateCharactersForProcessAsync(int processId)
        {
            try
            {
                var processInfo = await _processManagementService.GetProcessByIdAsync(processId);
                if (processInfo == null) return;

                var newCharacters = new List<PlayOnlineCharacter>();
                foreach (var window in processInfo.Windows)
                {
                    var ch = CreateCharacterFromWindow(window, processInfo);
                    if (ch != null) newCharacters.Add(ch);
                }

                await UpdateCharacterListForProcessAsync(processId, newCharacters);
            }
            catch (Exception ex)
            {
                await _loggingService.LogDebugAsync($"Error updating characters for PID {processId}: {ex.Message}", "PlayOnlineMonitorService");
            }
        }

        private async Task UpdateCharacterListForProcessAsync(int processId, List<PlayOnlineCharacter> newCharacters)
        {
            if (_disposed) return;

            var added = new List<PlayOnlineCharacter>();
            var updated = new List<PlayOnlineCharacter>();
            var removed = new List<int>();

            lock (_lockObject)
            {
                // Add or update characters for this process only
                foreach (var newChar in newCharacters)
                {
                    var existing = _characters.Find(c => c.ProcessId == newChar.ProcessId && c.WindowHandle == newChar.WindowHandle);
                    if (existing == null)
                    {
                        _characters.Add(newChar);
                        added.Add(newChar);
                    }
                    else
                    {
                        bool hasChanges = false;
                        if (existing.WindowTitle != newChar.WindowTitle)
                        {
                            existing.WindowTitle = newChar.WindowTitle;
                            hasChanges = true;
                        }
                        existing.LastSeen = newChar.LastSeen;
                        if (hasChanges)
                        {
                            updated.Add(existing);
                        }
                    }
                }

                // Remove characters for this process that are no longer present
                var existingForPid = _characters.Where(c => c.ProcessId == processId).ToList();
                foreach (var existing in existingForPid)
                {
                    var stillExists = newCharacters.Any(c => c.ProcessId == existing.ProcessId && c.WindowHandle == existing.WindowHandle);
                    if (!stillExists)
                    {
                        removed.Add(existing.ProcessId);
                        _characters.Remove(existing);
                    }
                }
            }

            // Fire events
            foreach (var character in added)
            {
                CharacterDetected?.Invoke(this, character);
                await _loggingService.LogInfoAsync($"New character detected: {character.DisplayName}", "PlayOnlineMonitorService");
            }
            foreach (var character in updated)
            {
                RaiseCharacterUpdated(character);
            }
            foreach (var pid in removed.Distinct())
            {
                CharacterRemoved?.Invoke(this, pid);
                await _loggingService.LogInfoAsync($"Character removed: Process {pid}", "PlayOnlineMonitorService");
            }
        }

        private PlayOnlineCharacter? CreateCharacterFromWindow(WindowInfo window, ProcessInfo processInfo)
        {
            try
            {
                var character = new PlayOnlineCharacter
                {
                    ProcessId = processInfo.ProcessId,
                    WindowHandle = window.Handle,
                    WindowTitle = window.Title,
                    CharacterName = string.Empty,
                    ServerName = string.Empty,
                    ProcessName = processInfo.ProcessName,
                    LastSeen = processInfo.LastSeen
                };

                return character;
            }
            catch (Exception ex)
            {
                _loggingService.LogDebugAsync($"Error creating character from window: {ex.Message}", "PlayOnlineMonitorService");
                return null;
            }
        }


        private async Task UpdateCharacterListAsync(List<PlayOnlineCharacter> newCharacters)
        {
            if (_disposed) return;

            var added = new List<PlayOnlineCharacter>();
            var updated = new List<PlayOnlineCharacter>();
            var removed = new List<int>();

            lock (_lockObject)
            {
                // Find new characters (global refresh)
                foreach (var newChar in newCharacters)
                {
                    var existing = _characters.Find(c => c.ProcessId == newChar.ProcessId && c.WindowHandle == newChar.WindowHandle);
                    if (existing == null)
                    {
                        _characters.Add(newChar);
                        added.Add(newChar);
                    }
                    else
                    {
                        // Update existing character
                        bool hasChanges = false;
                        
                        if (existing.WindowTitle != newChar.WindowTitle)
                        {
                            existing.WindowTitle = newChar.WindowTitle;
                            hasChanges = true;
                        }

                        existing.LastSeen = newChar.LastSeen;
                        
                        if (hasChanges)
                        {
                            updated.Add(existing);
                        }
                    }
                }

                // Find removed characters across all processes (global refresh)
                for (int i = _characters.Count - 1; i >= 0; i--)
                {
                    var existing = _characters[i];
                    var stillExists = newCharacters.Any(c => c.ProcessId == existing.ProcessId && c.WindowHandle == existing.WindowHandle);
                    
                    if (!stillExists)
                    {
                        removed.Add(existing.ProcessId);
                        _characters.RemoveAt(i);
                    }
                }
            }

            // Fire events for significant changes
            foreach (var character in added)
            {
                CharacterDetected?.Invoke(this, character);
                await _loggingService.LogInfoAsync($"New character detected: {character.DisplayName}", "PlayOnlineMonitorService");
            }

            foreach (var character in updated)
            {
                RaiseCharacterUpdated(character);
            }

            foreach (var processId in removed.Distinct())
            {
                CharacterRemoved?.Invoke(this, processId);
                await _loggingService.LogInfoAsync($"Character removed: Process {processId}", "PlayOnlineMonitorService");
            }
        }

        #endregion

        // Event-driven title updates handled by OnWindowTitleChanged

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();
            
            // Unsubscribe from process events
            _processManagementService.ProcessDetected -= OnProcessDetected;
            _processManagementService.ProcessTerminated -= OnProcessTerminated;
            _processManagementService.ProcessUpdated -= OnProcessUpdated;
            _processManagementService.WindowTitleChanged -= OnWindowTitleChanged;

            GC.SuppressFinalize(this);
        }
    }
}