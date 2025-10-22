using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin.ActionExecutors
{
    /// <summary>
    /// Executes "Launch" actions to start external applications.
    /// Handles process detection and stores process ID in context.
    /// UI readiness should be confirmed using separate detection-only workflow steps.
    /// </summary>
    /// <remarks>
    /// **Launch Action Flow:**
    /// 1. Lookup application from ExternalApplicationData settings (by name)
    /// 2. Check if already running (with optional skip logic)
    /// 3. Launch application via ExternalApplicationService
    /// 4. Wait for process detection (WMI watchers provide auto-detection)
    /// 5. Store process ID in context for later use
    ///
    /// **Parameters:**
    /// - ApplicationName (string, required): Name of app in settings
    /// - AllowSkipIfRunning (bool): Skip if already running (default: false)
    /// - AllowSkipIfNotConfigured (bool): Skip if not in settings (default: true)
    ///
    /// **Note:** For UI readiness confirmation, add a separate workflow step with
    /// a template and no navigation (detection-only step).
    /// </remarks>
    public class LaunchActionExecutor : BaseWorkflowActionExecutor
    {
        private readonly IExternalApplicationService _externalApplicationService;

        public override string ActionType => "Launch";

        public LaunchActionExecutor(
            ILoggingService loggingService,
            IExternalApplicationService externalApplicationService)
            : base(loggingService)
        {
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
        }

        protected override async Task<bool> ExecuteActionAsync(
            KeyboardAction action,
            WorkflowActionContext context,
            CancellationToken cancellationToken)
        {
            // Extract parameters
            var applicationName = action.GetParameter<string>("ApplicationName", string.Empty);
            var applicationIdParam = action.GetParameter<string>("ApplicationId", string.Empty);
            Guid appIdParam = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(applicationIdParam))
            {
                Guid.TryParse(applicationIdParam, out appIdParam);
            }
            var allowSkipIfRunning = action.GetParameter<bool>("AllowSkipIfRunning", false);
            var allowSkipIfNotConfigured = action.GetParameter<bool>("AllowSkipIfNotConfigured", true);

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                await _loggingService.LogErrorAsync("[LAUNCH] ApplicationName parameter is required for Launch actions");
                return false;
            }

            await _loggingService.LogInfoAsync($"[LAUNCH] Starting {applicationName} launch sequence");

            // Phase 1: Find application from settings
            await UpdateProgressAsync(context, 10, $"Finding {applicationName}");

            var apps = await _externalApplicationService.GetApplicationsAsync();
            ExternalApplication? app = null;
            if (appIdParam != Guid.Empty)
            {
                app = apps.FirstOrDefault(a => a.Id == appIdParam);
            }
            if (app == null && !string.IsNullOrWhiteSpace(applicationName))
            {
                app = apps.FirstOrDefault(a => a.Name.Equals(applicationName, StringComparison.OrdinalIgnoreCase));
            }

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
            // Persist name->id mapping for later steps that only pass a name
            if (!string.IsNullOrWhiteSpace(app.Name))
            {
                context.AutoLoginContext?.SetData(AutoLoginContextKeys.ApplicationIdMap(app.Name), app.Id);
            }

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

                    // Store process ID in context for potential later use (type-safe)
                    var stepId = context.WorkflowStep?.StepId ?? "launch";
                    var skippedPid = app.ProcessIds[0];
                    // GUID-based context keys (preferred)
                    context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchProcessIdByApp(app.Id), skippedPid);
                    context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchTimestampByApp(app.Id), DateTime.UtcNow);
                    // Legacy step/name-based keys to support downstream until fully migrated
                    context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchProcessId(stepId), skippedPid);
                    context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchSkipped(stepId), true);
                    context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchApplicationName(stepId), app.Name);

                    // **SPECIAL CASE**: If skipping PlayOnline launch, also store in well-known PlayOnlineProcessId key
                    if (app.Name.Contains("playonline", StringComparison.OrdinalIgnoreCase) ||
                        app.Name.Contains("pol", StringComparison.OrdinalIgnoreCase))
                    {
                        context.AutoLoginContext?.SetData(AutoLoginContextKeys.WellKnown.PlayOnlineProcessId, skippedPid);
                        await _loggingService.LogDebugAsync($"[LAUNCH] Stored existing PlayOnline PID {skippedPid} in well-known context key (skip case)");
                    }

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

            // Phase 4: Wait for process detection (configurable)
            await UpdateProgressAsync(context, 50, $"Waiting for {app.Name} process");

            var retryAttempts = Math.Max(1, action.GetParameter<int>("RetryAttempts", 60));
            var retryDelayMs = Math.Max(100, action.GetParameter<int>("RetryDelayMs", 500));
            var processDetected = false;

            for (int attempt = 0; attempt < retryAttempts && !cancellationToken.IsCancellationRequested; attempt++)
            {
                await _externalApplicationService.RefreshApplicationStatusAsync(app);

                if (app.IsRunning)
                {
                    processDetected = true;
                    break;
                }

                await Task.Delay(retryDelayMs, cancellationToken);
            }

            if (!processDetected)
            {
                var message = $"{app.Name} process not detected after {retryAttempts} attempts";
                await _loggingService.LogErrorAsync($"[LAUNCH] {message}");
                throw new TimeoutException(message);
            }

            var processId = app.ProcessIds[0];
            await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} process detected (PID: {processId})");

            // Store process ID in context for later use (type-safe)
            var stepIdForContext = context.WorkflowStep?.StepId ?? "launch";
            // GUID-based context keys (preferred)
            context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchProcessIdByApp(app.Id), processId);
            context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchTimestampByApp(app.Id), DateTime.UtcNow);
            // Legacy step/name-based keys to support downstream until fully migrated
            context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchProcessId(stepIdForContext), processId);
            context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchApplicationName(stepIdForContext), app.Name);
            context.AutoLoginContext?.SetData(AutoLoginContextKeys.LaunchTimestamp(stepIdForContext), DateTime.UtcNow);

            // **SPECIAL CASE**: If launching PlayOnline, also store in well-known PlayOnlineProcessId key
            // This enables CharacterOrderingService integration for auto-login instance detection
            if (app.Name.Contains("playonline", StringComparison.OrdinalIgnoreCase) ||
                app.Name.Contains("pol", StringComparison.OrdinalIgnoreCase))
            {
                context.AutoLoginContext?.SetData(AutoLoginContextKeys.WellKnown.PlayOnlineProcessId, processId);
                await _loggingService.LogDebugAsync($"[LAUNCH] Stored PlayOnline PID {processId} in well-known context key");
            }

            // Update action context with PID for subsequent actions
            context.ProcessId = processId;
            context.ApplicationName = app.Name;
            context.ApplicationId = app.Id;

            // Phase 5: Complete
            await UpdateProgressAsync(context, 100, $"{app.Name} launched");
            await _loggingService.LogInfoAsync($"[LAUNCH] {app.Name} launch sequence completed successfully");

            return true;
        }
    }
}
