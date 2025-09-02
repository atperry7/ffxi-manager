using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;

namespace FFXIManager.Services
{
    /// <summary>
    /// Event-driven window tracking using Win32 event hooks for instant title change detection.
    /// Replaces inefficient polling with real-time Windows events.
    /// </summary>
    public class WindowEventTracker : IWindowEventTracker, IDisposable
    {
        private readonly ILoggingService _logging;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly ConcurrentDictionary<int, ProcessWindowTracker> _trackers = new();
        
        private bool _disposed;

        // Win32 Event Hook constants
        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;      // Window title changed
        private const uint EVENT_OBJECT_CREATE = 0x8000;         // Window created
        private const uint EVENT_OBJECT_DESTROY = 0x8001;        // Window destroyed
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;       // Async callback
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;     // Skip our own process

        // P/Invoke declarations
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        // Events
        public event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;
        public event EventHandler<WindowEventArgs>? WindowCreated;
        public event EventHandler<WindowEventArgs>? WindowDestroyed;

        public WindowEventTracker(ILoggingService logging, IUiDispatcher uiDispatcher)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        public void StartTrackingProcess(int processId, string processName)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowEventTracker));

            if (_trackers.ContainsKey(processId))
            {
                _ = _logging.LogWarningAsync($"Already tracking process {processId} ({processName})", "WindowEventTracker");
                return;
            }

            try
            {
                // Log tracking attempts
                _ = _logging.LogInfoAsync($"Starting event tracking for PID {processId} ({processName})", "WindowEventTracker");
                
                var tracker = new ProcessWindowTracker(processId, processName, this);
                if (_trackers.TryAdd(processId, tracker))
                {
                    _ = _logging.LogInfoAsync($"Successfully started event-driven tracking for process {processId} ({processName})", "WindowEventTracker");
                }
                else
                {
                    tracker.Dispose();
                    _ = _logging.LogErrorAsync($"Failed to add tracker for process {processId} ({processName}) - already exists", null, "WindowEventTracker");
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync($"Exception starting tracking for process {processId} ({processName})", ex, "WindowEventTracker");
            }
        }

        public void StopTrackingProcess(int processId)
        {
            if (_trackers.TryRemove(processId, out var tracker))
            {
                tracker.Dispose();
                _ = _logging.LogInfoAsync($"Stopped tracking process {processId}", "WindowEventTracker");
            }
        }

        public void StopAllTracking()
        {
            var trackersToDispose = new List<ProcessWindowTracker>();
            
            foreach (var kvp in _trackers)
            {
                if (_trackers.TryRemove(kvp.Key, out var tracker))
                {
                    trackersToDispose.Add(tracker);
                }
            }

            foreach (var tracker in trackersToDispose)
            {
                tracker.Dispose();
            }

            _ = _logging.LogInfoAsync("Stopped all window event tracking", "WindowEventTracker");
        }

        // Internal method called by ProcessWindowTracker
        internal void OnWindowEvent(int processId, IntPtr windowHandle, WindowEventType eventType, string? windowTitle = null)
        {
            try
            {
                switch (eventType)
                {
                    case WindowEventType.TitleChanged:
                        if (!string.IsNullOrEmpty(windowTitle))
                        {
                            var args = new WindowTitleChangedEventArgs(processId, windowHandle, windowTitle);
                            _uiDispatcher.BeginInvoke(() => WindowTitleChanged?.Invoke(this, args));
                        }
                        break;

                    case WindowEventType.Created:
                        {
                            var args = new WindowEventArgs(processId, windowHandle, windowTitle ?? GetWindowTitle(windowHandle));
                            _uiDispatcher.BeginInvoke(() => WindowCreated?.Invoke(this, args));
                        }
                        break;

                    case WindowEventType.Destroyed:
                        {
                            var args = new WindowEventArgs(processId, windowHandle, windowTitle ?? string.Empty);
                            _uiDispatcher.BeginInvoke(() => WindowDestroyed?.Invoke(this, args));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _ = _logging.LogErrorAsync($"Error handling window event for process {processId}", ex, "WindowEventTracker");
            }
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
                    return string.Empty;

                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return string.Empty;

                var buffer = new char[length + 1];
                int result = GetWindowText(hWnd, buffer, buffer.Length);
                
                if (result <= 0)
                    return string.Empty;
                
                // **FIX**: Handle null terminators and clean up the string
                var title = new string(buffer, 0, result).Trim('\0').Trim();
                
                // **FIX**: If title is literally "NULL" or empty, return empty string
                if (string.IsNullOrWhiteSpace(title) || title.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
                
                return title;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopAllTracking();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Tracks window events for a specific process using Win32 event hooks
    /// </summary>
    internal sealed class ProcessWindowTracker : IDisposable
    {
        private readonly int _processId;
        private readonly string _processName;
        private readonly WindowEventTracker _parent;
        private readonly WinEventDelegate _eventDelegate;
        
        private IntPtr _titleChangeHook;
        private IntPtr _createHook;
        private IntPtr _destroyHook;
        private bool _disposed;
        
        // **FALLBACK**: Polling for processes that don't generate events
        private readonly Dictionary<IntPtr, string> _lastKnownTitles = new();
        private DateTime _lastEventTime = DateTime.UtcNow;

        public ProcessWindowTracker(int processId, string processName, WindowEventTracker parent)
        {
            _processId = processId;
            _processName = processName;
            _parent = parent;
            
            // Keep delegate alive to prevent garbage collection
            _eventDelegate = WinEventCallback;
            
            SetupEventHooks();
            
            // **DISABLED**: Fallback polling was causing race conditions
            // Will implement a cleaner solution focused specifically on POL processes
        }
        
        private static bool IsGameProcess(string processName)
        {
            // Known processes that may not generate proper window events
            var gameProcesses = new[] { "pol", "ffxi", "PlayOnlineViewer", "ffxi-boot" };
            return gameProcesses.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        private void SetupEventHooks()
        {
            try
            {
                // Hook for window title changes (most important for FFXI login tracking)
                _titleChangeHook = SetWinEventHook(
                    EVENT_OBJECT_NAMECHANGE,
                    EVENT_OBJECT_NAMECHANGE,
                    IntPtr.Zero,
                    _eventDelegate,
                    (uint)_processId,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
                );

                // Hook for window creation
                _createHook = SetWinEventHook(
                    EVENT_OBJECT_CREATE,
                    EVENT_OBJECT_CREATE,
                    IntPtr.Zero,
                    _eventDelegate,
                    (uint)_processId,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
                );

                // Hook for window destruction
                _destroyHook = SetWinEventHook(
                    EVENT_OBJECT_DESTROY,
                    EVENT_OBJECT_DESTROY,
                    IntPtr.Zero,
                    _eventDelegate,
                    (uint)_processId,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
                );

                // Verify hook handles
                bool titleHookOk = _titleChangeHook != IntPtr.Zero;
                bool createHookOk = _createHook != IntPtr.Zero;
                bool destroyHookOk = _destroyHook != IntPtr.Zero;
                
                if (!titleHookOk || !createHookOk || !destroyHookOk)
                {
                    var lastError = Marshal.GetLastWin32Error();
                    // Hook setup failed, but continue with whatever hooks succeeded
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void WinEventCallback(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, 
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                // **SMART FALLBACK**: Mark last event time
                _lastEventTime = DateTime.UtcNow;

                // Process window event callback

                // Only handle window events (idObject == 0)
                if (idObject != 0 || hwnd == IntPtr.Zero)
                {
                        return;
                }

                // Verify this is our process
                var threadId = GetWindowThreadProcessId(hwnd, out uint windowProcessId);
                if (threadId == 0)
                {
                    return;
                }
                
                if (windowProcessId != _processId)
                {
                    return;
                }

                // Check window visibility
                bool isVisible = IsWindowVisible(hwnd);

                // Get current window title for debugging
                var currentTitle = GetWindowTitle(hwnd);

                switch (eventType)
                {
                    case EVENT_OBJECT_NAMECHANGE:
                        var title = GetWindowTitle(hwnd);
                        if (!string.IsNullOrEmpty(title))
                        {
                            _lastKnownTitles[hwnd] = title; // Update cache
                            _parent.OnWindowEvent(_processId, hwnd, WindowEventType.TitleChanged, title);
                        }
                        else
                        {
                        }
                        break;

                    case EVENT_OBJECT_CREATE:
                        _parent.OnWindowEvent(_processId, hwnd, WindowEventType.Created);
                        break;

                    case EVENT_OBJECT_DESTROY:
                        _lastKnownTitles.Remove(hwnd); // Remove from cache
                        _parent.OnWindowEvent(_processId, hwnd, WindowEventType.Destroyed);
                        break;
                        
                    default:
                        break;
                }
            }
            catch (Exception)
            {
                // Ignore callback exceptions to prevent destabilizing the event system
            }
        }
        
        private static List<WindowInfo> GetProcessWindows(int processId)
        {
            var windows = new List<WindowInfo>();
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return windows;
                    
                // Get main window
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    windows.Add(new WindowInfo 
                    { 
                        Handle = process.MainWindowHandle,
                        Title = GetWindowTitle(process.MainWindowHandle),
                        IsMainWindow = true,
                        IsVisible = IsWindowVisible(process.MainWindowHandle)
                    });
                }
                
                // For now, just check main window - can be expanded if needed
            }
            catch (Exception)
            {
            }
            
            return windows;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {

                if (_titleChangeHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_titleChangeHook);
                    _titleChangeHook = IntPtr.Zero;
                }

                if (_createHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_createHook);
                    _createHook = IntPtr.Zero;
                }

                if (_destroyHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_destroyHook);
                    _destroyHook = IntPtr.Zero;
                }

            }
            catch (Exception)
            {
            }

            GC.SuppressFinalize(this);
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return string.Empty;

                var buffer = new char[length + 1];
                int result = GetWindowText(hWnd, buffer, buffer.Length);
                
                if (result <= 0)
                    return string.Empty;
                
                // **FIX**: Handle null terminators and clean up the string
                var title = new string(buffer, 0, result).Trim('\0').Trim();
                
                // **FIX**: If title is literally "NULL" or empty, return empty string
                if (string.IsNullOrWhiteSpace(title) || title.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
                
                return title;
            }
            catch
            {
                return string.Empty;
            }
        }

        // P/Invoke declarations (same as parent class)
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
        private const uint EVENT_OBJECT_CREATE = 0x8000;
        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    }

    /// <summary>
    /// Interface for the WindowEventTracker service
    /// </summary>
    public interface IWindowEventTracker : IDisposable
    {
        event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;
        event EventHandler<WindowEventArgs>? WindowCreated;
        event EventHandler<WindowEventArgs>? WindowDestroyed;

        void StartTrackingProcess(int processId, string processName);
        void StopTrackingProcess(int processId);
        void StopAllTracking();
    }

    /// <summary>
    /// Event arguments for window title changes
    /// </summary>
    public class WindowTitleChangedEventArgs : EventArgs
    {
        public int ProcessId { get; }
        public IntPtr WindowHandle { get; }
        public string NewTitle { get; }

        public WindowTitleChangedEventArgs(int processId, IntPtr windowHandle, string newTitle)
        {
            ProcessId = processId;
            WindowHandle = windowHandle;
            NewTitle = newTitle;
        }
    }

    /// <summary>
    /// Event arguments for window creation/destruction
    /// </summary>
    public class WindowEventArgs : EventArgs
    {
        public int ProcessId { get; }
        public IntPtr WindowHandle { get; }
        public string WindowTitle { get; }

        public WindowEventArgs(int processId, IntPtr windowHandle, string windowTitle)
        {
            ProcessId = processId;
            WindowHandle = windowHandle;
            WindowTitle = windowTitle;
        }
    }

    /// <summary>
    /// Types of window events we track
    /// </summary>
    internal enum WindowEventType
    {
        TitleChanged,
        Created,
        Destroyed
    }
}