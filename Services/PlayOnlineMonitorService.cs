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
        
        // Events
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterDetected;
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterUpdated;
        public event EventHandler<PlayOnlineCharacterEventArgs>? CharacterRemoved;
        
        // Target process names for PlayOnline/FFXI
        private readonly string[] _targetProcessNames = { "pol", "ffxi", "PlayOnlineViewer" };
        
        public PlayOnlineMonitorService(
            IUnifiedMonitoringService unifiedMonitoring,
            ILoggingService logging,
            IUiDispatcher uiDispatcher)
        {
            _unifiedMonitoring = unifiedMonitoring ?? throw new ArgumentNullException(nameof(unifiedMonitoring));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            
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
            
            try
            {
                // Use ProcessManagementService directly for window activation
                var processManagement = ServiceLocator.ProcessManagementService;
                var success = await processManagement.ActivateWindowAsync(character.WindowHandle, 5000);
                
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
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error activating window for {character.DisplayName}", ex, "PlayOnlineMonitorService");
                return false;
            }
        }
        
        public async Task RefreshCharactersAsync()
        {
            // The UnifiedMonitoringService handles all refreshing internally
            // We just need to get the latest data
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
                IsActive = false // Will be set by UI if needed
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
            if (e.MonitorId != _monitorId) return;
            
            // Convert and fire events for each window
            foreach (var window in e.Process.Windows)
            {
                var character = ConvertToCharacter(e.Process, window);
                _uiDispatcher.BeginInvoke(() => CharacterDetected?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
            }
            
            // If no windows yet, fire event for the process itself
            if (e.Process.Windows.Count == 0)
            {
                var character = ConvertToCharacter(e.Process, null);
                _uiDispatcher.BeginInvoke(() => CharacterDetected?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
            }
            
            _ = _logging.LogInfoAsync($"PlayOnline process detected: {e.Process.ProcessName} (PID: {e.Process.ProcessId})", 
                "PlayOnlineMonitorService");
        }
        
        private void OnProcessUpdated(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId) return;
            
            _ = _logging.LogInfoAsync($"[PlayOnline] Process updated: {e.Process.ProcessName} (PID: {e.Process.ProcessId}) with {e.Process.Windows.Count} windows", 
                "PlayOnlineMonitorService");
            
            // Fire update events for windows with title changes
            foreach (var window in e.Process.Windows)
            {
                _ = _logging.LogInfoAsync($"[PlayOnline] Firing CharacterUpdated for window: '{window.Title}'  (Handle: 0x{window.Handle.ToInt64():X})", 
                    "PlayOnlineMonitorService");
                
                var character = ConvertToCharacter(e.Process, window);
                _uiDispatcher.BeginInvoke(() => CharacterUpdated?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
            }
        }
        
        private void OnProcessRemoved(object? sender, MonitoredProcessEventArgs e)
        {
            if (e.MonitorId != _monitorId) return;
            
            // Need to create a character object for removal
            var character = new PlayOnlineCharacter 
            { 
                ProcessId = e.Process.ProcessId,
                ProcessName = e.Process.ProcessName
            };
            _uiDispatcher.BeginInvoke(() => CharacterRemoved?.Invoke(this, new PlayOnlineCharacterEventArgs(character)));
            
            _ = _logging.LogInfoAsync($"PlayOnline process removed: {e.Process.ProcessName} (PID: {e.Process.ProcessId})", 
                "PlayOnlineMonitorService");
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopMonitoring();
            
            // Unregister from unified monitoring
            _unifiedMonitoring.UnregisterMonitor(_monitorId);
            
            // Unsubscribe from events
            _unifiedMonitoring.ProcessDetected -= OnProcessDetected;
            _unifiedMonitoring.ProcessUpdated -= OnProcessUpdated;
            _unifiedMonitoring.ProcessRemoved -= OnProcessRemoved;
            
            GC.SuppressFinalize(this);
        }
    }
}
