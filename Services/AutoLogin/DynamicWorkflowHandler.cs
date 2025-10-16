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
        private readonly IPlayOnlineMonitorService _polMonitorService;
        private readonly IWorkflowActionExecutorFactory _actionExecutorFactory;

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IExternalApplicationService externalApplicationService,
            IPlayOnlineMonitorService polMonitorService,
            IWorkflowActionExecutorFactory actionExecutorFactory)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _polMonitorService = polMonitorService ?? throw new ArgumentNullException(nameof(polMonitorService));
            _actionExecutorFactory = actionExecutorFactory ?? throw new ArgumentNullException(nameof(actionExecutorFactory));
        }

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
        /// Uses unified action executor pattern - no routing based on StepType.
        /// </summary>
        protected override async Task ExecuteHandlerLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var stepDef = subtask.WorkflowStep!;

            await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Executing step: {stepDef.DisplayName} (StepId: {stepDef.StepId})");

            // ALL workflow steps now use the unified navigation/action execution flow
            await ExecuteNavigationStepAsync(subtask, queueItem, context, cancellationToken);

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
        /// Gets window handle using PlayOnlineMonitorService with optional PID hint from context.
        /// Leverages existing monitoring infrastructure for robust window discovery and validation.
        /// </summary>
        /// <remarks>
        /// Architecture:
        /// 1. Extract PID hint from launch step context (if available)
        /// 2. Delegate to PlayOnlineMonitorService which:
        ///    - Uses cached character data for fast lookup
        ///    - Validates window is still alive
        ///    - Falls back to other valid windows if hint is stale
        /// 3. System stays flexible and leverages existing monitoring
        /// </remarks>
        private async Task<IntPtr> GetOrDiscoverWindowHandleAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync("[WINDOW-DISCOVERY] Requesting valid POL window from PlayOnlineMonitorService");

            // Extract PID hint from context (from launch step if available)
            int? preferredProcessId = null;
            var launchStepPid = context.GetValueData<int>("launch_windower_ProcessId");
            if (launchStepPid > 0)
            {
                preferredProcessId = launchStepPid;
                await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Providing PID hint from launch step: {preferredProcessId}");
            }
            else
            {
                await _loggingService.LogDebugAsync("[WINDOW-DISCOVERY] No PID hint available - PlayOnlineMonitorService will use best match");
            }

            // Delegate to PlayOnlineMonitorService - it knows the truth about which windows are valid
            var windowHandle = await _polMonitorService.GetValidPlayOnlineWindowAsync(preferredProcessId);

            if (windowHandle == IntPtr.Zero)
            {
                await _loggingService.LogWarningAsync("[WINDOW-DISCOVERY] PlayOnlineMonitorService could not find valid POL window");
                throw new InvalidOperationException("Could not find valid PlayOnline window. Ensure PlayOnline is running.");
            }

            await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Using valid POL window from monitoring service: 0x{windowHandle.ToInt64():X}");
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
                    CheckInterval = TimeSpan.FromSeconds(1),
                    MaxAttempts = stepDef.MaxRetryAttempts > 0 ? stepDef.MaxRetryAttempts : null,
                    ScreenshotRetryCount = stepDef.ScreenshotRetryCount ?? Math.Max(stepDef.MaxRetryAttempts / 3, 5) // Use workflow value or auto-calculate
                };

                await _loggingService.LogInfoAsync($"Detection config - MaxAttempts: {options.MaxAttempts?.ToString() ?? "auto"}, ScreenshotRetries: {options.ScreenshotRetryCount}");

                return await WaitForScreenDetectionAsync(
                    subtask,
                    primaryTemplate,
                    windowHandle,
                    description,
                    stepDef.ConfidenceThreshold,
                    stepDef.Tolerance,
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
                            CheckInterval = TimeSpan.FromSeconds(1),
                            MaxAttempts = Math.Max(stepDef.MaxRetryAttempts / 2, 5), // Use half attempts for fallback
                            ScreenshotRetryCount = stepDef.ScreenshotRetryCount ?? Math.Max(stepDef.MaxRetryAttempts / 3, 5) // Use workflow value or auto-calculate
                        };

                        return await WaitForScreenDetectionAsync(
                            subtask,
                            fallbackTemplate,
                            windowHandle,
                            $"{description} (fallback)",
                            stepDef.ConfidenceThreshold,
                            stepDef.Tolerance,
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
        /// Executes navigation for the workflow step using the unified action executor pattern.
        /// Each action in the sequence is routed to the appropriate executor.
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
            if (navigation == null || navigation.Sequence == null || navigation.Sequence.Count == 0)
            {
                await _loggingService.LogInfoAsync($"No navigation configured for step '{stepDef.DisplayName}' - detection only");
                return true;
            }

            // Execute each action in the sequence using the appropriate executor
            await _loggingService.LogInfoAsync($"[NAVIGATION] Executing {navigation.Sequence.Count} action(s) for step: {stepDef.DisplayName}");

            // Build execution context
            var context = new WorkflowActionContext
            {
                WindowHandle = windowHandle,
                TemplateMatch = templateMatch,
                Subtask = subtask,
                QueueItem = null, // Can be passed if needed
                AutoLoginContext = null, // Can be passed if needed
                WorkflowStep = stepDef
            };

            for (int i = 0; i < navigation.Sequence.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var action = navigation.Sequence[i];
                await _loggingService.LogDebugAsync($"[NAVIGATION] Action {i + 1}/{navigation.Sequence.Count}: {action.Action}");

                try
                {
                    // Get appropriate executor for this action
                    var executor = _actionExecutorFactory.GetExecutor(action.Action);

                    // Execute the action
                    var success = await executor.ExecuteAsync(action, context, cancellationToken);

                    if (!success)
                    {
                        await _loggingService.LogErrorAsync($"[NAVIGATION] Action {action.Action} failed at position {i + 1}");
                        throw new InvalidOperationException($"Navigation action '{action.Action}' failed for step '{stepDef.DisplayName}'");
                    }

                    await _loggingService.LogDebugAsync($"[NAVIGATION] Action {action.Action} completed successfully");
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"[NAVIGATION] Failed to execute action {action.Action} at position {i + 1}", ex);
                    throw;
                }
            }

            // Post-navigation delay (if configured in navigation action)
            if (navigation.PostNavigationDelayMs > 0)
            {
                await _loggingService.LogDebugAsync($"[NAVIGATION] Post-navigation delay: {navigation.PostNavigationDelayMs}ms");
                await Task.Delay(navigation.PostNavigationDelayMs, cancellationToken);
            }

            await _loggingService.LogInfoAsync($"[NAVIGATION] All actions completed successfully for step: {stepDef.DisplayName}");
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
