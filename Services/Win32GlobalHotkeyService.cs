using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FFXIManager.Services
{
    /// <summary>
    /// Win32-based implementation of global hotkey service
    /// </summary>
    public class Win32GlobalHotkeyService : IGlobalHotkeyService
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private readonly Dictionary<int, HotkeyInfo> _registeredHotkeys = new();
        private readonly Window _window;
        private IntPtr _windowHandle;
        private HwndSource? _hwndSource;
        private bool _disposed;

        private sealed class HotkeyInfo
        {
            public ModifierKeys Modifiers { get; set; }
            public Key Key { get; set; }
            public bool IsRegistered { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

        public int RegisteredCount => _registeredHotkeys.Count(kvp => kvp.Value.IsRegistered);

        public Win32GlobalHotkeyService(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            
            // Get window handle - this might be IntPtr.Zero if window isn't shown yet
            _windowHandle = new WindowInteropHelper(_window).Handle;
            
            // If window is already loaded, setup immediately
            if (_windowHandle != IntPtr.Zero)
            {
                SetupMessageHook();
            }
            else
            {
                // Otherwise wait for the window to be loaded
                _window.SourceInitialized += OnWindowSourceInitialized;
            }
            
            // Cleanup on window closing
            _window.Closing += OnWindowClosing;
        }

        private void OnWindowSourceInitialized(object? sender, EventArgs e)
        {
            // Update window handle now that window is initialized
            _windowHandle = new WindowInteropHelper(_window).Handle;
            SetupMessageHook();
        }

        private void SetupMessageHook()
        {
            if (_windowHandle == IntPtr.Zero) 
            {
                _windowHandle = new WindowInteropHelper(_window).Handle;
            }
            
            if (_windowHandle == IntPtr.Zero) return;

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(WndProc);
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            UnregisterAll();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkeyInfo))
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(hotkeyId, hotkeyInfo.Modifiers, hotkeyInfo.Key));
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public bool RegisterHotkey(int id, ModifierKeys modifiers, Key key)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Win32GlobalHotkeyService));
            if (_windowHandle == IntPtr.Zero) return false;

            // Convert WPF keys to Win32 constants
            var win32Modifiers = ConvertModifiersToWin32(modifiers);
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);

            // Attempt to register with the system
            bool success = RegisterHotKey(_windowHandle, id, win32Modifiers, (uint)virtualKey);

            if (success)
            {
                _registeredHotkeys[id] = new HotkeyInfo
                {
                    Modifiers = modifiers,
                    Key = key,
                    IsRegistered = true
                };
            }

            return success;
        }

        public bool UnregisterHotkey(int id)
        {
            if (_disposed) return false;
            if (_windowHandle == IntPtr.Zero) return false;

            bool success = UnregisterHotKey(_windowHandle, id);
            
            if (success || !_registeredHotkeys.ContainsKey(id))
            {
                _registeredHotkeys.Remove(id);
            }

            return success;
        }

        public void UnregisterAll()
        {
            if (_disposed || _windowHandle == IntPtr.Zero) return;

            var keysToRemove = _registeredHotkeys.Keys.ToList();
            foreach (int id in keysToRemove)
            {
                UnregisterHotkey(id);
            }

            _registeredHotkeys.Clear();
        }

        public bool IsRegistered(int id)
        {
            return _registeredHotkeys.TryGetValue(id, out var info) && info.IsRegistered;
        }

        private static uint ConvertModifiersToWin32(ModifierKeys modifiers)
        {
            uint win32Mods = MOD_NONE;

            if (modifiers.HasFlag(ModifierKeys.Alt))
                win32Mods |= MOD_ALT;
            if (modifiers.HasFlag(ModifierKeys.Control))
                win32Mods |= MOD_CONTROL;
            if (modifiers.HasFlag(ModifierKeys.Shift))
                win32Mods |= MOD_SHIFT;
            if (modifiers.HasFlag(ModifierKeys.Windows))
                win32Mods |= MOD_WIN;

            return win32Mods;
        }

        public void Dispose()
        {
            if (_disposed) return;

            UnregisterAll();

            if (_window != null)
            {
                _window.SourceInitialized -= OnWindowSourceInitialized;
                _window.Closing -= OnWindowClosing;
            }

            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
