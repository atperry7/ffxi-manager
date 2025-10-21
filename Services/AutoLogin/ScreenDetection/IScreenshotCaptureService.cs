using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for capturing screenshots of application windows
    /// </summary>
    public interface IScreenshotCaptureService
    {
        /// <summary>
        /// Captures a screenshot of the specified window
        /// </summary>
        /// <param name="windowHandle">Handle to the window to capture</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Window screenshot with metadata</returns>
        Task<WindowScreenshot?> CaptureWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Captures a specific region within a window
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <param name="region">Region within the window to capture (window-relative coordinates)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Screenshot of the specified region</returns>
        Task<WindowScreenshot?> CaptureWindowRegionAsync(IntPtr windowHandle, Rectangle region, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current bounds of a window
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>Window bounds in screen coordinates</returns>
        Rectangle GetWindowBounds(IntPtr windowHandle);

        /// <summary>
        /// Checks if a window is valid and visible
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>True if the window is valid and visible</returns>
        bool IsWindowVisible(IntPtr windowHandle);

        /// <summary>
        /// Gets the DPI scaling factor for the window
        /// </summary>
        /// <param name="windowHandle">Handle to the window</param>
        /// <returns>DPI scaling factor (1.0 = 100%, 1.5 = 150%, etc.)</returns>
        float GetWindowDpiScale(IntPtr windowHandle);
    }
}