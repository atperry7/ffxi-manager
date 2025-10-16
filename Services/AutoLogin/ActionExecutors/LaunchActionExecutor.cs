using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Models.Settings;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes "Launch" actions to start external applications.
    /// Handles process detection, UI readiness confirmation via template matching,
    /// and optional post-launch navigation.
    /// </summary>
    /// <remarks>
    /// **Launch Action Flow:**
    /// 1. Lookup application from ExternalApplicationData settings (by name)
    /// 2. Check if already running (with optional skip logic)
    /// 3. Launch application via ExternalApplicationService
    /// 4. Wait for process detection (WMI watchers provide auto-detection)
    /// 5. **TEMPLATE DETECTION**: Confirm UI is ready for interaction
    /// 6. Store process ID in context for later use
    ///
    /// **Parameters:**
    /// - ApplicationName (string, required): Name of app in settings
    /// - AllowSkipIfRunning (bool): Skip if already running (default: false)
    /// - AllowSkipIfNotConfigured (bool): Skip if not in settings (default: true)
    /// - RetryAttempts (int): Template detection retries (default: 30)
    /// - RetryDelayMs (int): Delay between retries (default: 500ms)
    /// - TemplatePath (string, optional): Template for UI readiness check
    /// - ConfidenceThreshold (float): Template match threshold (default: 0.8)
    /// </remarks>
    public class LaunchActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IScreenshotCaptureService _screenshotService;
        private readonly ITemplateMatchingService _templateService;
        private readonly IUIAutomationService _automationService;

        public override string ActionType => "Launch";

        public LaunchActionExecutor(
            ILoggingService loggingService,
            IExternalApplicationService externalApplicationService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            IUIAutomationService automationService)
            : base(loggingService)
        {
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _screenshotService = screenshotService ?? throw new ArgumentNullException(nameof(screenshotService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Extract parameters
            var applicationName = action.GetParameter<string>("ApplicationName", string.Empty);
            var allowSkipIfRunning = action.GetParameter<bool>("AllowSkipIfRunning", false);
            var allowSkipIfNotConfigured = action.GetParameter<bool>("AllowSkipIfNotConfigured", true);
            var retryAttempts = action.GetParameter<int>("RetryAttempts", 30);
            var retryDelayMs = action.GetParameter<int>("RetryDelayMs", 500);
            var templatePath = action.GetParameter<string>("TemplatePath", string.Empty);
            var confidenceThreshold = action.GetParameter<float>("ConfidenceThreshold", 0.8f);

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                await _loggingService.LogErrorAsync("[LAUNCH] ApplicationName parameter is required for Launch actions");
                return false;
            }

            await _loggingService.LogInfoAsync($"[LAUNCH] Starting {applicationName} launch sequence");

            // Phase 1: Find application from settings
            await UpdateProgressAsync(context, 10, $"Finding {applicationName}");

            var apps = await _externalApplicationService.GetApplicationsAsync();
            var app = apps.FirstOrDefault(a =>
                a.Name.Equals(applicationName, StringComparison.OrdinalIgnoreCase));

            if (app == null)
            {
                var message = $"{applicationName} not configured in External Applications settings";

                if (allowSkipIfNotConfigured)
                {
                    await _loggingService.LogInfoAsync($"[LAUNCH] {message} - skipping action");
                    if (context.Subtask != null)
                    {
                        context.Subtask.Skip(message);
                    }
                    return true; // Skipping is considered success
                }

                await _loggingService.LogErrorAsync($"[LAUNCH] {message} - action marked as required");
                throw new InvalidOperationException($"Required application not found: {applicationName}. Please configure it in External Applications settings.");
            }

            await _loggingService.LogInfoAsync($"[LAUNCH] Found application: {app.Name} at {app.ExecutablePath}");

            // Phase 2: Check if already running
            await UpdateProgressAsync(context, 20, $"Checking {app.Name} status");

            if (app.IsRunning)
            {
                await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} is already running (PIDs: {string.Join(", ", app.ProcessIds)})");

                if (allowSkipIfRunning)
                {
                    await _loggingService.LogInfoAsync($"[LAUNCH] Skipping launch - app already running");
                    if (context.Subtask != null)
                    {
                        context.Subtask.Skip($"{app.Name} already running");
                    }

                    // Store process ID in context for potential later use
                    var stepId = context.WorkflowStep?.StepId ?? "launch";
                    context.AutoLoginContext?.SetData($"{stepId}_ProcessId", app.ProcessIds.First());
                    context.AutoLoginContext?.SetData($"{stepId}_Skipped", true);
                    return true; // Skipping is considered success
                }

                await _loggingService.LogInfoAsync($"[LAUNCH] AllowSkipIfRunning=false - will relaunch");
            }

            // Phase 3: Launch application
            await UpdateProgressAsync(context, 30, $"Launching {app.Name}");

            var launched = await _externalApplicationService.LaunchApplicationAsync(app);

            if (!launched)
            {
                var message = $"Failed to launch {app.Name}. Check executable path and permissions.";
                await _loggingService.LogErrorAsync($"[LAUNCH] {message}");
                throw new InvalidOperationException(message);
            }

            await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} launch initiated successfully");

            // Phase 4: Wait for process detection
            await UpdateProgressAsync(context, 50, $"Waiting for {app.Name} process");

            var timeoutEnd = DateTime.UtcNow.AddSeconds(30);
            var processDetected = false;

            while (DateTime.UtcNow < timeoutEnd && !cancellationToken.IsCancellationRequested)
            {
                // Refresh application status
                await _externalApplicationService.RefreshApplicationStatusAsync(app);

                if (app.IsRunning)
                {
                    processDetected = true;
                    break;
                }

                await Task.Delay(500, cancellationToken);
            }

            if (!processDetected)
            {
                var message = $"{app.Name} process did not start within 30 seconds";
                await _loggingService.LogErrorAsync($"[LAUNCH] {message}");
                throw new TimeoutException(message);
            }

            var processId = app.ProcessIds.First();
            await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} process detected (PID: {processId})");

            // Store process ID in context for later use
            var stepIdForContext = context.WorkflowStep?.StepId ?? "launch";
            context.AutoLoginContext?.SetData($"{stepIdForContext}_ProcessId", processId);

            // Phase 5: TEMPLATE DETECTION - wait for app UI to be READY
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                await UpdateProgressAsync(context, 70, $"Waiting for {app.Name} UI");

                var templateMatch = await WaitForUIReadinessAsync(
                    app,
                    templatePath,
                    confidenceThreshold,
                    retryAttempts,
                    retryDelayMs,
                    cancellationToken);

                if (templateMatch != null)
                {
                    // Store template match in context for potential navigation use
                    context.TemplateMatch = templateMatch;
                }
            }
            else
            {
                await _loggingService.LogWarningAsync($"[LAUNCH] No template path configured for {applicationName} - skipping UI readiness check");
            }

            // Phase 6: Complete
            await UpdateProgressAsync(context, 100, $"{app.Name} ready");
            await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} launch sequence completed successfully");

            return true;
        }

        /// <summary>
        /// Waits for application UI to be ready using template detection
        /// </summary>
        private async Task<TemplateMatchResult?> WaitForUIReadinessAsync(
            ExternalApplication app,
            string templatePath,
            float confidenceThreshold,
            int retryAttempts,
            int retryDelayMs,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[LAUNCH] Detecting UI readiness using template: {templatePath}");
            await _loggingService.LogDebugAsync($"[LAUNCH] Template detection config: {retryAttempts} attempts x {retryDelayMs}ms");

            TemplateMatchResult? capturedTemplateMatch = null;

            // Simple retry loop for template detection
            bool detected = false;
            for (int attempt = 1; attempt <= retryAttempts && !detected; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Get window handle for template detection
                    var windowHandle = IntPtr.Zero;
                    try
                    {
                        var processName = System.IO.Path.GetFileNameWithoutExtension(app.ExecutablePath);
                        windowHandle = await FindWindowHandleAsync(app, cancellationToken, maxAttempts: 3);
                    }
                    catch
                    {
                        // Window not ready yet, will retry
                        continue;
                    }

                    // Try to detect the template
                    var screenshot = await _screenshotService.CaptureWindowAsync(windowHandle, cancellationToken);
                    if (screenshot != null)
                    {
                        var templateMatch = await _templateService.FindElementAsync(screenshot, templatePath, cancellationToken);

                        if (templateMatch != null && templateMatch.Confidence >= confidenceThreshold)
                        {
                            detected = true;
                            capturedTemplateMatch = templateMatch;
                            await _loggingService.LogInfoAsync($"[LAUNCH] UI ready - template detected on attempt {attempt}/{retryAttempts} (confidence: {templateMatch.Confidence:P}, threshold: {confidenceThreshold:P})");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogDebugAsync($"[LAUNCH] Template detection attempt {attempt}/{retryAttempts} failed: {ex.Message}");
                }

                if (attempt < retryAttempts)
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
            }

            if (!detected)
            {
                var message = $"{app.Name} UI not ready - template '{templatePath}' not detected after {retryAttempts} attempts";
                await _loggingService.LogErrorAsync($"[LAUNCH] {message}");
                throw new TimeoutException(message);
            }

            return capturedTemplateMatch;
        }

        /// <summary>
        /// Finds window handle for the launched application
        /// </summary>
        private async Task<IntPtr> FindWindowHandleAsync(
            ExternalApplication app,
            CancellationToken cancellationToken,
            int maxAttempts = 10)
        {
            var processName = System.IO.Path.GetFileNameWithoutExtension(app.ExecutablePath);

            for (int i = 0; i < maxAttempts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Try to find window by process name using Process class
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                        {
                            var handle = process.MainWindowHandle;
                            process.Dispose();
                            return handle;
                        }
                    }
                    finally
                    {
                        process?.Dispose();
                    }
                }

                await Task.Delay(500, cancellationToken);
            }

            throw new InvalidOperationException($"Could not find window handle for {app.Name} after {maxAttempts} attempts");
        }
    }
}
