using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;

namespace FFXIManager.Services
{
    /// <summary>
    /// Low-level keyboard hook implementation that intercepts keys before other applications.
    /// This bypasses Windower and FFXI key interception by using SetWindowsHookEx(WH_KEYBOARD_LL).
    /// Thread-safe and properly manages Win32 resources.
    /// </summary>
    public sealed class LowLevelHotkeyService : IGlobalHotkeyService
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int HC_ACTION = 0;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt key
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        
        // Temp recording hotkey ID used by KeyRecorderControl
        private const int TEMP_RECORDING_HOTKEY_ID = 99999;

        private readonly Dictionary<int, HotkeyInfo> _registeredHotkeys = new();
        private readonly object _lock = new();
        private LowLevelKeyboardProc? _hookProc;
        private IntPtr _hookId = IntPtr.Zero;
        private volatile bool _disposed;

        private sealed class HotkeyInfo
        {
            public ModifierKeys Modifiers { get; set; }
            public Key Key { get; set; }
            public bool IsRegistered { get; set; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
        /// <summary>
        /// Gets the number of currently registered hotkeys.
        /// </summary>
        public int RegisteredCount 
        { 
            get 
            { 
                lock (_lock) 
                { 
                    return _registeredHotkeys.Count(kvp => kvp.Value.IsRegistered); 
                } 
            } 
        }

        /// <summary>
        /// Initializes a new instance of the LowLevelHotkeyService.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the keyboard hook cannot be installed.</exception>
        public LowLevelHotkeyService()
        {
            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);
            
            if (_hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to install keyboard hook. Win32 error: {error}");
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule?.ModuleName != null)
                {
                    return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                        GetModuleHandle(curModule.ModuleName), 0);
                }
            }
            return IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= HC_ACTION && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var vkCode = (int)hookStruct.vkCode;
                
                // Get current modifier key state
                var modifiers = GetCurrentModifiers();
                var key = KeyInterop.KeyFromVirtualKey(vkCode);
                
                // Check if this matches any registered hotkeys
                Dictionary<int, HotkeyInfo> snapshot;
                lock (_lock)
                {
                    // Take a snapshot of registered hotkeys to avoid holding lock during event invocation
                    snapshot = new Dictionary<int, HotkeyInfo>(_registeredHotkeys);
                }
                
                // Special case: if we have the temp recording hotkey registered, fire for ALL keys
                if (snapshot.ContainsKey(TEMP_RECORDING_HOTKEY_ID))
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(TEMP_RECORDING_HOTKEY_ID, modifiers, key));
                    // Don't consume the key in recording mode to allow normal processing
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }
                
                foreach (var kvp in snapshot)
                {
                    var hotkeyInfo = kvp.Value;
                    if (hotkeyInfo.IsRegistered && 
                        hotkeyInfo.Modifiers == modifiers && 
                        hotkeyInfo.Key == key)
                    {
                        // Fire the event
                        HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(kvp.Key, modifiers, key));
                        
                        // Consume the key event (don't pass to other applications)
                        return new IntPtr(1);
                    }
                }
            }

            // Pass the key to other applications
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static ModifierKeys GetCurrentModifiers()
        {
            ModifierKeys modifiers = ModifierKeys.None;
            
            if (GetAsyncKeyState(VK_SHIFT) < 0)
                modifiers |= ModifierKeys.Shift;
            if (GetAsyncKeyState(VK_CONTROL) < 0)
                modifiers |= ModifierKeys.Control;
            if (GetAsyncKeyState(VK_MENU) < 0)
                modifiers |= ModifierKeys.Alt;
            if (GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0)
                modifiers |= ModifierKeys.Windows;
                
            return modifiers;
        }

        /// <summary>
        /// Registers a hotkey to be monitored by the low-level hook.
        /// </summary>
        /// <param name="id">Unique identifier for the hotkey.</param>
        /// <param name="modifiers">Modifier keys (Ctrl, Alt, Shift, Win).</param>
        /// <param name="key">Primary key.</param>
        /// <returns>True if registration succeeded.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the service has been disposed.</exception>
        public bool RegisterHotkey(int id, ModifierKeys modifiers, Key key)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LowLevelHotkeyService));

            lock (_lock)
            {
                _registeredHotkeys[id] = new HotkeyInfo
                {
                    Modifiers = modifiers,
                    Key = key,
                    IsRegistered = true
                };
            }

            return true; // Low-level hooks always "succeed" at registration
        }

        /// <summary>
        /// Unregisters a hotkey from monitoring.
        /// </summary>
        /// <param name="id">The hotkey ID to unregister.</param>
        /// <returns>True if the hotkey was found and removed.</returns>
        public bool UnregisterHotkey(int id)
        {
            if (_disposed) return false;
            
            lock (_lock)
            {
                return _registeredHotkeys.Remove(id);
            }
        }

        /// <summary>
        /// Unregisters all hotkeys from monitoring.
        /// </summary>
        public void UnregisterAll()
        {
            if (_disposed) return;
            
            lock (_lock)
            {
                _registeredHotkeys.Clear();
            }
        }

        /// <summary>
        /// Checks if a hotkey ID is currently registered.
        /// </summary>
        /// <param name="id">The hotkey ID to check.</param>
        /// <returns>True if the hotkey is registered.</returns>
        public bool IsRegistered(int id)
        {
            if (_disposed) return false;
            
            lock (_lock)
            {
                return _registeredHotkeys.TryGetValue(id, out var info) && info.IsRegistered;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Set flag first to prevent race conditions during disposal
            _disposed = true;
            
            UnregisterAll();
            
            // Ensure we unhook in a thread-safe manner
            var hookToRemove = Interlocked.Exchange(ref _hookId, IntPtr.Zero);
            if (hookToRemove != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookToRemove);
            }

            GC.SuppressFinalize(this);
        }
    }
}
