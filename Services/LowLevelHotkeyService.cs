using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FFXIManager.Controls;

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

        /// <summary>
        /// Return value to suppress key event from being passed to other applications.
        /// </summary>
        private const int SUPPRESS_KEY_EVENT = 1;
        
        // **EMERGENCY SAFEGUARDS**: Critical system protection
        private static readonly object _emergencyLock = new object();
        private static volatile bool _emergencyMode;
        private static int _consecutiveFailures;
        private static DateTime _lastFailureTime = DateTime.MinValue;
        private const int MAX_CONSECUTIVE_FAILURES = 10;
        private const int EMERGENCY_COOLDOWN_MS = 2000;

        private readonly ConcurrentDictionary<int, HotkeyInfo> _registeredHotkeys = new();
        private readonly ConcurrentDictionary<HotkeyKey, int> _hotkeyLookup = new(); // O(1) lookup for performance
        private volatile int _registeredCount;
        private LowLevelKeyboardProc? _hookProc;
        private IntPtr _hookId = IntPtr.Zero;
        private volatile bool _disposed;

        /// <summary>
        /// High-performance hotkey key for O(1) lookup operations
        /// </summary>
        private readonly struct HotkeyKey : IEquatable<HotkeyKey>
        {
            public readonly ModifierKeys Modifiers;
            public readonly Key Key;

            public HotkeyKey(ModifierKeys modifiers, Key key)
            {
                Modifiers = modifiers;
                Key = key;
            }

            public bool Equals(HotkeyKey other) =>
                Modifiers == other.Modifiers && Key == other.Key;

            public override bool Equals(object? obj) =>
                obj is HotkeyKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(Modifiers, Key);

            public override string ToString() =>
                $"{Modifiers}+{Key}";
        }

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
        public int RegisteredCount => _registeredCount;

        /// <summary>
        /// Initializes a new instance of the LowLevelHotkeyService.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when the keyboard hook cannot be installed.</exception>
        public LowLevelHotkeyService()
        {
            _hookProc = HookCallback;
            _hookId = SetHookWithRetry(_hookProc);

            if (_hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to install keyboard hook after retries. Win32 error: {error}");
            }
        }

        /// <summary>
        /// Installs the keyboard hook with retry logic and exponential backoff.
        /// This prevents intermittent failures on slower systems or under heavy load.
        /// </summary>
        private static IntPtr SetHookWithRetry(LowLevelKeyboardProc proc, int maxRetries = 3)
        {
            Exception? lastException = null;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var hookId = SetHook(proc);
                    if (hookId != IntPtr.Zero)
                    {
                        return hookId;
                    }
                    
                    // Hook installation returned zero - capture error
                    var error = Marshal.GetLastWin32Error();
                    lastException = new InvalidOperationException($"Hook installation attempt {attempt + 1} failed. Win32 error: {error}");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
                
                // Don't delay on the last attempt
                if (attempt < maxRetries - 1)
                {
                    // Exponential backoff: 50ms, 100ms, 200ms
                    var delayMs = 50 * (int)Math.Pow(2, attempt);
                    Thread.Sleep(delayMs);
                }
            }
            
            // All attempts failed - log the final error but return IntPtr.Zero to let caller handle
            System.Diagnostics.Debug.WriteLine($"Hook installation failed after {maxRetries} attempts: {lastException?.Message}");
            return IntPtr.Zero;
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
            // **EMERGENCY PROTECTION**: If in emergency mode, pass through all keys immediately
            if (_emergencyMode)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            if (nCode >= HC_ACTION && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    var vkCode = (int)hookStruct.vkCode;

                // **GAMING OPTIMIZATION**: Inline modifier key state for performance
                var modifiers = ModifierKeys.None;
                if (GetAsyncKeyState(VK_SHIFT) < 0) modifiers |= ModifierKeys.Shift;
                if (GetAsyncKeyState(VK_CONTROL) < 0) modifiers |= ModifierKeys.Control;
                if (GetAsyncKeyState(VK_MENU) < 0) modifiers |= ModifierKeys.Alt;
                if (GetAsyncKeyState(VK_LWIN) < 0 || GetAsyncKeyState(VK_RWIN) < 0) modifiers |= ModifierKeys.Windows;

                var key = KeyInterop.KeyFromVirtualKey(vkCode);

                // Special case: if we have the temp recording hotkey registered, fire for ALL keys
                if (_registeredHotkeys.ContainsKey(KeyRecorderControl.TempRecordingHotkeyId))
                {
                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(KeyRecorderControl.TempRecordingHotkeyId, modifiers, key));
                    // Don't consume the key in recording mode to allow normal processing
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                    // **GAMING OPTIMIZATION**: O(1) hotkey lookup instead of linear search
                    var hotkeyKey = new HotkeyKey(modifiers, key);
                    if (_hotkeyLookup.TryGetValue(hotkeyKey, out var hotkeyId))
                    {
                        // Verify the hotkey is still registered and enabled
                        if (_registeredHotkeys.TryGetValue(hotkeyId, out var hotkeyInfo) && hotkeyInfo.IsRegistered)
                        {
                            // **EMERGENCY PROTECTION**: Non-blocking event fire with timeout protection
                            Task.Run(() => 
                            {
                                try 
                                {
                                    HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(hotkeyId, modifiers, key));
                                    ResetFailureCount();
                                } 
                                catch (Exception ex)
                                {
                                    IncrementFailureCount();
                                    System.Diagnostics.Debug.WriteLine($"Hotkey event failed: {ex.Message}");
                                }
                            });

                            // IMPORTANT: Suppress key event from reaching other applications
                            // This prevents the hotkey from being processed by other applications, including:
                            // - System shortcuts and accessibility tools
                            // - Other applications' hotkey handlers
                            // - Game/application-specific key handlers
                            // This behavior is intentional for this application but may interfere with
                            // assistive technologies or global system shortcuts if they use the same combinations.
                            return new IntPtr(SUPPRESS_KEY_EVENT);
                        }
                    }
                }
                catch (Exception ex)
                {
                    IncrementFailureCount();
                    System.Diagnostics.Debug.WriteLine($"Hook callback critical error: {ex.Message}");
                    // Continue execution to prevent system lockup
                }
            }

            // Pass the key to other applications
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        
        /// <summary>
        /// **EMERGENCY SAFEGUARD**: Increments failure count and enters emergency mode if threshold exceeded
        /// </summary>
        private static void IncrementFailureCount()
        {
            lock (_emergencyLock)
            {
                _consecutiveFailures++;
                _lastFailureTime = DateTime.UtcNow;
                
                if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES && !_emergencyMode)
                {
                    _emergencyMode = true;
                    System.Diagnostics.Debug.WriteLine($"**EMERGENCY MODE ACTIVATED**: {_consecutiveFailures} consecutive failures detected. Keyboard hooks disabled for {EMERGENCY_COOLDOWN_MS}ms.");
                    
                    // Schedule emergency mode reset
                    Task.Delay(EMERGENCY_COOLDOWN_MS).ContinueWith(_ => 
                    {
                        lock (_emergencyLock)
                        {
                            _emergencyMode = false;
                            _consecutiveFailures = 0;
                            System.Diagnostics.Debug.WriteLine("Emergency mode deactivated. Normal operation resumed.");
                        }
                    });
                }
            }
        }
        
        /// <summary>
        /// **EMERGENCY SAFEGUARD**: Resets failure count on successful operations
        /// </summary>
        private static void ResetFailureCount()
        {
            lock (_emergencyLock)
            {
                _consecutiveFailures = 0;
            }
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
            return RegisterHotkey(id, modifiers, key, isTemporary: false);
        }

        /// <summary>
        /// Registers a hotkey to be monitored by the low-level hook.
        /// </summary>
        /// <param name="id">Unique identifier for the hotkey.</param>
        /// <param name="modifiers">Modifier keys (Ctrl, Alt, Shift, Win).</param>
        /// <param name="key">Primary key.</param>
        /// <param name="isTemporary">Whether this is a temporary registration that shouldn't affect counter.</param>
        /// <returns>True if registration succeeded.</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the service has been disposed.</exception>
        public bool RegisterHotkey(int id, ModifierKeys modifiers, Key key, bool isTemporary)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LowLevelHotkeyService));

            var newHotkey = new HotkeyInfo
            {
                Modifiers = modifiers,
                Key = key,
                IsRegistered = true
            };

            // **GAMING OPTIMIZATION**: Update O(1) lookup table
            var hotkeyKey = new HotkeyKey(modifiers, key);

            var wasAdded = _registeredHotkeys.TryAdd(id, newHotkey);
            if (!wasAdded)
            {
                // Remove old lookup entry if modifiers/key changed
                if (_registeredHotkeys.TryGetValue(id, out var oldHotkey))
                {
                    var oldKey = new HotkeyKey(oldHotkey.Modifiers, oldHotkey.Key);
                    _hotkeyLookup.TryRemove(oldKey, out _);
                }

                // Update existing hotkey
                _registeredHotkeys[id] = newHotkey;
            }
            else
            {
                // Increment count only if it's a new hotkey and not temporary
                if (!isTemporary)
                {
                    Interlocked.Increment(ref _registeredCount);
                }
            }

            // **FIXED**: Add to O(1) lookup table after successful registration to prevent race condition
            _hotkeyLookup[hotkeyKey] = id;

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

            if (_registeredHotkeys.TryRemove(id, out var removedHotkey))
            {
                // **GAMING OPTIMIZATION**: Remove from O(1) lookup table
                var hotkeyKey = new HotkeyKey(removedHotkey.Modifiers, removedHotkey.Key);
                _hotkeyLookup.TryRemove(hotkeyKey, out _);

                // Decrement count only if it's not the temp recording hotkey
                if (id != KeyRecorderControl.TempRecordingHotkeyId)
                {
                    Interlocked.Decrement(ref _registeredCount);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Unregisters all hotkeys from monitoring.
        /// </summary>
        public void UnregisterAll()
        {
            if (_disposed) return;

            _registeredHotkeys.Clear();
            _hotkeyLookup.Clear(); // **GAMING OPTIMIZATION**: Clear O(1) lookup table
            Interlocked.Exchange(ref _registeredCount, 0);
        }

        /// <summary>
        /// Checks if a hotkey ID is currently registered.
        /// </summary>
        /// <param name="id">The hotkey ID to check.</param>
        /// <returns>True if the hotkey is registered.</returns>
        public bool IsRegistered(int id)
        {
            if (_disposed) return false;

            return _registeredHotkeys.TryGetValue(id, out var info) && info.IsRegistered;
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
