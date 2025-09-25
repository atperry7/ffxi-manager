using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FFXIManager.Services;

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
        private static extern bool GetCursorPos(out POINT lpPoint);

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

        // Keyboard event constants
        private const int KEYEVENTF_KEYDOWN = 0x0000;
        private const int KEYEVENTF_KEYUP = 0x0002;

        // Show window constants
        private const int SW_RESTORE = 9;

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
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var vkCode = ConsoleKeyToVirtualKey(key);
                    if (vkCode != 0)
                    {
                        keybd_event(vkCode, 0, KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(50);
                        keybd_event(vkCode, 0, KEYEVENTF_KEYUP, 0);
                    }

                    _loggingService.LogDebugAsync($"Key sent: {key}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Send key failed ({key}): {ex.Message}", ex);
                    throw;
                }
            }, cancellationToken);
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
            if (GetWindowRect(windowHandle, out RECT rect))
            {
                return new Point(
                    rect.Left + windowRelativePoint.X,
                    rect.Top + windowRelativePoint.Y
                );
            }

            return windowRelativePoint; // Fallback if window rect fails
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

        // Helper methods
        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private static byte ConsoleKeyToVirtualKey(ConsoleKey key)
        {
            // Map common console keys to virtual key codes
            return key switch
            {
                ConsoleKey.A => 0x41, // VK_A
                ConsoleKey.Enter => 0x0D, // VK_RETURN
                ConsoleKey.Escape => 0x1B, // VK_ESCAPE
                ConsoleKey.Tab => 0x09, // VK_TAB
                ConsoleKey.Spacebar => 0x20, // VK_SPACE
                ConsoleKey.Delete => 0x2E, // VK_DELETE
                ConsoleKey.Backspace => 0x08, // VK_BACK
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
                _ => 0
            };
        }
    }
}