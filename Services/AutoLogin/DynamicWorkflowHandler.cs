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
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly IPlayOnlineMonitorService _polMonitorService;
        private readonly IWorkflowActionExecutorFactory _actionExecutorFactory;
        private readonly IProcessUtilityService _processUtilityService;

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService,
            IExternalApplicationService externalApplicationService,
            IPlayOnlineMonitorService polMonitorService,
            IWorkflowActionExecutorFactory actionExecutorFactory,
            IProcessUtilityService processUtilityService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
            _polMonitorService = polMonitorService ?? throw new ArgumentNullException(nameof(polMonitorService));
            _actionExecutorFactory = actionExecutorFactory ?? throw new ArgumentNullException(nameof(actionExecutorFactory));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
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
                var targetApp = DetermineTargetApplicationForStep(stepDef);
                windowHandle = await GetOrDiscoverWindowHandleForAppAsync(targetApp, context, cancellationToken);
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
        /// Determines whether the given workflow step requires a PlayOnline window handle.
        /// Rules:
        /// - If a step-level template is configured, a window is required for detection.
        /// - If navigation contains UI-interacting actions (Click, CharacterSlot, MemberSlot, InputPassword, InputOTP), a window is required.
        /// - Pure workflow actions (Launch, Wait, Screenshot) do not require a window.
        /// - Keyboard actions can operate without a window (sent to foreground), but benefit from focus if available.
        /// </summary>
        private static bool StepRequiresWindow(WorkflowStepDefinition stepDef)
        {
            if (stepDef == null) return true; // Defensive: assume required

            // Any step-level template implies we need a valid window for detection
            if (!string.IsNullOrWhiteSpace(stepDef.TemplatePath))
                return true;

            var nav = stepDef.Navigation;
            if (nav == null || nav.Sequence == null || nav.Sequence.Count == 0)
                return false; // Detection-only with no template already handled above, but safe to say no window

            foreach (var action in nav.Sequence)
            {
                var act = (action.Action ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(act)) continue;

                var a = act.ToLowerInvariant();

                // Actions that require window/template context
                if (a == "click" || a == "characterslot" || a == "memberslot" || a == "inputpassword" || a == "inputotp")
                    return true;

                // Actions that do NOT require a window: launch/wait/screenshot/keyboard
                if (a == "launch" || a == "wait" || a == "screenshot" || a == "keyboard")
                    continue;

                // Unknown actions: be conservative and require a window
                return true;
            }

            return false;
        }

        private static bool ActionRequiresWindow(KeyboardAction action)
        {
            var a = (action.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(a)) return false;

            // Actions requiring a valid window handle
            if (a == "click" || a == "characterslot" || a == "memberslot" || a == "inputpassword" || a == "inputotp")
                return true;

            // Keyboard/Launch/Wait generally do not require window upfront (keyboard may focus if handle provided)
            return false;
        }

        private static bool IsKeyboardKey(string actionName)
        {
            if (string.IsNullOrWhiteSpace(actionName)) return false;
            var a = actionName.ToLowerInvariant();
            switch (a)
            {
                case "tab":
                case "enter":
                case "return":
                case "escape":
                case "esc":
                case "space":
                case "spacebar":
                case "down":
                case "downarrow":
                case "up":
                case "uparrow":
                case "left":
                case "leftarrow":
                case "right":
                case "rightarrow":
                case "home":
                case "end":
                case "pageup":
                case "pagedown":
                    return true;
                default:
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
            // Try multiple well-known context keys that launch steps set
            var candidateKeys = new[]
            {
                "launch_windower_ProcessId",
                "launch_pol_proxy_ProcessId",
                "launch_playonline_ProcessId",
                "launch_ffxi_ProcessId"
            };

            foreach (var key in candidateKeys)
            {
                var pid = context.GetValueData<int>(key);
                if (pid > 0)
                {
                    preferredProcessId = pid;
                    await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Providing PID hint from context '{key}': {preferredProcessId}");
                    break;
                }
            }

            if (!preferredProcessId.HasValue)
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
        /// Discovers a window handle for a target application name.
        /// Uses PlayOnlineMonitorService for POL; ExternalApplicationService + ProcessUtility for others.
        /// </summary>
        private async Task<IntPtr> GetOrDiscoverWindowHandleForAppAsync(
            string? applicationName,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var app = (applicationName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(app))
            {
                // Default to PlayOnline if unspecified
                return await GetOrDiscoverWindowHandleAsync(null!, null!, context, cancellationToken);
            }

            // Treat any variant of PlayOnline/FFXI as POL route
            var a = app.ToLowerInvariant();
            if (a.Contains("playonline") || a == "pol" || a.Contains("ffxi"))
            {
                return await GetOrDiscoverWindowHandleAsync(null!, null!, context, cancellationToken);
            }

            try
            {
                // Use ExternalApplicationService to find running PIDs
                var apps = await _externalApplicationService.GetApplicationsAsync();
                var target = apps.FirstOrDefault(x => x.Name.Equals(app, StringComparison.OrdinalIgnoreCase));
                if (target == null || !target.IsRunning)
                {
                    await _loggingService.LogWarningAsync($"[WINDOW-DISCOVERY] Target application not running: {app}");
                    return IntPtr.Zero;
                }

                // Try PID hint from context first
                var hintKey = $"launch_{a.Replace(" ", "_")}_ProcessId";
                var hintedPid = context.GetValueData<int>(hintKey);
                if (hintedPid > 0 && target.ProcessIds.Contains(hintedPid))
                {
                    var windows = await _processUtilityService.GetProcessWindowsAsync(hintedPid);
                    var handle = windows.FirstOrDefault()?.Handle ?? IntPtr.Zero;
                    if (handle != IntPtr.Zero && _processUtilityService.IsWindowValid(handle))
                    {
                        await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Using window from hint PID {hintedPid} for {app}: 0x{handle.ToInt64():X}");
                        return handle;
                    }
                }

                // Fallback: try any PID
                foreach (var pid in target.ProcessIds)
                {
                    var windows = await _processUtilityService.GetProcessWindowsAsync(pid);
                    var handle = windows.FirstOrDefault()?.Handle ?? IntPtr.Zero;
                    if (handle != IntPtr.Zero && _processUtilityService.IsWindowValid(handle))
                    {
                        await _loggingService.LogInfoAsync($"[WINDOW-DISCOVERY] Using discovered window for {app} (PID {pid}): 0x{handle.ToInt64():X}");
                        return handle;
                    }
                }

                await _loggingService.LogWarningAsync($"[WINDOW-DISCOVERY] No valid windows found for {app}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"[WINDOW-DISCOVERY] Error discovering window for {app}", ex);
                return IntPtr.Zero;
            }
        }

        private static string DetermineTargetApplicationForStep(WorkflowStepDefinition step)
        {
            // Prefer a Launch action's ApplicationName if present
            var nav = step.Navigation;
            if (nav?.Sequence != null)
            {
                var launch = nav.Sequence.FirstOrDefault(a => string.Equals(a.Action, "Launch", StringComparison.OrdinalIgnoreCase));
                if (launch != null)
                {
                    var appName = launch.GetParameter<string>("ApplicationName", string.Empty);
                    if (!string.IsNullOrWhiteSpace(appName)) return appName;
                }
            }

            // Derive from step-level TemplatePath prefix: "App/template"
            var tp = step.TemplatePath ?? string.Empty;
            var idx = tp.IndexOf('/');
            if (idx > 0) return tp.Substring(0, idx);

            // Default
            return "PlayOnline";
        }

        private static string DetermineTargetApplicationForAction(WorkflowStepDefinition step, NavigationAction navigation, int actionIndex)
        {
            // Explicit override on action
            var current = navigation.Sequence[actionIndex];
            var target = current.GetParameter<string>("TargetApplication", string.Empty);
            if (!string.IsNullOrWhiteSpace(target)) return target;

            // Use the most recent Launch action earlier in the sequence
            for (int j = actionIndex; j >= 0; j--)
            {
                var a = navigation.Sequence[j];
                if (string.Equals(a.Action, "Launch", StringComparison.OrdinalIgnoreCase))
                {
                    var app = a.GetParameter<string>("ApplicationName", string.Empty);
                    if (!string.IsNullOrWhiteSpace(app)) return app;
                }
            }

            // Derive from step-level TemplatePath prefix
            return DetermineTargetApplicationForStep(step);
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
                    MaxAttempts = attempts,
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

                        var attemptsFb = Math.Max((stepDef.RetryAttempts ?? 30) / 2, 5);
                        var delayFb = stepDef.RetryDelayMs ?? 500;
                        var options = new ScreenDetectionOptions
                        {
                            Timeout = TimeSpan.FromSeconds(Math.Max(15, (attemptsFb * (delayFb + 250)) / 1000)),
                            CheckInterval = TimeSpan.FromMilliseconds(delayFb),
                            MaxAttempts = attemptsFb,
                            ScreenshotRetryCount = stepDef.ScreenshotRetryCount ?? Math.Max(stepDef.MaxRetryAttempts / 3, 5)
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
        /// Template match is optional - actions may use step-level detection, action-level detection, or neither.
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

            // Execute each action in the sequence using the appropriate executor
            await _loggingService.LogInfoAsync($"[NAVIGATION] Executing {navigation.Sequence.Count} action(s) for step: {stepDef.DisplayName}");

            // Build execution context
            var actionContext = new WorkflowActionContext
            {
                WindowHandle = windowHandle,
                TemplateMatch = templateMatch,
                Subtask = subtask,
                QueueItem = null, // Not needed currently
                AutoLoginContext = autoLoginContext, // Provide context so actions (e.g., Launch) can persist data
                WorkflowStep = stepDef
            };

            for (int i = 0; i < navigation.Sequence.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var action = navigation.Sequence[i];
                await _loggingService.LogDebugAsync($"[NAVIGATION] Action {i + 1}/{navigation.Sequence.Count}: {action.Action}");

                // Determine target app for this action
                var targetApp = DetermineTargetApplicationForAction(stepDef, navigation, i);

                // On-demand window discovery for actions that require a window
                if (ActionRequiresWindow(action))
                {
                    if (actionContext.WindowHandle == IntPtr.Zero)
                    {
                        await _loggingService.LogDebugAsync("[NAVIGATION] Acquiring window handle on-demand for UI action");
                        var discovered = await GetOrDiscoverWindowHandleForAppAsync(targetApp, autoLoginContext, cancellationToken);
                        if (discovered == IntPtr.Zero)
                        {
                            throw new InvalidOperationException("Could not acquire a valid window for UI interaction.");
                        }
                        actionContext.WindowHandle = discovered;
                    }
                }

                // On-demand detection for actions that can accept action-level templates (e.g., Click)
                if (string.Equals(action.Action, "Click", StringComparison.OrdinalIgnoreCase))
                {
                    if (actionContext.TemplateMatch == null)
                    {
                        var actionTemplate = action.GetParameter<string>("TemplatePath", stepDef.TemplatePath);
                        if (!string.IsNullOrWhiteSpace(actionTemplate) && actionContext.WindowHandle != IntPtr.Zero)
                        {
                            await _loggingService.LogDebugAsync($"[NAVIGATION] Performing action-level detection for Click using template: {actionTemplate}");

                            // Build a temporary step to leverage existing DetectScreenAsync
                            var tempStep = stepDef.Clone();
                            tempStep.TemplatePath = actionTemplate;
                            tempStep.ConfidenceThreshold = action.GetParameter<float>("ConfidenceThreshold", stepDef.ConfidenceThreshold);
                            tempStep.Tolerance = action.GetParameter<int>("Tolerance", stepDef.Tolerance);
                            tempStep.EstimatedDurationSeconds = Math.Max(1, action.GetParameter<int>("TimeoutSeconds", stepDef.EstimatedDurationSeconds));

                            try
                            {
                                actionContext.TemplateMatch = await DetectScreenAsync(subtask, tempStep, actionContext.WindowHandle, cancellationToken);
                            }
                            catch (TimeoutException)
                            {
                                await _loggingService.LogWarningAsync($"[NAVIGATION] Action-level detection timed out for template '{actionTemplate}'");
                                // Let executor decide how to proceed (Click executor requires a match and will fail gracefully)
                            }
                        }
                    }
                }

                // Optional confirmation for keyboard actions
                if (string.Equals(action.Action, "Keyboard", StringComparison.OrdinalIgnoreCase) || IsKeyboardKey(action.Action))
                {
                    var confirmTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                    var requireMatch = action.GetParameter<bool>("RequireMatch", false);
                    if (!string.IsNullOrWhiteSpace(confirmTemplate) && actionContext.WindowHandle != IntPtr.Zero)
                    {
                        await _loggingService.LogDebugAsync($"[NAVIGATION] Keyboard confirmation detection using template: {confirmTemplate}");

                        var tempStep = stepDef.Clone();
                        tempStep.TemplatePath = confirmTemplate;
                        tempStep.ConfidenceThreshold = action.GetParameter<float>("ConfidenceThreshold", stepDef.ConfidenceThreshold);
                        tempStep.Tolerance = action.GetParameter<int>("Tolerance", stepDef.Tolerance);
                        tempStep.EstimatedDurationSeconds = Math.Max(1, action.GetParameter<int>("TimeoutSeconds", 3));

                        try
                        {
                            var match = await DetectScreenAsync(subtask, tempStep, actionContext.WindowHandle, cancellationToken);
                            actionContext.TemplateMatch = match;
                        }
                        catch (TimeoutException)
                        {
                            if (requireMatch)
                            {
                                throw new TimeoutException($"Keyboard confirmation template not found: {confirmTemplate}");
                            }
                            else
                            {
                                await _loggingService.LogInfoAsync($"[NAVIGATION] Confirmation template not found, proceeding: {confirmTemplate}");
                            }
                        }
                    }
                }

                try
                {
                    // Get appropriate executor for this action
                    var executor = _actionExecutorFactory.GetExecutor(action.Action);

                    // Execute the action
                    var success = await executor.ExecuteAsync(action, actionContext, cancellationToken);

                    if (!success)
                    {
                        await _loggingService.LogErrorAsync($"[NAVIGATION] Action {action.Action} failed at position {i + 1}");
                        throw new InvalidOperationException($"Navigation action '{action.Action}' failed for step '{stepDef.DisplayName}'");
                    }

                    await _loggingService.LogDebugAsync($"[NAVIGATION] Action {action.Action} completed successfully");

                    // Post-Launch readiness detection if TemplatePath is provided on the action
                    if (string.Equals(action.Action, "Launch", StringComparison.OrdinalIgnoreCase))
                    {
                        var readinessTemplate = action.GetParameter<string>("TemplatePath", string.Empty);
                        if (!string.IsNullOrWhiteSpace(readinessTemplate))
                        {
                            // Acquire window for the launched application
                            var launchedApp = action.GetParameter<string>("ApplicationName", string.Empty);
                            var handle = actionContext.WindowHandle;
                            if (handle == IntPtr.Zero)
                            {
                                handle = await GetOrDiscoverWindowHandleForAppAsync(launchedApp, autoLoginContext, cancellationToken);
                            }

                            if (handle != IntPtr.Zero)
                            {
                                var tempStep = stepDef.Clone();
                                tempStep.TemplatePath = readinessTemplate;
                                tempStep.ConfidenceThreshold = action.GetParameter<float>("ConfidenceThreshold", stepDef.ConfidenceThreshold);
                                tempStep.Tolerance = action.GetParameter<int>("Tolerance", stepDef.Tolerance);
                                tempStep.RetryAttempts = action.GetParameter<int>("RetryAttempts", stepDef.RetryAttempts ?? 60);
                                tempStep.RetryDelayMs = action.GetParameter<int>("RetryDelayMs", stepDef.RetryDelayMs ?? 500);
                                tempStep.EstimatedDurationSeconds = Math.Max(1, action.GetParameter<int>("TimeoutSeconds", (tempStep.RetryAttempts ?? 60) * ((tempStep.RetryDelayMs ?? 500) / 1000 + 1)));

                                try
                                {
                                    await _loggingService.LogInfoAsync($"[LAUNCH-READY] Waiting for readiness template: {readinessTemplate}");
                                    var match = await DetectScreenAsync(subtask, tempStep, handle, cancellationToken);
                                    actionContext.TemplateMatch = match;
                                    await _loggingService.LogInfoAsync("[LAUNCH-READY] Application readiness confirmed by template");
                                }
                                catch (TimeoutException)
                                {
                                    throw new TimeoutException($"Launch readiness template not detected: {readinessTemplate}");
                                }
                            }
                        }
                    }
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
