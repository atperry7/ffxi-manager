using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager.Infrastructure
{
    /// <summary>
    /// Simple utility service for process actions
    /// No monitoring - just actions and queries
    /// </summary>
    public interface IProcessUtilityService
    {
        Task<bool> KillProcessAsync(int processId, int timeoutMs = 5000);
        Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = 5000);
        Task<WindowActivationResult> ActivateWindowEnhancedAsync(IntPtr windowHandle, int timeoutMs = 5000);
        Task<List<WindowInfo>> GetProcessWindowsAsync(int processId);
        bool IsProcessRunning(int processId);
        Task<ProcessBasicInfo?> GetProcessInfoAsync(int processId);
        Task<List<ProcessBasicInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames);
    }

    /// <summary>
    /// Basic process information for queries
    /// </summary>
    public class ProcessBasicInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public bool IsResponding { get; set; } = true;
        public List<WindowInfo> Windows { get; set; } = new();
    }

    /// <summary>
    /// Simple process utility implementation
    /// </summary>
    public class ProcessUtilityService : IProcessUtilityService
    {
        private readonly ILoggingService _logging;
        private const int DEFAULT_TIMEOUT_MS = 5000;

        #region Windows API Imports

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

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

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        
        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool IsHungAppWindow(IntPtr hWnd);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        
        [DllImport("user32.dll")]
        private static extern int GetLastError();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        private const uint GW_HWNDNEXT = 2;
        private const uint GW_HWNDPREV = 3;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        #endregion

        public ProcessUtilityService(ILoggingService logging)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
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

                    await _logging.LogInfoAsync($"Successfully killed process {processId}",
                        "ProcessUtilityService");
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
                await _logging.LogWarningAsync($"Access denied killing process {processId}: {ex.Message}",
                    "ProcessUtilityService");
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error killing process {processId}", ex,
                    "ProcessUtilityService");
            }

            return false;
        }

        /// <summary>
        /// Enhanced window activation with detailed failure detection and diagnostics.
        /// </summary>
        public async Task<WindowActivationResult> ActivateWindowEnhancedAsync(IntPtr windowHandle, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // **VALIDATION**: Check if window handle is valid
                if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
                {
                    return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.InvalidHandle,
                        "Window handle is invalid or window has been destroyed");
                }
                
                // **DIAGNOSTICS**: Check if window is hung
                if (IsHungAppWindow(windowHandle))
                {
                    await _logging.LogWarningAsync($"Window 0x{windowHandle.ToInt64():X} appears to be hung", "ProcessUtilityService");
                    return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.WindowHung,
                        "Target window is not responding");
                }
                
                // **DIAGNOSTICS**: Capture initial window state
                var initialState = GetWindowState(windowHandle);
                await _logging.LogDebugAsync($"Initial window state: {initialState}", "ProcessUtilityService");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                
                // Perform activation with multiple strategies
                int attempts = 0;
                bool success = false;
                
                while (!success && attempts < 3 && !cts.Token.IsCancellationRequested)
                {
                    attempts++;
                    success = await AttemptWindowActivation(windowHandle, attempts, cts.Token);
                    
                    if (!success && attempts < 3)
                    {
                        await Task.Delay(50 * attempts, cts.Token); // Progressive delay
                    }
                }
                
                stopwatch.Stop();
                
                // **VERIFICATION**: Get final window state
                var finalState = GetWindowState(windowHandle);
                
                if (success || finalState.IsForeground)
                {
                    await _logging.LogDebugAsync($"Window activation succeeded after {attempts} attempts in {stopwatch.ElapsedMilliseconds}ms", "ProcessUtilityService");
                    var successResult = WindowActivationResult.Successful(windowHandle, stopwatch.Elapsed, attempts);
                    successResult.WindowState = finalState;
                    return successResult;
                }
                
                // **FAILURE ANALYSIS**: Determine why activation failed
                var failureReason = AnalyzeActivationFailure(windowHandle, initialState, finalState);
                
                var failedResult = WindowActivationResult.Failed(windowHandle, failureReason, 
                    $"Failed after {attempts} attempts. Final state: {finalState}");
                failedResult.Duration = stopwatch.Elapsed;
                failedResult.AttemptsRequired = attempts;
                failedResult.WindowState = finalState;
                return failedResult;
            }
            catch (OperationCanceledException)
            {
                return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.Timeout,
                    $"Activation timed out after {timeoutMs}ms");
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Unexpected error during window activation", ex, "ProcessUtilityService");
                return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.Unknown, ex.Message);
            }
        }
        
        public async Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

                bool success = await Task.Run(async () =>
                {
                    // **OPTIMIZATION**: Async window restoration instead of fixed delay
                    if (IsIconic(windowHandle))
                    {
                        ShowWindow(windowHandle, SW_RESTORE);
                        
                        // **GAMING OPTIMIZATION**: Poll for restoration completion with timeout
                        var restoreStart = Environment.TickCount;
                        const int maxRestoreWaitMs = 300; // Maximum wait for restore
                        
                        while (IsIconic(windowHandle) && (Environment.TickCount - restoreStart) < maxRestoreWaitMs)
                        {
                            await Task.Delay(5, cts.Token); // Non-blocking 5ms intervals
                        }
                        
                        // Log restore performance
                        var restoreTime = Environment.TickCount - restoreStart;
                        if (restoreTime > 50) // Log if restore took longer than expected
                        {
                            await _logging.LogDebugAsync($"Window restore took {restoreTime}ms", "ProcessUtilityService");
                        }
                    }
                    else
                    {
                        ShowWindow(windowHandle, SW_SHOW);
                    }

                    // **PERFORMANCE**: Try activation methods with short circuits
                    if (GetForegroundWindow() == windowHandle)
                    {
                        // Already active - early exit
                        return true;
                    }

                    // Primary activation attempt
                    SetForegroundWindow(windowHandle);
                    
                    // Quick check if that worked
                    if (GetForegroundWindow() == windowHandle)
                    {
                        return true;
                    }

                    // **FALLBACK**: Enhanced activation with thread attachment
                    BringWindowToTop(windowHandle);
                    
                    var currentThread = GetCurrentThreadId();
                    uint targetThread = GetWindowThreadProcessId(windowHandle, out _);

                    if (currentThread != targetThread)
                    {
                        try
                        {
                            // **IMPROVED**: More robust thread attachment
                            if (AttachThreadInput(currentThread, targetThread, true))
                            {
                                SetForegroundWindow(windowHandle);
                                BringWindowToTop(windowHandle);
                                
                                // Small delay to let the activation take effect
                                await Task.Delay(10, cts.Token);
                                
                                AttachThreadInput(currentThread, targetThread, false);
                            }
                        }
                        catch
                        {
                            // Thread attachment can fail - continue without it
                        }
                    }

                    // Final verification
                    return GetForegroundWindow() == windowHandle;
                }, cts.Token);

                if (success)
                {
                    await _logging.LogDebugAsync($"Successfully activated window 0x{windowHandle.ToInt64():X}",
                        "ProcessUtilityService");
                }
                else
                {
                    await _logging.LogDebugAsync($"Failed to activate window 0x{windowHandle.ToInt64():X}",
                        "ProcessUtilityService");
                }

                return success;
            }
            catch (OperationCanceledException)
            {
                await _logging.LogWarningAsync($"Window activation timeout ({timeoutMs}ms) for 0x{windowHandle.ToInt64():X}",
                    "ProcessUtilityService");
                return false;
            }
            catch (Exception ex)
            {
                await _logging.LogWarningAsync($"Error activating window 0x{windowHandle.ToInt64():X}: {ex.Message}",
                    "ProcessUtilityService");
                return false;
            }
        }

        public async Task<List<WindowInfo>> GetProcessWindowsAsync(int processId)
        {
            var windows = new List<WindowInfo>();

            try
            {
                await Task.Run(() =>
                {
                    EnumWindows((hWnd, lParam) =>
                    {
                        try
                        {
                            uint windowProcessId;
                            uint threadId = GetWindowThreadProcessId(hWnd, out windowProcessId);

                            if (threadId != 0 && windowProcessId == (uint)processId && IsWindowVisible(hWnd))
                            {
                                var title = GetWindowTitle(hWnd);
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    windows.Add(new WindowInfo
                                    {
                                        Handle = hWnd,
                                        Title = title,
                                        IsVisible = true,
                                        IsMainWindow = true,
                                        ProcessId = processId
                                    });
                                }
                            }
                        }
                        catch
                        {
                            // Skip windows we can't access
                        }
                        return true; // Continue enumeration
                    }, IntPtr.Zero);
                });
            }
            catch (Exception ex)
            {
                await _logging.LogDebugAsync($"Error enumerating windows for process {processId}: {ex.Message}",
                    "ProcessUtilityService");
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
            catch
            {
                return false;
            }
        }

        public async Task<ProcessBasicInfo?> GetProcessInfoAsync(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process?.HasExited == false)
                {
                    var info = new ProcessBasicInfo
                    {
                        ProcessId = process.Id,
                        ProcessName = GetSafeProcessName(process),
                        ExecutablePath = GetSafeProcessPath(process),
                        StartTime = GetSafeStartTime(process),
                        IsResponding = GetSafeResponding(process),
                        Windows = await GetProcessWindowsAsync(processId)
                    };

                    process.Dispose();
                    return info;
                }
            }
            catch (Exception ex)
            {
                await _logging.LogDebugAsync($"Error getting process info for {processId}: {ex.Message}",
                    "ProcessUtilityService");
            }

            return null;
        }

        public async Task<List<ProcessBasicInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames)
        {
            var result = new List<ProcessBasicInfo>();

            foreach (var processName in processNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                var info = new ProcessBasicInfo
                                {
                                    ProcessId = process.Id,
                                    ProcessName = GetSafeProcessName(process),
                                    ExecutablePath = GetSafeProcessPath(process),
                                    StartTime = GetSafeStartTime(process),
                                    IsResponding = GetSafeResponding(process),
                                    Windows = await GetProcessWindowsAsync(process.Id)
                                };
                                result.Add(info);
                            }
                        }
                        catch
                        {
                            // Skip processes we can't access
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _logging.LogDebugAsync($"Error getting processes for {processName}: {ex.Message}",
                        "ProcessUtilityService");
                }
            }

            return result;
        }

        #region Helper Methods

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    var buffer = new char[length + 1];
                    int result = GetWindowText(hWnd, buffer, buffer.Length);
                    if (result > 0)
                    {
                        return new string(buffer, 0, result);
                    }
                }
            }
            catch
            {
                // Ignore errors getting window title
            }
            return string.Empty;
        }

        private static string GetSafeProcessName(Process process)
        {
            try { return process.ProcessName; }
            catch { return "Unknown"; }
        }

        private static string GetSafeProcessPath(Process process)
        {
            try { return process.MainModule?.FileName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static DateTime GetSafeStartTime(Process process)
        {
            try { return process.StartTime; }
            catch { return DateTime.UtcNow; }
        }

        private static bool GetSafeResponding(Process process)
        {
            try { return process.Responding; }
            catch { return true; }
        }

        #endregion
        
        #region Enhanced Window Activation Helpers
        
        /// <summary>
        /// Gets detailed window state information for diagnostics.
        /// </summary>
        private static WindowStateInfo GetWindowState(IntPtr hWnd)
        {
            var state = new WindowStateInfo
            {
                IsVisible = IsWindowVisible(hWnd),
                IsMinimized = IsIconic(hWnd),
                IsMaximized = IsZoomed(hWnd),
                IsForeground = GetForegroundWindow() == hWnd,
                IsResponding = !IsHungAppWindow(hWnd),
                ZOrder = GetWindowZOrder(hWnd)
            };
            
            // Get window title
            var titleBuffer = new char[256];
            if (GetWindowText(hWnd, titleBuffer, 256) > 0)
            {
                state.WindowTitle = new string(titleBuffer).TrimEnd('\0');
            }
            
            // Get class name
            var classBuffer = new char[256];
            if (GetClassName(hWnd, classBuffer, 256) > 0)
            {
                state.ClassName = new string(classBuffer).TrimEnd('\0');
            }
            
            return state;
        }
        
        /// <summary>
        /// Gets the Z-order position of a window (lower number = higher in z-order).
        /// </summary>
        private static int GetWindowZOrder(IntPtr hWnd)
        {
            int zOrder = 0;
            IntPtr current = GetWindow(hWnd, GW_HWNDPREV);
            
            while (current != IntPtr.Zero && zOrder < 1000) // Limit to prevent infinite loop
            {
                if (IsWindowVisible(current))
                    zOrder++;
                current = GetWindow(current, GW_HWNDPREV);
            }
            
            return zOrder;
        }
        
        /// <summary>
        /// Attempts window activation using progressive strategies.
        /// </summary>
        private static async Task<bool> AttemptWindowActivation(IntPtr hWnd, int attemptNumber, CancellationToken cancellationToken)
        {
            // Strategy varies by attempt number
            switch (attemptNumber)
            {
                case 1:
                    // **ATTEMPT 1**: Simple activation
                    return await SimpleActivation(hWnd, cancellationToken);
                    
                case 2:
                    // **ATTEMPT 2**: Thread attachment
                    return await ThreadAttachmentActivation(hWnd, cancellationToken);
                    
                case 3:
                    // **ATTEMPT 3**: Aggressive activation with window restoration
                    return await AggressiveActivation(hWnd, cancellationToken);
                    
                default:
                    return false;
            }
        }
        
        private static async Task<bool> SimpleActivation(IntPtr hWnd, CancellationToken cancellationToken)
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                await Task.Delay(50, cancellationToken);
            }
            
            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
            
            await Task.Delay(10, cancellationToken);
            return GetForegroundWindow() == hWnd;
        }
        
        private static async Task<bool> ThreadAttachmentActivation(IntPtr hWnd, CancellationToken cancellationToken)
        {
            var currentThread = GetCurrentThreadId();
            var targetThread = GetWindowThreadProcessId(hWnd, out _);
            
            if (currentThread == targetThread)
            {
                return await SimpleActivation(hWnd, cancellationToken);
            }
            
            bool attached = false;
            try
            {
                attached = AttachThreadInput(currentThread, targetThread, true);
                if (attached)
                {
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        await Task.Delay(50, cancellationToken);
                    }
                    
                    SetForegroundWindow(hWnd);
                    BringWindowToTop(hWnd);
                    ShowWindow(hWnd, SW_SHOW);
                    
                    await Task.Delay(20, cancellationToken);
                }
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(currentThread, targetThread, false);
                }
            }
            
            return GetForegroundWindow() == hWnd;
        }
        
        private static async Task<bool> AggressiveActivation(IntPtr hWnd, CancellationToken cancellationToken)
        {
            // Force window to restore and show
            ShowWindow(hWnd, SW_RESTORE);
            await Task.Delay(100, cancellationToken);
            
            ShowWindow(hWnd, SW_SHOW);
            BringWindowToTop(hWnd);
            
            // Multiple activation attempts in quick succession
            for (int i = 0; i < 3; i++)
            {
                SetForegroundWindow(hWnd);
                await Task.Delay(10, cancellationToken);
                
                if (GetForegroundWindow() == hWnd)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Analyzes why window activation failed to provide detailed diagnostics.
        /// </summary>
        private static WindowActivationFailureReason AnalyzeActivationFailure(IntPtr hWnd, WindowStateInfo initialState, WindowStateInfo finalState)
        {
            // Window was destroyed during activation
            if (!IsWindow(hWnd))
                return WindowActivationFailureReason.WindowDestroyed;
            
            // Window is hung
            if (!finalState.IsResponding)
                return WindowActivationFailureReason.WindowHung;
            
            // Window is not visible (might be hidden by another process)
            if (!finalState.IsVisible)
                return WindowActivationFailureReason.Unknown;
            
            // Check if another window is blocking (full-screen application)
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != IntPtr.Zero && foregroundWindow != hWnd)
            {
                var blockingState = GetWindowState(foregroundWindow);
                if (blockingState.IsMaximized || blockingState.ClassName?.Contains("fullscreen", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return WindowActivationFailureReason.FullScreenBlocking;
                }
            }
            
            // Check for UAC/elevation issues
            try
            {
                var threadId = GetWindowThreadProcessId(hWnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                // If we can't access the process, it might be elevated
                _ = process.MainWindowTitle;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5) // Access denied
            {
                return WindowActivationFailureReason.ElevationMismatch;
            }
            catch
            {
                // Other access issues
                return WindowActivationFailureReason.AccessDenied;
            }
            
            // Focus stealing prevention might be active
            if (finalState.IsVisible && !finalState.IsMinimized && !finalState.IsForeground)
            {
                return WindowActivationFailureReason.FocusStealingPrevention;
            }
            
            return WindowActivationFailureReason.Unknown;
        }
        
        #endregion
    }
}
