using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for UI automation actions based on screenshot detection
    /// </summary>
    public interface IUIAutomationService
    {
        /// <summary>
        /// Performs a click at the specified screen coordinates
        /// </summary>
        /// <param name="screenPoint">Screen coordinates to click</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ClickAsync(Point screenPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a click at window-relative coordinates
        /// </summary>
        /// <param name="windowHandle">Window handle</param>
        /// <param name="windowRelativePoint">Window-relative coordinates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ClickWindowRelativeAsync(IntPtr windowHandle, Point windowRelativePoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a double-click at the specified screen coordinates
        /// </summary>
        /// <param name="screenPoint">Screen coordinates to double-click</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task DoubleClickAsync(Point screenPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Performs a right-click at the specified screen coordinates
        /// </summary>
        /// <param name="screenPoint">Screen coordinates to right-click</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task RightClickAsync(Point screenPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Types text at the current cursor position
        /// </summary>
        /// <param name="text">Text to type</param>
        /// <param name="delayBetweenKeys">Delay between keystrokes in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task TypeTextAsync(string text, int delayBetweenKeys = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// Types secure text (e.g., passwords) with additional safety measures
        /// </summary>
        /// <param name="secureText">Secure text to type</param>
        /// <param name="delayBetweenKeys">Delay between keystrokes in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task TypeSecureTextAsync(string secureText, int delayBetweenKeys = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a keyboard key press
        /// </summary>
        /// <param name="key">Key to press</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendKeyAsync(ConsoleKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a key combination (e.g., Ctrl+A)
        /// </summary>
        /// <param name="modifiers">Modifier keys (Ctrl, Alt, Shift)</param>
        /// <param name="key">Key to press with modifiers</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task SendKeyComboAsync(ConsoleModifiers modifiers, ConsoleKey key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears the current text field (Ctrl+A, Delete)
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ClearFieldAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the mouse to specified coordinates without clicking
        /// </summary>
        /// <param name="screenPoint">Screen coordinates</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task MoveMouseAsync(Point screenPoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a series of UI automation actions
        /// </summary>
        /// <param name="actions">Collection of actions to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task ExecuteActionsAsync(UIAutomationAction[] actions, CancellationToken cancellationToken = default);

        /// <summary>
        /// Converts window-relative coordinates to screen coordinates
        /// </summary>
        /// <param name="windowHandle">Window handle</param>
        /// <param name="windowRelativePoint">Window-relative coordinates</param>
        /// <returns>Screen coordinates</returns>
        Point ConvertToScreenCoordinates(IntPtr windowHandle, Point windowRelativePoint);

        /// <summary>
        /// Waits for a specified duration
        /// </summary>
        /// <param name="milliseconds">Duration to wait in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task WaitAsync(int milliseconds, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures the target window has focus before performing actions
        /// </summary>
        /// <param name="windowHandle">Window handle</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task EnsureWindowFocusAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);
    }
}