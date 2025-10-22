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
                using var screenshotMat = await _imageProcessor.CreateMatFromImageDataAsync(
                    screenshot.ImageData, screenshot.Width, screenshot.Height, screenshot.Channels, "screenshot");

                using var templateMat = await CreateTemplateMat(template);

                if (screenshotMat.Empty() || templateMat.Empty())
                {
                    return TemplateMatchResult.Failed(template);
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
                    // Iterate scales from MinScale..MaxScale in small steps, testing the nominal scale first
                    var min = Math.Max(0.2f, template.MinScale);
                    var max = Math.Min(3.0f, template.MaxScale);
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

finish_scales: ;
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
                    var result = TemplateMatchResult.Success(
                        template,
                        new System.Drawing.Point(best.Location.X, best.Location.Y),
                        new System.Drawing.Size(best.TemplateSize.Width, best.TemplateSize.Height),
                        best.Confidence,
                        bestScale);

                    result.MatchTimeMs = best.MatchTimeMs;
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
                        var min = Math.Max(0.2f, template.MinScale);
                        var max = Math.Min(3.0f, template.MaxScale);
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

finish_gray_scales: ;

                        if (bestGray != null && bestGray.IsSuccess)
                        {
                            var result = TemplateMatchResult.Success(
                                template,
                                new System.Drawing.Point(bestGray.Location.X, bestGray.Location.Y),
                                new System.Drawing.Size(bestGray.TemplateSize.Width, bestGray.TemplateSize.Height),
                                bestGray.Confidence,
                                bestGrayScale);
                            result.MatchTimeMs = bestGray.MatchTimeMs;
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

    }
}
