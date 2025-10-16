using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Builds AutoLoginSubtasks from WorkflowDefinition.
    /// Replaces the static subtask creation logic with data-driven workflow-based generation.
    /// </summary>
    /// <remarks>
    /// **Responsibilities:**
    /// - Convert workflow definitions to executable subtask sequences
    /// - Evaluate conditional steps based on account properties
    /// - Support both legacy LoginTaskStep and modern WorkflowStepDefinition
    /// - Maintain backward compatibility with existing handlers
    ///
    /// **Design:**
    /// This builder acts as an adapter between the data-driven workflow system
    /// and the existing task execution infrastructure. It bridges the gap by
    /// creating subtasks that can be handled by both legacy handlers (via TaskStep)
    /// and the new DynamicWorkflowHandler (via WorkflowStep).
    /// </remarks>
    public class WorkflowTaskBuilder
    {
        private readonly ILoggingService _loggingService;

        public WorkflowTaskBuilder(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
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
            var conditionEvaluator = CreateConditionEvaluator(account);
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
        private AutoLoginSubtask CreateSubtaskFromStep(WorkflowStepDefinition step, int executionOrder)
        {
            var subtask = new AutoLoginSubtask
            {
                Name = step.DisplayName,
                Description = step.Description,
                ExecutionOrder = executionOrder,
                EstimatedDurationSeconds = step.EstimatedDurationSeconds,
                IsSkippable = step.IsOptional,
                MaxRetryAttempts = step.MaxRetryAttempts,
                WorkflowStep = step, // Link to workflow step for DynamicWorkflowHandler
                TaskStep = step.LegacyTaskStep ?? LoginTaskStep.None // Support legacy handlers
            };

            return subtask;
        }

        /// <summary>
        /// Creates a condition evaluator function for the given account.
        /// Evaluates conditional expressions in workflow steps.
        /// </summary>
        /// <remarks>
        /// **Supported Condition Syntax:**
        /// - Account.IsOTPEnabled
        /// - Account.UseWindower
        /// - !Account.IsOTPEnabled
        /// - Account.Region == "NA"
        ///
        /// Future enhancements could support more complex expressions via a parser.
        /// </remarks>
        private Func<string?, bool> CreateConditionEvaluator(PlayOnlineMemberAccount account)
        {
            return condition =>
            {
                if (string.IsNullOrWhiteSpace(condition))
                    return true; // No condition means always execute

                try
                {
                    return EvaluateCondition(condition, account);
                }
                catch (Exception ex)
                {
                    // Log warning but don't fail - default to executing the step
                    _loggingService.LogWarningAsync($"Failed to evaluate condition '{condition}': {ex.Message}").Wait();
                    return true;
                }
            };
        }

        /// <summary>
        /// Evaluates a conditional expression against an account.
        /// Simple property-based evaluation for Phase 2.
        /// </summary>
        /// <remarks>
        /// **Supported Properties:**
        /// - Account.IsOTPEnabled: bool
        /// - Account.HasStoredPassword: bool
        /// - Account.POLMemberSlot: int
        /// - Account.FFXICharacterSlot: int
        /// </remarks>
        private bool EvaluateCondition(string condition, PlayOnlineMemberAccount account)
        {
            // Normalize condition
            condition = condition.Trim();

            // Handle negation
            bool negate = false;
            if (condition.StartsWith("!"))
            {
                negate = true;
                condition = condition.Substring(1).Trim();
            }

            // Evaluate property-based conditions
            bool result = condition switch
            {
                // Boolean properties
                "Account.IsOTPEnabled" => account.IsOTPEnabled,
                "Account.HasStoredPassword" => account.HasStoredPassword,

                // Slot-based conditions
                var c when c.StartsWith("Account.POLMemberSlot ==") => EvaluatePOLSlotCondition(c, account),
                var c when c.StartsWith("Account.FFXICharacterSlot ==") => EvaluateFFXISlotCondition(c, account),

                // Add more conditions as needed
                _ => throw new NotSupportedException($"Unsupported condition: {condition}")
            };

            return negate ? !result : result;
        }

        /// <summary>
        /// Evaluates POL member slot conditions (e.g., "Account.POLMemberSlot == 1")
        /// </summary>
        private bool EvaluatePOLSlotCondition(string condition, PlayOnlineMemberAccount account)
        {
            var parts = condition.Split("==", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                return false;

            if (int.TryParse(parts[1], out var expectedSlot))
            {
                return account.POLMemberSlot == expectedSlot;
            }

            return false;
        }

        /// <summary>
        /// Evaluates FFXI character slot conditions (e.g., "Account.FFXICharacterSlot == 1")
        /// </summary>
        private bool EvaluateFFXISlotCondition(string condition, PlayOnlineMemberAccount account)
        {
            var parts = condition.Split("==", StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                return false;

            if (int.TryParse(parts[1], out var expectedSlot))
            {
                return account.FFXICharacterSlot == expectedSlot;
            }

            return false;
        }

        /// <summary>
        /// Builds subtasks using the legacy static method (for backward compatibility).
        /// This is used when no workflow is assigned to an account.
        /// </summary>
        public List<AutoLoginSubtask> BuildLegacySubtasks()
        {
            _loggingService.LogDebugAsync("Building legacy subtasks using static method").Wait();
            return AutoLoginSubtask.CreateStandardLoginSubtasks();
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
            var conditionEvaluator = CreateConditionEvaluator(account);
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
        public int EstimateWorkflowDuration(WorkflowDefinition workflow, PlayOnlineMemberAccount account)
        {
            var conditionEvaluator = CreateConditionEvaluator(account);
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
            var conditionEvaluator = CreateConditionEvaluator(account);
            var executableSteps = workflow.GetExecutableSteps(conditionEvaluator);

            var summary = $"Workflow: {workflow.Name}\n";
            summary += $"Description: {workflow.Description}\n";
            summary += $"Steps to execute: {executableSteps.Count}\n";
            summary += $"Estimated duration: ~{EstimateWorkflowDuration(workflow, account)} seconds\n\n";
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
