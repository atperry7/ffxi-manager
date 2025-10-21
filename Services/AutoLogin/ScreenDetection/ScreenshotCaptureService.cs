using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Real implementation of screenshot capture service using Windows APIs
    /// </summary>
    public class ScreenshotCaptureService : IScreenshotCaptureService
    {
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;

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
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

        private const uint PW_CLIENTONLY = 0x1;
        private const uint PW_RENDERFULLCONTENT = 0x2;
        private const int DWMWA_CLOAKED = 14;

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

        public ScreenshotCaptureService(ILoggingService loggingService, ISettingsService settingsService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public async Task<WindowScreenshot?> CaptureWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
        {
            return await Task.Run(async () =>
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

                    var screenshot = new WindowScreenshot
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

                    // Save diagnostic screenshot if diagnostics are enabled
                    await SaveDiagnosticScreenshotAsync(bitmap, windowTitle, processId);

                    return screenshot;
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

                // Force window refresh to ensure we capture current content
                InvalidateRect(windowHandle, IntPtr.Zero, false);
                UpdateWindow(windowHandle);

                // Small delay to allow refresh to complete
                System.Threading.Thread.Sleep(50);

                // Get window device context (try GetDC first for client area)
                windowDC = GetDC(windowHandle);
                if (windowDC == IntPtr.Zero)
                {
                    _loggingService.LogDebugAsync($"GetDC failed, falling back to GetWindowDC").Wait();
                    windowDC = GetWindowDC(windowHandle);
                }
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

                // Try PrintWindow first (works better with DirectX applications like FFXI/PlayOnline)
                var printWindowResult = PrintWindow(windowHandle, memoryDC, PW_RENDERFULLCONTENT);
                if (printWindowResult)
                {
                    _loggingService.LogDebugAsync($"PrintWindow succeeded - Captured DirectX content {bounds.Width}x{bounds.Height}").Wait();
                }
                else
                {
                    _loggingService.LogDebugAsync($"PrintWindow failed, falling back to BitBlt").Wait();

                    // Fall back to BitBlt for non-DirectX windows
                    var bitBltResult = BitBlt(memoryDC, 0, 0, bounds.Width, bounds.Height, windowDC, 0, 0, SRCCOPY);
                    if (!bitBltResult)
                    {
                        var lastError = Marshal.GetLastWin32Error();
                        _loggingService.LogWarningAsync($"Both PrintWindow and BitBlt failed - LastError: {lastError}, Size: {bounds.Width}x{bounds.Height}").Wait();
                        return null;
                    }
                    _loggingService.LogDebugAsync($"BitBlt succeeded as fallback - Copied {bounds.Width}x{bounds.Height} pixels").Wait();
                }

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
                var stride = Math.Abs(bmpData.Stride);
                var bytesPerPixel = 3; // For 24bppRgb
                var expectedSize = bitmap.Width * bitmap.Height * bytesPerPixel;
                var rowBytes = bitmap.Width * bytesPerPixel;

                // If stride equals width * bytesPerPixel, no padding - copy directly
                if (stride == bitmap.Width * bytesPerPixel)
                {
                    var resultDirect = new byte[expectedSize];
                    Marshal.Copy(bmpData.Scan0, resultDirect, 0, expectedSize);
                    return resultDirect;
                }

                // Handle stride padding by copying row by row without padding
                var result = new byte[expectedSize];
                var srcPtr = bmpData.Scan0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(
                        srcPtr + (y * stride),
                        result,
                        y * rowBytes,
                        rowBytes
                    );
                }

                return result;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        /// <summary>
        /// Saves diagnostic screenshot if diagnostics are enabled with automatic cleanup
        /// </summary>
        private async Task SaveDiagnosticScreenshotAsync(Bitmap bitmap, string windowTitle, int processId)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                if (settings?.Diagnostics?.EnableDiagnostics != true)
                {
                    return;
                }

                // Create diagnostic screenshots directory
                var diagnosticDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FFXIManager", "Diagnostics", "Screenshots");

                Directory.CreateDirectory(diagnosticDir);

                // Clean up old screenshots before saving new one
                await CleanupOldDiagnosticScreenshotsAsync(diagnosticDir);

                // Generate filename with timestamp and process info
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var safeWindowTitle = string.Join("_", windowTitle.Split(Path.GetInvalidFileNameChars()));
                var filename = $"screenshot_{timestamp}_{processId}_{safeWindowTitle}.png";
                var filePath = Path.Combine(diagnosticDir, filename);

                // Save screenshot as PNG
                bitmap.Save(filePath, ImageFormat.Png);

                await _loggingService.LogInfoAsync($"[DIAGNOSTIC] Screenshot saved: {filename} ({bitmap.Width}x{bitmap.Height})");
            }
            catch (Exception ex)
            {
                // Don't fail the main capture operation due to diagnostic logging issues
                await _loggingService.LogWarningAsync($"Failed to save diagnostic screenshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up old diagnostic screenshots to prevent disk space issues
        /// Keeps only the last 50 screenshots and removes files older than 7 days
        /// </summary>
        private async Task CleanupOldDiagnosticScreenshotsAsync(string diagnosticDir)
        {
            try
            {
                if (!Directory.Exists(diagnosticDir))
                {
                    return;
                }

                var screenshotFiles = Directory.GetFiles(diagnosticDir, "screenshot_*.png")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();

                var cleanupTasks = new List<Task>();

                // Remove files older than 7 days
                var cutoffDate = DateTime.Now.AddDays(-7);
                var oldFiles = screenshotFiles.Where(f => f.CreationTime < cutoffDate).ToArray();

                foreach (var oldFile in oldFiles)
                {
                    cleanupTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            oldFile.Delete();
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebugAsync($"Failed to delete old diagnostic screenshot {oldFile.Name}: {ex.Message}");
                        }
                    }));
                }

                // Keep only the latest 50 screenshots
                var filesToRemove = screenshotFiles.Skip(50).ToArray();
                foreach (var fileToRemove in filesToRemove)
                {
                    cleanupTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            fileToRemove.Delete();
                        }
                        catch (Exception ex)
                        {
                            _loggingService.LogDebugAsync($"Failed to delete excess diagnostic screenshot {fileToRemove.Name}: {ex.Message}");
                        }
                    }));
                }

                await Task.WhenAll(cleanupTasks);

                var deletedCount = oldFiles.Length + filesToRemove.Length;
                if (deletedCount > 0)
                {
                    await _loggingService.LogDebugAsync($"[DIAGNOSTIC] Cleaned up {deletedCount} old diagnostic screenshots");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogWarningAsync($"Failed to cleanup diagnostic screenshots: {ex.Message}");
            }
        }
    }
}