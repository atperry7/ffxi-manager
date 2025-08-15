using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace FFXIManager.Services
{
    /// <summary>
    /// Low-level keyboard hook implementation that intercepts keys before other applications
    /// This bypasses Windower and FFXI key interception
    /// </summary>
    public class LowLevelHotkeyService : IGlobalHotkeyService
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int HC_ACTION = 0;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private readonly Dictionary<int, HotkeyInfo> _registeredHotkeys = new();
        private LowLevelKeyboardProc? _hookProc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _disposed;

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
        public int RegisteredCount => _registeredHotkeys.Count(kvp => kvp.Value.IsRegistered);

        public LowLevelHotkeyService()
        {
            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
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
                foreach (var kvp in _registeredHotkeys)
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
            
            if (GetAsyncKeyState(0x10) < 0) // VK_SHIFT
                modifiers |= ModifierKeys.Shift;
            if (GetAsyncKeyState(0x11) < 0) // VK_CONTROL
                modifiers |= ModifierKeys.Control;
            if (GetAsyncKeyState(0x12) < 0) // VK_MENU (Alt)
                modifiers |= ModifierKeys.Alt;
            if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) // VK_LWIN, VK_RWIN
                modifiers |= ModifierKeys.Windows;
                
            return modifiers;
        }

        public bool RegisterHotkey(int id, ModifierKeys modifiers, Key key)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LowLevelHotkeyService));

            _registeredHotkeys[id] = new HotkeyInfo
            {
                Modifiers = modifiers,
                Key = key,
                IsRegistered = true
            };

            return true; // Low-level hooks always "succeed" at registration
        }

        public bool UnregisterHotkey(int id)
        {
            if (_disposed) return false;
            return _registeredHotkeys.Remove(id);
        }

        public void UnregisterAll()
        {
            if (_disposed) return;
            _registeredHotkeys.Clear();
        }

        public bool IsRegistered(int id)
        {
            return _registeredHotkeys.TryGetValue(id, out var info) && info.IsRegistered;
        }

        public void Dispose()
        {
            if (_disposed) return;

            UnregisterAll();
            
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
