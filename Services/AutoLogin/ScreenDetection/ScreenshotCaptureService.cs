using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Services;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Real implementation of screenshot capture service using Windows APIs
    /// </summary>
    public class ScreenshotCaptureService : IScreenshotCaptureService
    {
        private readonly ILoggingService _loggingService;

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        private static extern bool IsWindowVisibleWin32(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SRCCOPY = 0x00CC0020;

        public ScreenshotCaptureService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public async Task<WindowScreenshot?> CaptureWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _loggingService.LogDebugAsync($"Screenshot capture starting - Window handle: 0x{windowHandle.ToInt64():X}").Wait();

                    if (!IsWindowValid(windowHandle))
                    {
                        _loggingService.LogWarningAsync($"Window validation failed - Handle: 0x{windowHandle.ToInt64():X}, IsWindow: {IsWindow(windowHandle)}, IsVisible: {IsWindowVisibleWin32(windowHandle)}").Wait();
                        return null;
                    }

                    var bounds = GetWindowBounds(windowHandle);
                    _loggingService.LogDebugAsync($"Window bounds retrieved - Left: {bounds.Left}, Top: {bounds.Top}, Width: {bounds.Width}, Height: {bounds.Height}").Wait();

                    if (bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        _loggingService.LogWarningAsync($"Invalid window bounds - Width: {bounds.Width}, Height: {bounds.Height}").Wait();
                        return null;
                    }

                    // Capture the window
                    using var bitmap = CaptureWindowToBitmap(windowHandle, bounds);
                    if (bitmap == null)
                    {
                        _loggingService.LogWarningAsync($"CaptureWindowToBitmap returned null for window 0x{windowHandle.ToInt64():X}").Wait();
                        return null;
                    }

                    _loggingService.LogDebugAsync($"Bitmap captured successfully - Width: {bitmap.Width}, Height: {bitmap.Height}").Wait();

                    // Convert bitmap to byte array
                    var imageData = ConvertBitmapToByteArray(bitmap);

                    // Get window metadata
                    var windowTitle = GetWindowTitle(windowHandle);
                    GetWindowThreadProcessId(windowHandle, out int processId);
                    var dpiScale = GetWindowDpiScale(windowHandle);

                    return new WindowScreenshot
                    {
                        WindowHandle = windowHandle,
                        WindowBounds = bounds,
                        ImageData = imageData,
                        Width = bitmap.Width,
                        Height = bitmap.Height,
                        Channels = 3, // BGR
                        DpiScale = dpiScale,
                        CaptureTime = DateTime.Now,
                        WindowTitle = windowTitle,
                        ProcessId = processId
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Exception during screenshot capture for window 0x{windowHandle.ToInt64():X}: {ex.Message}", ex).Wait();
                    return null;
                }
            }, cancellationToken);
        }

        public async Task<WindowScreenshot?> CaptureWindowRegionAsync(IntPtr windowHandle, Rectangle region, CancellationToken cancellationToken = default)
        {
            var fullScreenshot = await CaptureWindowAsync(windowHandle, cancellationToken);
            return fullScreenshot?.ExtractRegion(region);
        }

        public Rectangle GetWindowBounds(IntPtr windowHandle)
        {
            if (!GetWindowRect(windowHandle, out RECT rect))
            {
                return Rectangle.Empty;
            }

            return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public bool IsWindowVisible(IntPtr windowHandle)
        {
            return IsWindow(windowHandle) && IsWindowVisibleWin32(windowHandle);
        }

        public bool IsWindowValid(IntPtr windowHandle)
        {
            return windowHandle != IntPtr.Zero && IsWindow(windowHandle) && IsWindowVisibleWin32(windowHandle);
        }

        public float GetWindowDpiScale(IntPtr windowHandle)
        {
            try
            {
                var dpi = GetDpiForWindow(windowHandle);
                return dpi / 96.0f; // 96 DPI is 100% scale
            }
            catch
            {
                return 1.0f; // Default scale
            }
        }

        private string GetWindowTitle(IntPtr windowHandle)
        {
            try
            {
                var length = GetWindowTextLength(windowHandle);
                if (length == 0) return string.Empty;

                var sb = new System.Text.StringBuilder(length + 1);
                GetWindowText(windowHandle, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private Bitmap? CaptureWindowToBitmap(IntPtr windowHandle, Rectangle bounds)
        {
            IntPtr windowDC = IntPtr.Zero;
            IntPtr memoryDC = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                _loggingService.LogDebugAsync($"Starting bitmap capture - Handle: 0x{windowHandle.ToInt64():X}, Bounds: {bounds.Width}x{bounds.Height}").Wait();

                // Get window device context
                windowDC = GetWindowDC(windowHandle);
                if (windowDC == IntPtr.Zero)
                {
                    _loggingService.LogWarningAsync($"GetWindowDC failed for window 0x{windowHandle.ToInt64():X} - returned null DC").Wait();
                    return null;
                }
                _loggingService.LogDebugAsync($"GetWindowDC succeeded - DC: 0x{windowDC.ToInt64():X}").Wait();

                // Create compatible memory DC
                memoryDC = CreateCompatibleDC(windowDC);
                if (memoryDC == IntPtr.Zero)
                {
                    _loggingService.LogWarningAsync($"CreateCompatibleDC failed - returned null memory DC").Wait();
                    return null;
                }
                _loggingService.LogDebugAsync($"CreateCompatibleDC succeeded - Memory DC: 0x{memoryDC.ToInt64():X}").Wait();

                // Create compatible bitmap
                bitmap = CreateCompatibleBitmap(windowDC, bounds.Width, bounds.Height);
                if (bitmap == IntPtr.Zero)
                {
                    _loggingService.LogWarningAsync($"CreateCompatibleBitmap failed - Size: {bounds.Width}x{bounds.Height}").Wait();
                    return null;
                }
                _loggingService.LogDebugAsync($"CreateCompatibleBitmap succeeded - Bitmap: 0x{bitmap.ToInt64():X}").Wait();

                // Select bitmap into memory DC
                oldBitmap = SelectObject(memoryDC, bitmap);
                _loggingService.LogDebugAsync($"SelectObject completed - Old bitmap: 0x{oldBitmap.ToInt64():X}").Wait();

                // Copy window content to memory DC
                var bitBltResult = BitBlt(memoryDC, 0, 0, bounds.Width, bounds.Height, windowDC, 0, 0, SRCCOPY);
                if (!bitBltResult)
                {
                    var lastError = Marshal.GetLastWin32Error();
                    _loggingService.LogWarningAsync($"BitBlt failed - LastError: {lastError}, Size: {bounds.Width}x{bounds.Height}").Wait();
                    return null;
                }
                _loggingService.LogDebugAsync($"BitBlt succeeded - Copied {bounds.Width}x{bounds.Height} pixels").Wait();

                // Create managed bitmap from handle
                var managedBitmap = Image.FromHbitmap(bitmap);
                _loggingService.LogDebugAsync($"Created managed bitmap - Size: {managedBitmap.Width}x{managedBitmap.Height}").Wait();
                return managedBitmap;
            }
            finally
            {
                // Cleanup
                if (oldBitmap != IntPtr.Zero)
                    SelectObject(memoryDC, oldBitmap);
                if (bitmap != IntPtr.Zero)
                    DeleteObject(bitmap);
                if (memoryDC != IntPtr.Zero)
                    DeleteDC(memoryDC);
                if (windowDC != IntPtr.Zero)
                    ReleaseDC(windowHandle, windowDC);
            }
        }

        private static byte[] ConvertBitmapToByteArray(Bitmap bitmap)
        {
            // Lock bitmap data for direct access
            var bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                // Calculate the number of bytes
                var stride = bmpData.Stride;
                var bytes = Math.Abs(stride) * bitmap.Height;
                var result = new byte[bytes];

                // Copy data from bitmap
                Marshal.Copy(bmpData.Scan0, result, 0, bytes);

                return result;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
    }
}