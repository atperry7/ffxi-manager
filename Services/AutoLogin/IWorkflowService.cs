using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service for managing auto-login workflow definitions.
    /// Provides CRUD operations, validation, and resolution of workflows for accounts.
    /// </summary>
    /// <remarks>
    /// **Responsibilities:**
    /// - Load and save workflow definitions from/to persistent storage
    /// - Provide default workflows for common scenarios
    /// - Resolve account-specific workflows with fallback to defaults
    /// - Validate workflow integrity and step configuration
    /// - Support workflow import/export for sharing
    ///
    /// **Storage:**
    /// - Workflows stored as JSON files in application data directory
    /// - System-provided workflows are read-only
    /// - User-created workflows are editable
    /// - Workflow files: `workflows/{workflow-id}.json`
    /// </remarks>
    public interface IWorkflowService
    {
        /// <summary>
        /// Loads a workflow definition by its unique identifier.
        /// </summary>
        /// <param name="workflowId">Unique identifier of the workflow to load</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Workflow definition if found, null otherwise</returns>
        Task<WorkflowDefinition?> LoadWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all available workflow definitions.
        /// Includes both system-provided and user-created workflows.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of all available workflows</returns>
        Task<IList<WorkflowDefinition>> GetAvailableWorkflowsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a workflow definition to persistent storage.
        /// Updates LastModifiedDate automatically.
        /// </summary>
        /// <param name="workflow">Workflow definition to save</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if save succeeded, false otherwise</returns>
        /// <exception cref="InvalidOperationException">Thrown if workflow is read-only</exception>
        Task<bool> SaveWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a workflow definition from persistent storage.
        /// Cannot delete read-only or default workflows.
        /// </summary>
        /// <param name="workflowId">Unique identifier of the workflow to delete</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if deletion succeeded, false otherwise</returns>
        /// <exception cref="InvalidOperationException">Thrown if workflow is read-only or default</exception>
        Task<bool> DeleteWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates a workflow definition for completeness and correctness.
        /// Checks for required fields, duplicate step IDs, invalid orders, etc.
        /// </summary>
        /// <param name="workflow">Workflow definition to validate</param>
        /// <param name="errors">Output list of validation errors</param>
        /// <returns>True if workflow is valid, false if validation errors exist</returns>
        bool ValidateWorkflow(WorkflowDefinition workflow, out List<string> errors);

        /// <summary>
        /// Gets the default workflow used when no account-specific workflow is assigned.
        /// Falls back to the first available workflow if no default is explicitly marked.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Default workflow definition</returns>
        /// <exception cref="InvalidOperationException">Thrown if no workflows are available</exception>
        Task<WorkflowDefinition> GetDefaultWorkflowAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves the workflow to use for a specific account.
        /// Uses account-specific workflow if assigned, otherwise falls back to default.
        /// </summary>
        /// <param name="account">PlayOnline account to resolve workflow for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Resolved workflow definition for the account</returns>
        /// <exception cref="InvalidOperationException">Thrown if workflow resolution fails</exception>
        Task<WorkflowDefinition> GetWorkflowForAccountAsync(PlayOnlineMemberAccount account, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new workflow with a generated ID and default values.
        /// Workflow is not persisted until SaveWorkflowAsync is called.
        /// </summary>
        /// <param name="name">Name for the new workflow</param>
        /// <param name="description">Optional description</param>
        /// <returns>New workflow definition with empty steps collection</returns>
        WorkflowDefinition CreateNewWorkflow(string name, string? description = null);

        /// <summary>
        /// Creates a copy of an existing workflow with a new ID.
        /// Useful for creating customized versions of system workflows.
        /// </summary>
        /// <param name="workflowId">ID of workflow to clone</param>
        /// <param name="newName">Name for the cloned workflow</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Cloned workflow definition</returns>
        Task<WorkflowDefinition> CloneWorkflowAsync(Guid workflowId, string newName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports a workflow to JSON string for sharing or backup.
        /// </summary>
        /// <param name="workflowId">ID of workflow to export</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>JSON representation of the workflow</returns>
        Task<string> ExportWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Imports a workflow from JSON string.
        /// Assigns a new ID to avoid conflicts with existing workflows.
        /// </summary>
        /// <param name="workflowJson">JSON representation of the workflow</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Imported workflow definition (not yet saved)</returns>
        /// <exception cref="ArgumentException">Thrown if JSON is invalid</exception>
        Task<WorkflowDefinition> ImportWorkflowAsync(string workflowJson, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a workflow exists by ID.
        /// </summary>
        /// <param name="workflowId">Workflow ID to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if workflow exists, false otherwise</returns>
        Task<bool> WorkflowExistsAsync(Guid workflowId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets workflows filtered by tags.
        /// Useful for categorizing workflows (e.g., "windower", "pol-proxy", "no-otp").
        /// </summary>
        /// <param name="tags">Tags to filter by (any match)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Workflows that have at least one of the specified tags</returns>
        Task<IList<WorkflowDefinition>> GetWorkflowsByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a workflow as the default workflow.
        /// Clears default flag from all other workflows.
        /// </summary>
        /// <param name="workflowId">ID of workflow to set as default</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if operation succeeded, false otherwise</returns>
        Task<bool> SetDefaultWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the workflow cache by reloading all workflows from disk.
        /// Useful after external modifications to workflow files.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of workflows loaded</returns>
        Task<int> RefreshWorkflowsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the file path where a workflow is stored.
        /// Useful for direct file access or debugging.
        /// </summary>
        /// <param name="workflowId">Workflow ID</param>
        /// <returns>Full file path to the workflow JSON file</returns>
        string GetWorkflowFilePath(Guid workflowId);

        /// <summary>
        /// Restores default workflows from the application directory.
        /// Forces copy of all default workflows, overwriting existing defaults in APPDATA.
        /// Useful if user has modified defaults and wants to restore originals.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Number of workflows restored</returns>
        Task<int> RestoreDefaultWorkflowsAsync(CancellationToken cancellationToken = default);
    }
}
