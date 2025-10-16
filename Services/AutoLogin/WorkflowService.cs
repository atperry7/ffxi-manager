using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Configuration;
using FFXIManager.Models;
using FFXIManager.Models.AutoLogin;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Service implementation for managing auto-login workflow definitions.
    /// Uses JSON file storage in application data directory with in-memory caching.
    /// </summary>
    public class WorkflowService : IWorkflowService
    {
        private readonly ILoggingService _loggingService;
        private readonly ISettingsService _settingsService;
        private readonly string _workflowsDirectory;
        private readonly Dictionary<Guid, WorkflowDefinition> _workflowCache = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public WorkflowService(ILoggingService loggingService, ISettingsService settingsService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            // Use application data directory for workflow storage
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "FFXIManager");
            _workflowsDirectory = Path.Combine(appDirectory, "workflows");

            // Ensure directory exists
            Directory.CreateDirectory(_workflowsDirectory);

            // Initialize system workflows on first run
            _ = EnsureSystemWorkflowsAsync();
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

            // Get all JSON files in workflows directory (including defaults subdirectory)
            var files = Directory.GetFiles(_workflowsDirectory, "*.json", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var workflow = JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonOptions);

                    if (workflow != null)
                    {
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

            // TODO: Add WorkflowId property to PlayOnlineMemberAccount model
            // For now, always use default workflow
            return await GetDefaultWorkflowAsync(cancellationToken);
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

        public async Task<bool> WorkflowExistsAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            var filePath = GetWorkflowFilePath(workflowId);
            return File.Exists(filePath);
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
                // Determine source and destination directories
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var defaultWorkflowsSource = Path.Combine(appPath, "workflows", "defaults");

                if (!Directory.Exists(defaultWorkflowsSource))
                {
                    await _loggingService.LogWarningAsync($"Default workflows directory not found: {defaultWorkflowsSource}");
                    return 0;
                }

                var defaultWorkflowsDestination = Path.Combine(_workflowsDirectory, "defaults");
                Directory.CreateDirectory(defaultWorkflowsDestination);

                // Copy all default workflow JSON files, forcing overwrite
                var sourceFiles = Directory.GetFiles(defaultWorkflowsSource, "*.json");
                if (sourceFiles.Length == 0)
                {
                    await _loggingService.LogWarningAsync("No default workflow files found in application directory");
                    return 0;
                }

                int restoredCount = 0;
                foreach (var sourceFile in sourceFiles)
                {
                    var fileName = Path.GetFileName(sourceFile);
                    var destFile = Path.Combine(defaultWorkflowsDestination, fileName);

                    // Force copy/overwrite
                    File.Copy(sourceFile, destFile, overwrite: true);
                    restoredCount++;
                    await _loggingService.LogDebugAsync($"Restored default workflow: {fileName}");
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
        /// Copies them from the application directory to APPDATA if they don't exist yet.
        /// </summary>
        private async Task EnsureSystemWorkflowsAsync()
        {
            try
            {
                // Determine application directory containing default workflows
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                var defaultWorkflowsSource = Path.Combine(appPath, "workflows", "defaults");

                if (!Directory.Exists(defaultWorkflowsSource))
                {
                    await _loggingService.LogWarningAsync($"Default workflows directory not found: {defaultWorkflowsSource}");
                    return;
                }

                // Destination: APPDATA/FFXIManager/workflows/defaults/
                var defaultWorkflowsDestination = Path.Combine(_workflowsDirectory, "defaults");
                Directory.CreateDirectory(defaultWorkflowsDestination);

                // Copy all default workflow JSON files
                var sourceFiles = Directory.GetFiles(defaultWorkflowsSource, "*.json");
                if (sourceFiles.Length == 0)
                {
                    await _loggingService.LogWarningAsync("No default workflow files found in application directory");
                    return;
                }

                int copiedCount = 0;
                foreach (var sourceFile in sourceFiles)
                {
                    var fileName = Path.GetFileName(sourceFile);
                    var destFile = Path.Combine(defaultWorkflowsDestination, fileName);

                    // Copy if doesn't exist, or if source is newer
                    if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(destFile))
                    {
                        File.Copy(sourceFile, destFile, overwrite: true);
                        copiedCount++;
                        await _loggingService.LogDebugAsync($"Copied default workflow: {fileName}");
                    }
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
        /// Creates the default "Standard Windower" workflow that matches the current hardcoded flow.
        /// </summary>
        private WorkflowDefinition CreateStandardWindowerWorkflow()
        {
            var workflow = new WorkflowDefinition
            {
                WorkflowId = Guid.NewGuid(),
                Name = "Standard Windower Flow",
                Description = "Standard auto-login flow using Windower → PlayOnline → FFXI",
                Version = "1.0.0",
                IsDefault = true,
                IsReadOnly = true,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow,
                Author = "System",
                Tags = new List<string> { "windower", "standard", "default" }
            };

            // Build steps matching current LoginTaskStep.GetMainSteps() order
            var steps = new[]
            {
                new WorkflowStepDefinition
                {
                    StepId = "launch_pol_proxy",
                    DisplayName = "Launch POL Proxy",
                    Description = "Launch POL Proxy if configured",
                    Order = 0,
                    TemplatePath = "POLProxy/pol_proxy_detection",
                    IsEnabled = true,
                    IsOptional = true,
                    EstimatedDurationSeconds = 2,
                    LegacyTaskStep = LoginTaskStep.LaunchPOLProxy
                },
                new WorkflowStepDefinition
                {
                    StepId = "launch_windower",
                    DisplayName = "Launch Windower",
                    Description = "Launch Windower application",
                    Order = 1,
                    TemplatePath = "Windower/launch_arrow",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 3,
                    LegacyTaskStep = LoginTaskStep.LaunchWindower
                },
                new WorkflowStepDefinition
                {
                    StepId = "member_selection",
                    DisplayName = "Member Selection",
                    Description = "Select PlayOnline member slot",
                    Order = 2,
                    TemplatePath = "PlayOnline/member_selection_screen",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 3,
                    LegacyTaskStep = LoginTaskStep.MemberSelection
                },
                new WorkflowStepDefinition
                {
                    StepId = "password_entry",
                    DisplayName = "Password Entry",
                    Description = "Enter PlayOnline password",
                    Order = 3,
                    TemplatePath = "PlayOnline/login_information_screen",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 2,
                    LegacyTaskStep = LoginTaskStep.PasswordEntry
                },
                new WorkflowStepDefinition
                {
                    StepId = "otp_entry",
                    DisplayName = "OTP Entry",
                    Description = "Enter one-time password (if enabled)",
                    Order = 4,
                    TemplatePath = "PlayOnline/connect_button",
                    IsEnabled = true,
                    IsOptional = true,
                    Condition = "Account.IsOTPEnabled",
                    EstimatedDurationSeconds = 4,
                    LegacyTaskStep = LoginTaskStep.OTPEntry
                },
                new WorkflowStepDefinition
                {
                    StepId = "terms_acceptance",
                    DisplayName = "Terms Acceptance",
                    Description = "Accept FFXI terms and conditions",
                    Order = 5,
                    TemplatePath = "FFXI/accept_terms_button",
                    IsEnabled = true,
                    IsOptional = true,
                    EstimatedDurationSeconds = 2,
                    LegacyTaskStep = LoginTaskStep.TermsAcceptance
                },
                new WorkflowStepDefinition
                {
                    StepId = "character_selection",
                    DisplayName = "Character Selection",
                    Description = "Select FFXI character",
                    Order = 6,
                    TemplatePath = "FFXI/character_selection_screen",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 3,
                    LegacyTaskStep = LoginTaskStep.CharacterSelection
                },
                new WorkflowStepDefinition
                {
                    StepId = "character_slot_pick",
                    DisplayName = "Character Slot Pick",
                    Description = "Pick character slot number",
                    Order = 7,
                    TemplatePath = "FFXI/character_slot_selection",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 2,
                    LegacyTaskStep = LoginTaskStep.CharacterSlotPick
                },
                new WorkflowStepDefinition
                {
                    StepId = "confirm_login",
                    DisplayName = "Confirm Login",
                    Description = "Confirm character login",
                    Order = 8,
                    TemplatePath = "FFXI/login_confirmation",
                    IsEnabled = true,
                    IsOptional = false,
                    EstimatedDurationSeconds = 3,
                    LegacyTaskStep = LoginTaskStep.ConfirmLogin
                }
            };

            foreach (var step in steps)
            {
                workflow.Steps.Add(step);
            }

            return workflow;
        }
    }
}
