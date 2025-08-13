using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

        public async Task<bool> ActivateWindowAsync(IntPtr windowHandle, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                return false;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                
                bool success = await Task.Run(() =>
                {
                    // Restore if minimized
                    if (IsIconic(windowHandle))
                    {
                        ShowWindow(windowHandle, SW_RESTORE);
                        Thread.Sleep(100);
                    }
                    else
                    {
                        ShowWindow(windowHandle, SW_SHOW);
                    }

                    // Try multiple methods to activate
                    SetForegroundWindow(windowHandle);
                    BringWindowToTop(windowHandle);

                    // Use thread input attachment as fallback
                    var currentThread = GetCurrentThreadId();
                    uint targetThread = GetWindowThreadProcessId(windowHandle, out _);
                    
                    if (currentThread != targetThread)
                    {
                        AttachThreadInput(currentThread, targetThread, true);
                        SetForegroundWindow(windowHandle);
                        AttachThreadInput(currentThread, targetThread, false);
                    }

                    return GetForegroundWindow() == windowHandle;
                }, cts.Token);

                if (success)
                {
                    await _logging.LogDebugAsync($"Successfully activated window 0x{windowHandle.ToInt64():X}", 
                        "ProcessUtilityService");
                }

                return success;
            }
            catch (Exception ex)
            {
                await _logging.LogWarningAsync($"Error activating window: {ex.Message}", 
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
    }
}
