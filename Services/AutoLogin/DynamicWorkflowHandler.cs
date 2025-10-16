using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Dynamic handler that executes workflow steps based on WorkflowStepDefinition.
    /// This handler enables user-customizable login flows without code changes.
    /// Acts as a fallback when no specialized handler claims a subtask.
    /// </summary>
    /// <remarks>
    /// **Key Features:**
    /// - Template-driven screen detection and navigation
    /// - Supports custom navigation overrides per step
    /// - Resolution/DPI independent (uses hybrid navigation)
    /// - Conditional execution support
    /// - Retry logic built-in
    ///
    /// **Design Philosophy:**
    /// This handler bridges the gap between hardcoded handlers and fully data-driven workflows.
    /// It allows users to define new steps or customize existing ones through JSON configuration
    /// without modifying code.
    /// </remarks>
    public class DynamicWorkflowHandler : BaseLoginTaskHandler
    {
        private readonly IUIAutomationService _automationService;
        private readonly IExternalApplicationService _externalApplicationService;

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IExternalApplicationService externalApplicationService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
        }

        /// <summary>
        /// This handler doesn't claim any specific LoginTaskStep - it's a dynamic fallback.
        /// </summary>
        public override LoginTaskStep TaskStep => LoginTaskStep.None;

        /// <summary>
        /// Can handle any subtask that has a WorkflowStepDefinition.
        /// This allows the handler to execute user-defined workflow steps.
        /// </summary>
        public override bool CanHandle(AutoLoginSubtask subtask)
        {
            // Check if subtask has workflow step definition
            return subtask?.WorkflowStep != null;
        }

        /// <summary>
        /// Executes a workflow step dynamically based on its definition.
        /// Routes to appropriate handler based on StepType.
        /// </summary>
        protected override async Task ExecuteHandlerLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var stepDef = subtask.WorkflowStep!;

            await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Executing step: {stepDef.DisplayName} (Type: {stepDef.StepType}, StepId: {stepDef.StepId})");

            // Route based on step type
            switch (stepDef.StepType)
            {
                case "LaunchApplication":
                    await ExecuteLaunchApplicationStepAsync(subtask, queueItem, context, cancellationToken);
                    break;

                case "NavigateUI":
                default:
                    await ExecuteNavigationStepAsync(subtask, queueItem, context, cancellationToken);
                    break;
            }

            await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Completed step: {stepDef.DisplayName}");
        }

        /// <summary>
        /// Executes a UI navigation workflow step.
        /// This is the standard detection + navigation flow.
        /// </summary>
        private async Task ExecuteNavigationStepAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var stepDef = subtask.WorkflowStep!;

            // Phase 1: Validate step definition
            await UpdateProgressWithPhaseAsync(subtask, "startup", 5, "Validating workflow step");
            ValidateWorkflowStep(stepDef);

            // Phase 2: Get window handle from context or discover it
            var windowHandle = await GetOrDiscoverWindowHandleAsync(subtask, queueItem, context, cancellationToken);

            // Phase 3: Detect screen using template
            await UpdateProgressWithPhaseAsync(subtask, "authentication", 20, $"Looking for {stepDef.DisplayName}");
            var templateMatch = await DetectScreenAsync(subtask, stepDef, windowHandle, cancellationToken);

            // Phase 4: Execute navigation
            await UpdateProgressWithPhaseAsync(subtask, "authentication", 60, $"Navigating {stepDef.DisplayName}");
            await ExecuteNavigationAsync(subtask, stepDef, windowHandle, templateMatch, cancellationToken);

            // Phase 5: Post-navigation delay (if configured)
            if (stepDef.EstimatedDurationSeconds > 0)
            {
                await UpdateProgressWithPhaseAsync(subtask, "authentication", 90, "Waiting for screen transition");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(stepDef.EstimatedDurationSeconds, 3)), cancellationToken);
            }

            await UpdateProgressWithPhaseAsync(subtask, "authentication", 100, $"{stepDef.DisplayName} completed");
        }

        /// <summary>
        /// Executes a generic application launch workflow step.
        /// Leverages ExternalApplicationService + UnifiedMonitoringService for detection,
        /// then uses template matching to confirm app is ready for interaction.
        /// </summary>
        private async Task ExecuteLaunchApplicationStepAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var stepDef = subtask.WorkflowStep!;

            await _loggingService.LogInfoAsync($"[APP-LAUNCH] Starting {stepDef.ApplicationName} launch sequence");

            // Phase 1: Find application from settings (simple name lookup)
            await UpdateProgressWithPhaseAsync(subtask, "launch", 10, $"Finding {stepDef.ApplicationName}");

            var apps = await _externalApplicationService.GetApplicationsAsync();
            var app = apps.FirstOrDefault(a =>
                a.Name.Equals(stepDef.ApplicationName, StringComparison.OrdinalIgnoreCase));

            if (app == null)
            {
                var message = $"{stepDef.ApplicationName} not configured in External Applications settings";

                if (stepDef.AllowSkipIfNotConfigured)
                {
                    await _loggingService.LogInfoAsync($"[APP-LAUNCH] {message} - skipping step");
                    subtask.Skip(message);
                    return;
                }

                await _loggingService.LogErrorAsync($"[APP-LAUNCH] {message} - step marked as required");
                throw new InvalidOperationException($"Required application not found: {stepDef.ApplicationName}. Please configure it in External Applications settings.");
            }

            await _loggingService.LogInfoAsync($"[APP-LAUNCH] Found application: {app.Name} at {app.ExecutablePath}");

            // Phase 2: Check if already running (UnifiedMonitoringService provides real-time status)
            await UpdateProgressWithPhaseAsync(subtask, "launch", 20, $"Checking {app.Name} status");

            if (app.IsRunning)
            {
                await _loggingService.LogInfoAsync($"[APP-LAUNCH] {app.Name} is already running (PIDs: {string.Join(", ", app.ProcessIds)})");

                if (stepDef.AllowSkipIfRunning)
                {
                    await _loggingService.LogInfoAsync($"[APP-LAUNCH] Skipping launch - app already running");
                    subtask.Skip($"{app.Name} already running");

                    // Store process ID in context for potential later use
                    context.SetData($"{stepDef.StepId}_ProcessId", app.ProcessIds.First());
                    context.SetData($"{stepDef.StepId}_Skipped", true);
                    return;
                }

                await _loggingService.LogInfoAsync($"[APP-LAUNCH] AllowSkipIfRunning=false - will relaunch");
            }

            // Phase 3: Launch application (ExternalApplicationService handles everything)
            await UpdateProgressWithPhaseAsync(subtask, "launch", 30, $"Launching {app.Name}");

            var launched = await _externalApplicationService.LaunchApplicationAsync(app);

            if (!launched)
            {
                var message = $"Failed to launch {app.Name}. Check executable path and permissions.";
                await _loggingService.LogErrorAsync($"[APP-LAUNCH] {message}");
                throw new InvalidOperationException(message);
            }

            await _loggingService.LogInfoAsync($"[APP-LAUNCH] {app.Name} launch initiated successfully");

            // Phase 4: Wait for process detection (UnifiedMonitoringService via WMI watcher provides automatic detection)
            await UpdateProgressWithPhaseAsync(subtask, "launch", 50, $"Waiting for {app.Name} process");

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
                await _loggingService.LogErrorAsync($"[APP-LAUNCH] {message}");
                throw new TimeoutException(message);
            }

            var processId = app.ProcessIds.First();
            await _loggingService.LogInfoAsync($"[APP-LAUNCH] {app.Name} process detected (PID: {processId})");

            // Store process ID in context for later use
            context.SetData($"{stepDef.StepId}_ProcessId", processId);

            // Phase 5: TEMPLATE DETECTION - wait for app UI to be READY
            // This is the KEY innovation - confirms app is fully loaded and interactive
            await UpdateProgressWithPhaseAsync(subtask, "launch", 70, $"Waiting for {app.Name} UI");

            if (string.IsNullOrWhiteSpace(stepDef.TemplatePath))
            {
                await _loggingService.LogWarningAsync($"[APP-LAUNCH] No template path configured for {stepDef.ApplicationName} - skipping UI readiness check");
            }
            else
            {
                await _loggingService.LogInfoAsync($"[APP-LAUNCH] Detecting UI readiness using template: {stepDef.TemplatePath}");

                try
                {
                    var retryAttempts = stepDef.RetryAttempts ?? 30;
                    var retryDelay = stepDef.RetryDelayMs ?? 500;

                    await _loggingService.LogDebugAsync($"[APP-LAUNCH] Template detection config: {retryAttempts} attempts x {retryDelay}ms");

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
                                windowHandle = await FindWindowHandleAsync(
                                    subtask,
                                    processName,
                                    string.Empty,
                                    cancellationToken,
                                    maxAttempts: 3);
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
                                var templateMatch = await _templateService.FindElementAsync(screenshot, stepDef.TemplatePath, cancellationToken);
                                var metadata = await _templateManagementService.GetTemplateMetadataAsync(stepDef.TemplatePath);
                                var threshold = metadata?.ConfidenceThreshold ?? 0.8;

                                if (templateMatch != null && templateMatch.Confidence >= threshold)
                                {
                                    detected = true;
                                    await _loggingService.LogInfoAsync($"[APP-LAUNCH] UI ready - template detected on attempt {attempt}/{retryAttempts} (confidence: {templateMatch.Confidence:P})");
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await _loggingService.LogDebugAsync($"[APP-LAUNCH] Template detection attempt {attempt}/{retryAttempts} failed: {ex.Message}");
                        }

                        if (attempt < retryAttempts)
                        {
                            await Task.Delay(retryDelay, cancellationToken);
                        }
                    }

                    if (!detected)
                    {
                        var message = $"{app.Name} UI not ready - template '{stepDef.TemplatePath}' not detected after {retryAttempts} attempts";
                        await _loggingService.LogErrorAsync($"[APP-LAUNCH] {message}");
                        throw new TimeoutException(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"[APP-LAUNCH] Error during template detection", ex);
                    throw;
                }
            }

            // Phase 6: Execute navigation (e.g., click Launch Arrow in Windower)
            if (stepDef.Navigation != null)
            {
                await UpdateProgressWithPhaseAsync(subtask, "launch", 90, $"Interacting with {app.Name}");
                await _loggingService.LogInfoAsync($"[APP-LAUNCH] Executing post-launch navigation for {app.Name}");

                // Get window handle for the launched application
                var windowHandle = IntPtr.Zero;
                try
                {
                    var processName = System.IO.Path.GetFileNameWithoutExtension(app.ExecutablePath);
                    windowHandle = await FindWindowHandleAsync(
                        subtask,
                        processName,
                        string.Empty,
                        cancellationToken,
                        maxAttempts: 10);

                    await _loggingService.LogDebugAsync($"[APP-LAUNCH] Found window handle for {app.Name}: 0x{windowHandle.ToInt64():X}");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"[APP-LAUNCH] Could not find window handle for {app.Name}: {ex.Message}");
                }

                // Execute navigation sequence
                var success = await ExecuteNavigationActionAsync(
                    subtask,
                    stepDef.Navigation,
                    windowHandle,
                    null, // No template match needed for launch navigation
                    _automationService,
                    cancellationToken);

                if (!success)
                {
                    await _loggingService.LogWarningAsync($"[APP-LAUNCH] Navigation partially failed for {app.Name}, but continuing");
                }
                else
                {
                    await _loggingService.LogInfoAsync($"[APP-LAUNCH] Navigation completed successfully for {app.Name}");
                }
            }

            // Phase 7: Complete
            await UpdateProgressWithPhaseAsync(subtask, "launch", 100, $"{app.Name} ready");
            await _loggingService.LogInfoAsync($"[APP-LAUNCH] {app.Name} launch sequence completed successfully");
        }

        /// <summary>
        /// Validates that the workflow step definition is complete and valid.
        /// </summary>
        private void ValidateWorkflowStep(WorkflowStepDefinition stepDef)
        {
            if (!stepDef.Validate(out var errors))
            {
                var errorMessage = $"Invalid workflow step definition: {string.Join(", ", errors)}";
                throw new InvalidOperationException(errorMessage);
            }

            if (string.IsNullOrWhiteSpace(stepDef.TemplatePath))
            {
                throw new InvalidOperationException($"Workflow step '{stepDef.DisplayName}' has no template path configured");
            }
        }

        /// <summary>
        /// Gets window handle from context, or attempts to discover it.
        /// </summary>
        private async Task<IntPtr> GetOrDiscoverWindowHandleAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            // Try to get window handle from context first
            var windowHandle = context.GetValueData<IntPtr>("POLWindowHandle");

            if (windowHandle == IntPtr.Zero)
            {
                await _loggingService.LogDebugAsync("Window handle not in context, attempting to discover");

                // Try to find pol.exe window
                try
                {
                    windowHandle = await FindWindowHandleAsync(
                        subtask,
                        "pol",
                        string.Empty,
                        cancellationToken,
                        maxAttempts: 10);

                    // Store for future steps
                    context.SetData("POLWindowHandle", windowHandle);
                }
                catch (Exception ex)
                {
                    await _loggingService.LogWarningAsync($"Could not find POL window handle: {ex.Message}");
                    throw new InvalidOperationException("Could not find PlayOnline window. Ensure PlayOnline is running.", ex);
                }
            }

            return windowHandle;
        }

        /// <summary>
        /// Detects the screen using the template defined in the workflow step.
        /// Supports fallback templates for handling variations.
        /// </summary>
        private async Task<TemplateMatchResult> DetectScreenAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            IntPtr windowHandle,
            CancellationToken cancellationToken)
        {
            var primaryTemplate = stepDef.TemplatePath;
            var description = stepDef.DisplayName;

            // Try primary template first
            try
            {
                await _loggingService.LogDebugAsync($"Attempting detection with primary template: {primaryTemplate}");

                var options = new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(stepDef.EstimatedDurationSeconds, 30)),
                    CheckInterval = TimeSpan.FromSeconds(1)
                };

                return await WaitForScreenDetectionAsync(
                    subtask,
                    primaryTemplate,
                    windowHandle,
                    description,
                    cancellationToken,
                    options);
            }
            catch (TimeoutException) when (stepDef.FallbackTemplatePaths.Count > 0)
            {
                await _loggingService.LogInfoAsync($"Primary template '{primaryTemplate}' failed, trying fallback templates");

                // Try fallback templates
                foreach (var fallbackTemplate in stepDef.FallbackTemplatePaths)
                {
                    try
                    {
                        await _loggingService.LogDebugAsync($"Attempting detection with fallback template: {fallbackTemplate}");

                        var options = new ScreenDetectionOptions
                        {
                            Timeout = TimeSpan.FromSeconds(15),
                            CheckInterval = TimeSpan.FromSeconds(1)
                        };

                        return await WaitForScreenDetectionAsync(
                            subtask,
                            fallbackTemplate,
                            windowHandle,
                            $"{description} (fallback)",
                            cancellationToken,
                            options);
                    }
                    catch (TimeoutException)
                    {
                        await _loggingService.LogDebugAsync($"Fallback template '{fallbackTemplate}' also failed");
                        continue;
                    }
                }

                // All templates failed
                throw new TimeoutException($"Could not detect screen for step '{description}' using any configured templates");
            }
        }

        /// <summary>
        /// Executes navigation for the workflow step.
        /// Navigation must be defined in the workflow step definition.
        /// </summary>
        private async Task<bool> ExecuteNavigationAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            IntPtr windowHandle,
            TemplateMatchResult templateMatch,
            CancellationToken cancellationToken)
        {
            // Get navigation from step definition (100% workflow-driven)
            var navigation = stepDef.Navigation;

            // If no navigation defined, this is a detection-only step
            if (navigation == null)
            {
                await _loggingService.LogInfoAsync($"No navigation configured for step '{stepDef.DisplayName}' - detection only");
                return true;
            }

            // Execute the navigation action
            await _loggingService.LogDebugAsync($"Executing navigation: Type={navigation.Type}, Sequence Length={navigation.Sequence?.Count ?? 0}");

            var success = await ExecuteNavigationActionAsync(
                subtask,
                navigation,
                windowHandle,
                templateMatch,
                _automationService,
                cancellationToken);

            if (!success)
            {
                throw new InvalidOperationException($"Navigation failed for step '{stepDef.DisplayName}'");
            }

            await _loggingService.LogDebugAsync($"Navigation completed successfully for step: {stepDef.DisplayName}");
            return true;
        }

        /// <summary>
        /// Validates inputs specific to dynamic workflow execution.
        /// </summary>
        protected override void ValidateInputs(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
        {
            base.ValidateInputs(subtask, queueItem);

            if (subtask.WorkflowStep == null)
            {
                throw new InvalidOperationException("DynamicWorkflowHandler requires a WorkflowStepDefinition in the subtask");
            }
        }
    }
}
