using FFXIManager.Models;
using FFXIManager.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

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
        bool IsWindowValid(IntPtr windowHandle);
        Task<ProcessBasicInfo?> GetProcessInfoAsync(int processId);
        Task<List<ProcessBasicInfo>> GetProcessesByNamesAsync(IEnumerable<string> processNames);

        // Monitor detection and window positioning methods
        Rectangle GetPrimaryMonitorBounds();
        Rectangle GetWindowMonitorBounds(IntPtr windowHandle);
        bool IsWindowOnPrimaryMonitor(IntPtr windowHandle);
        Task<bool> MoveWindowToPrimaryMonitorAsync(IntPtr windowHandle);
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
        private readonly ILoggingService _loggingService; // Added for consistency in GetWindowTitle
        private const int DEFAULT_TIMEOUT_MS = 5000;

        // **FIX FOR WINDOW LOCKUP**: Serialize all window activations to prevent overlapping
        // AttachThreadInput calls that can corrupt a window's input queue when cancelled rapidly.
        private static readonly SemaphoreSlim _activationLock = new(1, 1);

        // **FIX FOR ISSUE #12**: Track the most recently AttachThreadInput-attached target thread so we
        // can defensively detach it before any new activation. Win11 KB5083769 delays full release of
        // AttachThreadInput, causing keystrokes to leak into prior windows. Cleared on successful detach.
        private static uint _lastAttachedTargetThread;

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
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        // **FIX FOR ISSUE #12**: Per-call foreground-lock bypass. Safer than SPI_SETFOREGROUNDLOCKTIMEOUT
        // because it doesn't mutate a global registry-backed setting that could leak on crash/interrupt.
        [DllImport("user32.dll")]
        private static extern bool LockSetForegroundWindow(uint uLockCode);
        private const uint LSFW_UNLOCK = 2;

        [DllImport("user32.dll")]
        private static extern int GetLastError();

        // Monitor detection APIs
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint GW_HWNDNEXT = 2;
        private const uint GW_HWNDPREV = 3;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        // Monitor constants
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOSIZE = 0x0001;

        // Monitor structures
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public uint cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
        }

        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        #endregion

        public ProcessUtilityService(ILoggingService logging)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _loggingService = logging; // Set both for compatibility
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

            // **FIX FOR WINDOW LOCKUP**: Acquire lock to serialize activations
            if (!await _activationLock.WaitAsync(timeoutMs))
            {
                return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.Timeout,
                    "Activation lock timeout - another activation in progress");
            }

            try
            {
                // **FIX FOR ISSUE #12**: Defensive detach of any stale thread input attachment from a
                // prior activation. Without this, Win11 KB5083769 can leave the OS routing keystrokes
                // into the previously-activated window's queue.
                if (_lastAttachedTargetThread != 0)
                {
                    AttachThreadInput(GetCurrentThreadId(), _lastAttachedTargetThread, false);
                    _lastAttachedTargetThread = 0;
                }

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

                // **PERFORMANCE**: Reduce attempts and delays for faster response
                int attempts = 0;
                bool success = false;

                while (!success && attempts < 3 && !cts.Token.IsCancellationRequested)
                {
                    attempts++;
                    success = await AttemptWindowActivation(windowHandle, attempts, cts.Token);

                    if (!success && attempts < 3)
                    {
                        // **PERFORMANCE**: Reduced delay between attempts
                        await Task.Delay(Math.Min(20 * attempts, 50), cts.Token); // Max 50ms delay
                    }
                }

                stopwatch.Stop();

                // **VERIFICATION**: Get final window state
                var finalState = GetWindowState(windowHandle);

                if (success || finalState.IsForeground)
                {
                    // **FIX FOR ISSUE #12**: Surface attempt count at Info level so beta testers can
                    // confirm activations are succeeding on the lighter-touch attempts (1=Simple, 2=Switch)
                    // and not falling back to the heavier ThreadAttachment path.
                    await _logging.LogInfoAsync($"Window activation succeeded on attempt {attempts}/3 in {stopwatch.ElapsedMilliseconds}ms", "ProcessUtilityService");

                    // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after successful activation
                    ResetKeyboardState();

                    // **FIX FOR ISSUE #12**: Brief settle delay (inside the lock) gives the OS input
                    // router time to fully retire the prior foreground assignment before the next
                    // hotkey can begin its activation. Combined with the upstream 150ms minimum
                    // interval check, this keeps the input queue chain from accumulating.
                    await Task.Delay(15, cts.Token);

                    var successResult = WindowActivationResult.Successful(windowHandle, stopwatch.Elapsed, attempts);
                    successResult.WindowState = finalState;
                    return successResult;
                }

                // **FAILURE ANALYSIS**: Determine why activation failed
                var failureReason = AnalyzeActivationFailure(windowHandle, initialState, finalState);

                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after failed activation
                ResetKeyboardState();

                var failedResult = WindowActivationResult.Failed(windowHandle, failureReason,
                    $"Failed after {attempts} attempts. Final state: {finalState}");
                failedResult.Duration = stopwatch.Elapsed;
                failedResult.AttemptsRequired = attempts;
                failedResult.WindowState = finalState;
                return failedResult;
            }
            catch (OperationCanceledException)
            {
                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after cancellation
                ResetKeyboardState();

                return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.Timeout,
                    $"Activation timed out after {timeoutMs}ms");
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Unexpected error during window activation", ex, "ProcessUtilityService");

                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after exception
                ResetKeyboardState();

                return WindowActivationResult.Failed(windowHandle, WindowActivationFailureReason.Unknown, ex.Message);
            }
            finally
            {
                // **FIX FOR WINDOW LOCKUP**: Always release the activation lock
                _activationLock.Release();
            }
        }

        public async Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            // **FIX FOR WINDOW LOCKUP**: Acquire lock to serialize activations
            // This prevents overlapping AttachThreadInput calls that can corrupt input queues
            if (!await _activationLock.WaitAsync(timeoutMs))
            {
                await _logging.LogWarningAsync($"Activation lock timeout for 0x{windowHandle.ToInt64():X}", "ProcessUtilityService");
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
                        bool attached = false;
                        try
                        {
                            // **FIX FOR KEYBOARD LOCKUP**: Ensure thread detachment with try-finally
                            // If cancellation or exception occurs, threads must be detached to prevent
                            // corrupted keyboard input routing between windows.
                            attached = AttachThreadInput(currentThread, targetThread, true);
                            if (attached)
                            {
                                SetForegroundWindow(windowHandle);
                                BringWindowToTop(windowHandle);

                                // Small delay to let the activation take effect
                                await Task.Delay(10, cts.Token);
                            }
                        }
                        finally
                        {
                            // **CRITICAL**: Always detach, even if cancelled or exception thrown
                            if (attached)
                            {
                                AttachThreadInput(currentThread, targetThread, false);
                            }
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

                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after activation (success or failure)
                ResetKeyboardState();

                return success;
            }
            catch (OperationCanceledException)
            {
                await _logging.LogWarningAsync($"Window activation timeout ({timeoutMs}ms) for 0x{windowHandle.ToInt64():X}",
                    "ProcessUtilityService");

                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after timeout
                ResetKeyboardState();

                return false;
            }
            catch (Exception ex)
            {
                await _logging.LogWarningAsync($"Error activating window 0x{windowHandle.ToInt64():X}: {ex.Message}",
                    "ProcessUtilityService");

                // **FIX FOR KEYBOARD LOCKUP**: Reset keyboard state after exception
                ResetKeyboardState();

                return false;
            }
            finally
            {
                // **FIX FOR WINDOW LOCKUP**: Always release the activation lock
                _activationLock.Release();
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

        private string GetWindowTitle(IntPtr hWnd)
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
                        var title = new string(buffer, 0, result);
                        _ = _loggingService.LogDebugAsync($"ProcessUtility.GetWindowTitle: Retrieved '{title}' for handle 0x{hWnd.ToInt64():X}", "ProcessUtilityService");
                        return title;
                    }
                    else
                    {
                        _ = _loggingService.LogDebugAsync($"ProcessUtility.GetWindowTitle: GetWindowText returned 0 for handle 0x{hWnd.ToInt64():X}, length was {length}", "ProcessUtilityService");
                    }
                }
                else
                {
                    _ = _loggingService.LogDebugAsync($"ProcessUtility.GetWindowTitle: GetWindowTextLength returned {length} for handle 0x{hWnd.ToInt64():X}", "ProcessUtilityService");
                }
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogDebugAsync($"ProcessUtility.GetWindowTitle: Exception for handle 0x{hWnd.ToInt64():X}: {ex.Message}", "ProcessUtilityService");
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
        /// <remarks>
        /// **FIX FOR ISSUE #12**: Ordering deliberately defers AttachThreadInput-based activation to the
        /// last attempt. Win11 KB5083769 causes thread-input attachment chains to leak keystrokes into
        /// previously-activated windows, so we prefer non-attaching strategies first.
        /// </remarks>
        private static async Task<bool> AttemptWindowActivation(IntPtr hWnd, int attemptNumber, CancellationToken cancellationToken)
        {
            switch (attemptNumber)
            {
                case 1:
                    // **ATTEMPT 1**: Simple activation - SetForegroundWindow + BringWindowToTop, no attach
                    return await SimpleActivation(hWnd, cancellationToken);

                case 2:
                    // **ATTEMPT 2**: Switch activation - SwitchToThisWindow + LockSetForegroundWindow unlock,
                    // still no thread attachment. Sufficient for the vast majority of activations.
                    return await SwitchActivation(hWnd, cancellationToken);

                case 3:
                    // **ATTEMPT 3**: Thread-attachment activation. Last resort because AttachThreadInput
                    // is the source of the input-leak bug — we rely on the defensive detach at the top
                    // of ActivateWindowEnhancedAsync to prevent the chain from accumulating.
                    return await ThreadAttachmentActivation(hWnd, cancellationToken);

                default:
                    return false;
            }
        }

        private static async Task<bool> SimpleActivation(IntPtr hWnd, CancellationToken cancellationToken)
        {
            // **PERFORMANCE**: Check if already foreground first
            if (GetForegroundWindow() == hWnd)
                return true;

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                // **PERFORMANCE**: Reduced delay
                await Task.Delay(20, cancellationToken);
            }

            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);

            // **PERFORMANCE**: Reduced delay
            await Task.Delay(5, cancellationToken);
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
                    // **FIX FOR ISSUE #12**: Record the attached thread so the next activation can
                    // defensively detach it on entry (in case our finally-block detach below is
                    // delayed by the OS — a Win11 KB5083769 behavior).
                    _lastAttachedTargetThread = targetThread;

                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        // **PERFORMANCE**: Reduced delay
                        await Task.Delay(20, cancellationToken);
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
                    // **FIX FOR ISSUE #12**: Clear the tracked thread only after our own detach call
                    // completes. If a future activation finds it non-zero, it means a prior detach
                    // didn't fully take and a defensive retry is warranted.
                    _lastAttachedTargetThread = 0;
                }
            }

            return GetForegroundWindow() == hWnd;
        }

        /// <summary>
        /// **FIX FOR ISSUE #12**: Switch-based activation that bypasses Win11 foreground-lock without
        /// AttachThreadInput. Combines SwitchToThisWindow, LockSetForegroundWindow(LSFW_UNLOCK), and
        /// the Alt-tap user-input simulation. Replaces the previous AggressiveActivation, which mutated
        /// the system-wide SPI_SETFOREGROUNDLOCKTIMEOUT — fragile under KB5083769 and crash-unsafe.
        /// </summary>
        private static async Task<bool> SwitchActivation(IntPtr hWnd, CancellationToken cancellationToken)
        {
            _ = GetWindowThreadProcessId(hWnd, out uint targetPid);

            // Grant the target process foreground rights so SetForegroundWindow can succeed.
            AllowSetForegroundWindow((int)targetPid);

            // Per-call unlock of the foreground-window state. Safer than the SPI timeout mutation —
            // scoped to this activation, no global cleanup required.
            LockSetForegroundWindow(LSFW_UNLOCK);

            // SwitchToThisWindow is more reliable than SetForegroundWindow for game windows on Win11.
            SwitchToThisWindow(hWnd, true);

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                await Task.Delay(30, cancellationToken);
            }

            ShowWindow(hWnd, SW_SHOW);
            BringWindowToTop(hWnd);

            for (int i = 0; i < 5; i++)
            {
                SetForegroundWindow(hWnd);

                if (i == 1)
                {
                    SwitchToThisWindow(hWnd, true);
                }

                if (i == 2)
                {
                    // Synthetic Alt-tap fakes user input, which lets the OS treat our SetForegroundWindow
                    // call as user-initiated. LowLevelHotkeyService skips LLKHF_INJECTED keys, so this
                    // doesn't re-enter our own hotkey hook.
                    keybd_event(0x12, 0, 0, 0); // Alt down
                    keybd_event(0x12, 0, 2, 0); // Alt up (KEYEVENTF_KEYUP)
                }

                await Task.Delay(10, cancellationToken);

                if (GetForegroundWindow() == hWnd)
                {
                    return true;
                }
            }

            return false;
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetKeyboardState(byte[] lpKeyState);

        // Virtual key codes for modifier keys
        private const byte VK_SHIFT = 0x10;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MENU = 0x12;     // Alt key
        private const byte VK_LSHIFT = 0xA0;
        private const byte VK_RSHIFT = 0xA1;
        private const byte VK_LCONTROL = 0xA2;
        private const byte VK_RCONTROL = 0xA3;
        private const byte VK_LMENU = 0xA4;    // Left Alt
        private const byte VK_RMENU = 0xA5;    // Right Alt
        private const byte VK_LWIN = 0x5B;
        private const byte VK_RWIN = 0x5C;

        /// <summary>
        /// Resets keyboard state by clearing all modifier keys.
        /// This is a defensive safeguard to prevent stuck modifier keys after window activation.
        /// </summary>
        /// <remarks>
        /// **FIX FOR KEYBOARD LOCKUP**: If keyboard simulation during window activation doesn't complete cleanly,
        /// or if thread input attachment corrupts state, Windows might believe modifier keys are still pressed.
        /// This method explicitly clears all modifier key states to prevent "partial keyboard functionality" issues.
        /// </remarks>
        private static bool ResetKeyboardState()
        {
            try
            {
                byte[] keyState = new byte[256];
                if (!GetKeyboardState(keyState))
                {
                    System.Diagnostics.Debug.WriteLine("[KEYBOARD RESET] Failed to get keyboard state");
                    return false;
                }

                // Clear all modifier keys (both generic and left/right specific)
                keyState[VK_SHIFT] = 0;
                keyState[VK_CONTROL] = 0;
                keyState[VK_MENU] = 0;
                keyState[VK_LSHIFT] = 0;
                keyState[VK_RSHIFT] = 0;
                keyState[VK_LCONTROL] = 0;
                keyState[VK_RCONTROL] = 0;
                keyState[VK_LMENU] = 0;
                keyState[VK_RMENU] = 0;
                keyState[VK_LWIN] = 0;
                keyState[VK_RWIN] = 0;

                if (!SetKeyboardState(keyState))
                {
                    System.Diagnostics.Debug.WriteLine("[KEYBOARD RESET] Failed to set keyboard state");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine("[KEYBOARD RESET] Successfully cleared all modifier keys");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KEYBOARD RESET] Exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a window handle is still valid and exists.
        /// </summary>
        public bool IsWindowValid(IntPtr windowHandle)
        {
            return windowHandle != IntPtr.Zero && IsWindow(windowHandle);
        }

        /// <summary>
        /// Analyzes why window activation failed to provide detailed diagnostics.
        /// </summary>
        private static WindowActivationFailureReason AnalyzeActivationFailure(IntPtr hWnd, WindowStateInfo initialState, WindowStateInfo finalState)
        {
            // Window was destroyed during activation
            if (!IsWindow(hWnd))
            {
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Window 0x{hWnd.ToInt64():X} was destroyed");
                return WindowActivationFailureReason.WindowDestroyed;
            }

            // Window is hung
            if (!finalState.IsResponding)
            {
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Window 0x{hWnd.ToInt64():X} is not responding");
                return WindowActivationFailureReason.WindowHung;
            }

            // Window is not visible (might be hidden by another process)
            if (!finalState.IsVisible)
            {
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Window 0x{hWnd.ToInt64():X} is not visible");
                return WindowActivationFailureReason.Unknown;
            }

            // Check if another window is blocking (full-screen application)
            var foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != IntPtr.Zero && foregroundWindow != hWnd)
            {
                var blockingState = GetWindowState(foregroundWindow);
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION DEBUG] Current foreground: {blockingState.WindowTitle} (0x{foregroundWindow.ToInt64():X})");

                if (blockingState.IsMaximized || blockingState.ClassName?.Contains("fullscreen", StringComparison.OrdinalIgnoreCase) == true)
                {
                    System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Blocked by fullscreen: {blockingState.WindowTitle}");
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
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Focus stealing prevention blocked window 0x{hWnd.ToInt64():X}");
                System.Diagnostics.Debug.WriteLine($"  Window State: Visible={finalState.IsVisible}, Minimized={finalState.IsMinimized}, Foreground={finalState.IsForeground}");
                System.Diagnostics.Debug.WriteLine($"  Z-Order: {finalState.ZOrder}, Title: {finalState.WindowTitle}");
                return WindowActivationFailureReason.FocusStealingPrevention;
            }

            System.Diagnostics.Debug.WriteLine($"[ACTIVATION FAILURE] Unknown reason for window 0x{hWnd.ToInt64():X}");
            return WindowActivationFailureReason.Unknown;
        }

        #endregion

        #region Monitor Detection and Window Positioning

        /// <summary>
        /// Gets the bounds of the primary monitor
        /// </summary>
        public Rectangle GetPrimaryMonitorBounds()
        {
            var primaryMonitor = IntPtr.Zero;
            var bounds = Rectangle.Empty;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                var monitorInfo = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    if ((monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0)
                    {
                        bounds = new Rectangle(
                            monitorInfo.rcMonitor.Left,
                            monitorInfo.rcMonitor.Top,
                            monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                            monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
                        );
                        return false; // Stop enumeration
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);

            return bounds;
        }

        /// <summary>
        /// Gets the bounds of the monitor containing the specified window
        /// </summary>
        public Rectangle GetWindowMonitorBounds(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return Rectangle.Empty;

            var hMonitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
                return Rectangle.Empty;

            var monitorInfo = new MonitorInfo { cbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
                return Rectangle.Empty;

            return new Rectangle(
                monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Top,
                monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top
            );
        }

        /// <summary>
        /// Checks if the window is currently on the primary monitor
        /// </summary>
        public bool IsWindowOnPrimaryMonitor(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return false;

            var windowMonitor = GetWindowMonitorBounds(windowHandle);
            var primaryMonitor = GetPrimaryMonitorBounds();

            return windowMonitor.Equals(primaryMonitor);
        }

        /// <summary>
        /// Moves a window to the primary monitor while preserving its relative position
        /// </summary>
        public async Task<bool> MoveWindowToPrimaryMonitorAsync(IntPtr windowHandle)
        {
            try
            {
                if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
                {
                    await _logging.LogWarningAsync("Cannot move window - invalid handle", "ProcessUtilityService");
                    return false;
                }

                // Check if window is already on primary monitor
                if (IsWindowOnPrimaryMonitor(windowHandle))
                {
                    await _logging.LogDebugAsync($"Window 0x{windowHandle.ToInt64():X} is already on primary monitor", "ProcessUtilityService");
                    return true;
                }

                var primaryBounds = GetPrimaryMonitorBounds();
                var currentMonitorBounds = GetWindowMonitorBounds(windowHandle);

                if (primaryBounds.IsEmpty || currentMonitorBounds.IsEmpty)
                {
                    await _logging.LogWarningAsync("Failed to get monitor bounds for window positioning", "ProcessUtilityService");
                    return false;
                }

                // Get current window position
                if (!GetWindowRect(windowHandle, out Rect currentRect))
                {
                    await _logging.LogWarningAsync("Failed to get window rectangle", "ProcessUtilityService");
                    return false;
                }

                // Calculate relative position within current monitor
                var relativeX = currentRect.Left - currentMonitorBounds.Left;
                var relativeY = currentRect.Top - currentMonitorBounds.Top;

                // Calculate new position on primary monitor
                var newX = primaryBounds.Left + relativeX;
                var newY = primaryBounds.Top + relativeY;

                // Ensure window stays within primary monitor bounds
                newX = Math.Max(primaryBounds.Left, Math.Min(newX, primaryBounds.Right - (currentRect.Right - currentRect.Left)));
                newY = Math.Max(primaryBounds.Top, Math.Min(newY, primaryBounds.Bottom - (currentRect.Bottom - currentRect.Top)));

                // Move the window
                bool success = SetWindowPos(windowHandle, IntPtr.Zero, newX, newY, 0, 0, SWP_NOSIZE | SWP_NOZORDER);

                if (success)
                {
                    await _logging.LogInfoAsync($"Successfully moved window 0x{windowHandle.ToInt64():X} to primary monitor at ({newX}, {newY})", "ProcessUtilityService");
                }
                else
                {
                    await _logging.LogWarningAsync($"Failed to move window 0x{windowHandle.ToInt64():X} to primary monitor", "ProcessUtilityService");
                }

                return success;
            }
            catch (Exception ex)
            {
                await _logging.LogErrorAsync($"Error moving window to primary monitor", ex, "ProcessUtilityService");
                return false;
            }
        }

        #endregion
    }
}
