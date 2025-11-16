using System.Drawing;
using System.Runtime.InteropServices;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Real implementation of UI automation service using Windows APIs
    /// </summary>
    public class UIAutomationService : IUIAutomationService
    {
        private readonly ILoggingService _loggingService;

        // Win32 API imports for mouse and keyboard simulation
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        // DirectX-compatible input APIs
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        // Mouse event constants
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const int MOUSEEVENTF_RIGHTUP = 0x0010;
        private const int MOUSEEVENTF_WHEEL = 0x0800;
        private const int WHEEL_DELTA = 120; // Standard wheel delta for 1 notch

        // Keyboard event constants
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // Show window constants
        private const int SW_RESTORE = 9;

        // DirectX-compatible input constants
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CHAR = 0x0102;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MAPVK_VK_TO_VSC = 0;

        public UIAutomationService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task ClickAsync(Point screenPoint, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Log pre-click cursor position
                    GetCursorPos(out POINT currentCursorPos);
                    var currentCursor = new Point(currentCursorPos.x, currentCursorPos.y);
                    _loggingService.LogDebugAsync($"Pre-click cursor position: {currentCursor}, Target: {screenPoint}");

                    // Move cursor to position
                    bool setCursorSuccess = SetCursorPos(screenPoint.X, screenPoint.Y);
                    if (!setCursorSuccess)
                    {
                        throw new InvalidOperationException($"Failed to move cursor to position {screenPoint}");
                    }

                    // Small delay to ensure cursor movement and verify position
                    Thread.Sleep(50);

                    GetCursorPos(out POINT actualCursorPos);
                    var actualCursor = new Point(actualCursorPos.x, actualCursorPos.y);
                    if (Math.Abs(actualCursor.X - screenPoint.X) > 2 || Math.Abs(actualCursor.Y - screenPoint.Y) > 2)
                    {
                        _loggingService.LogErrorAsync($"Cursor position mismatch. Expected: {screenPoint}, Actual: {actualCursor}");
                    }

                    // Perform left click with validation
                    _loggingService.LogDebugAsync($"Executing mouse click at verified position {actualCursor}");

                    mouse_event(MOUSEEVENTF_LEFTDOWN, screenPoint.X, screenPoint.Y, 0, 0);
                    Thread.Sleep(80); // Hold click longer for better recognition
                    mouse_event(MOUSEEVENTF_LEFTUP, screenPoint.X, screenPoint.Y, 0, 0);

                    _loggingService.LogDebugAsync($"Click sequence completed at {screenPoint.X}, {screenPoint.Y}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Click failed at {screenPoint.X}, {screenPoint.Y}: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task ClickWindowRelativeAsync(IntPtr windowHandle, Point windowRelativePoint, CancellationToken cancellationToken = default)
        {
            var screenPoint = ConvertToScreenCoordinates(windowHandle, windowRelativePoint);
            await ClickAsync(screenPoint, cancellationToken);
        }

        public async Task DoubleClickAsync(Point screenPoint, CancellationToken cancellationToken = default)
        {
            await ClickAsync(screenPoint, cancellationToken);
            await Task.Delay(100, cancellationToken); // Standard double-click interval
            await ClickAsync(screenPoint, cancellationToken);
        }

        public async Task RightClickAsync(Point screenPoint, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    SetCursorPos(screenPoint.X, screenPoint.Y);
                    Thread.Sleep(10);

                    mouse_event(MOUSEEVENTF_RIGHTDOWN, screenPoint.X, screenPoint.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_RIGHTUP, screenPoint.X, screenPoint.Y, 0, 0);

                    _loggingService.LogDebugAsync($"Right-click performed at {screenPoint.X}, {screenPoint.Y}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Right-click failed at {screenPoint.X}, {screenPoint.Y}: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task TypeTextAsync(string text, int delayBetweenKeys = 50, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text)) return;

            await Task.Run(() =>
            {
                try
                {
                    foreach (char c in text)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Send key down and up for each character with proper modifier handling
                        var vkCode = VkKeyScan(c);
                        if (vkCode != -1)
                        {
                            byte virtualKey = (byte)(vkCode & 0xFF);
                            byte shiftState = (byte)((vkCode >> 8) & 0xFF);

                            // Handle modifier keys (Shift, Ctrl, Alt)
                            bool needsShift = (shiftState & 1) != 0;
                            bool needsCtrl = (shiftState & 2) != 0;
                            bool needsAlt = (shiftState & 4) != 0;

                            // Press modifier keys down
                            if (needsShift) keybd_event(0x10, 0, KEYEVENTF_KEYDOWN, 0); // VK_SHIFT
                            if (needsCtrl) keybd_event(0x11, 0, KEYEVENTF_KEYDOWN, 0);  // VK_CONTROL
                            if (needsAlt) keybd_event(0x12, 0, KEYEVENTF_KEYDOWN, 0);   // VK_MENU (Alt)

                            Thread.Sleep(10); // Brief pause for modifier keys to register

                            // Press the main key
                            keybd_event(virtualKey, 0, KEYEVENTF_KEYDOWN, 0);
                            Thread.Sleep(25);
                            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, 0);

                            Thread.Sleep(10); // Brief pause before releasing modifiers

                            // Release modifier keys (in reverse order)
                            if (needsAlt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);   // VK_MENU (Alt)
                            if (needsCtrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);  // VK_CONTROL
                            if (needsShift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0); // VK_SHIFT

                            if (delayBetweenKeys > 0)
                            {
                                Thread.Sleep(delayBetweenKeys);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Type text failed: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task TypeSecureTextAsync(string secureText, int delayBetweenKeys = 50, CancellationToken cancellationToken = default)
        {
            // Same as TypeTextAsync but with logging that doesn't expose the text
            if (string.IsNullOrEmpty(secureText)) return;

            // DIAGNOSTIC: Log typing details without exposing password
            await _loggingService.LogDebugAsync($"[DIAGNOSTIC] TypeSecureTextAsync starting: {secureText.Length} characters to type");

            await Task.Run(() =>
            {
                try
                {
                    int charCount = 0;
                    int failedChars = 0;

                    foreach (char c in secureText)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var vkCode = VkKeyScan(c);
                        if (vkCode != -1)
                        {
                            byte virtualKey = (byte)(vkCode & 0xFF);
                            byte shiftState = (byte)((vkCode >> 8) & 0xFF);

                            // Handle modifier keys (Shift, Ctrl, Alt)
                            bool needsShift = (shiftState & 1) != 0;
                            bool needsCtrl = (shiftState & 2) != 0;
                            bool needsAlt = (shiftState & 4) != 0;

                            // Press modifier keys down
                            if (needsShift) keybd_event(0x10, 0, KEYEVENTF_KEYDOWN, 0); // VK_SHIFT
                            if (needsCtrl) keybd_event(0x11, 0, KEYEVENTF_KEYDOWN, 0);  // VK_CONTROL
                            if (needsAlt) keybd_event(0x12, 0, KEYEVENTF_KEYDOWN, 0);   // VK_MENU (Alt)

                            Thread.Sleep(10); // Brief pause for modifier keys to register

                            // Press the main key
                            keybd_event(virtualKey, 0, KEYEVENTF_KEYDOWN, 0);
                            Thread.Sleep(25);
                            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, 0);

                            Thread.Sleep(10); // Brief pause before releasing modifiers

                            // Release modifier keys (in reverse order)
                            if (needsAlt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);   // VK_MENU (Alt)
                            if (needsCtrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);  // VK_CONTROL
                            if (needsShift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0); // VK_SHIFT

                            if (delayBetweenKeys > 0)
                            {
                                Thread.Sleep(delayBetweenKeys);
                            }
                            charCount++;
                        }
                        else
                        {
                            // DIAGNOSTIC: Track characters that couldn't be typed
                            failedChars++;
                        }
                    }

                    // DIAGNOSTIC: Log typing completion stats
                    _loggingService.LogDebugAsync($"[DIAGNOSTIC] Secure text typing completed: {charCount} characters typed successfully, {failedChars} characters failed to type");
                    _loggingService.LogDebugAsync("Secure text typed successfully");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Type secure text failed: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task SendKeyAsync(ConsoleKey key, CancellationToken cancellationToken = default)
        {
            await SendKeyToDirectXAsync(key, IntPtr.Zero, cancellationToken);
        }

        public async Task SendKeyAsync(ConsoleKey key, IntPtr targetWindow, CancellationToken cancellationToken = default)
        {
            await SendKeyToDirectXAsync(key, targetWindow, cancellationToken);
        }

        /// <summary>
        /// DirectX-compatible key sending optimized for FFXI and similar DirectX applications
        /// Uses keybd_event with scan codes and proper extended key flags
        /// </summary>
        private async Task SendKeyToDirectXAsync(ConsoleKey key, IntPtr targetWindow, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var vkCode = ConsoleKeyToVirtualKey(key);
                    if (vkCode == 0)
                    {
                        _loggingService.LogWarningAsync($"Unknown virtual key code for ConsoleKey: {key}");
                        return;
                    }

                    // Arrow keys need special handling for DirectX games like FFXI
                    bool isArrowKey = key == ConsoleKey.LeftArrow || key == ConsoleKey.RightArrow ||
                                     key == ConsoleKey.UpArrow || key == ConsoleKey.DownArrow;

                    // Ensure window has focus first
                    if (targetWindow != IntPtr.Zero)
                    {
                        SetForegroundWindow(targetWindow);
                        Thread.Sleep(50); // Brief pause to ensure focus
                    }

                    _loggingService.LogDebugAsync($"[DirectX Input] Sending {(isArrowKey ? "arrow " : "")}key {key} (VK: 0x{vkCode:X2}) using keybd_event");

                    // Get scan code for the key
                    var scanCode = (byte)MapVirtualKey(vkCode, MAPVK_VK_TO_VSC);

                    if (isArrowKey)
                    {
                        // Arrow keys are extended keys - use KEYEVENTF_EXTENDEDKEY flag (0x0001)
                        const int KEYEVENTF_EXTENDEDKEY = 0x0001;

                        _loggingService.LogDebugAsync($"[DirectX Input] Sending arrow key with extended flag - VK: 0x{vkCode:X2}, Scan: 0x{scanCode:X2}");

                        // Send key down with extended key flag
                        keybd_event(vkCode, scanCode, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(100); // Hold arrow key longer for FFXI to register movement

                        // Send key up with extended key flag
                        keybd_event(vkCode, scanCode, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);

                        _loggingService.LogDebugAsync($"[DirectX Input] Arrow key {key} sent with extended flag");
                    }
                    else
                    {
                        // Non-arrow keys use scan code method which was working for Enter
                        if (scanCode == 0)
                        {
                            _loggingService.LogWarningAsync($"Could not map virtual key {vkCode:X2} to scan code, using virtual key only");
                            // Fallback to virtual key method
                            keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, 0);
                            Thread.Sleep(75); // Hold key longer for DirectX recognition
                            keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
                        }
                        else
                        {
                            _loggingService.LogDebugAsync($"[DirectX Input] Sending key with scan code - VK: 0x{vkCode:X2}, Scan: 0x{scanCode:X2}");

                            // Use scan code method (best for DirectX) - this was working for Enter
                            keybd_event(0, scanCode, (int)(KEYEVENTF_SCANCODE | KEYEVENTF_KEYDOWN), 0);
                            Thread.Sleep(75); // Hold key longer for DirectX recognition
                            keybd_event(0, scanCode, (int)(KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP), 0);
                        }

                        _loggingService.LogDebugAsync($"[DirectX Input] Key {key} sent successfully using scan code 0x{scanCode:X2}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Send DirectX key failed ({key}): {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Creates lParam for WM_KEYDOWN and WM_KEYUP messages
        /// </summary>
        private static int CreateKeyLParam(int repeatCount, byte scanCode, bool extended, bool previousKeyState, bool transitionState)
        {
            int lParam = 0;
            lParam |= repeatCount & 0x0000FFFF;                    // Repeat count (bits 0-15)
            lParam |= (scanCode & 0xFF) << 16;                     // Scan code (bits 16-23)
            lParam |= (extended ? 1 : 0) << 24;                    // Extended key flag (bit 24)
            lParam |= (previousKeyState ? 1 : 0) << 30;            // Previous key state (bit 30)
            lParam |= (transitionState ? 1 : 0) << 31;             // Transition state (bit 31)
            return lParam;
        }

        public async Task SendKeyComboAsync(ConsoleModifiers modifiers, ConsoleKey key, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Press modifier keys
                    var modifierKeys = new List<byte>();
                    if ((modifiers & ConsoleModifiers.Control) != 0)
                        modifierKeys.Add(0x11); // VK_CONTROL
                    if ((modifiers & ConsoleModifiers.Alt) != 0)
                        modifierKeys.Add(0x12); // VK_MENU
                    if ((modifiers & ConsoleModifiers.Shift) != 0)
                        modifierKeys.Add(0x10); // VK_SHIFT

                    // Press all modifier keys
                    foreach (var modKey in modifierKeys)
                    {
                        keybd_event(modKey, 0, KEYEVENTF_KEYDOWN, 0);
                    }

                    Thread.Sleep(50);

                    // Press main key
                    var vkCode = ConsoleKeyToVirtualKey(key);
                    if (vkCode != 0)
                    {
                        keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(50);
                        keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
                    }

                    Thread.Sleep(50);

                    // Release modifier keys in reverse order
                    for (int i = modifierKeys.Count - 1; i >= 0; i--)
                    {
                        keybd_event(modifierKeys[i], 0, KEYEVENTF_KEYUP, 0);
                    }

                    _loggingService.LogDebugAsync($"Key combo sent: {modifiers}+{key}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Send key combo failed ({modifiers}+{key}): {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task ClearFieldAsync(CancellationToken cancellationToken = default)
        {
            // Ctrl+A to select all, then Delete
            await SendKeyComboAsync(ConsoleModifiers.Control, ConsoleKey.A, cancellationToken);
            await Task.Delay(100, cancellationToken);
            await SendKeyAsync(ConsoleKey.Delete, cancellationToken);
        }

        public async Task MoveMouseAsync(Point screenPoint, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetCursorPos(screenPoint.X, screenPoint.Y);
                    _loggingService.LogDebugAsync($"Mouse moved to {screenPoint.X}, {screenPoint.Y}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Move mouse failed: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task ExecuteActionsAsync(UIAutomationAction[] actions, CancellationToken cancellationToken = default)
        {
            foreach (var action in actions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    switch (action.Type)
                    {
                        case UIActionType.Click:
                            var screenPoint = new Point(action.WindowRelativeCoordinates.X, action.WindowRelativeCoordinates.Y);
                            await ClickAsync(screenPoint, cancellationToken);
                            break;

                        case UIActionType.Wait:
                            await Task.Delay(action.DelayAfterMs, cancellationToken);
                            break;

                            // Add more action types as needed
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Action execution failed ({action.Type}): {ex.Message}", ex);
                    throw;
                }
            }
        }

        public Point ConvertToScreenCoordinates(IntPtr windowHandle, Point windowRelativePoint)
        {
            // Convert client-area (0,0) to screen coordinates to match how action executors calculate positions
            // This ensures coordinates relative to the client area (excluding title bar/borders) are converted correctly
            POINT topLeft = new POINT { x = 0, y = 0 };
            if (ClientToScreen(windowHandle, ref topLeft))
            {
                return new Point(
                    topLeft.x + windowRelativePoint.X,
                    topLeft.y + windowRelativePoint.Y
                );
            }

            return windowRelativePoint; // Fallback if conversion fails
        }

        public async Task WaitAsync(int milliseconds, CancellationToken cancellationToken = default)
        {
            await Task.Delay(milliseconds, cancellationToken);
        }

        public async Task EnsureWindowFocusAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Restore window if minimized and bring to foreground
                    ShowWindow(windowHandle, SW_RESTORE);
                    SetForegroundWindow(windowHandle);

                    _loggingService.LogDebugAsync($"Window focus ensured: {windowHandle}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Ensure window focus failed: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public async Task ScrollMouseWheelAsync(int delta, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Normalize delta to wheel notches (120 = 1 notch)
                    int wheelDelta = delta * WHEEL_DELTA;

                    // Use mouse_event to send wheel scroll
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, wheelDelta, 0);

                    _loggingService.LogDebugAsync($"Mouse wheel scrolled: delta={delta} (raw={wheelDelta})");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Scroll mouse wheel failed: {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
        }

        public Rectangle GetWindowClientRect(IntPtr windowHandle)
        {
            try
            {
                // Use GetClientRect to get accurate client area dimensions (excludes borders/title bar)
                if (!GetClientRect(windowHandle, out RECT clientRect))
                {
                    _loggingService.LogWarningAsync($"Failed to get client rect for handle {windowHandle}");
                    return Rectangle.Empty;
                }

                // GetClientRect returns dimensions relative to window (always starts at 0,0)
                // We need to convert (0,0) to screen coordinates to get the actual position
                POINT topLeft = new POINT { x = 0, y = 0 };
                if (!ClientToScreen(windowHandle, ref topLeft))
                {
                    _loggingService.LogWarningAsync($"Failed to convert client coordinates to screen for handle {windowHandle}");
                    return Rectangle.Empty;
                }

                // Return rectangle with screen coordinates and client dimensions
                // This correctly handles both windowed (with borders) and fullscreen (borderless)
                return new Rectangle(
                    topLeft.x,
                    topLeft.y,
                    clientRect.Right - clientRect.Left,  // Width
                    clientRect.Bottom - clientRect.Top   // Height
                );
            }
            catch (Exception ex)
            {
                _loggingService.LogErrorAsync($"GetWindowClientRect failed: {ex.Message}", ex);
                return Rectangle.Empty;
            }
        }

        public Point GetWindowCenter(IntPtr windowHandle)
        {
            var rect = GetWindowClientRect(windowHandle);
            if (rect.IsEmpty)
            {
                _loggingService.LogWarningAsync($"Cannot calculate center for empty window rect");
                return Point.Empty;
            }

            return new Point(
                rect.Left + rect.Width / 2,
                rect.Top + rect.Height / 2
            );
        }

        // Helper methods
        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private static byte ConsoleKeyToVirtualKey(ConsoleKey key)
        {
            // Map common console keys to virtual key codes - including arrow keys for FFXI navigation
            return key switch
            {
                ConsoleKey.A => 0x41, // VK_A
                ConsoleKey.Enter => 0x0D, // VK_RETURN
                ConsoleKey.Escape => 0x1B, // VK_ESCAPE
                ConsoleKey.Tab => 0x09, // VK_TAB
                ConsoleKey.Spacebar => 0x20, // VK_SPACE
                ConsoleKey.Delete => 0x2E, // VK_DELETE
                ConsoleKey.Backspace => 0x08, // VK_BACK

                // Arrow keys - crucial for FFXI navigation
                ConsoleKey.LeftArrow => 0x25, // VK_LEFT
                ConsoleKey.UpArrow => 0x26, // VK_UP
                ConsoleKey.RightArrow => 0x27, // VK_RIGHT
                ConsoleKey.DownArrow => 0x28, // VK_DOWN

                // Function keys
                ConsoleKey.F1 => 0x70, // VK_F1
                ConsoleKey.F2 => 0x71, // VK_F2
                ConsoleKey.F3 => 0x72, // VK_F3
                ConsoleKey.F4 => 0x73, // VK_F4
                ConsoleKey.F5 => 0x74, // VK_F5
                ConsoleKey.F6 => 0x75, // VK_F6
                ConsoleKey.F7 => 0x76, // VK_F7
                ConsoleKey.F8 => 0x77, // VK_F8
                ConsoleKey.F9 => 0x78, // VK_F9
                ConsoleKey.F10 => 0x79, // VK_F10
                ConsoleKey.F11 => 0x7A, // VK_F11
                ConsoleKey.F12 => 0x7B, // VK_F12

                // Additional useful keys
                ConsoleKey.Home => 0x24, // VK_HOME
                ConsoleKey.End => 0x23, // VK_END
                ConsoleKey.PageUp => 0x21, // VK_PRIOR
                ConsoleKey.PageDown => 0x22, // VK_NEXT

                _ => 0
            };
        }
    }
}