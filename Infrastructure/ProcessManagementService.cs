using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Management;
using System.Diagnostics.CodeAnalysis;
using FFXIManager.Services;

namespace FFXIManager.Infrastructure
{
    /// <summary>
    /// Unified process monitoring and management infrastructure
    /// Provides shared functionality for both Application Manager and Character Monitor
    /// </summary>
    public interface IProcessManagementService
    {
        Task<List<ProcessInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames);
        Task<List<ProcessInfo>> GetAllProcessesAsync();
        Task<ProcessInfo?> GetProcessByIdAsync(int processId);
        Task<bool> KillProcessAsync(int processId, int timeoutMs = 5000);
        Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = 5000);
        Task<List<WindowInfo>> GetProcessWindowsAsync(int processId);
        bool IsProcessRunning(int processId);
        void StartGlobalMonitoring(TimeSpan interval);
        void StopGlobalMonitoring();
        // New unified discovery/tracking API
        Guid RegisterDiscoveryWatch(DiscoveryFilter filter);
        void UnregisterDiscoveryWatch(Guid watchId);
        void TrackPid(int processId);
        void UntrackPid(int processId);
        // Nudge monitoring to run an immediate refresh (e.g., after settings changes)
        void RequestImmediateRefresh();
        // Event-driven window title changes
        event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;
        event EventHandler<ProcessInfo>? ProcessDetected;
        event EventHandler<ProcessInfo>? ProcessTerminated;
        event EventHandler<ProcessInfo>? ProcessUpdated;
    }

    /// <summary>
    /// Represents basic process information
    /// </summary>
    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public IntPtr MainWindowHandle { get; set; }
        public string MainWindowTitle { get; set; } = string.Empty;
        public bool IsResponding { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public List<WindowInfo> Windows { get; set; } = new();

        public bool HasExited => ProcessId <= 0 || LastSeen < DateTime.UtcNow.AddMinutes(-2);
    }

    /// <summary>
    /// Represents window information
    /// </summary>
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
        public bool IsMainWindow { get; set; }
        public int ProcessId { get; set; }
    }

    public class WindowTitleChangedEventArgs : EventArgs
    {
        public int ProcessId { get; init; }
        public IntPtr WindowHandle { get; init; }
        public string NewTitle { get; init; } = string.Empty;
    }

    /// <summary>
    /// Discovery filter used to scope process discovery.
    /// Supports simple include/exclude patterns with wildcard (*) matching (case-insensitive).
    /// </summary>
    public class DiscoveryFilter
    {
        public IEnumerable<string> IncludeNames { get; init; } = Array.Empty<string>();
        public IEnumerable<string> ExcludeNames { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Internal registration record for discovery filter
    /// </summary>
    internal sealed class DiscoveryWatch
    {
        public Guid Id { get; init; }
        public List<string> IncludeNames { get; init; } = new List<string>();
        public List<string> ExcludeNames { get; init; } = new List<string>();
    }

    /// <summary>
    /// Centralized process management service with shared Windows API functionality
    /// Enhanced with Win32Exception handling for process access issues
    /// </summary>
    public class ProcessManagementService : IProcessManagementService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly Dictionary<int, ProcessInfo> _trackedProcesses = new();
        private readonly SemaphoreSlim _processLock = new(1, 1);
        private readonly Timer? _globalMonitoringTimer;
        private readonly object _lockObject = new();
        private bool _isGlobalMonitoring;
        private bool _disposed;
        private CancellationTokenSource? _monitoringCts;
        private int _monitorIntervalMs = GLOBAL_MONITOR_INTERVAL_MS;

        // Unified discovery/tracking state
        private readonly List<DiscoveryWatch> _discoveryWatches = new();
        private readonly HashSet<int> _explicitTrackedPids = new();

        // WMI watchers for process start/stop
        private ManagementEventWatcher? _processStartWatcher;
        private ManagementEventWatcher? _processStopWatcher;

        // WinEvent hook for window title changes
        private IntPtr _winEventHookHandle = IntPtr.Zero;
        private WinEventDelegate? _winEventCallback;
        private readonly Dictionary<IntPtr, string> _lastWindowTitles = new();

        private const int DEFAULT_TIMEOUT_MS = 5000;
        private const int GAMING_ACTIVATION_TIMEOUT_MS = 2000; // Gaming-optimized activation timeout
        private const int GLOBAL_MONITOR_INTERVAL_MS = 2000; // Centralized polling interval

        public event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;
        public event EventHandler<ProcessInfo>? ProcessDetected;
        public event EventHandler<ProcessInfo>? ProcessTerminated;
        public event EventHandler<ProcessInfo>? ProcessUpdated;

        public ProcessManagementService(ILoggingService loggingService, IUiDispatcher uiDispatcher)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            // Single global monitoring timer for all process tracking
            _globalMonitoringTimer = new Timer(GlobalMonitoringCallback, null,
                Timeout.Infinite, Timeout.Infinite);
        }

        #region Windows API Imports - Centralized

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Suppress CA1838: StringBuilder is used here for simplicity and acceptable interop for window texts
        [SuppressMessage("Performance", "CA1838:Avoid 'StringBuilder' parameters for P/Invokes", Justification = "Interop signature uses StringBuilder for simplicity and is sufficient here.")]
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        // WinEvent hook imports
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        // WinEvent constants
        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
        private const int OBJID_WINDOW = 0;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        #endregion

        #region Public Interface

        public Guid RegisterDiscoveryWatch(DiscoveryFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var id = Guid.NewGuid();
            lock (_lockObject)
            {
                _discoveryWatches.Add(new DiscoveryWatch
                {
                    Id = id,
                    IncludeNames = (filter.IncludeNames ?? Array.Empty<string>()).Select(FFXIManager.Utilities.ProcessFilters.ExtractProcessName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ExcludeNames = (filter.ExcludeNames ?? Array.Empty<string>()).Select(FFXIManager.Utilities.ProcessFilters.ExtractProcessName).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                });
            }
            return id;
        }

        public void UnregisterDiscoveryWatch(Guid watchId)
        {
            lock (_lockObject)
            {
                var idx = _discoveryWatches.FindIndex(w => w.Id == watchId);
                if (idx >= 0) _discoveryWatches.RemoveAt(idx);
            }
        }

        public void TrackPid(int processId)
        {
            if (processId <= 0) return;
            lock (_lockObject)
            {
                _explicitTrackedPids.Add(processId);
            }
        }

        public void UntrackPid(int processId)
        {
            lock (_lockObject)
            {
                _explicitTrackedPids.Remove(processId);
            }
        }

        public async Task<List<ProcessInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames)
        {
            var result = new List<ProcessInfo>();

            foreach (var processName in processNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    var processInfos = await ConvertToProcessInfoAsync(processes);
                    result.AddRange(processInfos);
                }
                catch (Win32Exception ex)
                {
                    // Specific handling for Win32 access issues
                    await _loggingService.LogDebugAsync($"Win32 access denied for process '{processName}': {ex.Message}",
                        "ProcessManagementService");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"Error getting processes for {processName}: {ex.Message}",
                        "ProcessManagementService");
                }
            }

            return result;
        }

        public async Task<List<ProcessInfo>> GetAllProcessesAsync()
        {
            try
            {
                var processes = Process.GetProcesses();
                return await ConvertToProcessInfoAsync(processes);
            }
            catch (Win32Exception ex)
            {
                await _loggingService.LogDebugAsync($"Win32 access issue getting all processes: {ex.Message}", "ProcessManagementService");
                return new List<ProcessInfo>();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error getting all processes", ex, "ProcessManagementService");
                return new List<ProcessInfo>();
            }
        }

        public async Task<ProcessInfo?> GetProcessByIdAsync(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process?.HasExited == false)
                {
                    var processInfos = await ConvertToProcessInfoAsync(new[] { process });
                    return processInfos.FirstOrDefault();
                }
            }
            catch (ArgumentException)
            {
                // Process not found - this is expected and not an error
                return null;
            }
            catch (Win32Exception ex)
            {
                await _loggingService.LogDebugAsync($"Win32 access denied for process {processId}: {ex.Message}",
                    "ProcessManagementService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogDebugAsync($"Error getting process {processId}: {ex.Message}",
                    "ProcessManagementService");
            }

            return null;
        }

        public async Task<bool> KillProcessAsync(int processId, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

                var process = Process.GetProcessById(processId);
                if (process?.HasExited == false)
                {
                    await Task.Run(() =>
                    {
                        process.Kill();
                        process.WaitForExit(timeoutMs);
                    }, cts.Token);

                    await _loggingService.LogInfoAsync($"Successfully killed process {processId}",
                        "ProcessManagementService");
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Process already exited - consider this success
                return true;
            }
            catch (Win32Exception ex)
            {
                await _loggingService.LogWarningAsync($"Win32 access denied killing process {processId}: {ex.Message}",
                    "ProcessManagementService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error killing process {processId}", ex,
                    "ProcessManagementService");
            }

            return false;
        }

        public async Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            // **GAMING OPTIMIZATION**: Use gaming-optimized timeout by default
            var actualTimeout = timeoutMs == DEFAULT_TIMEOUT_MS ? GAMING_ACTIVATION_TIMEOUT_MS : timeoutMs;

            // **FAST VALIDATION**: Pre-validate window handle without expensive operations
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            // **OPTIMIZATION**: Quick visibility check - don't activate if already visible and foreground
            if (GetForegroundWindow() == windowHandle && IsWindowVisible(windowHandle))
            {
                await _loggingService.LogDebugAsync($"Window {windowHandle:X8} already active, skipping activation",
                    "ProcessManagementService");
                return true;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(actualTimeout));

                bool success = await Task.Run(async () =>
                {
                    return await PerformOptimizedWindowActivation(windowHandle);
                }, cts.Token);

                if (success)
                {
                    await _loggingService.LogDebugAsync($"Successfully activated window {windowHandle:X8}",
                        "ProcessManagementService");
                }

                return success;
            }
            catch (Win32Exception ex)
            {
                await _loggingService.LogDebugAsync($"Win32 error activating window {windowHandle:X8}: {ex.Message}",
                    "ProcessManagementService");
                return false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Error activating window {windowHandle:X8}", ex,
                    "ProcessManagementService");
                return false;
            }
        }

        public async Task<List<WindowInfo>> GetProcessWindowsAsync(int processId)
        {
            var windows = new List<WindowInfo>();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(DEFAULT_TIMEOUT_MS));

                await Task.Run(() =>
                {
                    try
                    {
                        EnumWindows((hWnd, lParam) =>
                        {
                            try
                            {
                                if (cts.Token.IsCancellationRequested) return false;

                                if (IsWindowVisible(hWnd))
                                {
                                    var threadId = GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                                    if (threadId == 0)
                                    {
                                        return true; // continue enumeration
                                    }

                                    if (windowProcessId == (uint)processId)
                                    {
                                        var title = GetWindowTitle(hWnd);

                                        if (FFXIManager.Utilities.ProcessFilters.IsAcceptableWindowTitle(title))
                                        {
                                            windows.Add(new WindowInfo
                                            {
                                                Handle = hWnd,
                                                Title = title,
                                                IsVisible = true,
                                                ProcessId = processId
                                            });
                                        }
                                    }
                                }
                            }
                            catch (Win32Exception)
                            {
                                // Ignore Win32 errors for individual windows
                            }
                            catch
                            {
                                // Ignore other individual window enumeration errors
                            }
                            return !cts.Token.IsCancellationRequested;
                        }, IntPtr.Zero);
                    }
                    catch (Win32Exception ex)
                    {
                        // Log but don't throw - this is expected for some processes
                        _ = _loggingService.LogDebugAsync($"Win32 error enumerating windows for process {processId}: {ex.Message}", "ProcessManagementService");
                    }
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"Window enumeration timeout for process {processId}",
                    "ProcessManagementService");
            }
            catch (Exception ex)
            {
                await _loggingService.LogDebugAsync($"Error enumerating windows for process {processId}: {ex.Message}",
                    "ProcessManagementService");
            }

            return windows;
        }

        public bool IsProcessRunning(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return process?.HasExited == false;
            }
            catch (ArgumentException)
            {
                // Process not found
                return false;
            }
            catch (Win32Exception)
            {
                // Access denied - assume running but inaccessible
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void StartGlobalMonitoring(TimeSpan interval)
        {
            lock (_lockObject)
            {
                if (!_isGlobalMonitoring)
                {
                    _isGlobalMonitoring = true;
                    _monitoringCts?.Cancel();
                    _monitoringCts?.Dispose();
                    _monitoringCts = new CancellationTokenSource();
                    _monitorIntervalMs = Math.Max((int)interval.TotalMilliseconds, 1000); // Minimum 1 second
                    _globalMonitoringTimer?.Change(0, _monitorIntervalMs);
                    StartWmiWatchers();
                    StartWinEventHook();
                    _loggingService.LogInfoAsync("Started global process monitoring", "ProcessManagementService");
                }
            }
        }

        public void StopGlobalMonitoring()
        {
            lock (_lockObject)
            {
                if (_isGlobalMonitoring)
                {
                    _isGlobalMonitoring = false;
                    _globalMonitoringTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                    _monitoringCts?.Cancel();
                    _monitoringCts?.Dispose();
                    _monitoringCts = null;
                    StopWmiWatchers();
                    StopWinEventHook();
                    _loggingService.LogInfoAsync("Stopped global process monitoring", "ProcessManagementService");
                }
            }
        }

        public void RequestImmediateRefresh()
        {
            lock (_lockObject)
            {
                if (_isGlobalMonitoring)
                {
                    _globalMonitoringTimer?.Change(0, _monitorIntervalMs);
                }
            }
        }

        #endregion

        #region Gaming-Optimized Window Activation

        /// <summary>
        /// Performs optimized window activation with thread input attachment and minimal delays
        /// </summary>
        private static async Task<bool> PerformOptimizedWindowActivation(IntPtr windowHandle)
        {
            try
            {
                // Get the window's thread ID
                var windowThreadId = GetWindowThreadProcessId(windowHandle, IntPtr.Zero);
                var currentThreadId = GetCurrentThreadId();

                bool attachedInput = false;

                try
                {
                    // **OPTIMIZATION**: Attach thread input if different threads
                    if (windowThreadId != 0 && windowThreadId != currentThreadId)
                    {
                        attachedInput = AttachThreadInput(currentThreadId, windowThreadId, true);
                    }

                    // **GAMING OPTIMIZATION**: Fast activation sequence with minimal delays
                    bool success = false;

                    // Step 1: Handle minimized windows with reduced delay
                    if (IsIconic(windowHandle))
                    {
                        ShowWindow(windowHandle, SW_RESTORE);
                        // **FIXED**: Use non-blocking wait instead of Thread.Sleep
                        await Task.Delay(15); // Async wait prevents input blocking
                    }
                    else
                    {
                        ShowWindow(windowHandle, SW_SHOW);
                    }

                    // Step 2: Primary activation attempt
                    success = SetForegroundWindow(windowHandle);

                    // Step 3: Alternative methods if primary failed
                    if (!success)
                    {
                        // Try bringing window to top first
                        BringWindowToTop(windowHandle);

                        // Then attempt SwitchToThisWindow with Alt+Tab behavior
                        SwitchToThisWindow(windowHandle, true);

                        // Final attempt with SetForegroundWindow
                        success = SetForegroundWindow(windowHandle);

                        // If still failed, assume success since SwitchToThisWindow doesn't return status
                        if (!success)
                        {
                            success = true;
                        }
                    }

                    return success;
                }
                finally
                {
                    // **CLEANUP**: Detach thread input if we attached it
                    if (attachedInput && windowThreadId != 0 && windowThreadId != currentThreadId)
                    {
                        AttachThreadInput(currentThreadId, windowThreadId, false);
                    }
                }
            }
            catch (Exception)
            {
                // Return false on any exception during activation
                return false;
            }
        }

        #endregion

        #region Private Methods

        private async Task<List<ProcessInfo>> ConvertToProcessInfoAsync(IEnumerable<Process> processes)
        {
            var result = new List<ProcessInfo>();

            foreach (var process in processes)
            {
                try
                {
                    if (!IsValidProcess(process))
                    {
                        continue;
                    }

                    bool debugging = Debugger.IsAttached;

                    var processInfo = new ProcessInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = GetSafeProcessName(process),
                        ExecutablePath = debugging ? string.Empty : GetSafeProcessPath(process),
                        MainWindowHandle = GetSafeMainWindowHandle(process),
                        MainWindowTitle = debugging ? string.Empty : GetSafeMainWindowTitle(process),
                        IsResponding = debugging ? true : GetSafeResponding(process),
                        StartTime = debugging ? DateTime.UtcNow : GetSafeStartTime(process),
                        LastSeen = DateTime.UtcNow
                    };

                    // Get windows for this process (with reduced frequency to avoid flooding)
                    processInfo.Windows = await GetProcessWindowsAsync(process.Id);

                    result.Add(processInfo);
                }
                catch (Win32Exception)
                {
                    // Silently skip processes we can't access
                    continue;
                }
                catch (Exception ex)
                {
                    // Log but continue with other processes
                    await _loggingService.LogDebugAsync($"Error converting process {process?.Id}: {ex.Message}", "ProcessManagementService");
                }
                finally
                {
                    try { process?.Dispose(); } catch { }
                }
            }

            return result;
        }

        private static bool IsValidProcess(Process? process)
        {
            if (process == null) return false;

            try
            {
                // Basic validation without accessing potentially restricted properties
                return !process.HasExited && process.Id > 0;
            }
            catch (Win32Exception)
            {
                // Can't access process information - skip it
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetSafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName ?? string.Empty;
            }
            catch (Win32Exception)
            {
                return "Unknown";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSafeProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch (Win32Exception)
            {
                // Common for system processes and processes we don't have access to
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IntPtr GetSafeMainWindowHandle(Process process)
        {
            try
            {
                return process.MainWindowHandle;
            }
            catch (Win32Exception)
            {
                return IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static string GetSafeMainWindowTitle(Process process)
        {
            try
            {
                return process.MainWindowTitle ?? string.Empty;
            }
            catch (Win32Exception)
            {
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool GetSafeResponding(Process process)
        {
            try
            {
                return process.Responding;
            }
            catch (Win32Exception)
            {
                // Assume responsive if we can't check
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static DateTime GetSafeStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch (Win32Exception)
            {
                // Use current time if we can't get start time
                return DateTime.UtcNow;
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                const int maxLength = 256;
                var title = new StringBuilder(maxLength);
                var len = GetWindowText(hWnd, title, maxLength);
                if (len <= 0)
                {
                    return string.Empty;
                }
                return title.ToString(0, len);
            }
            catch
            {
                return string.Empty;
            }
        }

        private async void GlobalMonitoringCallback(object? state)
        {
            if (!_isGlobalMonitoring || _disposed) return;

            if (!await _processLock.WaitAsync(100)) return;

            try
            {
                await UpdateTrackedProcessesAsync();
            }
            catch (Win32Exception)
            {
                // Silently ignore Win32 exceptions during monitoring
                // These are expected when processes are restricted
            }
            catch (Exception ex)
            {
                await _loggingService.LogDebugAsync($"Error in global monitoring: {ex.Message}",
                    "ProcessManagementService");
            }
            finally
            {
                _processLock.Release();
            }
        }

        private async Task UpdateTrackedProcessesAsync()
        {
            var currentProcesses = new Dictionary<int, ProcessInfo>();

            try
            {
                // Build composite discovery patterns from user settings and registered watches
                List<string> includePatterns;
                List<string> excludePatterns;
                lock (_lockObject)
                {
                    includePatterns = new List<string>();
                    excludePatterns = new List<string>();
                    foreach (var w in _discoveryWatches)
                    {
                        includePatterns.AddRange(w.IncludeNames);
                        excludePatterns.AddRange(w.ExcludeNames);
                    }
                }


                // Get all processes and filter via patterns for flexibility (supports wildcards)
                var allProcesses = await GetAllProcessesAsync();
                var relevantProcesses = allProcesses
                    .Where(p => FFXIManager.Utilities.ProcessFilters.MatchesNamePatterns(p.ProcessName, includePatterns, excludePatterns))
                    .ToList();

                try
                {
                    var diag = ServiceLocator.SettingsService.LoadSettings()?.Diagnostics;
                    if (diag?.EnableDiagnostics == true)
                    {
                        await _loggingService.LogDebugAsync($"Discovery matched {relevantProcesses.Count} processes (includes={includePatterns.Count}, excludes={excludePatterns.Count})", "ProcessManagementService");
                    }
                }
                catch { }

                foreach (var proc in relevantProcesses)
                {
                    currentProcesses[proc.ProcessId] = proc;
                }

                // Ensure explicitly tracked PIDs are included even if not in discovery set
                List<int> trackedPids;
                lock (_lockObject)
                {
                    trackedPids = _explicitTrackedPids.ToList();
                }

                foreach (var pid in trackedPids)
                {
                    if (!currentProcesses.ContainsKey(pid))
                    {
                        var pi = await GetProcessByIdAsync(pid);
                        if (pi != null)
                        {
                            currentProcesses[pi.ProcessId] = pi;
                        }
                    }
                }

                // Check for new processes
                foreach (var kvp in currentProcesses)
                {
                    var processId = kvp.Key;
                    var processInfo = kvp.Value;

                    if (_trackedProcesses.TryGetValue(processId, out var existing))
                    {
                        // Update existing process
                        existing.LastSeen = processInfo.LastSeen;
                        existing.IsResponding = processInfo.IsResponding;
                        existing.MainWindowTitle = processInfo.MainWindowTitle;
                        existing.Windows = processInfo.Windows;

                        _ = _uiDispatcher.InvokeAsync(() => ProcessUpdated?.Invoke(this, existing));
                    }
                    else
                    {
                        _trackedProcesses[processId] = processInfo;
                        _ = _uiDispatcher.InvokeAsync(() => ProcessDetected?.Invoke(this, processInfo));
                    }
                }

                // Check for terminated processes
                var terminatedProcesses = _trackedProcesses.Keys
                    .Where(pid => !currentProcesses.ContainsKey(pid))
                    .ToList();

                foreach (var processId in terminatedProcesses)
                {
                    var processInfo = _trackedProcesses[processId];
                    _trackedProcesses.Remove(processId);
                    _ = _uiDispatcher.InvokeAsync(() => ProcessTerminated?.Invoke(this, processInfo));
                }
            }
            catch (Win32Exception)
            {
                // Silently handle Win32 access issues during tracking
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error updating tracked processes", ex,
                    "ProcessManagementService");
            }
        }

        #endregion

        #region WinEvent Hook (Window Title Changes)

        private void StartWinEventHook()
        {
            try
            {
                if (_winEventHookHandle != IntPtr.Zero) return;
                _winEventCallback = OnWinEvent;
                _winEventHookHandle = SetWinEventHook(
                    EVENT_OBJECT_NAMECHANGE,
                    EVENT_OBJECT_NAMECHANGE,
                    IntPtr.Zero,
                    _winEventCallback,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (_winEventHookHandle == IntPtr.Zero)
                {
                    _ = _loggingService.LogWarningAsync("Failed to set WinEvent hook for name changes", "ProcessManagementService");
                }
                else
                {
                    _ = _loggingService.LogInfoAsync("WinEvent hook started (OBJECT_NAMECHANGE)", "ProcessManagementService");
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogWarningAsync($"Exception starting WinEvent hook: {ex.Message}", "ProcessManagementService");
            }
        }

        private void StopWinEventHook()
        {
            try
            {
                if (_winEventHookHandle != IntPtr.Zero)
                {
                    UnhookWinEvent(_winEventHookHandle);
                    _winEventHookHandle = IntPtr.Zero;
                    _winEventCallback = null;
                    _ = _loggingService.LogInfoAsync("WinEvent hook stopped", "ProcessManagementService");
                }
                lock (_lockObject)
                {
                    _lastWindowTitles.Clear();
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Exception stopping WinEvent hook: {ex.Message}", "ProcessManagementService");
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (eventType != EVENT_OBJECT_NAMECHANGE) return;
                if (idObject != OBJID_WINDOW) return;
                if (hwnd == IntPtr.Zero) return;

                // Filter by tracked PIDs if possible
                uint pid;
                var threadId = GetWindowThreadProcessId(hwnd, out pid);
                if (threadId == 0 || pid == 0)
                {
                    return;
                }

                bool shouldProcess = false;
                lock (_lockObject)
                {
                    if (_explicitTrackedPids.Contains((int)pid))
                    {
                        shouldProcess = true;
                    }
                    else if (_trackedProcesses.ContainsKey((int)pid))
                    {
                        shouldProcess = true;
                    }
                }

                if (!shouldProcess) return;

                var title = GetWindowTitle(hwnd);
                if (string.IsNullOrEmpty(title)) return;

                bool changed = false;
                lock (_lockObject)
                {
                    if (!_lastWindowTitles.TryGetValue(hwnd, out var last) || !string.Equals(last, title, StringComparison.Ordinal))
                    {
                        _lastWindowTitles[hwnd] = title;
                        changed = true;
                    }
                }

                if (!changed) return;

                var args = new WindowTitleChangedEventArgs
                {
                    ProcessId = (int)pid,
                    WindowHandle = hwnd,
                    NewTitle = title
                };
                try
                {
                    _ = _uiDispatcher.InvokeAsync(() => WindowTitleChanged?.Invoke(this, args));
                }
                catch (Exception ex)
                {
                    _ = _loggingService.LogDebugAsync($"Exception in WindowTitleChanged subscribers (PID {(int)pid}, HWND 0x{hwnd.ToInt64():X}): {ex.Message}", "ProcessManagementService");
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Exception handling WinEvent (HWND 0x{hwnd.ToInt64():X}): {ex.Message}", "ProcessManagementService");
            }
        }

        #endregion

        #region WMI Watchers

        private void StartWmiWatchers()
        {
            try
            {
                // Creation watcher
                var creationQuery = new WqlEventQuery
                {
                    EventClassName = "__InstanceCreationEvent",
                    WithinInterval = TimeSpan.FromSeconds(1),
                    Condition = "TargetInstance ISA 'Win32_Process'"
                };
                _processStartWatcher = new ManagementEventWatcher(new ManagementScope("root\\CIMV2"), creationQuery);
                _processStartWatcher.EventArrived += OnProcessCreated;
                _processStartWatcher.Start();
                _ = _loggingService.LogInfoAsync("WMI creation watcher started", "ProcessManagementService");

                // Deletion watcher
                var deletionQuery = new WqlEventQuery
                {
                    EventClassName = "__InstanceDeletionEvent",
                    WithinInterval = TimeSpan.FromSeconds(1),
                    Condition = "TargetInstance ISA 'Win32_Process'"
                };
                _processStopWatcher = new ManagementEventWatcher(new ManagementScope("root\\CIMV2"), deletionQuery);
                _processStopWatcher.EventArrived += OnProcessDeleted;
                _processStopWatcher.Start();
                _ = _loggingService.LogInfoAsync("WMI deletion watcher started", "ProcessManagementService");

                _ = _loggingService.LogInfoAsync("WMI process watchers started", "ProcessManagementService");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogWarningAsync($"Failed to start WMI watchers: {ex.Message}", "ProcessManagementService");
                StopWmiWatchers();
            }
        }

        private void StopWmiWatchers()
        {
            try
            {
                if (_processStartWatcher != null)
                {
                    _processStartWatcher.EventArrived -= OnProcessCreated;
                    _processStartWatcher.Stop();
                    _processStartWatcher.Dispose();
                    _processStartWatcher = null;
                }
                if (_processStopWatcher != null)
                {
                    _processStopWatcher.EventArrived -= OnProcessDeleted;
                    _processStopWatcher.Stop();
                    _processStopWatcher.Dispose();
                    _processStopWatcher = null;
                }
                _ = _loggingService.LogInfoAsync("WMI process watchers stopped", "ProcessManagementService");
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Exception stopping WMI watchers: {ex.Message}", "ProcessManagementService");
            }
        }

        private void OnProcessCreated(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (!_isGlobalMonitoring) return;
                var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
                if (target == null) return;

                var nameObj = target["Name"];
                var pidObj = target["ProcessId"];
                if (nameObj == null || pidObj == null) return;
                var name = nameObj.ToString() ?? string.Empty;
                var pid = Convert.ToInt32((uint)pidObj);

                // Check against discovery patterns quickly
                bool interested = false;
                try
                {
                    var include = new List<string>();
                    var exclude = new List<string>();
                    lock (_lockObject)
                    {
                        foreach (var w in _discoveryWatches)
                        {
                            include.AddRange(w.IncludeNames);
                            exclude.AddRange(w.ExcludeNames);
                        }
                    }
                    var normalized = FFXIManager.Utilities.ProcessFilters.ExtractProcessName(name);
                    interested = FFXIManager.Utilities.ProcessFilters.MatchesNamePatterns(normalized, include, exclude);
                }
                catch
                {
                    interested = false;
                }

                if (!interested) return;

                // Build ProcessInfo asynchronously and emit ProcessDetected if new
                var token = _monitoringCts?.Token ?? CancellationToken.None;
                Task.Run(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        var pi = await GetProcessByIdAsync(pid);
                        if (pi == null) return;
                        lock (_lockObject)
                        {
                            if (_trackedProcesses.ContainsKey(pid)) return;
                            _trackedProcesses[pid] = pi;
                        }
                        try
                        {
                            _ = _uiDispatcher.InvokeAsync(() => ProcessDetected?.Invoke(this, pi));
                        }
                        catch (Exception ex)
                        {
                            _ = _loggingService.LogDebugAsync($"Exception in ProcessDetected subscribers (PID {pid}, Name {name}): {ex.Message}", "ProcessManagementService");
                        }
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogDebugAsync($"Exception building ProcessInfo for PID {pid} on create: {ex.Message}", "ProcessManagementService");
                    }
                });
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Exception handling WMI process creation: {ex.Message}", "ProcessManagementService");
            }
        }

        private void OnProcessDeleted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                if (!_isGlobalMonitoring) return;
                var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
                if (target == null) return;

                var pidObj = target["ProcessId"];
                if (pidObj == null) return;
                var pid = Convert.ToInt32((uint)pidObj);

                ProcessInfo? removed = null;
                lock (_lockObject)
                {
                    if (_trackedProcesses.TryGetValue(pid, out var pi))
                    {
                        removed = pi;
                        _trackedProcesses.Remove(pid);
                    }
                }

                if (removed != null)
                {
                    try
                    {
                        _ = _uiDispatcher.InvokeAsync(() => ProcessTerminated?.Invoke(this, removed));
                    }
                    catch (Exception ex)
                    {
                        _ = _loggingService.LogDebugAsync($"Exception in ProcessTerminated subscribers (PID {pid}): {ex.Message}", "ProcessManagementService");
                    }
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"Exception handling WMI process deletion: {ex.Message}", "ProcessManagementService");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopGlobalMonitoring();
            _globalMonitoringTimer?.Dispose();
            _processLock?.Dispose();
            _monitoringCts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
