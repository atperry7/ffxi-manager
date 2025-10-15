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
        private readonly ConcurrentDictionary<string, UIElementTemplate> _templateCache = new();
        private readonly string _templatesBasePath;

        public TemplateManagementService(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));

            // Set templates base path relative to application directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            _templatesBasePath = Path.Combine(appDir, "Resources", "Templates");

            // Clear cache on startup to force fresh template loading with fixes
            _templateCache.Clear();
            _loggingService.LogInfoAsync("[DEBUG] Template cache cleared on startup - will force fresh loading");
        }

        public async Task<UIElementTemplate?> LoadTemplateAsync(string templatePath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Check cache first
                if (_templateCache.TryGetValue(templatePath, out var cachedTemplate))
                {
                    await _loggingService.LogInfoAsync($"[DEBUG] Template loaded from cache: {templatePath}");
                    return cachedTemplate;
                }

                await _loggingService.LogInfoAsync($"[DEBUG] Template not in cache, loading from disk: {templatePath}");

                // Load from disk
                var template = await LoadTemplateFromDiskAsync(templatePath, cancellationToken);
                if (template != null && template.IsValid())
                {
                    _templateCache.TryAdd(templatePath, template);
                }

                return template;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load template '{templatePath}': {ex.Message}", ex);
                return null;
            }
        }

        public async Task<IList<UIElementTemplate>> LoadTemplatesForStepAsync(LoginTaskStep step, CancellationToken cancellationToken = default)
        {
            var templates = new List<UIElementTemplate>();

            try
            {
                // Scan templates directory for files associated with this step
                var templateFiles = await FindTemplateFilesForStepAsync(step, cancellationToken);

                foreach (var templateFile in templateFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var template = await LoadTemplateAsync(templateFile, cancellationToken);
                    if (template != null)
                    {
                        templates.Add(template);
                    }
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load templates for step {step}: {ex.Message}", ex);
            }

            return templates;
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

                // Save JSON metadata
                var jsonPath = Path.ChangeExtension(templatePath, ".json");
                var json = JsonSerializer.Serialize(CreateTemplateMetadata(template), new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(jsonPath, json, cancellationToken);

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
            return await LoadTemplateAsync(templatePath, CancellationToken.None);
        }

        public async Task<IList<UIElementTemplate>> LoadTemplatesForStepAsync(LoginTaskStep step)
        {
            return await LoadTemplatesForStepAsync(step, CancellationToken.None);
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

        public async Task<FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata?> GetTemplateMetadataAsync(string templatePath)
        {
            try
            {
                var basePath = GetTemplateFilePath(templatePath);
                var jsonPath = Path.ChangeExtension(basePath, ".json");

                if (!File.Exists(jsonPath))
                {
                    return null;
                }

                var jsonContent = await File.ReadAllTextAsync(jsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                var md = JsonSerializer.Deserialize<FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata>(jsonContent, options);
                await LogDeprecatedMetadataAsync(templatePath, md);
                return md;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to get template metadata '{templatePath}': {ex.Message}", ex);
                return null;
            }
        }

        public IEnumerable<string> GetAvailableTemplatePaths()
        {
            try
            {
                if (!Directory.Exists(_templatesBasePath))
                {
                    return Enumerable.Empty<string>();
                }

                var jsonFiles = Directory.GetFiles(_templatesBasePath, "*.json", SearchOption.AllDirectories);
                return jsonFiles.Select(jsonFile =>
                {
                    var relativePath = Path.GetRelativePath(_templatesBasePath, jsonFile);
                    var templatePath = Path.ChangeExtension(relativePath, null);
                    return templatePath.Replace(Path.DirectorySeparatorChar, '/');
                });
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

        public async Task<string> GetTemplateVersionAsync(string templatePath)
        {
            try
            {
                var metadata = await GetTemplateMetadataAsync(templatePath);
                return metadata?.Version ?? "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }

        public async Task<bool> UpdateTemplateNavigationAsync(string templatePath, NavigationAction navigation)
        {
            try
            {
                var basePath = GetTemplateFilePath(templatePath);
                var jsonPath = Path.ChangeExtension(basePath, ".json");

                if (!File.Exists(jsonPath))
                {
                    await _loggingService.LogWarningAsync($"Template JSON not found for update: {templatePath}");
                    return false;
                }

                var jsonContent = await File.ReadAllTextAsync(jsonPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new JsonStringEnumConverter());

                var metadata = JsonSerializer.Deserialize<TemplateMetadata>(jsonContent, options);
                if (metadata == null)
                {
                    await _loggingService.LogWarningAsync($"Failed to deserialize metadata for update: {templatePath}");
                    return false;
                }

                metadata.Navigation = navigation;

                var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                writeOptions.Converters.Add(new JsonStringEnumConverter());

                var updatedJson = JsonSerializer.Serialize(metadata, writeOptions);
                await File.WriteAllTextAsync(jsonPath, updatedJson);

                // Invalidate cache so next load uses new metadata
                _templateCache.TryRemove(templatePath, out _);
                await _loggingService.LogInfoAsync($"[Template Update] Navigation saved and cache invalidated: {templatePath}");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to update template navigation for '{templatePath}': {ex.Message}", ex);
                return false;
            }
        }

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
                    var jsonFiles = Directory.GetFiles(_templatesBasePath, "*.json", SearchOption.AllDirectories);

                    foreach (var jsonFile in jsonFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var relativePath = Path.GetRelativePath(_templatesBasePath, jsonFile);
                        var templatePath = Path.ChangeExtension(relativePath, null);
                        templatePath = templatePath.Replace(Path.DirectorySeparatorChar, '/');
                        paths.Add(templatePath);
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to get available template paths: {ex.Message}", ex);
            }

            return paths;
        }

        private async Task<UIElementTemplate?> LoadTemplateFromDiskAsync(string templatePath, CancellationToken cancellationToken)
        {
            try
            {
                var basePath = GetTemplateFilePath(templatePath);
                var jsonPath = Path.ChangeExtension(basePath, ".json");
                var pngPath = Path.ChangeExtension(basePath, ".png");

                if (!File.Exists(jsonPath) || !File.Exists(pngPath))
                {
                    await _loggingService.LogWarningAsync($"Template files not found: {templatePath}");
                    return null;
                }

                // Load JSON metadata
                var jsonContent = await File.ReadAllTextAsync(jsonPath, cancellationToken);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                var metadata = JsonSerializer.Deserialize<FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata>(jsonContent, options);
                await LogDeprecatedMetadataAsync(templatePath, metadata);

                if (metadata == null)
                {
                    return null;
                }

                // Load PNG image
                var imageData = await LoadTemplateImageAsync(pngPath, cancellationToken);
                if (imageData == null)
                {
                    await _loggingService.LogWarningAsync($"[DEBUG] Failed to load image data for: {pngPath}");
                    return null;
                }

                await _loggingService.LogInfoAsync($"[DEBUG] Loaded image: {pngPath}, Size: {imageData.Value.width}x{imageData.Value.height}, Data length: {imageData.Value.data.Length}");

                // Create template
                var template = new UIElementTemplate
                {
                    Name = metadata.Name ?? Path.GetFileNameWithoutExtension(templatePath),
                    TemplatePath = templatePath,
                    ImageData = imageData.Value.data,
                    Width = imageData.Value.width,
                    Height = imageData.Value.height,
                    Channels = imageData.Value.channels,
                    // Legacy action.clickOffset is deprecated; do not marshal into runtime template
                    ClickOffset = new System.Drawing.Point(0, 0),
                    ConfidenceThreshold = metadata.ConfidenceThreshold,
                    PositionTolerance = metadata.Tolerance,
                    ApplicationName = GetApplicationFromPath(templatePath),
                    Version = metadata.Version ?? "1.0.0",
                    AssociatedStep = ParseLoginTaskStep(metadata.AssociatedStep),
                    Metadata = jsonContent
                };

                await _loggingService.LogInfoAsync($"[DEBUG] Created template: Name={template.Name}, Path={template.TemplatePath}, Valid={template.IsValid()}, ImageData.Length={template.ImageData?.Length ?? 0}, W={template.Width}, H={template.Height}");

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

        private async Task<IList<string>> FindTemplateFilesForStepAsync(LoginTaskStep step, CancellationToken cancellationToken)
        {
            var files = new List<string>();

            if (!Directory.Exists(_templatesBasePath))
            {
                return files;
            }

            await Task.Run(() =>
            {
                var jsonFiles = Directory.GetFiles(_templatesBasePath, "*.json", SearchOption.AllDirectories);

                foreach (var jsonFile in jsonFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var json = File.ReadAllText(jsonFile);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        options.Converters.Add(new JsonStringEnumConverter());
                        var mdEnum = JsonSerializer.Deserialize<FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata>(json, options);
                        // log fire-and-forget minimal
                        _ = LogDeprecatedMetadataAsync(templatePath: jsonFile, metadata: mdEnum);

                        if (mdEnum?.AssociatedStep != null && ParseLoginTaskStep(mdEnum.AssociatedStep) == step)
                        {
                            var relativePath = Path.GetRelativePath(_templatesBasePath, jsonFile);
                            var templatePath = Path.ChangeExtension(relativePath, null);
                            templatePath = templatePath.Replace(Path.DirectorySeparatorChar, '/');
                            files.Add(templatePath);
                        }
                    }
                    catch
                    {
                        // Skip invalid files
                    }
                }
            }, cancellationToken);

            return files;
        }

        private string GetTemplateFilePath(string templatePath)
        {
            return Path.Combine(_templatesBasePath, templatePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetApplicationFromPath(string templatePath)
        {
            var parts = templatePath.Split('/');
            return parts.Length > 0 ? parts[0] : "Unknown";
        }

        public async Task<TemplateValidationReport> ValidateAllTemplatesAsync(CancellationToken cancellationToken = default)
        {
            var report = new TemplateValidationReport();
            try
            {
                var jsonFiles = Directory.Exists(_templatesBasePath)
                    ? Directory.GetFiles(_templatesBasePath, "*.json", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                foreach (var jsonFile in jsonFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var json = await File.ReadAllTextAsync(jsonFile, cancellationToken);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        options.Converters.Add(new JsonStringEnumConverter());
                        var md = JsonSerializer.Deserialize<TemplateMetadata>(json, options);
                        report.TotalTemplates++;

                        if (md == null)
                        {
                            await _loggingService.LogWarningAsync($"[Template Validation] Failed to parse: {jsonFile}");
                            continue;
                        }

                        if (md.Navigation == null)
                        {
                            await _loggingService.LogWarningAsync($"[Template Validation] Missing navigation block: {jsonFile}");
                        }
                        else
                        {
                            if (md.Navigation.Type != FFXIManager.Models.NavigationType.Hybrid)
                            {
                                report.NonHybrid++;
                                await _loggingService.LogWarningAsync($"[Template Validation] Navigation.type is '{md.Navigation.Type}', expected 'Hybrid': {jsonFile}");
                            }
                            else
                            {
                                report.HybridConformant++;
                            }
                        }

                        if (md.Action != null && (md.Action.ClickOffset?.X != 0 || md.Action.ClickOffset?.Y != 0))
                        {
                            report.DeprecatedActionOffsets++;
                            await _loggingService.LogWarningAsync($"[Template Validation] Deprecated action.clickOffset present: {jsonFile}");
                        }

                        if (md.Properties != null && (md.Properties.ContainsKey("memberSlots") || md.Properties.ContainsKey("otpField")))
                        {
                            report.DeprecatedAbsoluteBlocks++;
                            await _loggingService.LogWarningAsync($"[Template Validation] Deprecated absolute block (memberSlots/otpField): {jsonFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogErrorAsync($"[Template Validation] Error validating {jsonFile}: {ex.Message}", ex);
                    }
                }

                await _loggingService.LogInfoAsync($"[Template Validation] Total={report.TotalTemplates}, Hybrid={report.HybridConformant}, NonHybrid={report.NonHybrid}, DeprecatedActionOffsets={report.DeprecatedActionOffsets}, DeprecatedBlocks={report.DeprecatedAbsoluteBlocks}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[Template Validation] Failed: {ex.Message}", ex);
            }
            return report;
        }

        private async Task LogDeprecatedMetadataAsync(string templatePath, TemplateMetadata? metadata)
        {
            try
            {
                if (metadata == null) return;

                // action.clickOffset with absolute X/Y is legacy and should be removed
                if (metadata.Action != null && (metadata.Action.ClickOffset?.X != 0 || metadata.Action.ClickOffset?.Y != 0))
                {
                    await _loggingService.LogWarningAsync($"[Template:{templatePath}] Deprecated 'action.clickOffset' detected. Please move to navigation.fallback.clickOffset and remove 'action'.");
                }

                // Known legacy fields sometimes embedded in Properties
                if (metadata.Properties != null)
                {
                    if (metadata.Properties.ContainsKey("memberSlots") || metadata.Properties.ContainsKey("otpField"))
                    {
                        await _loggingService.LogWarningAsync($"[Template:{templatePath}] Deprecated absolute coordinate fields detected (memberSlots/otpField). Remove and rely on Hybrid navigation.");
                    }
                }
            }
            catch
            {
                // Best-effort; do not block on diagnostics
            }
        }

        private static LoginTaskStep ParseLoginTaskStep(string stepName)
        {
            if (Enum.TryParse<LoginTaskStep>(stepName, true, out var step))
            {
                return step;
            }
            return LoginTaskStep.None;
        }

        private static FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata CreateTemplateMetadata(UIElementTemplate template)
        {
            return new FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata
            {
                Name = template.Name,
                TemplatePath = template.TemplatePath,
                AssociatedStep = template.AssociatedStep.ToString(),
                Action = new FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata.ActionConfig
                {
                    Type = "click",
                    ClickOffset = new FFXIManager.Services.AutoLogin.ScreenDetection.TemplateMetadata.ClickOffset
                    {
                        X = template.ClickOffset.X,
                        Y = template.ClickOffset.Y
                    }
                },
                ConfidenceThreshold = template.ConfidenceThreshold,
                Tolerance = template.PositionTolerance,
                Version = template.Version
            };
        }

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

    }
}
