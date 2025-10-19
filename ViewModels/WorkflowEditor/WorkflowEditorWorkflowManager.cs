using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models.AutoLogin;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin;

namespace FFXIManager.ViewModels.WorkflowEditor
{
    /// <summary>
    /// Manages workflow-level operations for the Workflow Editor.
    /// Handles workflow CRUD operations, import/export, and default workflow restoration.
    /// </summary>
    public class WorkflowEditorWorkflowManager
    {
        private readonly IWorkflowService _workflowService;
        private readonly ILoggingService _loggingService;
        private readonly IDialogService _dialogService;

        public WorkflowEditorWorkflowManager(
            IWorkflowService workflowService,
            ILoggingService loggingService,
            IDialogService dialogService)
        {
            _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        }

        /// <summary>
        /// Loads all workflows from the workflow service
        /// </summary>
        /// <returns>List of workflows, or empty list on error</returns>
        public async Task<List<WorkflowDefinition>> LoadWorkflowsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var workflows = await _workflowService.GetAvailableWorkflowsAsync(cancellationToken);
                await _loggingService.LogInfoAsync($"Loaded {workflows.Count} workflows");
                return workflows.ToList();
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error loading workflows", ex);
                await _dialogService.ShowMessageDialogAsync("Load Failed", $"Failed to load workflows: {ex.Message}");
                return new List<WorkflowDefinition>();
            }
        }

        /// <summary>
        /// Creates a new blank workflow
        /// </summary>
        /// <returns>The newly created workflow</returns>
        public async Task<WorkflowDefinition> CreateNewWorkflowAsync()
        {
            try
            {
                var newWorkflow = new WorkflowDefinition
                {
                    WorkflowId = Guid.NewGuid(),
                    Name = "New Workflow",
                    Description = "Custom workflow created by user",
                    Version = "1.0.0",
                    IsDefault = false,
                    IsReadOnly = false,
                    CreatedDate = DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow,
                    Steps = new System.Collections.ObjectModel.ObservableCollection<WorkflowStepDefinition>()
                };

                await _loggingService.LogInfoAsync("Created new workflow");
                return newWorkflow;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error creating workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Create Failed", $"Failed to create workflow: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Saves a workflow with automatic default-to-user conversion if needed
        /// </summary>
        /// <returns>Tuple of (success, workflowId, workflowName) - ID may change if converted from default</returns>
        public async Task<(bool success, Guid workflowId, string workflowName)> SaveWorkflowAsync(
            WorkflowDefinition workflow,
            CancellationToken cancellationToken)
        {
            if (workflow == null)
                return (false, Guid.Empty, string.Empty);

            try
            {
                // Validate before saving
                if (!_workflowService.ValidateWorkflow(workflow, out var errors))
                {
                    await _dialogService.ShowMessageDialogAsync("Validation Failed",
                        $"Workflow validation failed:\n{string.Join("\n", errors)}");
                    return (false, workflow.WorkflowId, workflow.Name);
                }

                // Check if this is a modified default workflow
                var expectedFilePath = _workflowService.GetWorkflowFilePath(workflow.WorkflowId);
                var isModifiedDefault = !System.IO.File.Exists(expectedFilePath) && workflow.IsDefault;

                if (isModifiedDefault)
                {
                    // Convert default workflow to user workflow
                    var oldId = workflow.WorkflowId;
                    var oldName = workflow.Name;

                    workflow.WorkflowId = Guid.NewGuid();
                    workflow.IsDefault = false;
                    workflow.Name = $"{workflow.Name} (Custom)";
                    workflow.CreatedDate = DateTime.UtcNow;
                    workflow.LastModifiedDate = DateTime.UtcNow;

                    await _loggingService.LogInfoAsync($"Converting default workflow '{oldName}' ({oldId}) to user workflow with new ID: {workflow.WorkflowId}");

                    // Inform user about the change
                    await _dialogService.ShowMessageDialogAsync("Workflow Converted",
                        $"The default workflow has been converted to a custom user workflow.\n\n" +
                        $"Original: {oldName}\n" +
                        $"New Name: {workflow.Name}\n\n" +
                        $"This prevents conflicts with the original default workflow.");
                }

                workflow.LastModifiedDate = DateTime.UtcNow;
                var success = await _workflowService.SaveWorkflowAsync(workflow, cancellationToken);

                if (success)
                {
                    await _loggingService.LogInfoAsync($"Saved workflow: {workflow.Name}");
                    await _dialogService.ShowMessageDialogAsync("Saved", "Workflow saved successfully");
                    return (true, workflow.WorkflowId, workflow.Name);
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Save Failed", "Failed to save workflow");
                    return (false, workflow.WorkflowId, workflow.Name);
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error saving workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Save Failed", $"Failed to save workflow: {ex.Message}");
                return (false, workflow.WorkflowId, workflow.Name);
            }
        }

        /// <summary>
        /// Deletes a workflow after confirmation
        /// </summary>
        /// <returns>True if deleted, false if cancelled or failed</returns>
        public async Task<bool> DeleteWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
        {
            if (workflow == null)
                return false;

            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Delete Workflow",
                    $"Are you sure you want to delete '{workflow.Name}'?\n\nThis action cannot be undone.");

                if (!result)
                    return false;

                var success = await _workflowService.DeleteWorkflowAsync(workflow.WorkflowId, cancellationToken);

                if (success)
                {
                    await _loggingService.LogInfoAsync("Deleted workflow");
                    return true;
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Delete Failed", "Failed to delete workflow");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error deleting workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Delete Failed", $"Failed to delete workflow: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clones a workflow with a new name
        /// </summary>
        /// <returns>The cloned workflow, or null on failure</returns>
        public async Task<WorkflowDefinition?> CloneWorkflowAsync(
            Guid workflowId,
            string newName,
            CancellationToken cancellationToken)
        {
            try
            {
                var clonedWorkflow = await _workflowService.CloneWorkflowAsync(
                    workflowId,
                    newName,
                    cancellationToken);

                await _loggingService.LogInfoAsync($"Cloned workflow: {clonedWorkflow.Name}");
                return clonedWorkflow;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error cloning workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Clone Failed", $"Failed to clone workflow: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Imports a workflow from file (not yet implemented)
        /// </summary>
        public async Task ImportWorkflowAsync()
        {
            try
            {
                await _dialogService.ShowMessageDialogAsync("Not Implemented", "Workflow import will be implemented in a future update");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error importing workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Import Failed", $"Failed to import workflow: {ex.Message}");
            }
        }

        /// <summary>
        /// Exports a workflow to file (not yet implemented)
        /// </summary>
        public async Task ExportWorkflowAsync()
        {
            try
            {
                await _dialogService.ShowMessageDialogAsync("Not Implemented", "Workflow export will be implemented in a future update");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error exporting workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Export Failed", $"Failed to export workflow: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores default workflows from application directory
        /// </summary>
        /// <returns>Number of workflows restored</returns>
        public async Task<int> RestoreDefaultWorkflowsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _dialogService.ShowConfirmationDialogAsync(
                    "Restore Defaults",
                    "This will restore all default workflows from the application directory, overwriting any changes you made to defaults.\n\nAre you sure?");

                if (!result)
                    return 0;

                var count = await _workflowService.RestoreDefaultWorkflowsAsync(cancellationToken);

                await _loggingService.LogInfoAsync($"Restored {count} default workflows");
                await _dialogService.ShowMessageDialogAsync("Restore Complete",
                    $"Successfully restored {count} default workflow(s).\n\nReloading workflows...");

                return count;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error restoring defaults", ex);
                await _dialogService.ShowMessageDialogAsync("Restore Failed",
                    $"Failed to restore default workflows: {ex.Message}");
                return 0;
            }
        }
    }
}
