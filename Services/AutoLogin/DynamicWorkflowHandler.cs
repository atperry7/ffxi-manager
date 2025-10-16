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

        public DynamicWorkflowHandler(
            ILoggingService loggingService,
            IScreenshotCaptureService screenshotService,
            ITemplateMatchingService templateService,
            ITemplateManagementService templateManagementService,
            IUIAutomationService automationService)
            : base(loggingService, screenshotService, templateService, templateManagementService)
        {
            _automationService = automationService ?? throw new ArgumentNullException(nameof(automationService));
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
        /// </summary>
        protected override async Task ExecuteHandlerLogicAsync(
            AutoLoginSubtask subtask,
            AutoLoginQueueItem queueItem,
            IAutoLoginContext context,
            CancellationToken cancellationToken)
        {
            var stepDef = subtask.WorkflowStep!;

            await _loggingService.LogInfoAsync($"DynamicWorkflowHandler executing step: {stepDef.DisplayName} (StepId: {stepDef.StepId})");

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
            await _loggingService.LogInfoAsync($"DynamicWorkflowHandler completed step: {stepDef.DisplayName}");
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
        /// Uses Navigation from step definition if defined, otherwise falls back to template's navigation.
        /// </summary>
        private async Task<bool> ExecuteNavigationAsync(
            AutoLoginSubtask subtask,
            WorkflowStepDefinition stepDef,
            IntPtr windowHandle,
            TemplateMatchResult templateMatch,
            CancellationToken cancellationToken)
        {
            NavigationAction? navigation = null;

            // WORKFLOW-FIRST: Check if step has navigation defined (primary source)
            if (stepDef.Navigation != null)
            {
                await _loggingService.LogDebugAsync($"Using step navigation for: {stepDef.DisplayName}");
                navigation = stepDef.Navigation;
            }
            else
            {
                // FALLBACK: Load navigation from template metadata for backward compatibility
                var templateMetadata = await _templateManagementService.GetTemplateMetadataAsync(stepDef.TemplatePath);
                if (templateMetadata?.Navigation != null)
                {
                    await _loggingService.LogDebugAsync($"Using template navigation (fallback) for step: {stepDef.DisplayName}");
                    navigation = templateMetadata.Navigation;
                }
            }

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
