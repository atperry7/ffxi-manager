using System;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace FFXIManager.Services.AutoLogin.ScreenDetection;

/// <summary>
/// Abstraction layer for consistent image processing operations.
/// Ensures standardized validation, error handling, and logging across all image processing tasks.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// Validates that image data conforms to expected dimensions and format.
    /// </summary>
    /// <param name="imageData">Raw image data</param>
    /// <param name="width">Expected width</param>
    /// <param name="height">Expected height</param>
    /// <param name="channels">Expected channel count</param>
    /// <returns>True if valid, false otherwise</returns>
    bool ValidateImageData(byte[]? imageData, int width, int height, int channels);

    /// <summary>
    /// Creates an OpenCV Mat from validated image data with proper error handling.
    /// </summary>
    /// <param name="imageData">Raw image data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="channels">Channel count (3 for BGR, 4 for BGRA)</param>
    /// <param name="description">Description for logging purposes</param>
    /// <returns>Created Mat or empty Mat on failure</returns>
    Task<Mat> CreateMatFromImageDataAsync(byte[] imageData, int width, int height, int channels, string description);

    /// <summary>
    /// Converts BGRA Mat to BGR Mat with proper resource management.
    /// </summary>
    /// <param name="sourceMat">Source BGRA Mat</param>
    /// <param name="description">Description for logging</param>
    /// <returns>Converted BGR Mat</returns>
    Task<Mat> ConvertBgraToBlrAsync(Mat sourceMat, string description);

    /// <summary>
    /// Performs template matching with comprehensive error handling and logging.
    /// </summary>
    /// <param name="screenshotMat">Screenshot Mat</param>
    /// <param name="templateMat">Template Mat</param>
    /// <param name="confidenceThreshold">Minimum confidence threshold</param>
    /// <param name="description">Description for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Match result with confidence and location</returns>
    Task<ImageMatchResult> PerformTemplateMatchingAsync(
        Mat screenshotMat,
        Mat templateMat,
        float confidenceThreshold,
        string description,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of image matching operation with standardized information.
/// </summary>
public class ImageMatchResult
{
    public bool IsSuccess { get; set; }
    public float Confidence { get; set; }
    public Point Location { get; set; }
    public Size TemplateSize { get; set; }
    public double MatchTimeMs { get; set; }
    public string? ErrorMessage { get; set; }

    public static ImageMatchResult Success(Point location, Size templateSize, float confidence, double matchTimeMs)
        => new()
        {
            IsSuccess = true,
            Location = location,
            TemplateSize = templateSize,
            Confidence = confidence,
            MatchTimeMs = matchTimeMs
        };

    public static ImageMatchResult Failure(string errorMessage, double matchTimeMs = 0)
        => new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            MatchTimeMs = matchTimeMs
        };
}