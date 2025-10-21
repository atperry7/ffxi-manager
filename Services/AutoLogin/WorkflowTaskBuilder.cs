using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Builds AutoLoginSubtasks from WorkflowDefinition.
    /// Converts data-driven workflow definitions into executable subtask sequences.
    /// </summary>
    /// <remarks>
    /// **Responsibilities:**
    /// - Convert workflow definitions to executable subtask sequences
    /// - Evaluate application-based skip logic (SkipIfApplicationRunning)
    /// - Link subtasks to workflow steps for execution by DynamicWorkflowHandler
    ///
    /// **Design:**
    /// This builder bridges the data-driven workflow system and the task execution
    /// infrastructure by creating subtasks with embedded WorkflowStepDefinition references
    /// that enable dynamic execution via DynamicWorkflowHandler.
    /// </remarks>
    public class WorkflowTaskBuilder
    {
        private readonly ILoggingService _loggingService;
        private readonly IExternalApplicationService _externalApplicationService;

        public WorkflowTaskBuilder(
            ILoggingService loggingService,
            IExternalApplicationService externalApplicationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
        }

        /// <summary>
        /// Builds AutoLoginSubtasks from a workflow definition.
        /// </summary>
        /// <param name="workflow">The workflow definition to build from</param>
        /// <param name="account">The account being logged in (used for conditional evaluation)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of executable subtasks in order</returns>
        public async Task<List<AutoLoginSubtask>> BuildSubtasksAsync(
            WorkflowDefinition workflow,
            PlayOnlineMemberAccount account,
            CancellationToken cancellationToken = default)
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));

            if (account == null)
                throw new ArgumentNullException(nameof(account));

            await _loggingService.LogDebugAsync($"Building subtasks from workflow: {workflow.Name} (ID: {workflow.WorkflowId})");

            // Validate workflow
            if (!workflow.Validate(out var errors))
            {
                var errorMessage = $"Invalid workflow: {string.Join(", ", errors)}";
                await _loggingService.LogErrorAsync(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // Get executable steps (respects IsEnabled and evaluates conditions)
            var conditionEvaluator = await CreateConditionEvaluatorAsync(account);
            var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

            await _loggingService.LogDebugAsync($"Workflow has {executableSteps.Count} executable steps (out of {workflow.Steps.Count} total)");

            // Convert workflow steps to subtasks
            var subtasks = new List<AutoLoginSubtask>();
            int executionOrder = 0;

            foreach (var step in executableSteps)
            {
                var subtask = CreateSubtaskFromStep(step, executionOrder++);
                subtasks.Add(subtask);

                await _loggingService.LogDebugAsync($"Created subtask: {subtask.Name} (Order: {subtask.ExecutionOrder}, StepId: {step.StepId})");
            }

            await _loggingService.LogInfoAsync($"Built {subtasks.Count} subtasks from workflow '{workflow.Name}'");

            return subtasks;
        }

        /// <summary>
        /// Creates a single AutoLoginSubtask from a WorkflowStepDefinition.
        /// </summary>
        private static AutoLoginSubtask CreateSubtaskFromStep(WorkflowStepDefinition step, int executionOrder)
        {
            var subtask = new AutoLoginSubtask
            {
                Name = step.DisplayName,
                Description = step.Description,
                ExecutionOrder = executionOrder,
                EstimatedDurationSeconds = step.EstimatedDurationSeconds,
                IsSkippable = step.IsOptional,
                MaxRetryAttempts = step.MaxRetryAttempts,
                WorkflowStep = step // Link to workflow step for DynamicWorkflowHandler
            };

            return subtask;
        }

        /// <summary>
        /// Creates a condition evaluator function that checks application-based skip logic.
        /// </summary>
        /// <remarks>
        /// **Application-Based Skip Logic:**
        /// - Checks WorkflowStepDefinition.SkipIfApplicationRunning
        /// - If specified application is running, step is skipped
        /// - Otherwise, step is always executed
        /// </remarks>
        private async Task<Func<WorkflowStepDefinition, bool>> CreateConditionEvaluatorAsync(PlayOnlineMemberAccount account)
        {
            // Load current application statuses
            var applications = await _externalApplicationService.GetApplicationsAsync();
            var runningAppNames = applications
                .Where(app => app.IsRunning)
                .Select(app => app.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await _loggingService.LogDebugAsync($"Running applications: {string.Join(", ", runningAppNames)}");

            return step =>
            {
                // Check SkipIfApplicationRunning
                if (!string.IsNullOrWhiteSpace(step.SkipIfApplicationRunning))
                {
                    if (runningAppNames.Contains(step.SkipIfApplicationRunning))
                    {
                        _loggingService.LogInfoAsync($"Skipping step '{step.DisplayName}' because '{step.SkipIfApplicationRunning}' is running").Wait();
                        return false; // Skip this step
                    }
                }

                return true; // No skip condition, always execute
            };
        }

        /// <summary>
        /// Validates that a workflow is suitable for a given account.
        /// Checks for required steps and compatibility.
        /// </summary>
        /// <returns>Tuple with (IsValid, ValidationErrors)</returns>
        public async Task<(bool IsValid, List<string> ValidationErrors)> ValidateWorkflowForAccountAsync(
            WorkflowDefinition workflow,
            PlayOnlineMemberAccount account)
        {
            var validationErrors = new List<string>();

            // Basic workflow validation
            if (!workflow.Validate(out var workflowErrors))
            {
                validationErrors.AddRange(workflowErrors);
                return (false, validationErrors);
            }

            // Get executable steps for this account
            var conditionEvaluator = await CreateConditionEvaluatorAsync(account);
            var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

            // Check for required steps based on account configuration
            if (executableSteps.Count == 0)
            {
                validationErrors.Add("Workflow has no executable steps for this account");
                return (false, validationErrors);
            }

            // Validate OTP step requirement
            if (account.IsOTPEnabled)
            {
                var hasOTPStep = executableSteps.Any(s =>
                    s.StepId.Contains("otp", StringComparison.OrdinalIgnoreCase) ||
                    s.DisplayName.Contains("OTP", StringComparison.OrdinalIgnoreCase));

                if (!hasOTPStep)
                {
                    await _loggingService.LogWarningAsync($"Workflow '{workflow.Name}' may not support OTP for account with OTP enabled");
                    // Don't fail validation, just warn - user might have a custom flow
                }
            }

            await _loggingService.LogDebugAsync($"Workflow '{workflow.Name}' validated successfully for account '{account.AccountName}'");
            return (validationErrors.Count == 0, validationErrors);
        }

        /// <summary>
        /// Estimates the total duration of a workflow for a given account.
        /// Considers only executable steps (respects conditions and enabled status).
        /// </summary>
        public async Task<int> EstimateWorkflowDurationAsync(WorkflowDefinition workflow, PlayOnlineMemberAccount account)
        {
            var conditionEvaluator = await CreateConditionEvaluatorAsync(account);
            var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

            return executableSteps.Sum(s => s.EstimatedDurationSeconds);
        }

        /// <summary>
        /// Creates a human-readable summary of what steps will execute for an account.
        /// Useful for displaying to users what the workflow will do.
        /// </summary>
        public async Task<string> GetWorkflowSummaryAsync(
            WorkflowDefinition workflow,
            PlayOnlineMemberAccount account)
        {
            var conditionEvaluator = await CreateConditionEvaluatorAsync(account);
            var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

            var summary = $"Workflow: {workflow.Name}\n";
            summary += $"Description: {workflow.Description}\n";
            summary += $"Steps to execute: {executableSteps.Count}\n";
            summary += $"Estimated duration: ~{await EstimateWorkflowDurationAsync(workflow, account)} seconds\n\n";
            summary += "Steps:\n";

            for (int i = 0; i < executableSteps.Count; i++)
            {
                var step = executableSteps[i];
                summary += $"  {i + 1}. {step.DisplayName}";
                if (step.IsOptional)
                    summary += " (optional)";
                summary += $" - ~{step.EstimatedDurationSeconds}s\n";
            }

            await _loggingService.LogDebugAsync($"Generated workflow summary for '{workflow.Name}'");
            return summary;
        }
    }
}
