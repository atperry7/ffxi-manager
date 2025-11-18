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
        /// Imports a workflow from a JSON file
        /// </summary>
        /// <returns>The imported workflow, or null if import failed/cancelled</returns>
        public async Task<WorkflowDefinition?> ImportWorkflowAsync()
        {
            try
            {
                // Create OpenFileDialog
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import Workflow",
                    Filter = "Workflow Files (*.json)|*.json|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                // Show dialog
                if (openFileDialog.ShowDialog() != true)
                {
                    await _loggingService.LogInfoAsync("Workflow import cancelled by user");
                    return null;
                }

                // Read JSON from file
                var json = await System.IO.File.ReadAllTextAsync(openFileDialog.FileName);

                if (string.IsNullOrWhiteSpace(json))
                {
                    await _dialogService.ShowMessageDialogAsync("Import Failed", "The selected file is empty");
                    return null;
                }

                // Import workflow (assigns new GUID, sets IsDefault=false, IsReadOnly=false)
                var importedWorkflow = await _workflowService.ImportWorkflowAsync(json, CancellationToken.None);

                // Validate workflow
                if (!_workflowService.ValidateWorkflow(importedWorkflow, out var errors))
                {
                    await _dialogService.ShowMessageDialogAsync("Validation Failed",
                        $"Imported workflow validation failed:\n\n{string.Join("\n", errors)}");
                    return null;
                }

                // Save the imported workflow
                var success = await _workflowService.SaveWorkflowAsync(importedWorkflow, CancellationToken.None);

                if (!success)
                {
                    await _dialogService.ShowMessageDialogAsync("Import Failed", "Failed to save imported workflow");
                    return null;
                }

                await _loggingService.LogInfoAsync($"Imported workflow '{importedWorkflow.Name}' from {openFileDialog.FileName}");

                // Check for missing templates
                var missingTemplates = await ValidateWorkflowTemplatesAsync(importedWorkflow);

                // Show appropriate success message
                if (missingTemplates.Count > 0)
                {
                    var missingList = string.Join("\n", missingTemplates.Select(t => $"  • {t}"));

                    // Combined warning with folder open prompt
                    var openFolder = await _dialogService.ShowConfirmationDialogAsync(
                        "Import Successful (With Warnings)",
                        $"Workflow '{importedWorkflow.Name}' has been imported successfully.\n\n" +
                        $"⚠️ Warning: The following template images are missing:\n\n{missingList}\n\n" +
                        $"Steps using these templates may fail during execution.\n\n" +
                        $"Please copy the missing template PNG files to:\n" +
                        $"%APPDATA%\\FFXIManager\\workflows\\templates\\\n\n" +
                        $"Would you like to open the templates folder in Windows Explorer to add the missing templates?");

                    if (openFolder)
                    {
                        await OpenTemplatesFolderAsync();
                    }
                }
                else
                {
                    await _dialogService.ShowMessageDialogAsync("Import Successful",
                        $"Workflow '{importedWorkflow.Name}' has been imported successfully.\n\n" +
                        $"All template images are available.");
                }

                return importedWorkflow;
            }
            catch (System.Text.Json.JsonException ex)
            {
                await _loggingService.LogErrorAsync("Invalid JSON format in workflow file", ex);
                await _dialogService.ShowMessageDialogAsync("Import Failed",
                    $"The selected file contains invalid JSON:\n\n{ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error importing workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Import Failed", $"Failed to import workflow: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates that all template images referenced by a workflow exist
        /// </summary>
        /// <returns>List of missing template file names</returns>
        private async Task<List<string>> ValidateWorkflowTemplatesAsync(WorkflowDefinition workflow)
        {
            var missingTemplates = new List<string>();

            try
            {
                // Get template directory path
                var templatesDir = GetTemplatesFolderPath();

                // Check step-level templates
                foreach (var step in workflow.Steps)
                {
                    if (!string.IsNullOrWhiteSpace(step.TemplatePath))
                    {
                        // Ensure .png extension (following TemplateManagementService pattern)
                        var fileName = step.TemplatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            ? step.TemplatePath
                            : step.TemplatePath + ".png";

                        var templatePath = System.IO.Path.Combine(templatesDir, fileName);
                        if (!System.IO.File.Exists(templatePath) && !missingTemplates.Contains(step.TemplatePath))
                        {
                            missingTemplates.Add(step.TemplatePath);
                        }
                    }

                    // Check action-level templates within navigation sequence
                    if (step.Navigation?.Sequence != null)
                    {
                        foreach (var action in step.Navigation.Sequence)
                        {
                            // Template path stored in Parameters dictionary
                            var actionTemplatePath = action.GetParameter<string>("TemplatePath", null);
                            if (!string.IsNullOrWhiteSpace(actionTemplatePath))
                            {
                                // Ensure .png extension (following TemplateManagementService pattern)
                                var fileName = actionTemplatePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                                    ? actionTemplatePath
                                    : actionTemplatePath + ".png";

                                var templatePath = System.IO.Path.Combine(templatesDir, fileName);
                                if (!System.IO.File.Exists(templatePath) && !missingTemplates.Contains(actionTemplatePath))
                                {
                                    missingTemplates.Add(actionTemplatePath);
                                }
                            }
                        }
                    }
                }

                if (missingTemplates.Count > 0)
                {
                    await _loggingService.LogWarningAsync($"Workflow '{workflow.Name}' is missing {missingTemplates.Count} template(s): {string.Join(", ", missingTemplates)}");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error validating workflow templates", ex);
            }

            return missingTemplates;
        }

        /// <summary>
        /// Exports a workflow to a JSON file
        /// </summary>
        /// <param name="workflow">The workflow to export</param>
        /// <returns>True if exported successfully, false otherwise</returns>
        public async Task<bool> ExportWorkflowAsync(WorkflowDefinition workflow)
        {
            if (workflow == null)
            {
                await _dialogService.ShowMessageDialogAsync("Export Failed", "No workflow selected");
                return false;
            }

            try
            {
                // Create SaveFileDialog
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Workflow",
                    Filter = "Workflow Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".json",
                    FileName = SanitizeFileName(workflow.Name)
                };

                // Show dialog
                if (saveFileDialog.ShowDialog() != true)
                {
                    await _loggingService.LogInfoAsync("Workflow export cancelled by user");
                    return false;
                }

                // Export workflow to JSON string
                var json = await _workflowService.ExportWorkflowAsync(workflow.WorkflowId, CancellationToken.None);

                // Write to file
                await System.IO.File.WriteAllTextAsync(saveFileDialog.FileName, json);

                await _loggingService.LogInfoAsync($"Exported workflow '{workflow.Name}' to {saveFileDialog.FileName}");

                // Combined success message with folder open prompt
                var openFolder = await _dialogService.ShowConfirmationDialogAsync(
                    "Export Successful",
                    $"Workflow '{workflow.Name}' has been exported successfully.\n\n" +
                    $"⚠️ Important: Template images are NOT included in the export.\n\n" +
                    $"If sharing this workflow with others, you must manually copy template PNG files from:\n" +
                    $"%APPDATA%\\FFXIManager\\workflows\\templates\\\n\n" +
                    $"Would you like to open the templates folder in Windows Explorer now?");

                if (openFolder)
                {
                    await OpenTemplatesFolderAsync();
                }

                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error exporting workflow", ex);
                await _dialogService.ShowMessageDialogAsync("Export Failed", $"Failed to export workflow: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sanitizes a file name by removing invalid characters
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", fileName.Split(invalidChars));
            return string.IsNullOrWhiteSpace(sanitized) ? "workflow" : sanitized;
        }

        /// <summary>
        /// Gets the templates folder path
        /// </summary>
        private string GetTemplatesFolderPath()
        {
            return System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "workflows",
                "templates");
        }

        /// <summary>
        /// Opens the templates folder in Windows Explorer
        /// </summary>
        private async Task OpenTemplatesFolderAsync()
        {
            try
            {
                var templatesPath = GetTemplatesFolderPath();

                // Create directory if it doesn't exist
                if (!System.IO.Directory.Exists(templatesPath))
                {
                    System.IO.Directory.CreateDirectory(templatesPath);
                }

                // Open in Explorer
                System.Diagnostics.Process.Start("explorer.exe", templatesPath);
                await _loggingService.LogInfoAsync($"Opened templates folder: {templatesPath}");
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Error opening templates folder", ex);
                await _dialogService.ShowMessageDialogAsync("Error",
                    $"Failed to open templates folder: {ex.Message}");
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
