using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base class for login task handlers providing common functionality and patterns.
    /// Implements the developer guidelines for consistent handler behavior.
    /// </summary>
    public abstract class BaseLoginTaskHandler : ILoginTaskHandler
    {
        protected readonly ILoggingService _loggingService;
        protected readonly IScreenshotCaptureService _screenshotService;
        protected readonly ITemplateMatchingService _templateService;
        protected readonly ITemplateManagementService _templateManagementService;

        protected BaseLoginTaskHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _templateManagementService = templateManagementService ?? throw new ArgumentNullException(nameof(templateManagementService));
        }

        public abstract LoginTaskStep TaskStep { get; }
        public abstract bool CanHandle(AutoLoginSubtask subtask);

        public async Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"Starting execution of {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                // Validate inputs
                ValidateInputs(subtask, queueItem);

                // Execute the handler-specific logic with common patterns applied
                await ExecuteHandlerLogicAsync(subtask, queueItem, context, cancellationToken);

                await _loggingService.LogDebugAsync($"Successfully completed {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"Execution of {subtask.TaskStep} was cancelled for {queueItem.DisplayName}");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to execute {subtask.TaskStep} for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        /// <summary>
        /// Implement the specific handler logic in derived classes.
        /// This method will be called with validated inputs and proper error handling in place.
        /// </summary>
        protected abstract Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Validates common inputs. Override in derived classes for additional validation.
        /// </summary>
        protected virtual void ValidateInputs(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
        {
            if (subtask == null)
                throw new ArgumentNullException(nameof(subtask));

            if (queueItem == null)
                throw new ArgumentNullException(nameof(queueItem));

            if (queueItem.Account == null)
                throw new InvalidOperationException($"Account information is required for {subtask.TaskStep}");
        }

        /// <summary>
        /// Executes an operation with retry logic and exponential backoff.
        /// Follows the patterns outlined in the implementation guide.
        /// </summary>
        protected async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string operationName,
            int maxRetries = 3,
            int baseDelayMs = 500,
            CancellationToken cancellationToken = default)
        {
            var delay = TimeSpan.FromMilliseconds(baseDelayMs);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await _loggingService.LogDebugAsync($"Attempting {operationName} (attempt {attempt}/{maxRetries})");
                    return await operation(cancellationToken);
                }
                catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
                {
                    await _loggingService.LogWarningAsync($"{operationName} attempt {attempt} failed, retrying in {delay.TotalMilliseconds}ms: {ex.Message}");
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 1.5); // Exponential backoff
                }
            }

            // This will never be reached due to the exception handling above
            throw new InvalidOperationException($"Retry logic failed unexpectedly for {operationName}");
        }

        /// <summary>
        /// Determines if an exception is retryable. Override in derived classes for specific retry logic.
        /// </summary>
        protected virtual bool IsRetryableException(Exception ex)
        {
            // Common retryable exceptions
            return ex is TimeoutException ||
                   ex is InvalidOperationException ||
                   (ex is System.ComponentModel.Win32Exception win32Ex && IsRetryableWin32Error(win32Ex.NativeErrorCode));
        }

        /// <summary>
        /// Determines if a Win32 error code indicates a retryable condition.
        /// </summary>
        protected virtual bool IsRetryableWin32Error(int errorCode)
        {
            // Common retryable Win32 error codes
            return errorCode == 5 ||    // Access denied (might be temporary)
                   errorCode == 1400 || // Invalid window handle (window might not be ready)
                   errorCode == 1401;   // Invalid menu handle
        }

        /// <summary>
        /// Waits for a condition to be met with timeout and progress reporting.
        /// Respects pause state and cancellation tokens as per guidelines.
        /// </summary>
        protected async Task WaitForConditionAsync(
            Func<Task<bool>> condition,
            string conditionDescription,
            AutoLoginSubtask subtask,
            AutoLoginTask task,
            TimeSpan timeout,
            int progressStart = 0,
            int progressEnd = 100,
            CancellationToken cancellationToken = default)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            var checkInterval = TimeSpan.FromMilliseconds(200);
            var lastProgressUpdate = DateTime.UtcNow;
            var progressUpdateInterval = TimeSpan.FromSeconds(1);

            while (DateTime.UtcNow < endTime)
            {
                // Respect pause state as per guidelines
                while (task?.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Check the condition
                if (await condition())
                {
                    subtask?.UpdateProgress(progressEnd, $"{conditionDescription} - condition met");
                    return;
                }

                // Update progress periodically
                if (DateTime.UtcNow - lastProgressUpdate > progressUpdateInterval)
                {
                    var elapsed = DateTime.UtcNow - (endTime - timeout);
                    var progressPercent = (int)(progressStart + (progressEnd - progressStart) * (elapsed.TotalMilliseconds / timeout.TotalMilliseconds));
                    subtask?.UpdateProgress(Math.Min(progressPercent, progressEnd - 1), $"{conditionDescription} - waiting...");
                    lastProgressUpdate = DateTime.UtcNow;
                }

                await Task.Delay(checkInterval, cancellationToken);
            }

            throw new TimeoutException($"Timeout waiting for condition: {conditionDescription}");
        }

        /// <summary>
        /// Updates progress with consistent logging.
        /// </summary>
        protected async Task UpdateProgressAsync(AutoLoginSubtask subtask, int progress, string message)
        {
            subtask?.UpdateProgress(progress, message);
            await _loggingService.LogDebugAsync($"Progress {progress}%: {message}");
        }

        /// <summary>
        /// Validates that required account properties are available.
        /// Throws meaningful exceptions when properties are missing.
        /// </summary>
        protected void ValidateAccountProperty(object value, string propertyName, string taskDescription)
        {
            if (value == null || (value is string str && string.IsNullOrEmpty(str)))
            {
                throw new InvalidOperationException($"{propertyName} is required for {taskDescription}. Please ensure the account is properly configured.");
            }
        }

        /// <summary>
        /// Creates a timeout cancellation token source that respects the provided cancellation token.
        /// </summary>
        protected CancellationTokenSource CreateTimeoutCancellationTokenSource(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timeoutCts = new CancellationTokenSource(timeout);
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        }

        /// <summary>
        /// Logs sensitive operations without exposing sensitive data.
        /// </summary>
        protected async Task LogSecureOperationAsync(string operation, string accountIdentifier)
        {
            await _loggingService.LogDebugAsync($"{operation} for account {accountIdentifier} (sensitive data masked)");
        }

        /// <summary>
        /// Standardized screen detection method using ScreenDetectionOptions configuration.
        /// This is the PREFERRED method for all screen detection operations across all handlers.
        /// </summary>
        protected async Task<TemplateMatchResult> WaitForScreenDetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            string screenDescription,
            CancellationToken cancellationToken,
            ScreenDetectionOptions options = null)
        {
            options ??= ScreenDetectionOptions.Default;

            var maxAttempts = (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);

            await _loggingService.LogInfoAsync($"Waiting for {screenDescription} (max {options.Timeout.TotalSeconds}s, checking every {options.CheckInterval.TotalSeconds}s)");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Update progress based on attempt
                var progress = Math.Min(95, (attempt * 100) / maxAttempts);
                subtask.UpdateProgress(progress, $"Detecting {screenDescription} ({attempt}/{maxAttempts})...");

                var screenshot = await CaptureScreenshotWithLogging(windowHandle, screenDescription, cancellationToken);
                var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence {match.Confidence:P}");

                if (match.Confidence >= options.ConfidenceThreshold)
                {
                    subtask.UpdateProgress(100, $"{screenDescription} detected successfully");
                    await _loggingService.LogInfoAsync($"{screenDescription} detected after {attempt} attempts (confidence: {match.Confidence:P})");
                    return match;
                }

                // Enhanced diagnostic logging for low confidence
                if (match.Confidence < 0.10f)
                {
                    await _loggingService.LogWarningAsync($"{screenDescription} very low confidence ({match.Confidence:P}) - possible template mismatch or screen state issue");
                }
                else if (match.Confidence >= 0.60f)
                {
                    await _loggingService.LogDebugAsync($"{screenDescription} partially detected (confidence: {match.Confidence:P}) - getting close");
                }
                else if (match.Confidence >= 0.30f)
                {
                    await _loggingService.LogDebugAsync($"{screenDescription} moderate confidence ({match.Confidence:P}) - template may be partially visible");
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            // Final attempt for diagnosis
            var finalScreenshot = await CaptureScreenshotWithLogging(windowHandle, $"final {screenDescription}", cancellationToken);
            var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);

            await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Final confidence: {finalMatch.Confidence:P}");

            throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (final confidence: {finalMatch.Confidence:P})");
        }

        /// <summary>
        /// Finds window handle for a process with standardized retry logic and progress reporting.
        /// </summary>
        protected async Task<IntPtr> FindWindowHandleAsync(
            AutoLoginSubtask subtask,
            string processName,
            string windowTitleFilter,
            CancellationToken cancellationToken,
            int maxAttempts = 15)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = Math.Min(90, (attempt * 100) / maxAttempts);
                subtask.UpdateProgress(progress, $"Finding {processName} window ({attempt}/{maxAttempts})...");

                var processes = Process.GetProcessesByName(processName);

                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited || process.MainWindowHandle == IntPtr.Zero)
                            continue;

                        var windowTitle = process.MainWindowTitle;
                        if (string.IsNullOrEmpty(windowTitleFilter) ||
                            windowTitle.Contains(windowTitleFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            subtask.UpdateProgress(100, $"Found {processName} window: {windowTitle}");
                            await _loggingService.LogInfoAsync($"Found {processName} window after {attempt} attempts - Handle: 0x{process.MainWindowHandle.ToInt64():X}, Title: '{windowTitle}', PID: {process.Id}");
                            return process.MainWindowHandle;
                        }
                    }
                    finally
                    {
                        process?.Dispose();
                    }
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }

            throw new InvalidOperationException($"No {processName} window found after {maxAttempts} attempts");
        }

        /// <summary>
        /// Safely clicks at window-relative coordinates with validation and error handling.
        /// </summary>
        protected async Task ClickAtCoordinatesAsync(
            AutoLoginSubtask subtask,
            Point windowRelativePoint,
            IntPtr windowHandle,
            string description,
            CancellationToken cancellationToken,
            IUIAutomationService automationService = null)
        {
            if (automationService == null)
                throw new ArgumentNullException(nameof(automationService), "IUIAutomationService must be provided for coordinate clicking");

            subtask.UpdateProgress(50, $"Preparing to click {description}...");

            var screenshot = await CaptureScreenshotWithLogging(windowHandle, description, cancellationToken);
            var screenPoint = screenshot.ToScreenCoordinates(windowRelativePoint);

            // Validate coordinates are within reasonable screen bounds
            if (screenPoint.X < 0 || screenPoint.Y < 0 || screenPoint.X > 3840 || screenPoint.Y > 2160)
            {
                throw new InvalidOperationException($"Invalid click coordinates for {description}: {screenPoint}");
            }

            await _loggingService.LogDebugAsync($"Clicking {description} at window-relative {windowRelativePoint}, screen coordinates {screenPoint}");

            subtask.UpdateProgress(75, $"Clicking {description}...");

            // Move mouse first for visual feedback
            await automationService.MoveMouseAsync(screenPoint, cancellationToken);
            await Task.Delay(200, cancellationToken);

            // Perform click
            await automationService.ClickAsync(screenPoint, cancellationToken);

            subtask.UpdateProgress(100, $"{description} clicked successfully");
            await _loggingService.LogDebugAsync($"{description} clicked at {screenPoint}");
        }

        /// <summary>
        /// Legacy method - DEPRECATED: Use WaitForScreenDetectionAsync with ScreenDetectionOptions instead.
        /// This method will be removed in a future version.
        /// </summary>
        [Obsolete("Use WaitForScreenDetectionAsync with ScreenDetectionOptions instead")]
        protected async Task<TemplateMatchResult> WaitForScreenDetection(
            string templatePath,
            IntPtr windowHandle,
            string screenDescription,
            CancellationToken cancellationToken,
            int timeoutSeconds = 30,
            float confidenceThreshold = 0.80f)
        {
            var attemptCount = 0;
            var maxAttempts = timeoutSeconds;

            await _loggingService.LogInfoAsync($"Waiting for {screenDescription} (max {timeoutSeconds}s, checking every 1s)");
            await _loggingService.LogDebugAsync($"Window handle: 0x{windowHandle.ToInt64():X}, Template path: {templatePath}");

            while (attemptCount < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attemptCount++;

                try
                {
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                    if (screenshot != null && screenshot.IsValid)
                    {
                        await _loggingService.LogDebugAsync($"Screenshot captured successfully: {screenshot.Width}x{screenshot.Height}, Window: '{screenshot.WindowTitle}'");

                        var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                        await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attemptCount}/{maxAttempts}: confidence {match.Confidence:P}");

                        // Success case
                        if (match.Confidence >= confidenceThreshold)
                        {
                            await _loggingService.LogInfoAsync($"{screenDescription} detected successfully after {attemptCount} attempts (confidence: {match.Confidence:P})");
                            return match;
                        }

                        // Progress indicator - show when we're getting close
                        if (match.Confidence >= 0.60f)
                        {
                            await _loggingService.LogDebugAsync($"{screenDescription} partially detected (confidence: {match.Confidence:P}), continuing to wait...");
                        }
                    }
                    else
                    {
                        var errorDetails = screenshot == null ? "Screenshot is null" : $"Screenshot invalid (IsValid: {screenshot.IsValid})";
                        await _loggingService.LogWarningAsync($"Screenshot capture failed on attempt {attemptCount}/{maxAttempts}: {errorDetails}");
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Exception during screenshot capture on attempt {attemptCount}/{maxAttempts}", ex);
                }

                // Wait 1 second before next attempt (don't wait after last attempt)
                if (attemptCount < maxAttempts)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }

            // Final attempt - capture what we have for final diagnosis
            try
            {
                var finalScreenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                if (finalScreenshot != null && finalScreenshot.IsValid)
                {
                    var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);
                    await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {timeoutSeconds}s. Final confidence: {finalMatch.Confidence:P}");
                    return finalMatch;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Exception during final screenshot capture", ex);
            }

            // Complete failure case
            await _loggingService.LogErrorAsync($"Failed to detect {screenDescription} after {timeoutSeconds} seconds - unable to capture final screenshot");
            throw new InvalidOperationException($"Failed to detect {screenDescription} after {timeoutSeconds} seconds of waiting");
        }

        /// <summary>
        /// Enhanced single-shot screenshot capture with detailed logging and retry capability.
        /// Use this for immediate screenshot needs (before actions) rather than WaitForScreenDetection.
        /// </summary>
        protected async Task<WindowScreenshot> CaptureScreenshotWithLogging(
            IntPtr windowHandle,
            string purpose,
            CancellationToken cancellationToken,
            int retryCount = 2)
        {
            for (int attempt = 1; attempt <= retryCount + 1; attempt++)
            {
                try
                {
                    await _loggingService.LogDebugAsync($"Capturing screenshot for {purpose} (attempt {attempt}/{retryCount + 1})");

                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);

                    if (screenshot != null && screenshot.IsValid)
                    {
                        await _loggingService.LogDebugAsync($"Screenshot captured successfully for {purpose}: {screenshot.Width}x{screenshot.Height}, Window: '{screenshot.WindowTitle}', WindowPos: ({screenshot.WindowBounds.X},{screenshot.WindowBounds.Y})");
                        return screenshot;
                    }

                    var errorDetails = screenshot == null ? "Screenshot is null" : $"Screenshot invalid (IsValid: {screenshot.IsValid})";
                    await _loggingService.LogWarningAsync($"Screenshot capture failed for {purpose} on attempt {attempt}: {errorDetails}");

                    if (attempt <= retryCount)
                    {
                        await Task.Delay(500, cancellationToken); // Brief delay before retry
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Exception during screenshot capture for {purpose} on attempt {attempt}", ex);

                    if (attempt <= retryCount)
                    {
                        await Task.Delay(500, cancellationToken); // Brief delay before retry
                    }
                }
            }

            await _loggingService.LogErrorAsync($"Failed to capture screenshot for {purpose} after {retryCount + 1} attempts");
            throw new InvalidOperationException($"Failed to capture screenshot for {purpose} after {retryCount + 1} attempts");
        }

        /// <summary>
        /// Clicks at coordinates defined in a template's JSON metadata.
        /// </summary>
        protected async Task ClickAtTemplateCoordinatesAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            CancellationToken cancellationToken,
            IUIAutomationService automationService = null)
        {
            if (automationService == null)
                throw new ArgumentNullException(nameof(automationService), "IUIAutomationService must be provided for template-based clicking");

            subtask.UpdateProgress(10, $"Loading template metadata for {templatePath}...");

            // Load template metadata to get click coordinates
            var metadata = await _templateManagementService.GetTemplateMetadataAsync(templatePath);
            if (metadata == null)
            {
                throw new InvalidOperationException($"Template metadata not found: {templatePath}");
            }

            var clickPoint = new Point(metadata.Action.ClickOffset.X, metadata.Action.ClickOffset.Y);
            var description = metadata.Action.Parameters.TryGetValue("description", out var desc) ? desc.ToString() : metadata.Name;

            subtask.UpdateProgress(30, $"Using template coordinates: {clickPoint}");

            // Use the standard coordinate-based click method
            await ClickAtCoordinatesAsync(
                subtask,
                clickPoint,
                windowHandle,
                description ?? "template-based button",
                cancellationToken,
                automationService);
        }
    }
}