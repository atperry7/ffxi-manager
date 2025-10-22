using OpenCvSharp;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    /// <summary>
    /// Real implementation of template matching service using OpenCV
    /// </summary>
    public class TemplateMatchingService : ITemplateMatchingService
    {
        private readonly ILoggingService _loggingService;
        private readonly ITemplateManagementService _templateManagementService;
        private readonly IImageProcessor _imageProcessor;
        private float _defaultConfidenceThreshold = 0.80f;

        // Smart downsampling configuration
        private const int TARGET_MAX_DIMENSION = 1920; // Downsample screenshots larger than this
        private const bool ENABLE_SMART_DOWNSAMPLING = true;
        private const bool ENABLE_DPI_SCALE_PREDICTION = true;
        private const float DPI_SCALE_TOLERANCE = 0.3f; // ±0.3 around predicted scale

        public TemplateMatchingService(
            ILoggingService loggingService,
            ITemplateManagementService templateManagementService,
            IImageProcessor imageProcessor)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _templateManagementService = templateManagementService ?? throw new ArgumentNullException(nameof(templateManagementService));
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        }

        public async Task<TemplateMatchResult> FindElementAsync(WindowScreenshot screenshot, UIElementTemplate template, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (screenshot?.ImageData == null || !screenshot.IsValid)
                {
                    return TemplateMatchResult.Failed(template);
                }

                if (template?.ImageData == null || !template.IsValid())
                {
                    return TemplateMatchResult.Failed(template!);
                }

                // Create Mats using standardized image processor
                using var screenshotMatOriginal = await _imageProcessor.CreateMatFromImageDataAsync(
                    screenshot.ImageData, screenshot.Width, screenshot.Height, screenshot.Channels, "screenshot");

                using var templateMatOriginal = await CreateTemplateMat(template);

                if (screenshotMatOriginal.Empty() || templateMatOriginal.Empty())
                {
                    return TemplateMatchResult.Failed(template);
                }

                // Apply smart downsampling for performance optimization
                var downsampleResult = DownsampleForMatching(
                    screenshotMatOriginal, templateMatOriginal, screenshot.DpiScale);
                using var screenshotMat = downsampleResult.downsampledScreenshot;
                using var templateMat = downsampleResult.downsampledTemplate;
                var downsampleFactor = downsampleResult.scaleFactor;

                if (downsampleFactor < 1.0f)
                {
                    await _loggingService.LogInfoAsync(
                        $"[OPTIMIZATION] Downsampled for matching: {screenshotMatOriginal.Width}x{screenshotMatOriginal.Height} → " +
                        $"{screenshotMat.Width}x{screenshotMat.Height} (factor: {downsampleFactor:F3}, " +
                        $"memory saved: {(1 - downsampleFactor * downsampleFactor) * 100:F1}%)");
                }

                // Perform template matching (with optional multi-scale search)
                var description = $"template '{template.TemplatePath ?? template.Name}'";

                ImageMatchResult? best = null;
                float bestScale = 1.0f;

                // Helper local function to evaluate one scale
                async Task EvaluateScaleAsync(float scale)
                {
                    // Skip invalid sizes
                    int scaledW = (int)Math.Round(templateMat.Width * scale);
                    int scaledH = (int)Math.Round(templateMat.Height * scale);
                    if (scaledW <= 1 || scaledH <= 1) return;
                    if (scaledW > screenshotMat.Width || scaledH > screenshotMat.Height) return;

                    using var scaledTemplate = new OpenCvSharp.Mat();
                    OpenCvSharp.Cv2.Resize(templateMat, scaledTemplate, new OpenCvSharp.Size(scaledW, scaledH), 0, 0, OpenCvSharp.InterpolationFlags.Area);

                    var result = await _imageProcessor.PerformTemplateMatchingAsync(
                        screenshotMat, scaledTemplate, template.ConfidenceThreshold, description, cancellationToken);

                    if (best == null || result.Confidence > best.Confidence)
                    {
                        best = result;
                        bestScale = scale;
                    }
                }

                if (template.SupportsMultiScale)
                {
                    // Calculate DPI-aware scale range (or use full range if DPI prediction disabled)
                    var baseMin = Math.Max(0.2f, template.MinScale);
                    var baseMax = Math.Min(3.0f, template.MaxScale);
                    var (min, max) = CalculateScaleRange(screenshot.DpiScale, baseMin, baseMax);

                    // Log scale range for diagnostics
                    if (ENABLE_DPI_SCALE_PREDICTION && screenshot.DpiScale > 0.1f)
                    {
                        await _loggingService.LogInfoAsync(
                            $"[OPTIMIZATION] DPI-aware scale prediction: DPI={screenshot.DpiScale:F2}, " +
                            $"predicted range [{min:F2}, {max:F2}] (base: [{baseMin:F2}, {baseMax:F2}])");
                    }

                    var step = 0.10f; // fewer checks, faster; early-exit remains

                    // Try nominal scale first for fast exit
                    await EvaluateScaleAsync(1.0f);

                    // Heuristic: on very large windows (e.g., 4K), try 2.0x early
                    if (screenshotMat.Width >= 3000 || screenshotMat.Height >= 1800)
                    {
                        if (2.0f >= min && 2.0f <= max)
                        {
                            await EvaluateScaleAsync(2.0f);
                            if (best != null && best.Confidence >= template.ConfidenceThreshold)
                            {
                                // strong enough already
                                goto finish_scales;
                            }
                        }
                    }

                    // Sweep outward from 1.0 alternately +/- step for better early exits
                    for (float delta = step; delta <= (max - min); delta += step)
                    {
                        var up = 1.0f + delta;
                        var down = 1.0f - delta;
                        if (up <= max) await EvaluateScaleAsync(up);
                        if (down >= min) await EvaluateScaleAsync(down);

                        // Early exit if we already exceed threshold by a healthy margin
                        if (best != null && best.Confidence >= template.ConfidenceThreshold)
                            break;
                    }

                    finish_scales:;
                }
                else
                {
                    // Single-scale matching
                    best = await _imageProcessor.PerformTemplateMatchingAsync(
                        screenshotMat, templateMat, template.ConfidenceThreshold, description, cancellationToken);
                    bestScale = 1.0f;
                }

                // Convert ImageMatchResult to TemplateMatchResult
                if (best != null && best.IsSuccess)
                {
                    // Scale coordinates back to original size if downsampling was applied
                    var scaledLocation = downsampleFactor < 1.0f
                        ? new System.Drawing.Point(
                            (int)Math.Round(best.Location.X / downsampleFactor),
                            (int)Math.Round(best.Location.Y / downsampleFactor))
                        : new System.Drawing.Point(best.Location.X, best.Location.Y);

                    var scaledTemplateSize = downsampleFactor < 1.0f
                        ? new System.Drawing.Size(
                            (int)Math.Round(best.TemplateSize.Width / downsampleFactor),
                            (int)Math.Round(best.TemplateSize.Height / downsampleFactor))
                        : new System.Drawing.Size(best.TemplateSize.Width, best.TemplateSize.Height);

                    var result = TemplateMatchResult.Success(
                        template,
                        scaledLocation,
                        scaledTemplateSize,
                        best.Confidence,
                        bestScale);

                    result.MatchTimeMs = best.MatchTimeMs;

                    // Log optimization metrics
                    if (downsampleFactor < 1.0f)
                    {
                        await _loggingService.LogInfoAsync(
                            $"[OPTIMIZATION] Match coordinates scaled from downsampled space: " +
                            $"({best.Location.X},{best.Location.Y}) → ({scaledLocation.X},{scaledLocation.Y})");
                    }

                    return result;
                }

                // If color match failed to meet threshold, try a single grayscale fallback pass (lean processing)
                // This helps with AA/gamma differences on fullscreen DX without many extra steps.
                if (best == null || best.Confidence < template.ConfidenceThreshold)
                {
                    await _loggingService.LogInfoAsync($"[DETECTION] Grayscale fallback matching for {description} (previous best: {(best?.Confidence ?? 0):P})");

                    using var screenshotGray = await _imageProcessor.ConvertToGrayscaleAsync(screenshotMat, "screenshot");
                    using var templateGrayBase = await _imageProcessor.ConvertToGrayscaleAsync(templateMat, description);

                    ImageMatchResult? bestGray = null;
                    float bestGrayScale = 1.0f;

                    async Task EvaluateGrayScaleAsync(float scale)
                    {
                        int scaledW = (int)Math.Round(templateGrayBase.Width * scale);
                        int scaledH = (int)Math.Round(templateGrayBase.Height * scale);
                        if (scaledW <= 1 || scaledH <= 1) return;
                        if (scaledW > screenshotGray.Width || scaledH > screenshotGray.Height) return;

                        using var scaledTemplate = new OpenCvSharp.Mat();
                        OpenCvSharp.Cv2.Resize(templateGrayBase, scaledTemplate, new OpenCvSharp.Size(scaledW, scaledH), 0, 0, OpenCvSharp.InterpolationFlags.Area);

                        var result = await _imageProcessor.PerformTemplateMatchingAsync(
                            screenshotGray, scaledTemplate, template.ConfidenceThreshold, description + " [grayscale]", cancellationToken);

                        if (bestGray == null || result.Confidence > bestGray.Confidence)
                        {
                            bestGray = result;
                            bestGrayScale = scale;
                        }
                    }

                    if (!screenshotGray.Empty() && !templateGrayBase.Empty())
                    {
                        // Use same DPI-aware scale range for grayscale fallback
                        var baseMin = Math.Max(0.2f, template.MinScale);
                        var baseMax = Math.Min(3.0f, template.MaxScale);
                        var (min, max) = CalculateScaleRange(screenshot.DpiScale, baseMin, baseMax);
                        var step = 0.10f;

                        await EvaluateGrayScaleAsync(1.0f);
                        if (screenshotMat.Width >= 3000 || screenshotMat.Height >= 1800)
                        {
                            if (2.0f >= min && 2.0f <= max)
                            {
                                await EvaluateGrayScaleAsync(2.0f);
                                if (bestGray != null && bestGray.Confidence >= template.ConfidenceThreshold)
                                    goto finish_gray_scales;
                            }
                        }

                        for (float delta = step; delta <= (max - min); delta += step)
                        {
                            var up = 1.0f + delta;
                            var down = 1.0f - delta;
                            if (up <= max) await EvaluateGrayScaleAsync(up);
                            if (down >= min) await EvaluateGrayScaleAsync(down);
                            if (bestGray != null && bestGray.Confidence >= template.ConfidenceThreshold)
                                break;
                        }

                        finish_gray_scales:;

                        if (bestGray != null && bestGray.IsSuccess)
                        {
                            // Scale grayscale match coordinates back to original size
                            var scaledGrayLocation = downsampleFactor < 1.0f
                                ? new System.Drawing.Point(
                                    (int)Math.Round(bestGray.Location.X / downsampleFactor),
                                    (int)Math.Round(bestGray.Location.Y / downsampleFactor))
                                : new System.Drawing.Point(bestGray.Location.X, bestGray.Location.Y);

                            var scaledGrayTemplateSize = downsampleFactor < 1.0f
                                ? new System.Drawing.Size(
                                    (int)Math.Round(bestGray.TemplateSize.Width / downsampleFactor),
                                    (int)Math.Round(bestGray.TemplateSize.Height / downsampleFactor))
                                : new System.Drawing.Size(bestGray.TemplateSize.Width, bestGray.TemplateSize.Height);

                            var result = TemplateMatchResult.Success(
                                template,
                                scaledGrayLocation,
                                scaledGrayTemplateSize,
                                bestGray.Confidence,
                                bestGrayScale);
                            result.MatchTimeMs = bestGray.MatchTimeMs;

                            if (downsampleFactor < 1.0f)
                            {
                                await _loggingService.LogInfoAsync(
                                    $"[OPTIMIZATION] Grayscale match coordinates scaled: " +
                                    $"({bestGray.Location.X},{bestGray.Location.Y}) → ({scaledGrayLocation.X},{scaledGrayLocation.Y})");
                            }

                            return result;
                        }
                        else if (bestGray != null && bestGray.Confidence > (best?.Confidence ?? 0))
                        {
                            // keep improved confidence even if below threshold for diagnostics
                            best = bestGray;
                            bestScale = bestGrayScale;
                        }
                    }
                }

                var failedResult = TemplateMatchResult.Failed(template);
                if (best != null)
                {
                    failedResult.Confidence = best.Confidence;
                    failedResult.MatchTimeMs = best.MatchTimeMs;
                    failedResult.Scale = bestScale;
                }
                return failedResult;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Template matching failed: {ex.Message}", ex);
                return TemplateMatchResult.Failed(template);
            }
        }

        /// <summary>
        /// Creates a template Mat with proper BGRA to BGR conversion if needed
        /// </summary>
        private async Task<Mat> CreateTemplateMat(UIElementTemplate template)
        {
            var templateMat = await _imageProcessor.CreateMatFromImageDataAsync(
                template.ImageData!, template.Width, template.Height, template.Channels,
                $"template '{template.TemplatePath ?? template.Name}'");

            // Convert BGRA to BGR if needed
            if (template.Channels == 4 && !templateMat.Empty())
            {
                var bgrMat = await _imageProcessor.ConvertBgraToBlrAsync(templateMat,
                    $"template '{template.TemplatePath ?? template.Name}'");
                templateMat.Dispose();
                return bgrMat;
            }

            return templateMat;
        }

        public async Task<TemplateMatchResult> FindElementAsync(WindowScreenshot screenshot, string templatePath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Load template by path
                var template = await _templateManagementService.LoadTemplateAsync(templatePath);
                if (template == null)
                {
                    await _loggingService.LogWarningAsync($"Template not found: {templatePath}");
                    return TemplateMatchResult.Failed(new UIElementTemplate { TemplatePath = templatePath });
                }

                await _loggingService.LogInfoAsync($"[DEBUG] Template loaded successfully: {templatePath}, Valid: {template.IsValid()}");

                return await FindElementAsync(screenshot, template, cancellationToken);
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to find element by path '{templatePath}': {ex.Message}", ex);
                return TemplateMatchResult.Failed(new UIElementTemplate { TemplatePath = templatePath });
            }
        }

        public async Task<IList<TemplateMatchResult>> FindMultipleElementsAsync(WindowScreenshot screenshot, IEnumerable<UIElementTemplate> templates, CancellationToken cancellationToken = default)
        {
            var results = new List<TemplateMatchResult>();

            foreach (var template in templates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await FindElementAsync(screenshot, template, cancellationToken);
                results.Add(result);
            }

            return results;
        }

        public async Task<IList<TemplateMatchResult>> FindAllInstancesAsync(WindowScreenshot screenshot, UIElementTemplate template, int maxMatches = 10, CancellationToken cancellationToken = default)
        {
            var results = new List<TemplateMatchResult>();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (screenshot?.ImageData == null || !screenshot.IsValid)
                {
                    return results;
                }

                if (template?.ImageData == null || !template.IsValid())
                {
                    return results;
                }

                // Create Mats using standardized image processor
                using var screenshotMat = await _imageProcessor.CreateMatFromImageDataAsync(
                    screenshot.ImageData, screenshot.Width, screenshot.Height, screenshot.Channels, "screenshot");

                using var templateMat = await CreateTemplateMat(template);

                if (screenshotMat.Empty() || templateMat.Empty())
                {
                    return results;
                }

                using var result = new Mat();
                Cv2.MatchTemplate(screenshotMat, templateMat, result, TemplateMatchModes.CCoeffNormed);

                // Find all matches above threshold
                var threshold = template.ConfidenceThreshold;
                var matches = new List<(OpenCvSharp.Point location, float confidence)>();

                // Scan for matches
                for (int y = 0; y < result.Rows; y++)
                {
                    for (int x = 0; x < result.Cols; x++)
                    {
                        var confidence = result.At<float>(y, x);
                        if (confidence >= threshold)
                        {
                            matches.Add((new OpenCvSharp.Point(x, y), confidence));
                        }
                    }
                }

                // Sort by confidence and take top matches
                matches = matches.OrderByDescending(m => m.confidence).Take(maxMatches).ToList();

                foreach (var (location, confidence) in matches)
                {
                    var matchResult = TemplateMatchResult.Success(
                        template,
                        new System.Drawing.Point(location.X, location.Y),
                        new System.Drawing.Size(templateMat.Width, templateMat.Height),
                        confidence
                    );
                    results.Add(matchResult);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Finding all instances failed: {ex.Message}", ex);
            }

            return results;
        }

        public async Task<bool> ValidateMatchAsync(WindowScreenshot screenshot, TemplateMatchResult previousMatch, CancellationToken cancellationToken = default)
        {
            if (previousMatch?.Template == null) return false;

            // Re-match at the same location with some tolerance
            var result = await FindElementAsync(screenshot, previousMatch.Template, cancellationToken);

            if (!result.IsValid) return false;

            // Check if the new match is close to the previous location
            var tolerance = previousMatch.Template.PositionTolerance;
            var distance = Math.Sqrt(
                Math.Pow(result.WindowRelativePosition.X - previousMatch.WindowRelativePosition.X, 2) +
                Math.Pow(result.WindowRelativePosition.Y - previousMatch.WindowRelativePosition.Y, 2));

            return distance <= tolerance;
        }

        public void SetDefaultConfidenceThreshold(float threshold)
        {
            _defaultConfidenceThreshold = Math.Max(0.0f, Math.Min(1.0f, threshold));
        }

        public float GetDefaultConfidenceThreshold()
        {
            return _defaultConfidenceThreshold;
        }

        /// <summary>
        /// Calculates the optimal downsample factor for a screenshot based on its dimensions.
        /// Returns 1.0 if no downsampling is needed, or a value less than 1.0 to downsample.
        /// </summary>
        private float CalculateDownsampleFactor(int width, int height)
        {
            if (!ENABLE_SMART_DOWNSAMPLING)
                return 1.0f;

            var maxDimension = Math.Max(width, height);

            if (maxDimension <= TARGET_MAX_DIMENSION)
                return 1.0f; // No downsampling needed

            return (float)TARGET_MAX_DIMENSION / maxDimension;
        }

        /// <summary>
        /// Downsamples both screenshot and template proportionally for faster matching.
        /// Returns downsampled Mats and the scale factor used.
        /// </summary>
        private (Mat downsampledScreenshot, Mat downsampledTemplate, float scaleFactor)
            DownsampleForMatching(Mat screenshot, Mat template, float screenshotDpiScale)
        {
            var downsampleFactor = CalculateDownsampleFactor(screenshot.Width, screenshot.Height);

            if (downsampleFactor >= 1.0f)
            {
                // No downsampling needed - return clones
                return (screenshot.Clone(), template.Clone(), 1.0f);
            }

            // Calculate new dimensions
            var newScreenshotSize = new OpenCvSharp.Size(
                (int)Math.Round(screenshot.Width * downsampleFactor),
                (int)Math.Round(screenshot.Height * downsampleFactor));

            var newTemplateSize = new OpenCvSharp.Size(
                (int)Math.Round(template.Width * downsampleFactor),
                (int)Math.Round(template.Height * downsampleFactor));

            // Validate downsampled template won't be too small
            if (newTemplateSize.Width < 10 || newTemplateSize.Height < 10)
            {
                // Template would be too small, skip downsampling
                return (screenshot.Clone(), template.Clone(), 1.0f);
            }

            // Perform downsampling
            var downsampledScreenshot = new Mat();
            var downsampledTemplate = new Mat();

            OpenCvSharp.Cv2.Resize(screenshot, downsampledScreenshot, newScreenshotSize,
                0, 0, OpenCvSharp.InterpolationFlags.Area); // Area interpolation is best for downsampling

            OpenCvSharp.Cv2.Resize(template, downsampledTemplate, newTemplateSize,
                0, 0, OpenCvSharp.InterpolationFlags.Area);

            return (downsampledScreenshot, downsampledTemplate, downsampleFactor);
        }

        /// <summary>
        /// Calculates narrowed scale search range based on DPI scale prediction.
        /// Uses DPI information to dramatically reduce the number of scale iterations needed.
        /// </summary>
        private (float minScale, float maxScale) CalculateScaleRange(
            float dpiScale,
            float baseMinScale,
            float baseMaxScale)
        {
            if (!ENABLE_DPI_SCALE_PREDICTION || dpiScale <= 0.1f)
            {
                // DPI not available or disabled, use full range
                return (baseMinScale, baseMaxScale);
            }

            // Predict likely scale based on DPI
            // DPI 1.0 (96 DPI) → look near scale 1.0
            // DPI 1.5 (144 DPI) → look near scale 0.67
            // DPI 2.0 (192 DPI) → look near scale 0.5
            var predictedScale = 1.0f / Math.Max(0.1f, dpiScale);

            // Narrow search range around predicted scale
            var minScale = Math.Max(baseMinScale, predictedScale - DPI_SCALE_TOLERANCE);
            var maxScale = Math.Min(baseMaxScale, predictedScale + DPI_SCALE_TOLERANCE);

            // Ensure we have a valid range
            if (minScale >= maxScale)
            {
                // Fallback to full range if prediction is problematic
                return (baseMinScale, baseMaxScale);
            }

            return (minScale, maxScale);
        }

    }
}
