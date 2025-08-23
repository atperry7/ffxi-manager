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
        private readonly IWindowEventTracker _windowEventTracker;

        private readonly Dictionary<Guid, MonitoringProfile> _profiles = new();
        private readonly Dictionary<int, MonitoredProcess> _processes = new();
        private readonly object _lock = new();

        private bool _isMonitoring;
        private bool _disposed;
        private Timer? _periodicScanTimer;

        // WMI watchers for process lifecycle
        private ManagementEventWatcher? _processStartWatcher;
        private ManagementEventWatcher? _processStopWatcher;

        // **REPLACED**: Old polling architecture removed - now using real-time Win32 event hooks
        private const int SAFETY_SCAN_MS = 30000;    // Safety scan every 30 seconds (minimal fallback)

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
            IUiDispatcher uiDispatcher,
            IWindowEventTracker windowEventTracker)
        {
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _windowEventTracker = windowEventTracker ?? throw new ArgumentNullException(nameof(windowEventTracker));
            
            // Subscribe to real-time window events
            _windowEventTracker.WindowTitleChanged += OnWindowTitleChanged;
            _windowEventTracker.WindowCreated += OnWindowCreated;
            _windowEventTracker.WindowDestroyed += OnWindowDestroyed;
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

            // Start minimal safety scan (much less frequent now)
            _periodicScanTimer?.Dispose();
            _periodicScanTimer = new Timer(
                SafetyScanCallback,
                null,
                SAFETY_SCAN_MS,
                SAFETY_SCAN_MS);

            // Initial scan for all profiles
            Task.Run(() => InitialScanAsync());

            _ = _logging.LogInfoAsync("Started unified monitoring", "UnifiedMonitoringService");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;

            // Stop timers
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

                    // **REMOVED**: Window title tracking now handled by event-driven architecture
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

        #region Real-Time Window Event Handlers

        /// <summary>
        /// Handles real-time window title changes from Win32 event hooks
        /// </summary>
        private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
        {
            if (!_isMonitoring || _disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    MonitoredProcess? process;
                    List<MonitoringProfile> affectedProfiles = new();
                    string? logMessage = null;

                    lock (_lock)
                    {
                        if (!_processes.TryGetValue(e.ProcessId, out process))
                            return; // Process not tracked

                        // Find and update the window
                        var window = process.Windows.FirstOrDefault(w => w.Handle == e.WindowHandle);
                        if (window != null)
                        {
                            var oldTitle = window.Title;
                            
                            // **FIX**: Only update title if the new title is valid
                            // Don't overwrite good titles with empty/null/garbage
                            var shouldUpdate = false;
                            if (!string.IsNullOrWhiteSpace(e.NewTitle) && 
                                !e.NewTitle.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                            {
                                // Good title - always update
                                window.Title = e.NewTitle;
                                window.LastTitleUpdate = DateTime.UtcNow;
                                shouldUpdate = true;
                                logMessage = $"[EVENT-DRIVEN] Window title changed: PID {e.ProcessId}, '{oldTitle}' → '{e.NewTitle}' (Handle: 0x{e.WindowHandle.ToInt64():X})";
                            }
                            else
                            {
                                // Bad title - don't update, but log for debugging
                                logMessage = $"[EVENT-DRIVEN] IGNORED bad title change: PID {e.ProcessId}, '{oldTitle}' → '{e.NewTitle}' (Handle: 0x{e.WindowHandle.ToInt64():X}) - keeping existing title";
                            }

                            // Only fire events if we actually updated something
                            if (shouldUpdate)
                            {
                                // Get affected profiles
                                foreach (var monitorId in process.MonitorIds)
                                {
                                    if (_profiles.TryGetValue(monitorId, out var profile) && profile.TrackWindowTitles)
                                    {
                                        affectedProfiles.Add(profile);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // **FIX**: Only add new windows if they have valid titles
                            if (!string.IsNullOrWhiteSpace(e.NewTitle) && 
                                !e.NewTitle.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                            {
                                // Window not in our tracking - might be newly created
                                process.Windows.Add(new MonitoredWindow
                                {
                                    Handle = e.WindowHandle,
                                    Title = e.NewTitle,
                                    IsMainWindow = false,
                                    IsVisible = true,
                                    LastTitleUpdate = DateTime.UtcNow
                                });

                                logMessage = $"[EVENT-DRIVEN] New window detected: PID {e.ProcessId}, '{e.NewTitle}' (Handle: 0x{e.WindowHandle.ToInt64():X})";

                                // Get affected profiles
                                foreach (var monitorId in process.MonitorIds)
                                {
                                    if (_profiles.TryGetValue(monitorId, out var profile))
                                    {
                                        affectedProfiles.Add(profile);
                                    }
                                }
                            }
                            else
                            {
                                logMessage = $"[EVENT-DRIVEN] IGNORED new window with bad title: PID {e.ProcessId}, '{e.NewTitle}' (Handle: 0x{e.WindowHandle.ToInt64():X})";
                            }
                        }

                        process.LastSeen = DateTime.UtcNow;
                    }

                    // Log outside the lock
                    if (!string.IsNullOrEmpty(logMessage))
                    {
                        await _logging.LogInfoAsync(logMessage, "UnifiedMonitoringService");
                    }

                    // Fire update events
                    foreach (var profile in affectedProfiles)
                    {
                        FireProcessUpdated(process, profile);
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogErrorAsync($"Error handling window title change for PID {e.ProcessId}", ex, "UnifiedMonitoringService");
                }
            });
        }

        /// <summary>
        /// Handles real-time window creation events
        /// </summary>
        private void OnWindowCreated(object? sender, WindowEventArgs e)
        {
            if (!_isMonitoring || _disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await _logging.LogDebugAsync($"[EVENT-DRIVEN] Window created: PID {e.ProcessId}, '{e.WindowTitle}' (Handle: 0x{e.WindowHandle.ToInt64():X})", "UnifiedMonitoringService");
                    // Window creation is handled by title change events
                }
                catch (Exception ex)
                {
                    await _logging.LogErrorAsync($"Error handling window creation for PID {e.ProcessId}", ex, "UnifiedMonitoringService");
                }
            });
        }

        /// <summary>
        /// Handles real-time window destruction events
        /// </summary>
        private void OnWindowDestroyed(object? sender, WindowEventArgs e)
        {
            if (!_isMonitoring || _disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    MonitoredProcess? process;
                    List<MonitoringProfile> affectedProfiles = new();
                    string? logMessage = null;

                    lock (_lock)
                    {
                        if (!_processes.TryGetValue(e.ProcessId, out process))
                            return;

                        // Remove the destroyed window
                        var window = process.Windows.FirstOrDefault(w => w.Handle == e.WindowHandle);
                        if (window != null)
                        {
                            process.Windows.Remove(window);
                            process.LastSeen = DateTime.UtcNow;

                            logMessage = $"[EVENT-DRIVEN] Window destroyed: PID {e.ProcessId}, '{window.Title}' (Handle: 0x{e.WindowHandle.ToInt64():X})";

                            // Get affected profiles
                            foreach (var monitorId in process.MonitorIds)
                            {
                                if (_profiles.TryGetValue(monitorId, out var profile))
                                {
                                    affectedProfiles.Add(profile);
                                }
                            }
                        }
                    }

                    // Log outside the lock
                    if (!string.IsNullOrEmpty(logMessage))
                    {
                        await _logging.LogInfoAsync(logMessage, "UnifiedMonitoringService");
                    }

                    // Fire update events
                    foreach (var profile in affectedProfiles)
                    {
                        FireProcessUpdated(process, profile);
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogErrorAsync($"Error handling window destruction for PID {e.ProcessId}", ex, "UnifiedMonitoringService");
                }
            });
        }

        // **REMOVED**: Old polling methods replaced with real-time event-driven architecture

        #endregion

        #region Removed: Active Window Detection
        // **PERFORMANCE IMPROVEMENT**: Removed 250ms polling for IsActive tracking
        // LastActivated tracking is now handled by HotkeyActivationService when characters are actually activated
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
            string? trackingMessage = null;

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

                            // **REMOVED**: Window title tracking now handled by event-driven architecture
                        }
                    }

                    _processes[processInfo.ProcessId] = monitoredProcess;
                    
                    // **NEW**: Start real-time window event tracking for this process
                    trackingMessage = profile.TrackWindowTitles ? 
                        $"🔍 Starting window event tracking for PID {processInfo.ProcessId} ({processInfo.ProcessName}) - Profile: {profile.Name}" :
                        $"⚠️ Profile '{profile.Name}' has TrackWindowTitles=false - NOT starting event tracking for PID {processInfo.ProcessId}";
                    
                    if (profile.TrackWindowTitles)
                    {
                        _windowEventTracker.StartTrackingProcess(processInfo.ProcessId, processInfo.ProcessName);
                    }
                    
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

            // Log tracking message outside the lock
            if (!string.IsNullOrEmpty(trackingMessage))
            {
                await _logging.LogInfoAsync(trackingMessage, "UnifiedMonitoringService");
            }

            // Fire events outside of lock
            if (isNew)
            {
                FireProcessDetected(monitoredProcess, profile);
                await _logging.LogInfoAsync($"Process detected: {processInfo.ProcessName} (PID: {processInfo.ProcessId}) for monitor '{profile.Name}'",
                    "UnifiedMonitoringService");
            }
        }

        /// <summary>
        /// Minimal safety scan - event-driven architecture with fallback disabled due to issues
        /// </summary>
        private void SafetyScanCallback(object? state)
        {
            if (!_isMonitoring || _disposed) return;

            Task.Run(async () =>
            {
                try
                {
                    await _logging.LogDebugAsync("Running minimal safety scan (fallback polling disabled)", "UnifiedMonitoringService");

                    // Only check for dead processes - no additional polling to avoid conflicts
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

                            // **NEW**: Stop event tracking for this process
                            _windowEventTracker.StopTrackingProcess(pid);
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
                    // Removed IsActive - now using LastActivated tracking in PlayOnlineCharacter
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
            
            // **NEW**: Dispose window event tracker
            _windowEventTracker?.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

