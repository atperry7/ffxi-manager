using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using FFXIManager.Services;

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
                    return TemplateMatchResult.Failed(template);
                }

                // Create Mats using standardized image processor
                using var screenshotMat = await _imageProcessor.CreateMatFromImageDataAsync(
                    screenshot.ImageData, screenshot.Width, screenshot.Height, screenshot.Channels, "screenshot");

                using var templateMat = await CreateTemplateMat(template);

                if (screenshotMat.Empty() || templateMat.Empty())
                {
                    return TemplateMatchResult.Failed(template);
                }

                // Perform template matching using standardized processor
                var matchResult = await _imageProcessor.PerformTemplateMatchingAsync(
                    screenshotMat, templateMat, template.ConfidenceThreshold,
                    $"template '{template.TemplatePath ?? template.Name}'", cancellationToken);

                // Convert ImageMatchResult to TemplateMatchResult
                if (matchResult.IsSuccess)
                {
                    var result = TemplateMatchResult.Success(
                        template,
                        new System.Drawing.Point(matchResult.Location.X, matchResult.Location.Y),
                        new System.Drawing.Size(matchResult.TemplateSize.Width, matchResult.TemplateSize.Height),
                        matchResult.Confidence);

                    result.MatchTimeMs = matchResult.MatchTimeMs;
                    return result;
                }

                var failedResult = TemplateMatchResult.Failed(template);
                failedResult.Confidence = matchResult.Confidence;
                failedResult.MatchTimeMs = matchResult.MatchTimeMs;
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