using System.Drawing;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Represents a screenshot of an application window
    /// </summary>
    public class WindowScreenshot
    {
        /// <summary>
        /// Handle to the window that was captured
        /// </summary>
        public IntPtr WindowHandle { get; set; }

        /// <summary>
        /// Window bounds in screen coordinates at time of capture
        /// </summary>
        public Rectangle WindowBounds { get; set; }

        /// <summary>
        /// Raw image data (BGR or BGRA format)
        /// </summary>
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Width of the captured image in pixels
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the captured image in pixels
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Number of channels (3 for BGR, 4 for BGRA)
        /// </summary>
        public int Channels { get; set; } = 3;

        /// <summary>
        /// DPI scaling factor of the window
        /// </summary>
        public float DpiScale { get; set; } = 1.0f;

        /// <summary>
        /// Timestamp when the screenshot was captured
        /// </summary>
        public DateTime CaptureTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Optional title of the window at capture time
        /// </summary>
        public string WindowTitle { get; set; } = string.Empty;

        /// <summary>
        /// Process ID of the window owner
        /// </summary>
        public int ProcessId { get; set; }

        /// <summary>
        /// Gets the size of the image data in bytes
        /// </summary>
        public int DataSize => Width * Height * Channels;

        /// <summary>
        /// Checks if the screenshot is valid
        /// </summary>
        public bool IsValid => ImageData.Length > 0 && Width > 0 && Height > 0;

        /// <summary>
        /// Converts a window-relative point to screen coordinates
        /// </summary>
        /// <param name="windowRelativePoint">Point relative to window</param>
        /// <returns>Point in screen coordinates</returns>
        public Point ToScreenCoordinates(Point windowRelativePoint)
        {
            return new Point(
                WindowBounds.Left + windowRelativePoint.X,
                WindowBounds.Top + windowRelativePoint.Y
            );
        }

        /// <summary>
        /// Converts a screen point to window-relative coordinates
        /// </summary>
        /// <param name="screenPoint">Point in screen coordinates</param>
        /// <returns>Point relative to window</returns>
        public Point ToWindowRelativeCoordinates(Point screenPoint)
        {
            return new Point(
                screenPoint.X - WindowBounds.Left,
                screenPoint.Y - WindowBounds.Top
            );
        }

        /// <summary>
        /// Gets a region of the screenshot as a new WindowScreenshot
        /// </summary>
        /// <param name="region">Region to extract (window-relative coordinates)</param>
        /// <returns>New WindowScreenshot containing only the specified region</returns>
        public WindowScreenshot? ExtractRegion(Rectangle region)
        {
            if (region.X < 0 || region.Y < 0 ||
                region.Right > Width || region.Bottom > Height)
            {
                return null;
            }

            var regionData = new byte[region.Width * region.Height * Channels];
            var sourceStride = Width * Channels;
            var destStride = region.Width * Channels;

            for (int y = 0; y < region.Height; y++)
            {
                var sourceOffset = ((region.Y + y) * Width + region.X) * Channels;
                var destOffset = y * region.Width * Channels;
                Array.Copy(ImageData, sourceOffset, regionData, destOffset, destStride);
            }

            return new WindowScreenshot
            {
                WindowHandle = WindowHandle,
                WindowBounds = new Rectangle(
                    WindowBounds.Left + region.X,
                    WindowBounds.Top + region.Y,
                    region.Width,
                    region.Height
                ),
                ImageData = regionData,
                Width = region.Width,
                Height = region.Height,
                Channels = Channels,
                DpiScale = DpiScale,
                CaptureTime = CaptureTime,
                WindowTitle = WindowTitle,
                ProcessId = ProcessId
            };
        }
    }
}