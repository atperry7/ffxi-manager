using OpenCvSharp;
using System.Runtime.InteropServices;

namespace FFXIManager.Services.AutoLogin.ScreenDetection;

/// <summary>
/// Standard implementation of image processing operations with consistent validation and error handling.
/// Provides architectural consistency across all image processing tasks.
/// </summary>
public class ImageProcessor : IImageProcessor
{
    private readonly ILoggingService _loggingService;

    public ImageProcessor(ILoggingService loggingService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
    }

    public bool ValidateImageData(byte[]? imageData, int width, int height, int channels)
    {
        if (imageData == null || imageData.Length == 0)
        {
            _loggingService.LogWarningAsync("[ImageProcessor] Image data is null or empty");
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            _loggingService.LogWarningAsync($"[ImageProcessor] Invalid dimensions: {width}x{height}");
            return false;
        }

        if (channels != 3 && channels != 4)
        {
            _loggingService.LogWarningAsync($"[ImageProcessor] Unsupported channel count: {channels}. Expected 3 (BGR) or 4 (BGRA)");
            return false;
        }

        // Removed rigid size validation - OpenCV template matching should handle size variations
        // Template matching works with different sized images and should focus on visual pattern recognition
        _loggingService.LogInfoAsync($"[ImageProcessor] Image validation passed: {width}x{height}, {channels} channels, {imageData.Length} bytes");

        return true;
    }

    public async Task<Mat> CreateMatFromImageDataAsync(byte[] imageData, int width, int height, int channels, string description)
    {
        try
        {
            if (!ValidateImageData(imageData, width, height, channels))
            {
                return new Mat();
            }

            var matType = channels == 3 ? MatType.CV_8UC3 : MatType.CV_8UC4;
            var mat = new Mat(height, width, matType);
            Marshal.Copy(imageData, 0, mat.Data, imageData.Length);

            await _loggingService.LogInfoAsync($"[ImageProcessor] Created Mat for {description}: {mat.Width}x{mat.Height}, Channels: {channels}, Type: {matType}");
            return mat;
        }
        catch (Exception ex)
        {
            await _loggingService.LogErrorAsync($"[ImageProcessor] Failed to create Mat for {description}: {ex.Message}", ex);
            return new Mat();
        }
    }

    public async Task<Mat> ConvertBgraToBlrAsync(Mat sourceMat, string description)
    {
        try
        {
            if (sourceMat.Empty() || sourceMat.Channels() != 4)
            {
                await _loggingService.LogWarningAsync($"[ImageProcessor] Invalid BGRA Mat for conversion: {description}");
                return new Mat();
            }

            var bgrMat = new Mat();
            Cv2.CvtColor(sourceMat, bgrMat, ColorConversionCodes.BGRA2BGR);

            await _loggingService.LogInfoAsync($"[ImageProcessor] Converted BGRA to BGR for {description}: {bgrMat.Width}x{bgrMat.Height}");
            return bgrMat;
        }
        catch (Exception ex)
        {
            await _loggingService.LogErrorAsync($"[ImageProcessor] BGRA to BGR conversion failed for {description}: {ex.Message}", ex);
            return new Mat();
        }
    }

    public async Task<ImageMatchResult> PerformTemplateMatchingAsync(
        Mat screenshotMat,
        Mat templateMat,
        float confidenceThreshold,
        string description,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;

        try
        {
            // Validate input Mats
            if (screenshotMat.Empty() || templateMat.Empty())
            {
                var error = $"Empty Mat provided for {description} - Screenshot empty: {screenshotMat.Empty()}, Template empty: {templateMat.Empty()}";
                await _loggingService.LogWarningAsync($"[ImageProcessor] {error}");
                return ImageMatchResult.Failure(error);
            }

            // Validate dimensions
            if (screenshotMat.Width < templateMat.Width || screenshotMat.Height < templateMat.Height)
            {
                var error = $"Template larger than screenshot for {description} - Screenshot: {screenshotMat.Width}x{screenshotMat.Height}, Template: {templateMat.Width}x{templateMat.Height}";
                await _loggingService.LogWarningAsync($"[ImageProcessor] {error}");
                return ImageMatchResult.Failure(error);
            }

            await _loggingService.LogInfoAsync($"[ImageProcessor] Starting template matching for {description}: Screenshot {screenshotMat.Width}x{screenshotMat.Height}, Template {templateMat.Width}x{templateMat.Height}");

            cancellationToken.ThrowIfCancellationRequested();

            // Perform template matching
            using var result = new Mat();
            Cv2.MatchTemplate(screenshotMat, templateMat, result, TemplateMatchModes.CCoeffNormed);

            await _loggingService.LogInfoAsync($"[ImageProcessor] Template matching completed for {description}, result size: {result.Width}x{result.Height}");

            // Find best match
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

            var confidence = (float)maxVal;
            var matchTime = (DateTime.Now - startTime).TotalMilliseconds;

            await _loggingService.LogInfoAsync($"[ImageProcessor] Template matching result for {description}: confidence={confidence:P}, threshold={confidenceThreshold:P}, match_time={matchTime:F1}ms, location=({maxLoc.X},{maxLoc.Y})");

            if (confidence >= confidenceThreshold)
            {
                return ImageMatchResult.Success(
                    new Point(maxLoc.X, maxLoc.Y),
                    new Size(templateMat.Width, templateMat.Height),
                    confidence,
                    matchTime);
            }

            return ImageMatchResult.Failure($"Confidence {confidence:P} below threshold {confidenceThreshold:P} for {description}", matchTime);
        }
        catch (OperationCanceledException)
        {
            await _loggingService.LogInfoAsync($"[ImageProcessor] Template matching cancelled for {description}");
            throw;
        }
        catch (Exception ex)
        {
            var matchTime = (DateTime.Now - startTime).TotalMilliseconds;
            await _loggingService.LogErrorAsync($"[ImageProcessor] Template matching failed for {description}: {ex.Message}", ex);
            return ImageMatchResult.Failure($"Template matching exception: {ex.Message}", matchTime);
        }
    }
}