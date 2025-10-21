using FFXIManager.Infrastructure;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;

namespace FFXIManager.ViewModels.WorkflowEditor
{
    /// <summary>
    /// Manages workflow step operations for the Workflow Editor.
    /// Handles step CRUD operations and reordering within workflows.
    /// </summary>
    public class WorkflowEditorStepManager
    {
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;

        public WorkflowEditorStepManager(
            ILoggingService loggingService,
            IDialogService dialogService,
            IUiDispatcher uiDispatcher)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        }

        /// <summary>
        /// Adds a new step to the workflow
        /// </summary>
        /// <returns>The newly created step</returns>
        public async Task<WorkflowStepDefinition> AddStepAsync(WorkflowDefinition workflow)
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));

            try
            {
                var newStep = new WorkflowStepDefinition
                {
                    StepId = Guid.NewGuid().ToString(),
                    DisplayName = "New Step",
                    Description = "Configure this step",
                    Order = workflow.Steps.Count,
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 5,
                    MaxRetryAttempts = 3,
                    TemplatePath = string.Empty
                };

                await _uiDispatcher.InvokeAsync(() =>
                {
                    workflow.AddStep(newStep);
                });

                await _loggingService.LogInfoAsync("Added new step to workflow");

                return newStep;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error adding step", ex);
                await _dialogService.ShowMessageDialogAsync("Add Failed", $"Failed to add step: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Removes the specified step from the workflow after confirmation
        /// </summary>
        /// <returns>True if step was removed, false if cancelled or failed</returns>
        public async Task<bool> RemoveStepAsync(WorkflowDefinition workflow, WorkflowStepDefinition step)
        {
            if (workflow == null || step == null) return false;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Remove Step",
                    $"Are you sure you want to remove '{step.DisplayName}'?");

                if (result)
                {
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        workflow.RemoveStep(step);
                    });

                    await _loggingService.LogInfoAsync("Removed step from workflow");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error removing step", ex);
                await _dialogService.ShowMessageDialogAsync("Remove Failed", $"Failed to remove step: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Moves the specified step up in the workflow order
        /// </summary>
        public bool MoveStepUp(WorkflowDefinition workflow, WorkflowStepDefinition step)
        {
            if (workflow == null || step == null) return false;

            try
            {
                workflow.MoveStep(step, step.Order - 1);
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving step up", ex);
                return false;
            }
        }

        /// <summary>
        /// Moves the specified step down in the workflow order
        /// </summary>
        public bool MoveStepDown(WorkflowDefinition workflow, WorkflowStepDefinition step)
        {
            if (workflow == null || step == null) return false;

            try
            {
                workflow.MoveStep(step, step.Order + 1);
                return true;
            }
            catch (Exception ex)
            {
                _ = _loggingService.LogErrorAsync("Error moving step down", ex);
                return false;
            }
        }

        /// <summary>
        /// Logs editing of a step (step properties are bound directly to UI)
        /// </summary>
        public async Task EditStepAsync(WorkflowStepDefinition step)
        {
            if (step == null) return;

            try
            {
                // Step properties are bound directly to UI, so edits are automatic
                await _loggingService.LogDebugAsync($"Editing step: {step.DisplayName}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error editing step", ex);
            }
        }
    }
}
