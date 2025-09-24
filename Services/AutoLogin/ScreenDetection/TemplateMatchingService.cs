using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        private float _defaultConfidenceThreshold = 0.80f;

        public TemplateMatchingService(
            ILoggingService loggingService,
            ITemplateManagementService templateManagementService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _templateManagementService = templateManagementService ?? throw new ArgumentNullException(nameof(templateManagementService));
        }

        public async Task<TemplateMatchResult> FindElementAsync(WindowScreenshot screenshot, UIElementTemplate template, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
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

                    var startTime = DateTime.Now;

                    // Convert screenshot to OpenCV Mat
                    using var screenshotMat = ConvertToMat(screenshot);
                    using var templateMat = ConvertToMat(template);

                    if (screenshotMat.Empty() || templateMat.Empty())
                    {
                        _loggingService.LogWarningAsync($"[DEBUG] Mat conversion failed - Screenshot empty: {screenshotMat.Empty()}, Template empty: {templateMat.Empty()}");
                        return TemplateMatchResult.Failed(template);
                    }

                    _loggingService.LogInfoAsync($"[DEBUG] Template matching: Screenshot {screenshotMat.Width}x{screenshotMat.Height}, Template {templateMat.Width}x{templateMat.Height}");

                    // Perform template matching
                    using var result = new Mat();
                    Cv2.MatchTemplate(screenshotMat, templateMat, result, TemplateMatchModes.CCoeffNormed);

                    // Find the best match
                    Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                    var confidence = (float)maxVal;
                    var matchTime = (DateTime.Now - startTime).TotalMilliseconds;

                    if (confidence >= template.ConfidenceThreshold)
                    {
                        var matchResult = TemplateMatchResult.Success(
                            template,
                            new System.Drawing.Point(maxLoc.X, maxLoc.Y),
                            new System.Drawing.Size(templateMat.Width, templateMat.Height),
                            confidence
                        );

                        matchResult.MatchTimeMs = matchTime;
                        return matchResult;
                    }

                    var failedResult = TemplateMatchResult.Failed(template);
                    failedResult.Confidence = confidence;
                    failedResult.MatchTimeMs = matchTime;
                    return failedResult;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _loggingService.LogErrorAsync($"Template matching failed: {ex.Message}", ex);
                    return TemplateMatchResult.Failed(template);
                }
            }, cancellationToken);
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
            return await Task.Run(() =>
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

                    using var screenshotMat = ConvertToMat(screenshot);
                    using var templateMat = ConvertToMat(template);

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
                    _loggingService.LogErrorAsync($"Finding all instances failed: {ex.Message}", ex);
                }

                return results;
            }, cancellationToken);
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

        private Mat ConvertToMat(WindowScreenshot screenshot)
        {
            try
            {
                // Create Mat from byte array (assuming BGR format)
                using var mat = Mat.FromArray<byte>(screenshot.ImageData);
                var resizedMat = mat.Reshape(3, screenshot.Height);
                _loggingService.LogInfoAsync($"[DEBUG] Screenshot Mat: {resizedMat.Width}x{resizedMat.Height}, Channels: 3");
                return resizedMat.Clone(); // Clone to ensure data ownership
            }
            catch (Exception ex)
            {
                _loggingService.LogWarningAsync($"[DEBUG] Screenshot Mat conversion failed: {ex.Message}");
                return new Mat();
            }
        }

        private Mat ConvertToMat(UIElementTemplate template)
        {
            try
            {
                _loggingService.LogInfoAsync($"[DEBUG] Template conversion: {template.Width}x{template.Height}, Channels: {template.Channels}, Data length: {template.ImageData?.Length ?? 0}");

                // Create Mat from byte array
                using var mat = Mat.FromArray<byte>(template.ImageData);
                var resizedMat = mat.Reshape(template.Channels, template.Height);

                // Convert to BGR if necessary
                if (template.Channels == 4)
                {
                    var bgrMat = new Mat();
                    Cv2.CvtColor(resizedMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                    _loggingService.LogInfoAsync($"[DEBUG] Template Mat final: {bgrMat.Width}x{bgrMat.Height}, BGR converted");
                    return bgrMat;
                }

                _loggingService.LogInfoAsync($"[DEBUG] Template Mat final: {resizedMat.Width}x{resizedMat.Height}");
                return resizedMat.Clone();
            }
            catch (Exception ex)
            {
                _loggingService.LogWarningAsync($"[DEBUG] Template Mat conversion failed: {ex.Message}");
                return new Mat();
            }
        }
    }
}