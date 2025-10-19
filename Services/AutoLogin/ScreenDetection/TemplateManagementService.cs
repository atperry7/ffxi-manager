using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Service for managing UI element templates (loading, caching, validation)
    /// </summary>
    public class TemplateManagementService : ITemplateManagementService
    {
        private readonly ILoggingService _loggingService;
        private readonly IImageCropService? _imageCropService;
        private readonly ConcurrentDictionary<string, UIElementTemplate> _templateCache = new();
        private readonly string _templatesBasePath;

        public TemplateManagementService(ILoggingService loggingService, IImageCropService? imageCropService = null)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _imageCropService = imageCropService; // Optional dependency

            // Set templates base path to workflow directory (centralized with workflow definitions)
            // Templates are shared by all workflows (default and custom) in a flat structure
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "FFXIManager");
            _templatesBasePath = Path.Combine(appDirectory, "workflows", "templates");

            // Ensure templates directory exists
            Directory.CreateDirectory(_templatesBasePath);

            // Clear cache on startup to force fresh template loading with fixes
            _templateCache.Clear();
            _loggingService.LogInfoAsync("[DEBUG] Template cache cleared on startup - will force fresh loading from workflows directory");
        }

        public async Task<UIElementTemplate?> LoadTemplateAsync(string templatePath, float confidenceThreshold = 0.8f, int tolerance = 5, CancellationToken cancellationToken = default)
        {
            try
            {
                // Create cache key that includes metadata since different steps may use same template with different thresholds
                var cacheKey = $"{templatePath}_{confidenceThreshold}_{tolerance}";

                // Check cache first
                if (_templateCache.TryGetValue(cacheKey, out var cachedTemplate))
                {
                    await _loggingService.LogInfoAsync($"[DEBUG] Template loaded from cache: {templatePath}");
                    return cachedTemplate;
                }

                await _loggingService.LogInfoAsync($"[DEBUG] Template not in cache, loading from disk: {templatePath}");

                // Load from disk with metadata
                var template = await LoadTemplateFromDiskAsync(templatePath, confidenceThreshold, tolerance, cancellationToken);
                if (template != null && template.IsValid())
                {
                    _templateCache.TryAdd(cacheKey, template);
                }

                return template;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load template '{templatePath}': {ex.Message}", ex);
                return null;
            }
        }

        public async Task<bool> ValidateTemplateAsync(UIElementTemplate template, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return template?.IsValid() == true;
                }
                catch
                {
                    return false;
                }
            }, cancellationToken);
        }

        public async Task SaveTemplateAsync(UIElementTemplate template, CancellationToken cancellationToken = default)
        {
            try
            {
                if (template == null || !template.IsValid())
                {
                    throw new ArgumentException("Invalid template");
                }

                var templatePath = GetTemplateFilePath(template.TemplatePath);
                var templateDir = Path.GetDirectoryName(templatePath);

                if (!string.IsNullOrEmpty(templateDir) && !Directory.Exists(templateDir))
                {
                    Directory.CreateDirectory(templateDir);
                }

                // Note: JSON metadata is no longer saved - all metadata is now in workflow step definitions (workflow-first architecture)

                // Save PNG image
                var pngPath = Path.ChangeExtension(templatePath, ".png");
                await SaveTemplateImageAsync(template, pngPath, cancellationToken);

                // Update cache
                _templateCache.AddOrUpdate(template.TemplatePath, template, (_, _) => template);

                await _loggingService.LogDebugAsync($"Template saved: {template.TemplatePath}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to save template '{template?.TemplatePath}': {ex.Message}", ex);
                throw;
            }
        }

        public void ClearCache()
        {
            _templateCache.Clear();
        }

        public async Task<UIElementTemplate?> LoadTemplateAsync(string templatePath)
        {
            // Use default metadata values for backward compatibility
            return await LoadTemplateAsync(templatePath, 0.8f, 5, CancellationToken.None);
        }

        public async Task<IList<UIElementTemplate>> LoadTemplatesForApplicationAsync(string applicationName)
        {
            var templates = new List<UIElementTemplate>();

            try
            {
                var allPaths = await GetAvailableTemplatePathsAsync();
                foreach (var path in allPaths.Where(p => p.StartsWith(applicationName + "/")))
                {
                    var template = await LoadTemplateAsync(path);
                    if (template != null)
                    {
                        templates.Add(template);
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load templates for application {applicationName}: {ex.Message}", ex);
            }

            return templates;
        }

        public async Task<int> PreloadAllTemplatesAsync()
        {
            try
            {
                var allPaths = await GetAvailableTemplatePathsAsync();
                var loadedCount = 0;

                foreach (var path in allPaths)
                {
                    var template = await LoadTemplateAsync(path);
                    if (template != null)
                    {
                        loadedCount++;
                    }
                }

                return loadedCount;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to preload templates: {ex.Message}", ex);
                return 0;
            }
        }

        // Legacy metadata retrieval removed (workflow-first architecture)

        public IEnumerable<string> GetAvailableTemplatePaths()
        {
            try
            {
                if (!Directory.Exists(_templatesBasePath))
                {
                    return Enumerable.Empty<string>();
                }

                // Scan for PNG files directly (no JSON metadata files)
                var pngFiles = Directory.GetFiles(_templatesBasePath, "*.png", SearchOption.TopDirectoryOnly);
                return pngFiles.Select(pngFile => Path.GetFileName(pngFile));
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        public async Task<bool> ValidateTemplateAsync(string templatePath)
        {
            try
            {
                var template = await LoadTemplateAsync(templatePath);
                return template?.IsValid() == true;
            }
            catch
            {
                return false;
            }
        }

        // Legacy template version retrieval removed

        // Legacy metadata update removed (workflow-first architecture)

        public async Task<IList<string>> GetAvailableTemplatePathsAsync(CancellationToken cancellationToken = default)
        {
            var paths = new List<string>();

            try
            {
                if (!Directory.Exists(_templatesBasePath))
                {
                    return paths;
                }

                await Task.Run(() =>
                {
                    // Scan for PNG files directly (no JSON metadata files)
                    var pngFiles = Directory.GetFiles(_templatesBasePath, "*.png", SearchOption.TopDirectoryOnly);

                    foreach (var pngFile in pngFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        paths.Add(Path.GetFileName(pngFile));
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to get available template paths: {ex.Message}", ex);
            }

            return paths;
        }

        private async Task<UIElementTemplate?> LoadTemplateFromDiskAsync(string templatePath, float confidenceThreshold, int tolerance, CancellationToken cancellationToken)
        {
            try
            {
                // Template path is now just a filename (e.g., "member_selection_screen" or "member_selection_screen.png")
                // Ensure .png extension is present
                var fileName = templatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? templatePath
                    : templatePath + ".png";

                var pngPath = Path.Combine(_templatesBasePath, fileName);

                if (!File.Exists(pngPath))
                {
                    await _loggingService.LogWarningAsync($"Template PNG not found: {pngPath}");
                    return null;
                }

                // Load PNG image (no JSON metadata needed - all metadata comes from workflow step)
                var imageData = await LoadTemplateImageAsync(pngPath, cancellationToken);
                if (imageData == null)
                {
                    await _loggingService.LogWarningAsync($"[DEBUG] Failed to load image data for: {pngPath}");
                    return null;
                }

                await _loggingService.LogInfoAsync($"[DEBUG] Loaded image: {pngPath}, Size: {imageData.Value.width}x{imageData.Value.height}, Data length: {imageData.Value.data.Length}");

                // Create template with metadata from workflow step (not from JSON file)
                var template = new UIElementTemplate
                {
                    Name = Path.GetFileNameWithoutExtension(templatePath),
                    TemplatePath = templatePath,
                    ImageData = imageData.Value.data,
                    Width = imageData.Value.width,
                    Height = imageData.Value.height,
                    Channels = imageData.Value.channels,
                    ClickOffset = new System.Drawing.Point(0, 0),
                    ConfidenceThreshold = confidenceThreshold,
                    PositionTolerance = tolerance,
                    ApplicationName = "Unknown", // No longer needed with workflow-first architecture
                    Version = "2.0.0", // Workflow-first architecture version
                    Metadata = string.Empty
                };

                await _loggingService.LogInfoAsync($"[DEBUG] Created template: Name={template.Name}, Path={template.TemplatePath}, Valid={template.IsValid()}, Threshold={confidenceThreshold}, Tolerance={tolerance}, ImageData.Length={template.ImageData?.Length ?? 0}, W={template.Width}, H={template.Height}");

                return template;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load template from disk '{templatePath}': {ex.Message}", ex);
                return null;
            }
        }

        private async Task<(byte[] data, int width, int height, int channels)?> LoadTemplateImageAsync(string pngPath, CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[DEBUG] LoadTemplateImageAsync called for: {pngPath}");

            try
            {
                using var bitmap = new Bitmap(pngPath);

                // Log bitmap properties before conversion
                await _loggingService.LogInfoAsync($"[ImageProcessor] Loading PNG: {Path.GetFileName(pngPath)} - Dimensions: {bitmap.Width}x{bitmap.Height}, PixelFormat: {bitmap.PixelFormat}");

                var imageData = await ConvertBitmapToByteArrayAsync(bitmap);
                var expectedSize = bitmap.Width * bitmap.Height * 3;

                await _loggingService.LogInfoAsync($"[ImageProcessor] PNG conversion: Expected {expectedSize} bytes, Got {imageData.Length} bytes, Difference: {imageData.Length - expectedSize}");

                return (imageData, bitmap.Width, bitmap.Height, 3); // Assuming BGR
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load template image '{pngPath}': {ex.Message}", ex);
                return null;
            }
        }

        private async Task SaveTemplateImageAsync(UIElementTemplate template, string pngPath, CancellationToken cancellationToken)
        {
            try
            {
                using var bitmap = ConvertByteArrayToBitmap(template.ImageData, template.Width, template.Height);
                bitmap.Save(pngPath, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to save template image '{pngPath}': {ex.Message}", ex);
                throw;
            }
        }

        
        private string GetTemplateFilePath(string templatePath)
        {
            // Template paths are now simple filenames (e.g., "member_selection_screen" or "member_selection_screen.png")
            // Ensure .png extension is present
            var fileName = templatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? templatePath
                : templatePath + ".png";

            return Path.Combine(_templatesBasePath, fileName);
        }

        private static string GetApplicationFromPath(string templatePath)
        {
            var parts = templatePath.Split('/');
            return parts.Length > 0 ? parts[0] : "Unknown";
        }

        // Legacy JSON validation removed (workflow-first architecture)

        // Legacy deprecation diagnostics for JSON metadata removed

        
        
        private async Task<byte[]> ConvertBitmapToByteArrayAsync(Bitmap bitmap)
        {
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

                await _loggingService.LogInfoAsync($"[ImageProcessor] Bitmap conversion: {bitmap.Width}x{bitmap.Height}, Stride: {stride}, Expected row bytes: {rowBytes}, Total expected: {expectedSize}");

                // If stride equals width * bytesPerPixel, no padding - copy directly
                if (stride == bitmap.Width * bytesPerPixel)
                {
                    await _loggingService.LogInfoAsync($"[ImageProcessor] No stride padding detected - direct copy");
                    var resultDirect = new byte[expectedSize];
                    Marshal.Copy(bmpData.Scan0, resultDirect, 0, expectedSize);
                    return resultDirect;
                }

                // Handle stride padding by copying row by row without padding
                await _loggingService.LogInfoAsync($"[ImageProcessor] Stride padding detected ({stride - rowBytes} bytes per row) - copying row by row");
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

        private static Bitmap ConvertByteArrayToBitmap(byte[] imageData, int width, int height)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var bmpData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                Marshal.Copy(imageData, 0, bmpData.Scan0, imageData.Length);
                return bitmap;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        public async Task<bool> ReplaceTemplateImageAsync(string templatePath, string newImagePath)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrEmpty(templatePath) || string.IsNullOrEmpty(newImagePath))
                {
                    await _loggingService.LogWarningAsync("Invalid parameters for template image replacement");
                    return false;
                }

                // Verify new image file exists
                if (!File.Exists(newImagePath))
                {
                    await _loggingService.LogWarningAsync($"New image file not found: {newImagePath}");
                    return false;
                }

                // Get the template file path
                var basePath = GetTemplateFilePath(templatePath);
                var pngPath = Path.ChangeExtension(basePath, ".png");

                if (!File.Exists(pngPath))
                {
                    await _loggingService.LogWarningAsync($"Template PNG not found: {pngPath}");
                    return false;
                }

                // Validate the new image
                try
                {
                    using var testImage = new Bitmap(newImagePath);

                    // Check reasonable dimensions
                    if (testImage.Width < 10 || testImage.Height < 10)
                    {
                        await _loggingService.LogWarningAsync($"Image too small: {testImage.Width}x{testImage.Height}");
                        return false;
                    }

                    if (testImage.Width > 3840 || testImage.Height > 2160)
                    {
                        await _loggingService.LogWarningAsync($"Image too large: {testImage.Width}x{testImage.Height}");
                        return false;
                    }

                    await _loggingService.LogInfoAsync($"Validated new image: {testImage.Width}x{testImage.Height}, Format: {testImage.PixelFormat}");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Failed to validate image file: {ex.Message}", ex);
                    return false;
                }

                // Create backup of existing file
                var backupPath = pngPath + ".backup";
                File.Copy(pngPath, backupPath, true);
                await _loggingService.LogInfoAsync($"Backed up original template to: {backupPath}");

                try
                {
                    // Copy new image to template location
                    File.Copy(newImagePath, pngPath, true);
                    await _loggingService.LogInfoAsync($"Replaced template image: {pngPath}");

                    // Clear cache to force reload
                    _templateCache.TryRemove(templatePath, out _);

                    // Verify the template still loads correctly (use default metadata for validation)
                    var template = await LoadTemplateAsync(templatePath);
                    if (template == null || !template.IsValid())
                    {
                        // Rollback on failure
                        await _loggingService.LogWarningAsync("New template failed validation, rolling back");
                        File.Copy(backupPath, pngPath, true);
                        _templateCache.TryRemove(templatePath, out _);
                        return false;
                    }

                    await _loggingService.LogInfoAsync($"Template image replacement successful: {templatePath}");

                    // Delete backup on success
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    // Rollback on any error
                    await _loggingService.LogErrorAsync($"Error replacing template image, rolling back: {ex.Message}", ex);

                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, pngPath, true);
                        File.Delete(backupPath);
                    }

                    _templateCache.TryRemove(templatePath, out _);
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Template image replacement failed: {ex.Message}", ex);
                return false;
            }
        }

        public async Task<bool> CropAndReplaceTemplateImageAsync(string templatePath, string newImagePath, Rectangle? cropRectangle)
        {
            try
            {
                // If no crop rectangle, just use the existing replace method
                if (cropRectangle == null)
                {
                    return await ReplaceTemplateImageAsync(templatePath, newImagePath);
                }

                // Ensure crop service is available
                if (_imageCropService == null)
                {
                    await _loggingService.LogWarningAsync("Image crop service not available, falling back to direct replacement");
                    return await ReplaceTemplateImageAsync(templatePath, newImagePath);
                }

                // Create temporary file for cropped image
                var tempPath = Path.Combine(Path.GetTempPath(), $"template_crop_{Guid.NewGuid()}.png");

                try
                {
                    // Crop the image
                    var cropSuccess = await _imageCropService.CropImageAsync(newImagePath, cropRectangle.Value, tempPath);
                    if (!cropSuccess)
                    {
                        await _loggingService.LogWarningAsync("Failed to crop image");
                        return false;
                    }

                    // Replace template with cropped image
                    var result = await ReplaceTemplateImageAsync(templatePath, tempPath);

                    return result;
                }
                finally
                {
                    // Clean up temporary file
                    if (File.Exists(tempPath))
                    {
                        try
                        {
                            File.Delete(tempPath);
                        }
                        catch
                        {
                            // Best effort cleanup
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Crop and replace template image failed: {ex.Message}", ex);
                return false;
            }
        }

    }
}
