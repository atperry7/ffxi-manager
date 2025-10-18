using System;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.Infrastructure;

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
        private readonly IWorkflowActionExecutorFactory _actionExecutorFactory;
        private readonly IWindowDiscoveryService _windowDiscoveryService;

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IWorkflowActionExecutorFactory actionExecutorFactory,
            IWindowDiscoveryService windowDiscoveryService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _actionExecutorFactory = actionExecutorFactory ?? throw new ArgumentNullException(nameof(actionExecutorFactory));
            _windowDiscoveryService = windowDiscoveryService ?? throw new ArgumentNullException(nameof(windowDiscoveryService));
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

            // Phase 2: Decide detection strategy
            // If this is a detection-only step (no navigation), do traditional pre-detection.
            // Otherwise, defer window discovery and detection to per-action execution to allow Launch-first flows.
            bool hasNavigation = stepDef.Navigation != null && stepDef.Navigation.Sequence != null && stepDef.Navigation.Sequence.Count > 0;

            IntPtr windowHandle = IntPtr.Zero;
            TemplateMatchResult? templateMatch = null;

            if (!hasNavigation && !string.IsNullOrWhiteSpace(stepDef.TemplatePath))
            {
                await _loggingService.LogDebugAsync("[DYNAMIC-WORKFLOW] Detection-only step - performing pre-detection");
                var targetApp = TargetApplicationResolver.ResolveForStep(stepDef);
                var windowInfo = await _windowDiscoveryService.DiscoverWindowInfoAsync(targetApp, context, cancellationToken);
                windowHandle = windowInfo.WindowHandle;
                await UpdateProgressWithPhaseAsync(subtask, "authentication", 20, $"Looking for {stepDef.DisplayName}");
                templateMatch = await DetectScreenAsync(subtask, stepDef, windowHandle, cancellationToken);
            }
            else
            {
                await _loggingService.LogDebugAsync("[DYNAMIC-WORKFLOW] Action-driven step - deferring window discovery/detection to per-action execution");
            }

            // Phase 3: Execute navigation with on-demand discovery/detection
            await UpdateProgressWithPhaseAsync(subtask, "authentication", 60, $"Navigating {stepDef.DisplayName}");
            await ExecuteNavigationAsync(subtask, stepDef, queueItem, context, windowHandle, templateMatch, cancellationToken);

            // Phase 5: Post-navigation delay (if configured)
            if (stepDef.EstimatedDurationSeconds > 0)
            {
                await UpdateProgressWithPhaseAsync(subtask, "authentication", 90, "Waiting for screen transition");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(stepDef.EstimatedDurationSeconds, 3)), cancellationToken);
            }

            await UpdateProgressWithPhaseAsync(subtask, "authentication", 100, $"{stepDef.DisplayName} completed");
        }


        /// <summary>
        /// Checks if an action requires a window handle by querying the executor.
        /// Delegates to the executor's RequiresWindowHandle property for accurate determination.
        /// </summary>
        private bool ActionRequiresWindow(KeyboardAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Action))
                return false;

            try
            {
                // Get the executor for this action and check its requirements
                var executor = _actionExecutorFactory.GetExecutor(action.Action);
                return executor.RequiresWindowHandle;
            }
            catch
            {
                // If executor not found, assume window not required
                return false;
            }
        }


        /// <summary>
        /// Validates that the workflow step definition is complete and valid.
        /// Step-level templates are optional - steps can use action-level templates or blind navigation.
        /// </summary>
        private void ValidateWorkflowStep(WorkflowStepDefinition stepDef)
        {
            if (!stepDef.Validate(out var errors))
            {
                var errorMessage = $"Invalid workflow step definition: {string.Join(", ", errors)}";
                throw new InvalidOperationException(errorMessage);
            }

            // Step-level template is OPTIONAL
            // Valid patterns:
            // 1. TemplatePath + Navigation = Wait for screen, then navigate
            // 2. TemplatePath only = Detection-only step (no navigation)
            // 3. Navigation only = Blind navigation or action-level templates (Launch actions)
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

                var attempts = stepDef.RetryAttempts ?? 30;
                var delayMs = stepDef.RetryDelayMs ?? 500;
                var options = new ScreenDetectionOptions
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(stepDef.EstimatedDurationSeconds, Math.Max(30, (attempts * (delayMs + 250)) / 1000))),
                    CheckInterval = TimeSpan.FromMilliseconds(delayMs),
                    MaxAttempts = attempts
                };

                await _loggingService.LogInfoAsync($"Detection config - MaxAttempts: {options.MaxAttempts?.ToString() ?? "auto"}, Delay: {delayMs}ms");

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

                        var attemptsFb = Math.Max((stepDef.RetryAttempts ?? 30) / 2, 5);
                        var delayFb = stepDef.RetryDelayMs ?? 500;
                        var options = new ScreenDetectionOptions
                        {
                            Timeout = TimeSpan.FromSeconds(Math.Max(15, (attemptsFb * (delayFb + 250)) / 1000)),
                            CheckInterval = TimeSpan.FromMilliseconds(delayFb),
                            MaxAttempts = attemptsFb
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
        /// Performs action-level detection using a template.
        /// Consolidates the pattern of building temporary step definitions for on-demand detection.
        /// </summary>
        /// <param name="subtask">Subtask for progress reporting</param>
        /// <param name="stepDef">Base workflow step definition to clone</param>
        /// <param name="action">Action containing detection parameters</param>
        /// <param name="windowHandle">Window handle for screenshot capture</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="defaultTimeout">Default timeout if not specified in action</param>
        /// <returns>Template match result if detected, null if timeout and not required</returns>
        /// <exception cref="TimeoutException">Thrown if detection times out and match is required</exception>
        private async Task<TemplateMatchResult?> PerformActionLevelDetectionAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            KeyboardAction action,
            IntPtr windowHandle,
            CancellationToken cancellationToken,
            int defaultTimeout = 3)
        {
            var templatePath = action.GetParameter<string>("TemplatePath", string.Empty);
            if (string.IsNullOrWhiteSpace(templatePath) || windowHandle == IntPtr.Zero)
            {
                return null;
            }

            var requireMatch = action.GetParameter<bool>("RequireMatch", false);

            await _loggingService.LogDebugAsync($"[DETECTION] Performing action-level detection using template: {templatePath}");

            // Build detection configuration from action parameters
            var tempStep = stepDef.Clone();
            tempStep.TemplatePath = templatePath;
            tempStep.ConfidenceThreshold = action.GetParameter<float>("ConfidenceThreshold", stepDef.ConfidenceThreshold);
            tempStep.Tolerance = action.GetParameter<int>("Tolerance", stepDef.Tolerance);
            tempStep.RetryAttempts = action.GetParameter<int>("RetryAttempts", stepDef.RetryAttempts ?? 60);
            tempStep.RetryDelayMs = action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500);
            tempStep.EstimatedDurationSeconds = Math.Max(1, action.GetParameter<int>("TimeoutSeconds", defaultTimeout));

            try
            {
                return await DetectScreenAsync(subtask, tempStep, windowHandle, cancellationToken);
            }
            catch (TimeoutException)
            {
                if (requireMatch)
                {
                    await _loggingService.LogErrorAsync($"[DETECTION] Required template not found: {templatePath}");
                    throw new TimeoutException($"Required template not detected: {templatePath}");
                }
                else
                {
                    await _loggingService.LogInfoAsync($"[DETECTION] Optional template not found, proceeding: {templatePath}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Executes navigation for the workflow step using the unified action executor pattern.
        /// Orchestrates the navigation sequence by delegating to focused helper methods.
        /// </summary>
        private async Task<bool> ExecuteNavigationAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext autoLoginContext,
            IntPtr windowHandle,
            TemplateMatchResult? templateMatch,
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

            await _loggingService.LogInfoAsync($"[NAVIGATION] Executing {navigation.Sequence.Count} action(s) for step: {stepDef.DisplayName}");

            // Build execution context
            var actionContext = BuildActionContext(subtask, stepDef, autoLoginContext, windowHandle, templateMatch);

            // Execute action sequence
            await ExecuteNavigationSequence(subtask, stepDef, navigation, actionContext, autoLoginContext, cancellationToken);

            // Post-navigation delay (if configured)
            if (navigation.PostNavigationDelayMs > 0)
            {
                await _loggingService.LogDebugAsync($"[NAVIGATION] Post-navigation delay: {navigation.PostNavigationDelayMs}ms");
                await Task.Delay(navigation.PostNavigationDelayMs, cancellationToken);
            }

            await _loggingService.LogInfoAsync($"[NAVIGATION] All actions completed successfully for step: {stepDef.DisplayName}");
            return true;
        }

        /// <summary>
        /// Builds the WorkflowActionContext for executing navigation actions.
        /// Centralizes context creation with PID-first architecture support.
        /// </summary>
        private WorkflowActionContext BuildActionContext(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            IAutoLoginContext autoLoginContext,
            IntPtr windowHandle,
            TemplateMatchResult? templateMatch)
        {
            return new WorkflowActionContext
            {
                WindowHandle = windowHandle,
                TemplateMatch = templateMatch,
                Subtask = subtask,
                QueueItem = null, // Not needed currently
                AutoLoginContext = autoLoginContext, // Provide context so actions (e.g., Launch) can persist data
                WorkflowStep = stepDef,
                WindowDiscoveryService = _windowDiscoveryService // Enable auto-refresh capability
            };
        }

        /// <summary>
        /// Executes all actions in the navigation sequence.
        /// Coordinates action execution with on-demand window discovery and detection.
        /// </summary>
        private async Task ExecuteNavigationSequence(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            NavigationAction navigation,
            WorkflowActionContext actionContext,
            IAutoLoginContext autoLoginContext,
            CancellationToken cancellationToken)
        {
            for (int i = 0; i < navigation.Sequence.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var action = navigation.Sequence[i];
                await _loggingService.LogDebugAsync($"[NAVIGATION] Action {i + 1}/{navigation.Sequence.Count}: {action.Action}");

                // Determine target app for this action
                var targetApp = TargetApplicationResolver.ResolveForAction(stepDef, navigation, i);

                // Ensure window handle is available if needed
                await EnsureWindowHandleForAction(action, targetApp, actionContext, autoLoginContext, cancellationToken);

                // Ensure template match is available if needed
                await EnsureTemplateMatchForAction(action, subtask, stepDef, actionContext, cancellationToken);

                // Execute the action
                await ExecuteSingleAction(action, subtask, stepDef, actionContext, autoLoginContext, i + 1, cancellationToken);
            }
        }

        /// <summary>
        /// Ensures a valid window handle is available for actions that require UI interaction.
        /// Uses on-demand window discovery if needed, updating the context with PID and handle.
        /// </summary>
        private async Task EnsureWindowHandleForAction(
            KeyboardAction action,
            string targetApp,
            WorkflowActionContext actionContext,
            IAutoLoginContext autoLoginContext,
            CancellationToken cancellationToken)
        {
            if (!ActionRequiresWindow(action))
                return;

            if (actionContext.WindowHandle == IntPtr.Zero)
            {
                await _loggingService.LogDebugAsync("[NAVIGATION] Acquiring window info on-demand for UI action");
                var windowInfo = await _windowDiscoveryService.DiscoverWindowInfoAsync(targetApp, autoLoginContext, cancellationToken);

                if (!windowInfo.IsValid)
                {
                    throw new InvalidOperationException("Could not acquire a valid window for UI interaction.");
                }

                // Update context with PID + handle
                actionContext.UpdateFromWindowInfo(windowInfo);
                await _loggingService.LogInfoAsync($"[NAVIGATION] Acquired window - PID: {windowInfo.ProcessId}, Handle: 0x{windowInfo.WindowHandle.ToInt64():X}");
            }
        }

        /// <summary>
        /// Ensures a template match is available for actions that need screen detection.
        /// Performs on-demand detection for Click actions (required) and Keyboard actions (optional).
        /// </summary>
        private async Task EnsureTemplateMatchForAction(
            KeyboardAction action,
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            WorkflowActionContext actionContext,
            CancellationToken cancellationToken)
        {
            // Click actions: require template match if no existing match
            if (string.Equals(action.Action, "Click", StringComparison.OrdinalIgnoreCase))
            {
                if (actionContext.TemplateMatch == null)
                {
                    actionContext.TemplateMatch = await PerformActionLevelDetectionAsync(
                        subtask,
                        stepDef,
                        action,
                        actionContext.WindowHandle,
                        cancellationToken,
                        defaultTimeout: stepDef.EstimatedDurationSeconds);
                }
                return;
            }

            // Keyboard actions: optional confirmation detection
            if (string.Equals(action.Action, "Keyboard", StringComparison.OrdinalIgnoreCase) ||
                _actionExecutorFactory.IsKeyboardAction(action.Action))
            {
                var match = await PerformActionLevelDetectionAsync(
                    subtask,
                    stepDef,
                    action,
                    actionContext.WindowHandle,
                    cancellationToken,
                    defaultTimeout: 3);

                if (match != null)
                {
                    actionContext.TemplateMatch = match;
                }
            }
        }

        /// <summary>
        /// Executes a single action in the navigation sequence.
        /// Handles executor resolution, execution, error handling, and post-launch readiness confirmation.
        /// </summary>
        private async Task ExecuteSingleAction(
            KeyboardAction action,
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            WorkflowActionContext actionContext,
            IAutoLoginContext autoLoginContext,
            int actionNumber,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get appropriate executor for this action
                var executor = _actionExecutorFactory.GetExecutor(action.Action);

                // Execute the action
                var success = await executor.ExecuteAsync(action, actionContext, cancellationToken);

                if (!success)
                {
                    await _loggingService.LogErrorAsync($"[NAVIGATION] Action {action.Action} failed at position {actionNumber}");
                    throw new InvalidOperationException($"Navigation action '{action.Action}' failed for step '{stepDef.DisplayName}'");
                }

                await _loggingService.LogDebugAsync($"[NAVIGATION] Action {action.Action} completed successfully");

                // Confirm launch readiness if this was a Launch action
                if (string.Equals(action.Action, "Launch", StringComparison.OrdinalIgnoreCase))
                {
                    await ConfirmLaunchReadiness(action, subtask, stepDef, actionContext, autoLoginContext, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[NAVIGATION] Failed to execute action {action.Action} at position {actionNumber}", ex);
                throw;
            }
        }

        /// <summary>
        /// Confirms application readiness after a Launch action by detecting a readiness template.
        /// Only runs if the action specifies a TemplatePath parameter for UI readiness confirmation.
        /// </summary>
        private async Task ConfirmLaunchReadiness(
            KeyboardAction action,
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            WorkflowActionContext actionContext,
            IAutoLoginContext autoLoginContext,
            CancellationToken cancellationToken)
        {
            var readinessTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
            if (string.IsNullOrWhiteSpace(readinessTemplate))
                return;

            await _loggingService.LogInfoAsync($"[LAUNCH-READY] Waiting for readiness template: {readinessTemplate}");

            // Acquire window for the launched application if not already available
            var handle = actionContext.WindowHandle;
            if (handle == IntPtr.Zero)
            {
                var launchedApp = action.GetParameter<string>("ApplicationName", string.Empty);
                var launchedInfo = await _windowDiscoveryService.DiscoverWindowInfoAsync(launchedApp, autoLoginContext, cancellationToken);

                if (launchedInfo.IsValid)
                {
                    actionContext.UpdateFromWindowInfo(launchedInfo);
                    handle = launchedInfo.WindowHandle;
                    await _loggingService.LogInfoAsync($"[LAUNCH-READY] Acquired window for {launchedApp} - PID: {launchedInfo.ProcessId}, Handle: 0x{handle.ToInt64():X}");
                }
            }

            if (handle == IntPtr.Zero)
                return;

            // Calculate timeout based on retry configuration
            var retryAttempts = action.GetParameter<int>("RetryAttempts", stepDef.RetryAttempts ?? 60);
            var retryDelayMs = action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500);
            var defaultTimeout = retryAttempts * (retryDelayMs / 1000 + 1);

            // Create a modified action with RequireMatch = true for launch readiness
            var readinessAction = new KeyboardAction
            {
                Action = "Launch",
                Parameters = new Dictionary<string, object>(action.Parameters)
            };
            readinessAction.Parameters["RequireMatch"] = true; // Launch readiness always required

            // Perform readiness detection
            var match = await PerformActionLevelDetectionAsync(
                subtask,
                stepDef,
                readinessAction,
                handle,
                cancellationToken,
                defaultTimeout: defaultTimeout);

            if (match != null)
            {
                actionContext.TemplateMatch = match;
                await _loggingService.LogInfoAsync("[LAUNCH-READY] Application readiness confirmed by template");
            }
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
