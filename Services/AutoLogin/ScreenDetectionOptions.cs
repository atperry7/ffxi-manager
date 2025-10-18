using System;

namespace FFXIManager.Services.AutoLogin;

/// <summary>
/// Configuration options for screenshot detection operations.
/// Provides standardized timeout, interval, and confidence settings.
/// </summary>
public class ScreenDetectionOptions
{
    /// <summary>
    /// Maximum time to wait for screen detection before timing out.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Interval between detection attempts.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum confidence threshold for successful template matching.
    /// </summary>
    public float ConfidenceThreshold { get; set; } = 0.80f;

    /// <summary>
    /// Maximum number of detection attempts. If null, calculated from Timeout / CheckInterval.
    /// Use this to override timeout-based attempt calculation with workflow-specified value.
    /// </summary>
    public int? MaxAttempts { get; set; } = null;

    /// <summary>
    /// Default options: 30-second timeout, 1-second intervals, 80% confidence.
    /// Screenshot capture uses built-in retry logic (2 retries per capture).
    /// </summary>
    public static ScreenDetectionOptions Default => new();

    /// <summary>
    /// Quick detection for fast UI transitions: 10-second timeout.
    /// </summary>
    public static ScreenDetectionOptions Quick => new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Extended detection for slow systems: 60-second timeout.
    /// </summary>
    public static ScreenDetectionOptions Extended => new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    /// <summary>
    /// High confidence detection for critical elements: 90% confidence threshold.
    /// </summary>
    public static ScreenDetectionOptions HighConfidence => new()
    {
        ConfidenceThreshold = 0.90f
    };

    /// <summary>
    /// Creates options with custom timeout while keeping other defaults.
    /// </summary>
    /// <param name="timeoutSeconds">Timeout in seconds</param>
    /// <returns>Configured options</returns>
    public static ScreenDetectionOptions WithTimeout(int timeoutSeconds) => new()
    {
        Timeout = TimeSpan.FromSeconds(timeoutSeconds)
    };

    /// <summary>
    /// Creates options with custom confidence while keeping other defaults.
    /// </summary>
    /// <param name="confidence">Confidence threshold (0.0 to 1.0)</param>
    /// <returns>Configured options</returns>
    public static ScreenDetectionOptions WithConfidence(float confidence) => new()
    {
        ConfidenceThreshold = confidence
    };
}