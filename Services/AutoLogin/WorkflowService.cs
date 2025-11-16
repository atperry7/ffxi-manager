using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;
using System.IO;
using System.Text.Json;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service implementation for managing auto-login workflow definitions.
    /// Uses JSON file storage in application data directory with in-memory caching.
    /// </summary>
    public class WorkflowService : IWorkflowService, IDisposable
    {
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;
        private readonly IExternalApplicationService _externalApplicationService;
        private readonly string _workflowsDirectory;
        private readonly Dictionary<Guid, WorkflowDefinition> _workflowCache = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private bool _disposed;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        public WorkflowService(
            ILoggingService loggingService,
            ISettingsService settingsService,
            IExternalApplicationService externalApplicationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));

            // Use application data directory for workflow storage
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "FFXIManager");
            _workflowsDirectory = Path.Combine(appDirectory, "workflows");

            // Ensure directory exists
            Directory.CreateDirectory(_workflowsDirectory);

            // Initialize system workflows on first run
            // Note: Must be called synchronously to ensure workflows are deployed before use
            EnsureSystemWorkflowsSync();
        }

        public async Task<WorkflowDefinition?> LoadWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            // Check cache first
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (_workflowCache.TryGetValue(workflowId, out var cachedWorkflow))
                {
                    return cachedWorkflow;
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            // Load from file
            var filePath = GetWorkflowFilePath(workflowId);
            if (!File.Exists(filePath))
            {
                await _loggingService.LogDebugAsync($"Workflow file not found: {filePath}");
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);

                if (workflow != null)
                {
                    // Migrate application identity to GUID-based if needed
                    var migrated = await MigrateWorkflowApplicationIdsAsync(workflow, cancellationToken);
                    if (migrated)
                    {
                        // Best-effort save to persist migration
                        _ = SaveWorkflowAsync(workflow, cancellationToken);
                    }
                    // Add to cache
                    await _cacheLock.WaitAsync(cancellationToken);
                    try
                    {
                        _workflowCache[workflowId] = workflow;
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }

                    await _loggingService.LogDebugAsync($"Loaded workflow: {workflow.Name} ({workflowId})");
                }

                return workflow;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to load workflow {workflowId}", ex);
                return null;
            }
        }

        public async Task<IList<WorkflowDefinition>> GetAvailableWorkflowsAsync(CancellationToken cancellationToken = default)
        {
            var workflows = new List<WorkflowDefinition>();

            // Ensure directory exists
            Directory.CreateDirectory(_workflowsDirectory);

            // Get all JSON files in workflows directory (top level only - flat structure)
            // Note: Changed from SearchOption.AllDirectories to TopDirectoryOnly to avoid
            // loading duplicate workflows from legacy workflows/defaults/ subdirectory
            var files = Directory.GetFiles(_workflowsDirectory, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);

                    if (workflow != null)
                    {
                        // Migrate application identity to GUID-based if needed
                        var migrated = await MigrateWorkflowApplicationIdsAsync(workflow, cancellationToken);
                        if (migrated)
                        {
                            _ = SaveWorkflowAsync(workflow, cancellationToken);
                        }
                        workflows.Add(workflow);

                        // Update cache
                        await _cacheLock.WaitAsync(cancellationToken);
                        try
                        {
                            _workflowCache[workflow.WorkflowId] = workflow;
                        }
                        finally
                        {
                            _cacheLock.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _loggingService.LogErrorAsync($"Failed to load workflow from {file}", ex);
                }
            }

            await _loggingService.LogInfoAsync($"Loaded {workflows.Count} workflows from {_workflowsDirectory}");
            return workflows.OrderBy(w => w.Name).ToList();
        }

        public async Task<bool> SaveWorkflowAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default)
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));

            if (workflow.IsReadOnly)
                throw new InvalidOperationException($"Cannot save read-only workflow: {workflow.Name}");

            // Validate before saving
            if (!ValidateWorkflow(workflow, out var errors))
            {
                await _loggingService.LogErrorAsync($"Workflow validation failed for {workflow.Name}: {string.Join(", ", errors)}");
                return false;
            }

            try
            {
                // Migrate application identity (ensures newly created/edited workflows persist GUIDs)
                await MigrateWorkflowApplicationIdsAsync(workflow, cancellationToken);

                // Update metadata
                workflow.LastModifiedDate = DateTime.UtcNow;

                // Serialize to JSON
                var json = JsonSerializer.Serialize(workflow, JsonOptions);

                // Save to file
                var filePath = GetWorkflowFilePath(workflow.WorkflowId);
                await File.WriteAllTextAsync(filePath, json, cancellationToken);

                // Update cache
                await _cacheLock.WaitAsync(cancellationToken);
                try
                {
                    _workflowCache[workflow.WorkflowId] = workflow;
                }
                finally
                {
                    _cacheLock.Release();
                }

                await _loggingService.LogInfoAsync($"Saved workflow: {workflow.Name} ({workflow.WorkflowId})");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to save workflow {workflow.Name}", ex);
                return false;
            }
        }

        /// <summary>
        /// Ensures Launch actions include ApplicationId (GUID) parameter by resolving from ApplicationName.
        /// Returns true if any changes were made.
        /// </summary>
        private async Task<bool> MigrateWorkflowApplicationIdsAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
        {
            if (workflow?.Steps == null || workflow.Steps.Count == 0)
                return false;

            var applications = await _externalApplicationService.GetApplicationsAsync();
            bool changed = false;

            foreach (var step in workflow.Steps)
            {
                var seq = step?.Navigation?.Sequence;
                if (seq == null || seq.Count == 0) continue;

                foreach (var action in seq)
                {
                    if (!string.Equals(action.Action, "Launch", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var idStr = action.GetParameter<string>("ApplicationId", string.Empty);
                    if (!string.IsNullOrWhiteSpace(idStr))
                        continue; // already set

                    var name = action.GetParameter<string>("ApplicationName", string.Empty);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var app = applications.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (app != null && app.Id != Guid.Empty)
                    {
                        action.SetParameter("ApplicationId", app.Id.ToString());
                        // Normalize name to current value (in case of capitalization updates)
                        action.SetParameter("ApplicationName", app.Name);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        public async Task<bool> DeleteWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            // Load workflow to check if it's deletable
            var workflow = await LoadWorkflowAsync(workflowId, cancellationToken);
            if (workflow == null)
            {
                await _loggingService.LogWarningAsync($"Cannot delete workflow {workflowId}: not found");
                return false;
            }

            if (workflow.IsReadOnly)
                throw new InvalidOperationException($"Cannot delete read-only workflow: {workflow.Name}");

            if (workflow.IsDefault)
                throw new InvalidOperationException($"Cannot delete default workflow: {workflow.Name}. Set another workflow as default first.");

            try
            {
                var filePath = GetWorkflowFilePath(workflowId);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Remove from cache
                await _cacheLock.WaitAsync(cancellationToken);
                try
                {
                    _workflowCache.Remove(workflowId);
                }
                finally
                {
                    _cacheLock.Release();
                }

                await _loggingService.LogInfoAsync($"Deleted workflow: {workflow.Name} ({workflowId})");
                return true;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to delete workflow {workflowId}", ex);
                return false;
            }
        }

        public bool ValidateWorkflow(WorkflowDefinition workflow, out List<string> errors)
        {
            return workflow.Validate(out errors);
        }

        public async Task<WorkflowDefinition> GetDefaultWorkflowAsync(CancellationToken cancellationToken = default)
        {
            var workflows = await GetAvailableWorkflowsAsync(cancellationToken);

            // Find explicitly marked default
            var defaultWorkflow = workflows.FirstOrDefault(w => w.IsDefault);

            // Fall back to first workflow if no default marked
            if (defaultWorkflow == null)
            {
                defaultWorkflow = workflows.FirstOrDefault();
            }

            if (defaultWorkflow == null)
            {
                throw new InvalidOperationException("No workflows available. System workflows may not have been initialized.");
            }

            return defaultWorkflow;
        }

        public async Task<WorkflowDefinition> GetWorkflowForAccountAsync(PlayOnlineMemberAccount account, CancellationToken cancellationToken = default)
        {
            if (account == null)
                throw new ArgumentNullException(nameof(account));

            // Check if account has a custom workflow assigned
            if (account.WorkflowId.HasValue && account.WorkflowId.Value != Guid.Empty)
            {
                var workflow = await LoadWorkflowAsync(account.WorkflowId.Value, cancellationToken);
                if (workflow != null)
                {
                    await _loggingService.LogDebugAsync($"Using custom workflow '{workflow.Name}' for account '{account.AccountName}'");
                    return workflow;
                }

                // Custom workflow not found, log warning and fall back to default
                await _loggingService.LogWarningAsync($"Custom workflow {account.WorkflowId} not found for account '{account.AccountName}', falling back to default workflow");
            }

            // Use default workflow
            var defaultWorkflow = await GetDefaultWorkflowAsync(cancellationToken);
            await _loggingService.LogDebugAsync($"Using default workflow '{defaultWorkflow.Name}' for account '{account.AccountName}'");
            return defaultWorkflow;
        }

        public WorkflowDefinition CreateNewWorkflow(string name, string? description = null)
        {
            return new WorkflowDefinition
            {
                WorkflowId = Guid.NewGuid(),
                Name = name,
                Description = description ?? string.Empty,
                Version = "1.0.0",
                IsDefault = false,
                IsReadOnly = false,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow,
                Author = Environment.UserName
            };
        }

        public async Task<WorkflowDefinition> CloneWorkflowAsync(Guid workflowId, string newName, CancellationToken cancellationToken = default)
        {
            var source = await LoadWorkflowAsync(workflowId, cancellationToken);
            if (source == null)
                throw new ArgumentException($"Workflow {workflowId} not found", nameof(workflowId));

            return source.Clone(newName);
        }

        public async Task<string> ExportWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            var workflow = await LoadWorkflowAsync(workflowId, cancellationToken);
            if (workflow == null)
                throw new ArgumentException($"Workflow {workflowId} not found", nameof(workflowId));

            return JsonSerializer.Serialize(workflow, JsonOptions);
        }

        public async Task<WorkflowDefinition> ImportWorkflowAsync(string workflowJson, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(workflowJson))
                throw new ArgumentException("Workflow JSON cannot be empty", nameof(workflowJson));

            try
            {
                var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(workflowJson, JsonOptions);
                if (workflow == null)
                    throw new ArgumentException("Failed to deserialize workflow JSON");

                // Assign new ID to avoid conflicts
                workflow.WorkflowId = Guid.NewGuid();
                workflow.IsDefault = false;
                workflow.IsReadOnly = false;
                workflow.CreatedDate = DateTime.UtcNow;
                workflow.LastModifiedDate = DateTime.UtcNow;

                await _loggingService.LogInfoAsync($"Imported workflow: {workflow.Name}");
                return workflow;
            }
            catch (JsonException ex)
            {
                await _loggingService.LogErrorAsync("Failed to import workflow: invalid JSON", ex);
                throw new ArgumentException("Invalid workflow JSON format", nameof(workflowJson), ex);
            }
        }

        public Task<bool> WorkflowExistsAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            var filePath = GetWorkflowFilePath(workflowId);
            return Task.FromResult(File.Exists(filePath));
        }

        public async Task<IList<WorkflowDefinition>> GetWorkflowsByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
        {
            var allWorkflows = await GetAvailableWorkflowsAsync(cancellationToken);
            var tagList = tags.ToList();

            return allWorkflows
                .Where(w => w.Tags.Any(t => tagList.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }

        public async Task<bool> SetDefaultWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            var allWorkflows = await GetAvailableWorkflowsAsync(cancellationToken);
            var targetWorkflow = allWorkflows.FirstOrDefault(w => w.WorkflowId == workflowId);

            if (targetWorkflow == null)
            {
                await _loggingService.LogWarningAsync($"Cannot set default: workflow {workflowId} not found");
                return false;
            }

            // Clear default flag from all workflows
            foreach (var workflow in allWorkflows)
            {
                if (workflow.IsDefault)
                {
                    workflow.IsDefault = false;
                    await SaveWorkflowAsync(workflow, cancellationToken);
                }
            }

            // Set target as default
            targetWorkflow.IsDefault = true;
            return await SaveWorkflowAsync(targetWorkflow, cancellationToken);
        }

        public async Task<int> RefreshWorkflowsAsync(CancellationToken cancellationToken = default)
        {
            // Clear cache
            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                _workflowCache.Clear();
            }
            finally
            {
                _cacheLock.Release();
            }

            // Reload all workflows
            var workflows = await GetAvailableWorkflowsAsync(cancellationToken);
            return workflows.Count;
        }

        public string GetWorkflowFilePath(Guid workflowId)
        {
            return Path.Combine(_workflowsDirectory, $"{workflowId}.json");
        }

        public async Task<int> RestoreDefaultWorkflowsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // Determine source directory (flat structure - workflows at root)
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var defaultWorkflowsSource = Path.Combine(appPath, "workflows");

                if (!Directory.Exists(defaultWorkflowsSource))
                {
                    await _loggingService.LogWarningAsync($"Default workflows directory not found: {defaultWorkflowsSource}");
                    return 0;
                }

                // Copy all default workflow JSON files to root workflows/ directory using GUID filenames
                // Skip README.md and other non-workflow files
                var sourceFiles = Directory.GetFiles(defaultWorkflowsSource, "*.json")
                    .Where(f => !f.EndsWith("README.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (sourceFiles.Length == 0)
                {
                    await _loggingService.LogWarningAsync("No default workflow files found in application directory");
                    return 0;
                }

                int restoredCount = 0;
                foreach (var sourceFile in sourceFiles)
                {
                    // Read the JSON to extract the WorkflowId
                    var json = await File.ReadAllTextAsync(sourceFile);
                    var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);

                    if (workflow == null)
                    {
                        await _loggingService.LogWarningAsync($"Failed to deserialize workflow from {sourceFile}");
                        continue;
                    }

                    // Destination: workflows/{guid}.json (flat structure)
                    var destFile = GetWorkflowFilePath(workflow.WorkflowId);

                    // Force copy/overwrite to restore defaults
                    File.Copy(sourceFile, destFile, overwrite: true);
                    restoredCount++;
                    await _loggingService.LogDebugAsync($"Restored default workflow: {workflow.Name} to {workflow.WorkflowId}.json");
                }

                // Also restore template PNG files to shared workflows/templates/ directory (flat structure)
                var templatesSource = Path.Combine(defaultWorkflowsSource, "templates");
                if (Directory.Exists(templatesSource))
                {
                    var templatesDestination = Path.Combine(_workflowsDirectory, "templates");
                    Directory.CreateDirectory(templatesDestination);

                    var templateFiles = Directory.GetFiles(templatesSource, "*.png");
                    foreach (var templateFile in templateFiles)
                    {
                        var fileName = Path.GetFileName(templateFile);
                        var destFile = Path.Combine(templatesDestination, fileName);

                        // Force copy/overwrite to restore templates
                        File.Copy(templateFile, destFile, overwrite: true);
                        await _loggingService.LogDebugAsync($"Restored template: {fileName}");
                    }

                    if (templateFiles.Length > 0)
                    {
                        await _loggingService.LogInfoAsync($"Restored {templateFiles.Length} workflow template(s)");
                    }
                }

                // Clear cache to force reload
                await _cacheLock.WaitAsync(cancellationToken);
                try
                {
                    _workflowCache.Clear();
                }
                finally
                {
                    _cacheLock.Release();
                }

                await _loggingService.LogInfoAsync($"Restored {restoredCount} default workflow(s)");
                return restoredCount;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to restore default workflows", ex);
                return 0;
            }
        }

        /// <summary>
        /// Ensures system-provided default workflows exist.
        /// Copies them from the application directory to APPDATA using GUID-based filenames in flat structure.
        /// Templates are shared in workflows/templates/ directory.
        /// </summary>
        private async Task EnsureSystemWorkflowsAsync()
        {
            try
            {
                // Determine application directory containing default workflows (flat structure)
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var defaultWorkflowsSource = Path.Combine(appPath, "workflows");

                if (!Directory.Exists(defaultWorkflowsSource))
                {
                    await _loggingService.LogWarningAsync($"Default workflows directory not found: {defaultWorkflowsSource}");
                    return;
                }

                // Copy all default workflow JSON files to root workflows/ directory using GUID filenames
                // Skip README.md and other non-workflow files
                var sourceFiles = Directory.GetFiles(defaultWorkflowsSource, "*.json")
                    .Where(f => !f.EndsWith("README.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (sourceFiles.Length == 0)
                {
                    await _loggingService.LogWarningAsync("No default workflow files found in application directory");
                    return;
                }

                int copiedCount = 0;
                foreach (var sourceFile in sourceFiles)
                {
                    // Read the JSON to extract the WorkflowId
                    var json = await File.ReadAllTextAsync(sourceFile);
                    var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);

                    if (workflow == null)
                    {
                        await _loggingService.LogWarningAsync($"Failed to deserialize workflow from {sourceFile}");
                        continue;
                    }

                    // Destination: workflows/{guid}.json (flat structure)
                    var destFile = GetWorkflowFilePath(workflow.WorkflowId);

                    // Copy if doesn't exist, or if source is newer
                    if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destFile))
                    {
                        File.Copy(sourceFile, destFile, overwrite: true);
                        copiedCount++;
                        await _loggingService.LogDebugAsync($"Copied default workflow: {workflow.Name} to {workflow.WorkflowId}.json");
                    }
                }

                // Copy template PNG files to workflows/templates/ (flat structure, shared by all workflows)
                var templatesSource = Path.Combine(defaultWorkflowsSource, "templates");
                if (Directory.Exists(templatesSource))
                {
                    var templatesDestination = Path.Combine(_workflowsDirectory, "templates");
                    Directory.CreateDirectory(templatesDestination);

                    var templateFiles = Directory.GetFiles(templatesSource, "*.png");
                    int templatesCopied = 0;

                    foreach (var templateFile in templateFiles)
                    {
                        var fileName = Path.GetFileName(templateFile);
                        var destFile = Path.Combine(templatesDestination, fileName);

                        // Copy if doesn't exist, or if source is newer
                        if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(templateFile) > File.GetLastWriteTimeUtc(destFile))
                        {
                            File.Copy(templateFile, destFile, overwrite: true);
                            templatesCopied++;
                            await _loggingService.LogDebugAsync($"Copied template: {fileName}");
                        }
                    }

                    if (templatesCopied > 0)
                    {
                        await _loggingService.LogInfoAsync($"Initialized {templatesCopied} workflow template(s)");
                    }
                }
                else
                {
                    await _loggingService.LogWarningAsync($"Templates directory not found: {templatesSource}");
                }

                if (copiedCount > 0)
                {
                    await _loggingService.LogInfoAsync($"Initialized {copiedCount} default workflow(s)");
                }
                else
                {
                    await _loggingService.LogDebugAsync("Default workflows are up to date");
                }
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync("Failed to initialize system workflows", ex);
            }
        }

        /// <summary>
        /// Synchronous version of EnsureSystemWorkflowsAsync for constructor initialization.
        /// Ensures system-provided default workflows exist by copying from application directory to APPDATA.
        /// </summary>
        private void EnsureSystemWorkflowsSync()
        {
            try
            {
                // Determine application directory containing default workflows
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var defaultWorkflowsSource = Path.Combine(appPath, "workflows");

                if (!Directory.Exists(defaultWorkflowsSource))
                {
                    // Can't log yet - logging service may not be initialized
                    return;
                }

                // Copy all default workflow JSON files directly (no deserialization to avoid failures)
                var sourceFiles = Directory.GetFiles(defaultWorkflowsSource, "*.json")
                    .Where(f => !f.EndsWith("README.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var sourceFile in sourceFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(sourceFile);
                        var destFile = Path.Combine(_workflowsDirectory, fileName);

                        // Copy if doesn't exist, or if source is newer
                        if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destFile))
                        {
                            File.Copy(sourceFile, destFile, overwrite: true);
                        }
                    }
                    catch
                    {
                        // Silently continue - will be logged by async version if needed
                    }
                }

                // Copy template PNG files to workflows/templates/
                var templatesSource = Path.Combine(defaultWorkflowsSource, "templates");
                if (Directory.Exists(templatesSource))
                {
                    var templatesDestination = Path.Combine(_workflowsDirectory, "templates");
                    Directory.CreateDirectory(templatesDestination);

                    var templateFiles = Directory.GetFiles(templatesSource, "*.png");
                    foreach (var templateFile in templateFiles)
                    {
                        try
                        {
                            var fileName = Path.GetFileName(templateFile);
                            var destFile = Path.Combine(templatesDestination, fileName);

                            // Copy if doesn't exist, or if source is newer
                            if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(templateFile) > File.GetLastWriteTimeUtc(destFile))
                            {
                                File.Copy(templateFile, destFile, overwrite: true);
                            }
                        }
                        catch
                        {
                            // Silently continue
                        }
                    }
                }
            }
            catch
            {
                // Silently fail - logging service may not be initialized yet
                // The async version will log any issues during normal operation
            }
        }

        /// <summary>
        /// Disposes the WorkflowService and cleans up resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _cacheLock?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

    }
}
