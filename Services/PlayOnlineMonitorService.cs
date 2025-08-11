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
        private readonly List<PlayOnlineCharacter> _characters = new();
        private readonly object _lockObject = new();
        private bool _isMonitoring;
        private bool _disposed;

        // Target process names for PlayOnline/FFXI monitoring
        private readonly string[] _targetProcessNames = { "pol", "ffxi", "PlayOnlineViewer" };

        public event EventHandler<PlayOnlineCharacter>? CharacterDetected;
        public event EventHandler<PlayOnlineCharacter>? CharacterUpdated;
        public event EventHandler<int>? CharacterRemoved;

        public PlayOnlineMonitorService(ILoggingService loggingService, IProcessManagementService processManagementService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _processManagementService = processManagementService ?? throw new ArgumentNullException(nameof(processManagementService));
            
            // Subscribe to global process events
            _processManagementService.ProcessDetected += OnProcessDetected;
            _processManagementService.ProcessTerminated += OnProcessTerminated;
            _processManagementService.ProcessUpdated += OnProcessUpdated;
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
                
                // Start global monitoring if not already started
                _processManagementService.StartGlobalMonitoring(TimeSpan.FromSeconds(3));
                
                _loggingService.LogInfoAsync("Started PlayOnline character monitoring", "PlayOnlineMonitorService");
            }
        }

        public void StopMonitoring()
        {
            if (_isMonitoring)
            {
                _isMonitoring = false;
                _loggingService.LogInfoAsync("Stopped PlayOnline character monitoring", "PlayOnlineMonitorService");
            }
        }

        #region Private Methods

        private void OnProcessDetected(object? sender, ProcessInfo processInfo)
        {
            if (!_isMonitoring || !IsTargetProcess(processInfo)) return;
            
            Task.Run(async () =>
            {
                try
                {
                    await RefreshCharacterDataAsync();
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

            lock (_lockObject)
            {
                var existingCharacters = _characters
                    .Where(c => c.ProcessId == processInfo.ProcessId)
                    .ToList();

                foreach (var character in existingCharacters)
                {
                    character.LastSeen = processInfo.LastSeen;
                    
                    // Update window information if needed
                    var matchingWindow = processInfo.Windows
                        .FirstOrDefault(w => w.Handle == character.WindowHandle);
                    
                    if (matchingWindow != null)
                    {
                        character.WindowTitle = matchingWindow.Title;
                        CharacterUpdated?.Invoke(this, character);
                    }
                }
            }
        }

        private bool IsTargetProcess(ProcessInfo processInfo)
        {
            return _targetProcessNames.Contains(processInfo.ProcessName, StringComparer.OrdinalIgnoreCase);
        }

        private PlayOnlineCharacter? CreateCharacterFromWindow(WindowInfo window, ProcessInfo processInfo)
        {
            try
            {
                // Parse character name from window title
                var (characterName, serverName) = ParseCharacterFromTitle(window.Title);
                
                var character = new PlayOnlineCharacter
                {
                    ProcessId = processInfo.ProcessId,
                    WindowHandle = window.Handle,
                    WindowTitle = window.Title,
                    CharacterName = characterName,
                    ServerName = serverName,
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

        private (string CharacterName, string ServerName) ParseCharacterFromTitle(string windowTitle)
        {
            var characterName = string.Empty;
            var serverName = string.Empty;

            if (string.IsNullOrEmpty(windowTitle))
                return (characterName, serverName);

            try
            {
                // Remove common prefixes first
                var title = windowTitle
                    .Replace("FINAL FANTASY XI - ", "")
                    .Replace("PlayOnline Viewer - ", "")
                    .Replace("FFXI - ", "")
                    .Trim();

                // Look for pattern: [Character] - [Server]
                if (title.Contains(" - "))
                {
                    var parts = title.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
                    
                    if (parts.Length >= 2)
                    {
                        characterName = parts[0].Trim();
                        serverName = parts[1].Trim();
                        
                        // Handle cases where server name might be "PlayOnline" or similar
                        if (serverName.Equals("PlayOnline", StringComparison.OrdinalIgnoreCase) ||
                            serverName.Equals("Viewer", StringComparison.OrdinalIgnoreCase))
                        {
                            serverName = string.Empty;
                        }
                    }
                    else if (parts.Length == 1)
                    {
                        characterName = parts[0].Trim();
                    }
                }
                else
                {
                    // No " - " separator, assume entire title is character name
                    characterName = title;
                }

                // Clean up bracketed names [CharacterName] -> CharacterName
                characterName = characterName.Trim('[', ']', ' ');
                serverName = serverName.Trim('[', ']', ' ');

                // If character name is still empty, use the window title
                if (string.IsNullOrEmpty(characterName))
                {
                    characterName = windowTitle;
                }
            }
            catch (Exception ex)
            {
                // If parsing fails, just use the window title as character name
                characterName = windowTitle;
                System.Diagnostics.Debug.WriteLine($"Error parsing window title '{windowTitle}': {ex.Message}");
            }

            return (characterName, serverName);
        }

        private async Task UpdateCharacterListAsync(List<PlayOnlineCharacter> newCharacters)
        {
            if (_disposed) return;

            var added = new List<PlayOnlineCharacter>();
            var updated = new List<PlayOnlineCharacter>();
            var removed = new List<int>();

            lock (_lockObject)
            {
                // Find new characters
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
                        
                        if (existing.CharacterName != newChar.CharacterName)
                        {
                            existing.CharacterName = newChar.CharacterName;
                            hasChanges = true;
                        }
                        
                        if (existing.ServerName != newChar.ServerName)
                        {
                            existing.ServerName = newChar.ServerName;
                            hasChanges = true;
                        }

                        existing.LastSeen = newChar.LastSeen;
                        
                        if (hasChanges)
                        {
                            updated.Add(existing);
                        }
                    }
                }

                // Find removed characters
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

            foreach (var character in updated.Where(c => !string.IsNullOrEmpty(c.CharacterName)))
            {
                CharacterUpdated?.Invoke(this, character);
            }

            foreach (var processId in removed.Distinct())
            {
                CharacterRemoved?.Invoke(this, processId);
                await _loggingService.LogInfoAsync($"Character removed: Process {processId}", "PlayOnlineMonitorService");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();
            
            // Unsubscribe from process events
            _processManagementService.ProcessDetected -= OnProcessDetected;
            _processManagementService.ProcessTerminated -= OnProcessTerminated;
            _processManagementService.ProcessUpdated -= OnProcessUpdated;
        }
    }
}