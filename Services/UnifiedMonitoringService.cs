using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// THE single source of truth for all process monitoring
    /// Owns WMI watchers, WinEvent hooks, and all process state
    /// </summary>
    public class UnifiedMonitoringService : IUnifiedMonitoringService, IDisposable
    {
        private readonly IProcessUtilityService _processUtility;
        private readonly ILoggingService _logging;
        private readonly IUiDispatcher _uiDispatcher;
        
        private readonly Dictionary<Guid, MonitoringProfile> _profiles = new();
        private readonly Dictionary<int, MonitoredProcess> _processes = new();
        private readonly object _lock = new();
        
        private bool _isMonitoring;
        private bool _disposed;
        private Timer? _windowTitleTimer;
        private Timer? _periodicScanTimer;
        
        // WMI watchers for process lifecycle
        private ManagementEventWatcher? _processStartWatcher;
        private ManagementEventWatcher? _processStopWatcher;
        
        // For window title tracking - we'll use polling for reliability
        private readonly Dictionary<IntPtr, string> _lastWindowTitles = new();
        
        private const int WINDOW_TITLE_POLL_MS = 500; // Poll every 500ms for window title changes
        private const int PERIODIC_SCAN_MS = 10000;   // Safety scan every 10 seconds
        
        public bool IsMonitoring => _isMonitoring;
        
        // Per-monitor events
        public event EventHandler<MonitoredProcessEventArgs>? ProcessDetected;
        public event EventHandler<MonitoredProcessEventArgs>? ProcessUpdated;
        public event EventHandler<MonitoredProcessEventArgs>? ProcessRemoved;
        
        // Global events
        public event EventHandler<MonitoredProcess>? GlobalProcessDetected;
        public event EventHandler<MonitoredProcess>? GlobalProcessUpdated;
        public event EventHandler<int>? GlobalProcessRemoved;
        
        public UnifiedMonitoringService(
            IProcessUtilityService processUtility,
            ILoggingService logging,
            IUiDispatcher uiDispatcher)
        {
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }
        
        #region Public Methods
        
        public Guid RegisterMonitor(MonitoringProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            
            lock (_lock)
            {
                _profiles[profile.Id] = profile;
                
                _ = _logging.LogInfoAsync($"Registered monitor '{profile.Name}' (ID: {profile.Id}) for processes: {string.Join(", ", profile.ProcessNames)}", 
                    "UnifiedMonitoringService");
                
                // If monitoring is active, scan for existing processes
                if (_isMonitoring)
                {
                    Task.Run(() => ScanForExistingProcessesAsync(profile.Id));
                }
                
                return profile.Id;
            }
        }
        
        public void UnregisterMonitor(Guid monitorId)
        {
            lock (_lock)
            {
                if (_profiles.Remove(monitorId))
                {
                    // Remove this monitor from all processes
                    foreach (var process in _processes.Values)
                    {
                        process.MonitorIds.Remove(monitorId);
                        process.ContextData.Remove(monitorId);
                    }
                    
                    // Clean up processes that no longer have any monitors
                    var toRemove = _processes
                        .Where(kvp => kvp.Value.MonitorIds.Count == 0)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var pid in toRemove)
                    {
                        _processes.Remove(pid);
                    }
                    
                    _ = _logging.LogInfoAsync($"Unregistered monitor {monitorId}", "UnifiedMonitoringService");
                }
            }
        }
        
        public void UpdateMonitorProfile(Guid monitorId, MonitoringProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            
            lock (_lock)
            {
                if (_profiles.ContainsKey(monitorId))
                {
                    profile.Id = monitorId;
                    _profiles[monitorId] = profile;
                    
                    _ = _logging.LogInfoAsync($"Updated monitor profile {monitorId}", "UnifiedMonitoringService");
                    
                    // Rescan for this profile
                    if (_isMonitoring)
                    {
                        Task.Run(() => ScanForExistingProcessesAsync(monitorId));
                    }
                }
            }
        }
        
        public async Task<List<MonitoredProcess>> GetProcessesAsync(Guid monitorId)
        {
            await Task.Yield();
            
            lock (_lock)
            {
                return _processes.Values
                    .Where(p => p.MonitorIds.Contains(monitorId))
                    .Select(CloneProcess)
                    .ToList();
            }
        }
        
        public async Task<MonitoredProcess?> GetProcessAsync(Guid monitorId, int processId)
        {
            await Task.Yield();
            
            lock (_lock)
            {
                if (_processes.TryGetValue(processId, out var process) && 
                    process.MonitorIds.Contains(monitorId))
                {
                    return CloneProcess(process);
                }
                return null;
            }
        }
        
        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            
            _isMonitoring = true;
            
            // Start WMI watchers for process lifecycle
            StartWmiWatchers();
            
            // Start window title polling timer
            _windowTitleTimer?.Dispose();
            _windowTitleTimer = new Timer(
                WindowTitlePollCallback,
                null,
                0,
                WINDOW_TITLE_POLL_MS);
            
            // Start periodic safety scan
            _periodicScanTimer?.Dispose();
            _periodicScanTimer = new Timer(
                PeriodicScanCallback,
                null,
                PERIODIC_SCAN_MS,
                PERIODIC_SCAN_MS);
            
            // Initial scan for all profiles
            Task.Run(() => InitialScanAsync());
            
            _ = _logging.LogInfoAsync("Started unified monitoring", "UnifiedMonitoringService");
        }
        
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            
            _isMonitoring = false;
            
            // Stop timers
            _windowTitleTimer?.Dispose();
            _windowTitleTimer = null;
            
            _periodicScanTimer?.Dispose();
            _periodicScanTimer = null;
            
            // Stop WMI watchers
            StopWmiWatchers();
            
            _ = _logging.LogInfoAsync("Stopped unified monitoring", "UnifiedMonitoringService");
        }
        
        #endregion
        
        #region WMI Monitoring
        
        private void StartWmiWatchers()
        {
            try
            {
                // Process creation watcher
                var creationQuery = new WqlEventQuery
                {
                    EventClassName = "__InstanceCreationEvent",
                    WithinInterval = TimeSpan.FromSeconds(1),
                    Condition = "TargetInstance ISA 'Win32_Process'"
                };
                _processStartWatcher = new ManagementEventWatcher(new ManagementScope("root\\CIMV2"), creationQuery);
                _processStartWatcher.EventArrived += OnWmiProcessCreated;
                _processStartWatcher.Start();
                
                // Process deletion watcher
                var deletionQuery = new WqlEventQuery
                {
                    EventClassName = "__InstanceDeletionEvent",
                    WithinInterval = TimeSpan.FromSeconds(1),
                    Condition = "TargetInstance ISA 'Win32_Process'"
                };
                _processStopWatcher = new ManagementEventWatcher(new ManagementScope("root\\CIMV2"), deletionQuery);
                _processStopWatcher.EventArrived += OnWmiProcessDeleted;
                _processStopWatcher.Start();
                
                _ = _logging.LogInfoAsync("WMI process watchers started", "UnifiedMonitoringService");
            }
            catch (Exception ex)
            {
                _ = _logging.LogWarningAsync($"Failed to start WMI watchers: {ex.Message}", "UnifiedMonitoringService");
            }
        }
        
        private void StopWmiWatchers()
        {
            try
            {
                if (_processStartWatcher != null)
                {
                    _processStartWatcher.EventArrived -= OnWmiProcessCreated;
                    _processStartWatcher.Stop();
                    _processStartWatcher.Dispose();
                    _processStartWatcher = null;
                }
                
                if (_processStopWatcher != null)
                {
                    _processStopWatcher.EventArrived -= OnWmiProcessDeleted;
                    _processStopWatcher.Stop();
                    _processStopWatcher.Dispose();
                    _processStopWatcher = null;
                }
                
                _ = _logging.LogInfoAsync("WMI process watchers stopped", "UnifiedMonitoringService");
            }
            catch (Exception ex)
            {
                _ = _logging.LogDebugAsync($"Error stopping WMI watchers: {ex.Message}", "UnifiedMonitoringService");
            }
        }
        
        private void OnWmiProcessCreated(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (!_isMonitoring) return;
                
                var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
                if (target == null) return;
                
                var nameObj = target["Name"];
                var pidObj = target["ProcessId"];
                if (nameObj == null || pidObj == null) return;
                
                var name = nameObj.ToString() ?? string.Empty;
                var pid = Convert.ToInt32((uint)pidObj);
                
                // Check if any profile is interested in this process
                List<MonitoringProfile> matchingProfiles;
                lock (_lock)
                {
                    matchingProfiles = _profiles.Values
                        .Where(p => ProcessMatchesProfile(name, p))
                        .ToList();
                }
                
                if (matchingProfiles.Count > 0)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var processInfo = await _processUtility.GetProcessInfoAsync(pid);
                            if (processInfo != null)
                            {
                                foreach (var profile in matchingProfiles)
                                {
                                    await AddOrUpdateProcessAsync(processInfo, profile);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await _logging.LogDebugAsync($"Error handling process creation: {ex.Message}", 
                                "UnifiedMonitoringService");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogDebugAsync($"Exception in WMI process creation handler: {ex.Message}", 
                    "UnifiedMonitoringService");
            }
        }
        
        private void OnWmiProcessDeleted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (!_isMonitoring) return;
                
                var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
                if (target == null) return;
                
                var pidObj = target["ProcessId"];
                if (pidObj == null) return;
                
                var pid = Convert.ToInt32((uint)pidObj);
                
                MonitoredProcess? process;
                List<MonitoringProfile> affectedProfiles = new();
                
                lock (_lock)
                {
                    if (!_processes.TryGetValue(pid, out process))
                        return;
                    
                    // Get affected profiles before removing
                    foreach (var monitorId in process.MonitorIds)
                    {
                        if (_profiles.TryGetValue(monitorId, out var profile))
                        {
                            affectedProfiles.Add(profile);
                        }
                    }
                    
                    // Remove the process
                    _processes.Remove(pid);
                    
                    // Clear window title tracking
                    foreach (var window in process.Windows)
                    {
                        _lastWindowTitles.Remove(window.Handle);
                    }
                }
                
                // Fire removal events for each affected profile
                foreach (var profile in affectedProfiles)
                {
                    FireProcessRemoved(process, profile);
                }
                
                _uiDispatcher.BeginInvoke(() =>
                {
                    GlobalProcessRemoved?.Invoke(this, pid);
                });
                
                _ = _logging.LogInfoAsync($"Process terminated: {process.ProcessName} (PID: {pid})", 
                    "UnifiedMonitoringService");
            }
            catch (Exception ex)
            {
                _ = _logging.LogDebugAsync($"Exception in WMI process deletion handler: {ex.Message}", 
                    "UnifiedMonitoringService");
            }
        }
        
        #endregion
        
        #region Window Title Polling
        
        private void WindowTitlePollCallback(object? state)
        {
            if (!_isMonitoring || _disposed) return;
            
            Task.Run(async () =>
            {
                try
                {
                    List<MonitoredProcess> processesToCheck;
                    lock (_lock)
                    {
                        // Get processes that need window title tracking
                        processesToCheck = _processes.Values
                            .Where(p => p.MonitorIds.Any(id => 
                                _profiles.TryGetValue(id, out var profile) && profile.TrackWindowTitles))
                            .ToList();
                    }
                    
                    foreach (var process in processesToCheck)
                    {
                        await UpdateProcessWindowsAsync(process);
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogDebugAsync($"Error in window title polling: {ex.Message}", 
                        "UnifiedMonitoringService");
                }
            });
        }
        
        private async Task UpdateProcessWindowsAsync(MonitoredProcess process)
        {
            try
            {
                var windows = await _processUtility.GetProcessWindowsAsync(process.ProcessId);
                bool hasChanges = false;
                List<MonitoringProfile> affectedProfiles = new();
                
                lock (_lock)
                {
                    // Check for new or updated windows
                    foreach (var window in windows)
                    {
                        var existingWindow = process.Windows.FirstOrDefault(w => w.Handle == window.Handle);
                        
                        if (existingWindow != null)
                        {
                            // Check if title changed
                            if (!_lastWindowTitles.TryGetValue(window.Handle, out var lastTitle) ||
                                lastTitle != window.Title)
                            {
                                existingWindow.Title = window.Title;
                                existingWindow.LastTitleUpdate = DateTime.UtcNow;
                                _lastWindowTitles[window.Handle] = window.Title;
                                hasChanges = true;
                                
                                _ = _logging.LogInfoAsync($"[Unified] Window title changed for PID {process.ProcessId}: '{window.Title}' (Handle: 0x{window.Handle.ToInt64():X})", 
                                    "UnifiedMonitoringService");
                            }
                        }
                        else
                        {
                            // New window
                            process.Windows.Add(new MonitoredWindow
                            {
                                Handle = window.Handle,
                                Title = window.Title,
                                IsMainWindow = window.IsMainWindow,
                                IsVisible = window.IsVisible,
                                LastTitleUpdate = DateTime.UtcNow
                            });
                            _lastWindowTitles[window.Handle] = window.Title;
                            hasChanges = true;
                        }
                    }
                    
                    // Remove closed windows
                    var closedWindows = process.Windows
                        .Where(w => !windows.Any(nw => nw.Handle == w.Handle))
                        .ToList();
                    
                    foreach (var closedWindow in closedWindows)
                    {
                        process.Windows.Remove(closedWindow);
                        _lastWindowTitles.Remove(closedWindow.Handle);
                        hasChanges = true;
                    }
                    
                    // Get profiles that track window titles
                    if (hasChanges)
                    {
                        foreach (var monitorId in process.MonitorIds)
                        {
                            if (_profiles.TryGetValue(monitorId, out var profile) && profile.TrackWindowTitles)
                            {
                                affectedProfiles.Add(profile);
                            }
                        }
                    }
                }
                
                // Fire update events if there were changes
                if (hasChanges && affectedProfiles.Count > 0)
                {
                    foreach (var profile in affectedProfiles)
                    {
                        FireProcessUpdated(process, profile);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logging.LogDebugAsync($"Error updating windows for process {process.ProcessId}: {ex.Message}", 
                    "UnifiedMonitoringService");
            }
        }
        
        #endregion
        
        #region Scanning and Detection
        
        private async Task InitialScanAsync()
        {
            try
            {
                await _logging.LogInfoAsync("Starting initial process scan", "UnifiedMonitoringService");
                
                List<Guid> profileIds;
                lock (_lock)
                {
                    profileIds = _profiles.Keys.ToList();
                }
                
                foreach (var profileId in profileIds)
                {
                    await ScanForExistingProcessesAsync(profileId);
                }
                
                await _logging.LogInfoAsync("Completed initial process scan", "UnifiedMonitoringService");
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync("Error during initial scan", ex, "UnifiedMonitoringService");
            }
        }
        
        private async Task ScanForExistingProcessesAsync(Guid profileId)
        {
            try
            {
                MonitoringProfile? profile;
                lock (_lock)
                {
                    if (!_profiles.TryGetValue(profileId, out profile))
                        return;
                }
                
                // Get processes matching this profile
                var processes = await _processUtility.GetProcessesByNamesAsync(profile.ProcessNames);
                
                foreach (var processInfo in processes)
                {
                    await AddOrUpdateProcessAsync(processInfo, profile);
                }
            }
            catch (Exception ex)
            {
                await _logging.LogDebugAsync($"Error scanning for profile {profileId}: {ex.Message}", 
                    "UnifiedMonitoringService");
            }
        }
        
        private async Task AddOrUpdateProcessAsync(ProcessBasicInfo processInfo, MonitoringProfile profile)
        {
            MonitoredProcess? monitoredProcess = null;
            bool isNew = false;
            
            lock (_lock)
            {
                if (!_processes.TryGetValue(processInfo.ProcessId, out monitoredProcess))
                {
                    // Create new monitored process
                    monitoredProcess = new MonitoredProcess
                    {
                        ProcessId = processInfo.ProcessId,
                        ProcessName = processInfo.ProcessName,
                        ExecutablePath = profile.IncludeProcessPath ? processInfo.ExecutablePath : null,
                        StartTime = processInfo.StartTime,
                        LastSeen = DateTime.UtcNow,
                        IsResponding = processInfo.IsResponding
                    };
                    
                    // Add windows if tracking
                    if (profile.TrackWindows)
                    {
                        foreach (var window in processInfo.Windows)
                        {
                            monitoredProcess.Windows.Add(new MonitoredWindow
                            {
                                Handle = window.Handle,
                                Title = profile.TrackWindowTitles ? window.Title : string.Empty,
                                IsMainWindow = window.IsMainWindow,
                                IsVisible = window.IsVisible,
                                LastTitleUpdate = DateTime.UtcNow
                            });
                            
                            if (profile.TrackWindowTitles)
                            {
                                _lastWindowTitles[window.Handle] = window.Title;
                            }
                        }
                    }
                    
                    _processes[processInfo.ProcessId] = monitoredProcess;
                    isNew = true;
                }
                
                // Update process information
                monitoredProcess.LastSeen = DateTime.UtcNow;
                monitoredProcess.IsResponding = processInfo.IsResponding;
                
                // Add this monitor to the process
                if (monitoredProcess.MonitorIds.Add(profile.Id))
                {
                    monitoredProcess.ContextData[profile.Id] = profile.Context;
                }
            }
            
            // Fire events outside of lock
            if (isNew)
            {
                FireProcessDetected(monitoredProcess, profile);
                await _logging.LogInfoAsync($"Process detected: {processInfo.ProcessName} (PID: {processInfo.ProcessId}) for monitor '{profile.Name}'", 
                    "UnifiedMonitoringService");
            }
        }
        
        private void PeriodicScanCallback(object? state)
        {
            if (!_isMonitoring || _disposed) return;
            
            Task.Run(async () =>
            {
                try
                {
                    await _logging.LogDebugAsync("Running periodic safety scan", "UnifiedMonitoringService");
                    
                    // Check for dead processes
                    List<int> deadProcessIds;
                    lock (_lock)
                    {
                        deadProcessIds = _processes.Keys
                            .Where(pid => !_processUtility.IsProcessRunning(pid))
                            .ToList();
                    }
                    
                    foreach (var pid in deadProcessIds)
                    {
                        // Remove dead process directly
                        MonitoredProcess? process;
                        List<MonitoringProfile> affectedProfiles = new();
                        
                        lock (_lock)
                        {
                            if (!_processes.TryGetValue(pid, out process))
                                continue;
                            
                            // Get affected profiles before removing
                            foreach (var monitorId in process.MonitorIds)
                            {
                                if (_profiles.TryGetValue(monitorId, out var profile))
                                {
                                    affectedProfiles.Add(profile);
                                }
                            }
                            
                            // Remove the process
                            _processes.Remove(pid);
                            
                            // Clear window title tracking
                            foreach (var window in process.Windows)
                            {
                                _lastWindowTitles.Remove(window.Handle);
                            }
                        }
                        
                        // Fire removal events for each affected profile
                        if (process != null)
                        {
                            foreach (var profile in affectedProfiles)
                            {
                                FireProcessRemoved(process, profile);
                            }
                            
                            _uiDispatcher.BeginInvoke(() =>
                            {
                                GlobalProcessRemoved?.Invoke(this, pid);
                            });
                            
                            await _logging.LogInfoAsync($"Process terminated (periodic scan): {process.ProcessName} (PID: {pid})", 
                                "UnifiedMonitoringService");
                        }
                    }
                    
                    // Scan for new processes
                    await InitialScanAsync();
                }
                catch (Exception ex)
                {
                    await _logging.LogDebugAsync($"Error in periodic scan: {ex.Message}", 
                        "UnifiedMonitoringService");
                }
            });
        }
        
        #endregion
        
        #region Event Firing
        
        private void FireProcessDetected(MonitoredProcess process, MonitoringProfile profile)
        {
            var args = new MonitoredProcessEventArgs
            {
                MonitorId = profile.Id,
                Process = CloneProcess(process),
                Profile = profile
            };
            
            _uiDispatcher.BeginInvoke(() =>
            {
                ProcessDetected?.Invoke(this, args);
                GlobalProcessDetected?.Invoke(this, args.Process);
            });
        }
        
        private void FireProcessUpdated(MonitoredProcess process, MonitoringProfile profile)
        {
            var args = new MonitoredProcessEventArgs
            {
                MonitorId = profile.Id,
                Process = CloneProcess(process),
                Profile = profile
            };
            
            _uiDispatcher.BeginInvoke(() =>
            {
                ProcessUpdated?.Invoke(this, args);
                GlobalProcessUpdated?.Invoke(this, args.Process);
            });
        }
        
        private void FireProcessRemoved(MonitoredProcess process, MonitoringProfile profile)
        {
            var args = new MonitoredProcessEventArgs
            {
                MonitorId = profile.Id,
                Process = CloneProcess(process),
                Profile = profile
            };
            
            _uiDispatcher.BeginInvoke(() =>
            {
                ProcessRemoved?.Invoke(this, args);
            });
        }
        
        #endregion
        
        #region Helper Methods
        
        private static bool ProcessMatchesProfile(string processName, MonitoringProfile profile)
        {
            var normalized = FFXIManager.Utilities.ProcessFilters.ExtractProcessName(processName);
            
            foreach (var targetName in profile.ProcessNames)
            {
                if (string.Equals(normalized, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private MonitoredProcess CloneProcess(MonitoredProcess process)
        {
            return new MonitoredProcess
            {
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                ExecutablePath = process.ExecutablePath,
                StartTime = process.StartTime,
                LastSeen = process.LastSeen,
                IsResponding = process.IsResponding,
                Windows = process.Windows.Select(w => new MonitoredWindow
                {
                    Handle = w.Handle,
                    Title = w.Title,
                    IsMainWindow = w.IsMainWindow,
                    IsVisible = w.IsVisible,
                    LastTitleUpdate = w.LastTitleUpdate
                }).ToList(),
                MonitorIds = new HashSet<Guid>(process.MonitorIds),
                ContextData = new Dictionary<Guid, object?>(process.ContextData)
            };
        }
        
        #endregion
        
        #region IDisposable
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            StopMonitoring();
            
            GC.SuppressFinalize(this);
        }
        
        #endregion
    }
}

