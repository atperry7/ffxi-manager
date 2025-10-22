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
    public class DynamicWorkflowHandler : ILoginTaskHandler
    {
        private readonly ILoggingService _loggingService;
        private readonly IUIAutomationService _automationService;
        private readonly IWorkflowActionExecutorFactory _actionExecutorFactory;
        private readonly IWindowDiscoveryService _windowDiscoveryService;
        private readonly IScreenDetectionCoordinator _screenDetectionCoordinator;
        private readonly IWorkflowProgressService _progressService;
        private readonly IQueueStatisticsService _statisticsService;

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IUIAutomationService automationService,
            IWorkflowActionExecutorFactory actionExecutorFactory,
            IWindowDiscoveryService windowDiscoveryService,
            IScreenDetectionCoordinator screenDetectionCoordinator,
            IWorkflowProgressService progressService,
            IQueueStatisticsService statisticsService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _actionExecutorFactory = actionExecutorFactory ?? throw new ArgumentNullException(nameof(actionExecutorFactory));
            _windowDiscoveryService = windowDiscoveryService ?? throw new ArgumentNullException(nameof(windowDiscoveryService));
            _screenDetectionCoordinator = screenDetectionCoordinator ?? throw new ArgumentNullException(nameof(screenDetectionCoordinator));
            _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
            _statisticsService = statisticsService ?? throw new ArgumentNullException(nameof(statisticsService));
        }

        /// <summary>
        /// Can handle any subtask that has a WorkflowStepDefinition.
        /// This allows the handler to execute user-defined workflow steps.
        /// </summary>
        public bool CanHandle(AutoLoginSubtask subtask)
        {
            // Check if subtask has workflow step definition
            return subtask?.WorkflowStep != null;
        }

        /// <summary>
        /// Executes a workflow step dynamically based on its definition.
        /// Uses unified action executor pattern - no routing based on StepType.
        /// </summary>
        public async Task ExecuteAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"Starting execution of '{subtask.Name}' for {queueItem.DisplayName}");
            // Record step start for performance tracking
            try { _statisticsService.RecordStepStart(queueItem, subtask); } catch { }

            try
            {
                ValidateInputs(subtask, queueItem);

                var stepDef = subtask.WorkflowStep!;

                await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Executing step: {stepDef.DisplayName} (StepId: {stepDef.StepId})");
                await ExecuteNavigationStepAsync(subtask, queueItem, context, cancellationToken);
                await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Completed step: {stepDef.DisplayName}");

                // Record success
                try { _statisticsService.RecordStepCompleted(queueItem, subtask, success: true); } catch { }

                await _loggingService.LogDebugAsync($"Successfully completed '{subtask.Name}' for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"Execution of '{subtask.Name}' was cancelled for {queueItem.DisplayName}");
                throw;
            }
            catch (Exception ex)
            {
                var stepDef = subtask.WorkflowStep;
                if (stepDef?.IsOptional == true)
                {
                    await _loggingService.LogWarningAsync($"[DYNAMIC-WORKFLOW] Optional step failed and will be skipped: {stepDef.DisplayName} - {ex.Message}");
                    subtask.Skip($"Optional step failed: {ex.Message}");
                    return;
                }

                await _loggingService.LogErrorAsync($"Failed to execute '{subtask.Name}' for {queueItem.DisplayName}", ex);
                try { _statisticsService.RecordStepCompleted(queueItem, subtask, success: false); } catch { }
                throw;
            }
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
            await _progressService.UpdateProgressWithPhaseAsync(subtask, "startup", 5, "Validating workflow step");
            ValidateWorkflowStep(stepDef);

            // OTP-aware short-circuit: If this is an OTP step but the account doesn't use OTP, skip the entire step.
            try
            {
                var account = queueItem?.Account;
                var hasOtpNavigation = stepDef.Navigation != null && stepDef.Navigation.Sequence != null && stepDef.Navigation.Sequence.Count > 0;
                var hasOtpAction = hasOtpNavigation && (stepDef.Navigation?.Sequence != null) && stepDef.Navigation.Sequence.Any(a => string.Equals(a.Action, "InputOTP", StringComparison.OrdinalIgnoreCase));

                if (account != null && account.IsOTPEnabled == false && hasOtpAction)
                {
                    await _loggingService.LogInfoAsync($"[DYNAMIC-WORKFLOW] Skipping OTP step '{stepDef.DisplayName}' for account without OTP enabled");
                    return; // Treat as successful no-op
                }
            }
            catch { /* best-effort */ }

            // Phase 2: Decide detection strategy
            // Pre-detect when:
            //  - detection-only step (no navigation), OR
            //  - step has TemplatePath and navigation does NOT include a Launch action (gated navigation)
            // Otherwise (Launch-first flows), defer detection to per-action execution.
            bool hasNavigation = stepDef.Navigation != null && stepDef.Navigation.Sequence != null && stepDef.Navigation.Sequence.Count > 0;
            bool hasTemplate = !string.IsNullOrWhiteSpace(stepDef.TemplatePath);
            bool navHasLaunch = hasNavigation && (stepDef.Navigation?.Sequence != null) && stepDef.Navigation.Sequence.Any(a => string.Equals(a.Action, "Launch", StringComparison.OrdinalIgnoreCase));

            IntPtr windowHandle = IntPtr.Zero;
            TemplateMatchResult? templateMatch = null;

            if ((hasTemplate && !hasNavigation) || (hasTemplate && hasNavigation && !navHasLaunch))
            {
                await _loggingService.LogDebugAsync("[DYNAMIC-WORKFLOW] Detection-only step - using on-demand discovery via refresh callback");
                var targetApp = TargetApplicationResolver.ResolveForStep(stepDef);

                await _progressService.UpdateProgressWithPhaseAsync(subtask, "authentication", 20, $"Looking for {stepDef.DisplayName}");

                // Build discovery-on-each-attempt refresh function
                IntPtr latestHandle = IntPtr.Zero;
                int latestPid = 0;
                Func<CancellationToken, Task<IntPtr>> refreshHandleAsync = async (ct) =>
                {
                    try
                    {
                        var info = await _windowDiscoveryService.DiscoverWindowInfoAsync(targetApp, context, ct);
                        if (info.IsValid)
                        {
                            latestHandle = info.WindowHandle;
                            latestPid = info.ProcessId;
                            return info.WindowHandle;
                        }
                    }
                    catch { }
                    return IntPtr.Zero;
                };

                // Start detection with no initial handle; coordinator will call refresh per attempt
                // For pre-detection with subsequent navigation, avoid marking progress 100 on detection
                templateMatch = await DetectScreenAsync(subtask, stepDef, IntPtr.Zero, cancellationToken, refreshHandleAsync, completeOnDetection: false);

                // Capture last discovered handle (if any) for subsequent navigation phase
                if (latestHandle != IntPtr.Zero)
                {
                    windowHandle = latestHandle;
                }

                // Persist discovered PID to shared context for downstream steps
                if (latestPid > 0 && context != null)
                {
                    var wellKnownKey = GetWellKnownPidKeyForAppName(targetApp);
                    if (!string.IsNullOrEmpty(wellKnownKey))
                    {
                        context.SetData(wellKnownKey!, latestPid);
                        await _loggingService.LogDebugAsync($"[DYNAMIC-WORKFLOW] Persisted PID {latestPid} for '{targetApp}' to context key '{wellKnownKey}'");
                    }
                }
            }
            else
            {
                await _loggingService.LogDebugAsync("[DYNAMIC-WORKFLOW] Action-driven step - deferring window discovery/detection to per-action execution");
            }

            // Phase 3: Execute navigation with on-demand discovery/detection
            await _progressService.UpdateProgressWithPhaseAsync(subtask, "authentication", 60, $"Navigating {stepDef.DisplayName}");
            await ExecuteNavigationAsync(subtask, stepDef, queueItem ?? throw new ArgumentNullException(nameof(queueItem)), context ?? throw new ArgumentNullException(nameof(context)), windowHandle, templateMatch, cancellationToken);

            // Phase 5: Post-navigation delay (if configured)
            if (stepDef.EstimatedDurationSeconds > 0)
            {
                await _progressService.UpdateProgressWithPhaseAsync(subtask, "authentication", 90, "Waiting for screen transition");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(stepDef.EstimatedDurationSeconds, 3)), cancellationToken);
            }

            await _progressService.UpdateProgressWithPhaseAsync(subtask, "authentication", 100, $"{stepDef.DisplayName} completed");
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
            // Supported patterns:
            // 1. TemplatePath only = Detection-only step (no navigation)
            // 2. TemplatePath + Navigation (no Launch) = Pre-detect screen, then run navigation (gated navigation)
            // 3. Navigation with Launch = Launch-first; detection occurs per-action (Click requires action-level TemplatePath)
            // 4. Navigation only (no templates) = Blind navigation
        }

        /// <summary>
        /// Detects the screen using the template defined in the workflow step.
        /// Supports fallback templates for handling variations.
        /// </summary>
        private async Task<TemplateMatchResult> DetectScreenAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            IntPtr windowHandle,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<IntPtr>>? refreshHandleAsync = null,
            bool completeOnDetection = true)
        {
            var detectStart = DateTime.UtcNow;
            var primaryTemplate = stepDef.TemplatePath;
            var description = stepDef.DisplayName;

            // Try primary template first
            try
            {
                await _loggingService.LogDebugAsync($"Attempting detection with primary template: {primaryTemplate}");

                var attempts = Math.Max(1, stepDef.RetryAttempts ?? 30);
                var delayMs = Math.Max(100, stepDef.RetryDelayMs ?? 500); // guard against 0ms hammering
                var options = new ScreenDetectionOptions
                {
                    // EstimatedDurationSeconds is a hard cap for this step
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, stepDef.EstimatedDurationSeconds)),
                    CheckInterval = TimeSpan.FromMilliseconds(delayMs),
                    MaxAttempts = attempts
                };

                // Budget sanity check: will configured attempts x delay fit in step hard cap?
                var expectedMs = (attempts * delayMs);
                if (TimeSpan.FromMilliseconds(expectedMs) > options.Timeout)
                {
                    await _loggingService.LogWarningAsync($"Detection polling budget ({attempts}x{delayMs}ms = {expectedMs / 1000.0:F1}s) exceeds step hard cap of {options.Timeout.TotalSeconds}s for '{description}'. Consider adjusting RetryAttempts/RetryDelayMs or EstimatedDurationSeconds.");
                }

                await _loggingService.LogInfoAsync($"Detection config [primary] - Attempts: {options.MaxAttempts?.ToString() ?? "auto"}, Interval: {delayMs}ms, Timeout: {options.Timeout.TotalSeconds}s (hard cap)");

                if (refreshHandleAsync != null)
                {
                    var matchPrimary = await _screenDetectionCoordinator.WaitForScreenDetectionWithHandleRefreshAsync(
                        subtask,
                        primaryTemplate,
                        windowHandle,
                        refreshHandleAsync,
                        description,
                        stepDef.ConfidenceThreshold,
                        stepDef.Tolerance,
                        cancellationToken,
                        options,
                        completeOnDetection: completeOnDetection);
                    try { _statisticsService.RecordDetectionResult(stepDef.StepId, stepDef.DisplayName, matchPrimary.Confidence, (DateTime.UtcNow - detectStart).TotalSeconds); } catch { }
                    return matchPrimary;
                }
                else
                {
                    var matchPrimary = await _screenDetectionCoordinator.WaitForScreenDetectionAsync(
                        subtask,
                        primaryTemplate,
                        windowHandle,
                        description,
                        stepDef.ConfidenceThreshold,
                        stepDef.Tolerance,
                        cancellationToken,
                        options,
                        completeOnDetection: completeOnDetection);
                    try { _statisticsService.RecordDetectionResult(stepDef.StepId, stepDef.DisplayName, matchPrimary.Confidence, (DateTime.UtcNow - detectStart).TotalSeconds); } catch { }
                    return matchPrimary;
                }
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

                        // Use the configured retry attempts for fallback as well (avoid hard-coded minimums)
                        var attemptsFb = Math.Max(1, stepDef.RetryAttempts ?? 30);
                        var delayFb = Math.Max(100, stepDef.RetryDelayMs ?? 500);
                        var options = new ScreenDetectionOptions
                        {
                            Timeout = TimeSpan.FromSeconds(Math.Max(1, stepDef.EstimatedDurationSeconds)),
                            CheckInterval = TimeSpan.FromMilliseconds(delayFb),
                            MaxAttempts = attemptsFb
                        };
                        // Budget sanity check for fallback as well
                        var expectedMsFb = (attemptsFb * delayFb);
                        if (TimeSpan.FromMilliseconds(expectedMsFb) > options.Timeout)
                        {
                            await _loggingService.LogWarningAsync($"Fallback detection budget ({attemptsFb}x{delayFb}ms = {expectedMsFb / 1000.0:F1}s) exceeds step hard cap of {options.Timeout.TotalSeconds}s for '{description}'. Consider adjusting configuration.");
                        }
                        await _loggingService.LogInfoAsync($"Detection config [fallback] - Attempts: {options.MaxAttempts}, Interval: {delayFb}ms, Timeout: {options.Timeout.TotalSeconds}s (hard cap)");

                        if (refreshHandleAsync != null)
                        {
                            var matchFallback = await _screenDetectionCoordinator.WaitForScreenDetectionWithHandleRefreshAsync(
                                subtask,
                                fallbackTemplate,
                                windowHandle,
                                refreshHandleAsync,
                                $"{description} (fallback)",
                                stepDef.ConfidenceThreshold,
                                stepDef.Tolerance,
                                cancellationToken,
                                options,
                                completeOnDetection: completeOnDetection);
                            try { _statisticsService.RecordDetectionResult(stepDef.StepId, stepDef.DisplayName, matchFallback.Confidence, (DateTime.UtcNow - detectStart).TotalSeconds); } catch { }
                            return matchFallback;
                        }
                        else
                        {
                            var matchFallback = await _screenDetectionCoordinator.WaitForScreenDetectionAsync(
                                subtask,
                                fallbackTemplate,
                                windowHandle,
                                $"{description} (fallback)",
                                stepDef.ConfidenceThreshold,
                                stepDef.Tolerance,
                                cancellationToken,
                                options,
                                completeOnDetection: completeOnDetection);
                            try { _statisticsService.RecordDetectionResult(stepDef.StepId, stepDef.DisplayName, matchFallback.Confidence, (DateTime.UtcNow - detectStart).TotalSeconds); } catch { }
                            return matchFallback;
                        }
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
            int defaultTimeout = 3,
            Func<CancellationToken, Task<IntPtr>>? refreshHandleAsync = null)
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
            tempStep.RetryDelayMs = Math.Max(100, action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500));
            tempStep.EstimatedDurationSeconds = Math.Max(1, action.GetParameter<int>("TimeoutSeconds", defaultTimeout));

            try
            {
                // Action-level detection is part of navigation; avoid marking 100% on detection
                return await DetectScreenAsync(subtask, tempStep, windowHandle, cancellationToken, refreshHandleAsync, completeOnDetection: false);
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
            var actionContext = BuildActionContext(subtask, queueItem, stepDef, autoLoginContext, windowHandle, templateMatch);

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
            AutoLoginQueueItem queueItem,
            WorkflowStepDefinition stepDef,
            IAutoLoginContext autoLoginContext,
            IntPtr windowHandle,
            TemplateMatchResult? templateMatch)
        {
            var actionCtx = new WorkflowActionContext
            {
                WindowHandle = windowHandle,
                TemplateMatch = templateMatch,
                Subtask = subtask,
                QueueItem = queueItem,
                AutoLoginContext = autoLoginContext, // Provide context so actions (e.g., Launch) can persist data
                WorkflowStep = stepDef,
                WindowDiscoveryService = _windowDiscoveryService // Enable auto-refresh capability
            };

            try
            {
                var targetApp = TargetApplicationResolver.ResolveForStep(stepDef);
                var pidHint = GetPidHintForApp(autoLoginContext, targetApp);
                if (pidHint > 0)
                {
                    actionCtx.ProcessId = pidHint;
                    actionCtx.ApplicationName = targetApp;
                }
            }
            catch { /* best-effort */ }

            return actionCtx;
        }

        private static string? GetWellKnownPidKeyForAppName(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return null;
            var name = appName.Trim().ToLowerInvariant();
            if (name.Contains("playonline") || name == "pol") return AutoLoginContextKeys.WellKnown.PlayOnlineProcessId;
            if (name.Contains("windower")) return AutoLoginContextKeys.WellKnown.WindowerProcessId;
            if (name.Contains("proxy") || name.Contains("pol proxy")) return AutoLoginContextKeys.WellKnown.POLProxyProcessId;
            if (name.Contains("ashita")) return AutoLoginContextKeys.WellKnown.AshitaProcessId;
            if (name.Contains("ffxi")) return AutoLoginContextKeys.WellKnown.FFXIProcessId;
            return null;
        }

        private static int GetPidHintForApp(IAutoLoginContext context, string appName)
        {
            var key = GetWellKnownPidKeyForAppName(appName);
            if (string.IsNullOrEmpty(key)) return 0;
            try { return context.GetValueData<int>(key!); } catch { return 0; }
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

                // Action-level retry budget (applies to entire action execution including detection)
                var actionAttempts = Math.Max(1, action.GetParameter<int>("RetryAttempts", stepDef.RetryAttempts ?? 30));
                var actionDelayMs = Math.Max(100, action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500));

                Exception? lastError = null;
                for (int attempt = 1; attempt <= actionAttempts; attempt++)
                {
                    try
                    {
                        // Reset per-attempt transient detection to avoid stale matches
                        actionContext.TemplateMatch = null;

                        // Ensure window handle is available if needed
                        await EnsureWindowHandleForAction(action, targetApp, actionContext, autoLoginContext, cancellationToken);

                        // Ensure template match is available if needed (per-attempt)
                        await EnsureTemplateMatchForAction(action, subtask, stepDef, actionContext, cancellationToken);

                        // Execute the action
                        await ExecuteSingleAction(action, subtask, stepDef, actionContext, autoLoginContext, i + 1, cancellationToken);

                        // Success — break out of per-action retry loop
                        lastError = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        await _loggingService.LogWarningAsync($"[NAVIGATION] Action '{action.Action}' attempt {attempt}/{actionAttempts} failed: {ex.Message}");
                        if (attempt < actionAttempts)
                        {
                            await Task.Delay(actionDelayMs, cancellationToken);
                        }
                    }
                }

                if (lastError != null)
                {
                    // Exhausted action-level retries
                    throw new InvalidOperationException($"Navigation action '{action.Action}' failed after {actionAttempts} attempt(s)", lastError);
                }
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

                // Use step-level retry configuration to wait for spawned apps (e.g., PlayOnline after launch)
                var stepDef = actionContext.WorkflowStep;
                var attempts = Math.Max(1, stepDef?.RetryAttempts ?? 30);
                var delayMs = Math.Max(100, stepDef?.RetryDelayMs ?? 500);

                Exception? lastError = null;
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var windowInfo = await _windowDiscoveryService.DiscoverWindowInfoAsync(targetApp, autoLoginContext, cancellationToken);
                        if (windowInfo.IsValid)
                        {
                            // Update context with PID + handle
                            actionContext.UpdateFromWindowInfo(windowInfo);
                            await _loggingService.LogInfoAsync($"[NAVIGATION] Acquired window - PID: {windowInfo.ProcessId}, Handle: 0x{windowInfo.WindowHandle.ToInt64():X}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                    }

                    if (attempt < attempts)
                    {
                        await _loggingService.LogDebugAsync($"[NAVIGATION] Window not available yet for '{targetApp}' (attempt {attempt}/{attempts}). Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs, cancellationToken);
                    }
                }

                // If we reach here, we failed to get a window within retry budget
                throw new InvalidOperationException("Could not acquire a valid window for UI interaction.", lastError);
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
                    var refresh = BuildHandleRefreshFunc(action, stepDef, actionContext, actionContext.AutoLoginContext!, cancellationToken);

                    actionContext.TemplateMatch = await PerformActionLevelDetectionAsync(
                        subtask,
                        stepDef,
                        action,
                        actionContext.WindowHandle,
                        cancellationToken,
                        defaultTimeout: stepDef.EstimatedDurationSeconds,
                        refreshHandleAsync: refresh);
                }
                return;
            }

            // Member/Character slot actions: perform action-level detection if action supplies its own template
            if (string.Equals(action.Action, "MemberSlot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action.Action, "CharacterSlot", StringComparison.OrdinalIgnoreCase))
            {
                var actionTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                if (!string.IsNullOrWhiteSpace(actionTemplate))
                {
                    var refresh = BuildHandleRefreshFunc(action, stepDef, actionContext, actionContext.AutoLoginContext!, cancellationToken);

                    actionContext.TemplateMatch = await PerformActionLevelDetectionAsync(
                        subtask,
                        stepDef,
                        action,
                        actionContext.WindowHandle,
                        cancellationToken,
                        defaultTimeout: stepDef.EstimatedDurationSeconds,
                        refreshHandleAsync: refresh);
                }
                return;
            }

            // Keyboard actions: optional confirmation detection
            if (string.Equals(action.Action, "Keyboard", StringComparison.OrdinalIgnoreCase) ||
                _actionExecutorFactory.IsKeyboardAction(action.Action))
            {
                var refreshKb = BuildHandleRefreshFunc(action, stepDef, actionContext, actionContext.AutoLoginContext!, cancellationToken);

                var match = await PerformActionLevelDetectionAsync(
                    subtask,
                    stepDef,
                    action,
                    actionContext.WindowHandle,
                    cancellationToken,
                    defaultTimeout: 3,
                    refreshHandleAsync: refreshKb);

                if (match != null)
                {
                    actionContext.TemplateMatch = match;
                }
            }
        }

        private Func<CancellationToken, Task<IntPtr>> BuildHandleRefreshFunc(
            KeyboardAction action,
            WorkflowStepDefinition stepDef,
            WorkflowActionContext actionContext,
            IAutoLoginContext autoLoginContext,
            CancellationToken cancellationToken)
        {
            return async (ct) =>
            {
                // PID-first refresh via discovery service
                if (actionContext.ProcessId > 0 && actionContext.WindowDiscoveryService != null)
                {
                    var fresh = await actionContext.WindowDiscoveryService.GetFreshWindowHandleFromPidAsync(actionContext.ProcessId, actionContext.ApplicationName);
                    if (fresh != IntPtr.Zero)
                    {
                        actionContext.WindowHandle = fresh; // keep context in sync
                        return fresh;
                    }
                }

                // Fallback: rediscover by app name (from action or step)
                var appName = action.GetParameter<string>("ApplicationName", string.Empty);
                if (string.IsNullOrWhiteSpace(appName))
                {
                    appName = TargetApplicationResolver.ResolveForStep(stepDef);
                }

                if (!string.IsNullOrWhiteSpace(appName))
                {
                    var info = await _windowDiscoveryService.DiscoverWindowInfoAsync(appName, autoLoginContext, ct);
                    if (info.IsValid)
                    {
                        actionContext.UpdateFromWindowInfo(info);
                        return info.WindowHandle;
                    }
                }

                return IntPtr.Zero;
            };
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

            // Build detection options and thresholds from action parameters
            var retryAttempts = Math.Max(1, action.GetParameter<int>("RetryAttempts", stepDef.RetryAttempts ?? 60));
            var retryDelayMs = Math.Max(100, action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500));
            var confidence = action.GetParameter<float>("ConfidenceThreshold", stepDef.ConfidenceThreshold);
            var tolerance = action.GetParameter<int>("Tolerance", stepDef.Tolerance);

            var options = new ScreenDetectionOptions
            {
                // EstimatedDurationSeconds is a hard cap for the step
                Timeout = TimeSpan.FromSeconds(Math.Max(1, stepDef.EstimatedDurationSeconds)),
                CheckInterval = TimeSpan.FromMilliseconds(retryDelayMs),
                MaxAttempts = retryAttempts
            };

            // Define handle refresh strategy (PID-first; falls back to rediscovery by app name)
            var refreshHandleAsync = new Func<CancellationToken, Task<IntPtr>>(async ct =>
            {
                if (actionContext.ProcessId > 0 && actionContext.WindowDiscoveryService != null)
                {
                    var fresh = await actionContext.WindowDiscoveryService.GetFreshWindowHandleFromPidAsync(actionContext.ProcessId, actionContext.ApplicationName);
                    if (fresh != IntPtr.Zero)
                    {
                        actionContext.WindowHandle = fresh; // keep context in sync
                        return fresh;
                    }
                }

                var appName = action.GetParameter<string>("ApplicationName", string.Empty);
                if (!string.IsNullOrWhiteSpace(appName))
                {
                    var info = await _windowDiscoveryService.DiscoverWindowInfoAsync(appName, autoLoginContext, ct);
                    if (info.IsValid)
                    {
                        actionContext.UpdateFromWindowInfo(info);
                        return info.WindowHandle;
                    }
                }
                return IntPtr.Zero;
            });

            // Perform readiness detection with handle refresh awareness
            var readyStart = DateTime.UtcNow;
            var match = await _screenDetectionCoordinator.WaitForScreenDetectionWithHandleRefreshAsync(
                subtask,
                readinessTemplate,
                handle,
                refreshHandleAsync,
                screenDescription: stepDef.DisplayName,
                confidenceThreshold: confidence,
                tolerance: tolerance,
                cancellationToken: cancellationToken,
                options: options,
                completeOnDetection: false);

            if (match != null)
            {
                actionContext.TemplateMatch = match;
                await _loggingService.LogInfoAsync("[LAUNCH-READY] Application readiness confirmed by template");
                try { _statisticsService.RecordDetectionResult(stepDef.StepId, stepDef.DisplayName, match.Confidence, (DateTime.UtcNow - readyStart).TotalSeconds); } catch { }
            }
        }

        /// <summary>
        /// Validates inputs specific to dynamic workflow execution.
        /// </summary>
        private void ValidateInputs(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
        {
            if (subtask == null)
                throw new ArgumentNullException(nameof(subtask));

            if (queueItem == null)
                throw new ArgumentNullException(nameof(queueItem));

            if (queueItem.Account == null)
                throw new InvalidOperationException($"Account information is required for '{subtask.Name}'");

            if (subtask.WorkflowStep == null)
            {
                throw new InvalidOperationException("DynamicWorkflowHandler requires a WorkflowStepDefinition in the subtask");
            }
        }
    }
}
