using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ScreenDetection
{
    public class ScreenDetectionCoordinator : IScreenDetectionCoordinator
    {
        private readonly ILoggingService _loggingService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;
        private readonly IProcessUtilityService _processUtility;

        public ScreenDetectionCoordinator(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            IProcessUtilityService processUtility)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _processUtility = processUtility ?? throw new ArgumentNullException(nameof(processUtility));
        }

        public async Task<TemplateMatchResult> WaitForScreenDetectionAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr windowHandle,
            string screenDescription,
            float confidenceThreshold,
            int tolerance,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null,
            bool completeOnDetection = true)
        {
            options ??= ScreenDetectionOptions.Default;

            await _loggingService.LogInfoAsync($"Using workflow-defined confidence threshold: {confidenceThreshold:P} for {screenDescription}");
            var maxAttempts = options.MaxAttempts ?? (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var startTime = DateTime.UtcNow;

            await _loggingService.LogInfoAsync($"Starting {screenDescription} detection - MaxAttempts: {maxAttempts}, Interval: {options.CheckInterval.TotalMilliseconds}ms, Timeout: {options.Timeout.TotalSeconds}s");

            var phase = GetDetectionPhase(screenDescription);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = Math.Min(95, (attempt * 100) / maxAttempts);
                var elapsed = DateTime.UtcNow - startTime;
                var userFriendlyMessage = CreateContextualProgressMessage($"Waiting for {GetUserFriendlyScreenName(screenDescription)}", attempt, maxAttempts, elapsed);
                subtask.UpdateProgressWithPhase(phase, progress, userFriendlyMessage);

                WindowScreenshot? screenshot = null;
                try
                {
                    screenshot = await CaptureScreenshotWithLogging(windowHandle, screenDescription, cancellationToken, retryCount: 0);
                }
                catch (InvalidOperationException ex)
                {
                    await _loggingService.LogDebugAsync($"{screenDescription} screenshot failed on attempt {attempt}/{maxAttempts}: {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(options.CheckInterval, cancellationToken);
                    }
                    continue;
                }

                var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);
                await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence={match.Confidence:P}, threshold={confidenceThreshold:P}");

                if (match.Confidence >= confidenceThreshold)
                {
                    if (completeOnDetection)
                    {
                        subtask.UpdateProgressWithPhase(phase, 100, "Connection established");
                    }
                    await _loggingService.LogInfoAsync($"{screenDescription} detected after {attempt} attempts (confidence: {match.Confidence:P})");
                    return match;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            // Final attempt diagnostics
            try
            {
                var finalScreenshot = await CaptureScreenshotWithLogging(windowHandle, $"final {screenDescription}", cancellationToken, retryCount: 0);
                var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);
                await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Final confidence: {finalMatch.Confidence:P}");
                throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (final confidence: {finalMatch.Confidence:P})");
            }
            catch (InvalidOperationException)
            {
                await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Window no longer available for final diagnostic screenshot.");
                throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (window became unavailable)");
            }
        }

        public async Task<TemplateMatchResult> WaitForScreenDetectionWithHandleRefreshAsync(
            AutoLoginSubtask subtask,
            string templatePath,
            IntPtr initialWindowHandle,
            Func<CancellationToken, Task<IntPtr>> refreshHandleAsync,
            string screenDescription,
            float confidenceThreshold,
            int tolerance,
            CancellationToken cancellationToken,
            ScreenDetectionOptions? options = null,
            bool completeOnDetection = true)
        {
            options ??= ScreenDetectionOptions.Default;

            await _loggingService.LogInfoAsync($"Using workflow-defined confidence threshold: {confidenceThreshold:P} for {screenDescription}");
            var maxAttempts = options.MaxAttempts ?? (int)(options.Timeout.TotalSeconds / options.CheckInterval.TotalSeconds);
            var startTime = DateTime.UtcNow;
            var phase = GetDetectionPhase(screenDescription);
            var currentHandle = initialWindowHandle;

            await _loggingService.LogInfoAsync($"Starting {screenDescription} detection (with handle refresh) - MaxAttempts: {maxAttempts}, Interval: {options.CheckInterval.TotalMilliseconds}ms, Timeout: {options.Timeout.TotalSeconds}s");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = Math.Min(95, (attempt * 100) / maxAttempts);
                var elapsed = DateTime.UtcNow - startTime;
                var userFriendlyMessage = CreateContextualProgressMessage($"Waiting for {GetUserFriendlyScreenName(screenDescription)}", attempt, maxAttempts, elapsed);
                subtask.UpdateProgressWithPhase(phase, progress, userFriendlyMessage);

                // Ensure handle is valid, refresh if not
                var handleValid = await IsHandleValidAsync(currentHandle);
                if (!handleValid)
                {
                    IntPtr refreshed = IntPtr.Zero;
                    try
                    {
                        refreshed = await refreshHandleAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await _loggingService.LogDebugAsync($"Handle refresh callback threw: {ex.Message}");
                    }

                    if (refreshed != IntPtr.Zero && refreshed != currentHandle)
                    {
                        await _loggingService.LogInfoAsync($"Refreshed window handle for {screenDescription}: 0x{currentHandle.ToInt64():X} → 0x{refreshed.ToInt64():X}");
                        currentHandle = refreshed;
                    }
                    else if (attempt < maxAttempts)
                    {
                        await Task.Delay(options.CheckInterval, cancellationToken);
                        continue;
                    }
                }

                WindowScreenshot? screenshot = null;
                try
                {
                    screenshot = await CaptureScreenshotWithLogging(currentHandle, screenDescription, cancellationToken, retryCount: 0);
                }
                catch (InvalidOperationException ex)
                {
                    await _loggingService.LogDebugAsync($"{screenDescription} screenshot failed on attempt {attempt}/{maxAttempts}: {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(options.CheckInterval, cancellationToken);
                    }
                    continue;
                }

                var match = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);
                await _loggingService.LogDebugAsync($"{screenDescription} detection attempt {attempt}/{maxAttempts}: confidence={match.Confidence:P}, threshold={confidenceThreshold:P}");

                if (match.Confidence >= confidenceThreshold)
                {
                    if (completeOnDetection)
                    {
                        subtask.UpdateProgressWithPhase(phase, 100, "Connection established");
                    }
                    await _loggingService.LogInfoAsync($"{screenDescription} detected after {attempt} attempts (confidence: {match.Confidence:P})");
                    return match;
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(options.CheckInterval, cancellationToken);
                }
            }

            // Final diagnostic attempt
            try
            {
                var finalScreenshot = await CaptureScreenshotWithLogging(currentHandle, $"final {screenDescription}", cancellationToken, retryCount: 0);
                var finalMatch = await _templateService.FindElementAsync(finalScreenshot, templatePath, cancellationToken);
                await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Final confidence: {finalMatch.Confidence:P}");
                throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (final confidence: {finalMatch.Confidence:P})");
            }
            catch (InvalidOperationException)
            {
                await _loggingService.LogWarningAsync($"{screenDescription} detection timed out after {options.Timeout.TotalSeconds}s. Window no longer available for final diagnostic screenshot.");
                throw new TimeoutException($"Failed to detect {screenDescription} after {options.Timeout.TotalSeconds}s (window became unavailable)");
            }
        }

        public async Task<WindowScreenshot> CaptureScreenshotWithLogging(
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
                        await Task.Delay(500, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Exception during screenshot capture for {purpose} on attempt {attempt}", ex);

                    if (attempt <= retryCount)
                    {
                        await Task.Delay(500, cancellationToken);
                    }
                }
            }

            await _loggingService.LogErrorAsync($"Failed to capture screenshot for {purpose} after {retryCount + 1} attempts");
            throw new InvalidOperationException($"Failed to capture screenshot for {purpose} after {retryCount + 1} attempts");
        }

        private async Task<bool> IsHandleValidAsync(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            var valid = _processUtility.IsWindowValid(hWnd);
            // Log similarly to prior pattern
            await _loggingService.LogDebugAsync($"Window validation - Handle: 0x{hWnd.ToInt64():X}, IsValid: {valid}");
            if (!valid)
            {
                await _loggingService.LogWarningAsync($"Window validation failed - Handle: 0x{hWnd.ToInt64():X}, IsValid: {valid}");
            }
            return valid;
        }

        private string GetDetectionPhase(string screenDescription)
        {
            var desc = screenDescription.ToLowerInvariant();
            if (desc.Contains("member") || desc.Contains("selection")) return "authentication";
            if (desc.Contains("login") || desc.Contains("password")) return "authentication";
            if (desc.Contains("game") || desc.Contains("world")) return "gameconnection";
            if (desc.Contains("windower")) return "windower";
            if (desc.Contains("pol") || desc.Contains("playonline")) return "startup";
            return "startup";
        }

        private string GetUserFriendlyScreenName(string screenDescription)
        {
            var desc = screenDescription.ToLowerInvariant();
            if (desc.Contains("member selection")) return "PlayOnline to load";
            if (desc.Contains("login screen")) return "login screen";
            if (desc.Contains("password")) return "password prompt";
            if (desc.Contains("game world")) return "game world";
            if (desc.Contains("character")) return "character selection";
            if (desc.Contains("windower")) return "Windower to start";
            return "game interface";
        }

        private string CreateContextualProgressMessage(string operation, int attempt, int maxAttempts, TimeSpan elapsed)
        {
            var baseMessage = $"{operation}...";
            if (elapsed.TotalSeconds > 15)
                baseMessage += " (This may take a moment)";
            else if (elapsed.TotalSeconds > 30)
                baseMessage += " (Please wait, this can take up to 2 minutes)";

            if (maxAttempts > 1 && attempt > 1)
                baseMessage += $" [Attempt {attempt}]";

            return baseMessage;
        }
    }
}
