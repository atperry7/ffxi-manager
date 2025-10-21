using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models.AutoLogin
{
    /// <summary>
    /// Defines a complete auto-login workflow as an ordered sequence of steps.
    /// Workflows are data-driven configurations that replace the hardcoded login sequence,
    /// enabling users to customize the login flow without modifying code.
    /// </summary>
    /// <remarks>
    /// **Design Philosophy:**
    /// - Workflows are composable and reusable configurations
    /// - Each workflow defines its own step sequence, conditions, and navigation
    /// - Multiple workflows can exist, allowing different flows for different scenarios
    /// - Workflows can be shared, versioned, and migrated
    ///
    /// **Common Workflow Examples:**
    /// - "Standard Windower + PlayOnline" - Default flow for Windower users
    /// - "Direct PlayOnline" - No Windower, direct POL launch
    /// - "POL Proxy Optimized" - Streamlined flow when using POL Proxy
    /// - "No OTP Flow" - Skips OTP entry for accounts without 2FA
    /// </remarks>
    public class WorkflowDefinition : INotifyPropertyChanged
    {
        private Guid _workflowId;
        private string _name = string.Empty;
        private string _description = string.Empty;
        private string _version = "1.0.0";
        private bool _isDefault;
        private bool _isReadOnly;
        private DateTime _createdDate;
        private DateTime _lastModifiedDate;

        /// <summary>
        /// Unique identifier for this workflow.
        /// Used for referencing workflows in account configurations and persistence.
        /// </summary>
        public Guid WorkflowId
        {
            get => _workflowId;
            set => SetProperty(ref _workflowId, value);
        }

        /// <summary>
        /// Human-readable name for this workflow (e.g., "Standard Windower Flow")
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Detailed description of what this workflow accomplishes and when to use it
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// Semantic version of this workflow (e.g., "1.2.0")
        /// Used for compatibility checking and migration
        /// </summary>
        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        /// <summary>
        /// Whether this is the default workflow used when no account-specific workflow is specified
        /// </summary>
        public bool IsDefault
        {
            get => _isDefault;
            set => SetProperty(ref _isDefault, value);
        }

        /// <summary>
        /// Whether this workflow is read-only (system-provided, cannot be modified).
        /// Users can clone read-only workflows to create customized versions.
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set => SetProperty(ref _isReadOnly, value);
        }

        /// <summary>
        /// When this workflow was created
        /// </summary>
        public DateTime CreatedDate
        {
            get => _createdDate;
            set => SetProperty(ref _createdDate, value);
        }

        /// <summary>
        /// When this workflow was last modified
        /// </summary>
        public DateTime LastModifiedDate
        {
            get => _lastModifiedDate;
            set => SetProperty(ref _lastModifiedDate, value);
        }

        /// <summary>
        /// Ordered collection of steps that comprise this workflow.
        /// Steps are filtered by IsEnabled and any evaluator-provided conditions (e.g., application-based skip),
        /// then executed in ascending Order.
        ///
        /// Notes:
        /// - Template assets are PNGs stored in %APPDATA%/FFXIManager/workflows/templates (flat folder).
        /// - All detection thresholds/navigation are defined at the step or per-action level (workflow-first).
        /// </summary>
        public ObservableCollection<WorkflowStepDefinition> Steps { get; set; } = new();

        /// <summary>
        /// Tags for categorizing and filtering workflows (e.g., "windower", "pol-proxy", "no-otp")
        /// </summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>
        /// Author of this workflow (for shared/community workflows)
        /// </summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Additional metadata for extensibility
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Computed Properties

        /// <summary>
        /// Total estimated duration of the workflow in seconds
        /// </summary>
        public int TotalEstimatedDurationSeconds => Steps.Sum(s => s.EstimatedDurationSeconds);

        /// <summary>
        /// Number of enabled steps in this workflow
        /// </summary>
        public int EnabledStepCount => Steps.Count(s => s.IsEnabled);

        /// <summary>
        /// Number of optional steps in this workflow
        /// </summary>
        public int OptionalStepCount => Steps.Count(s => s.IsOptional);

        /// <summary>
        /// Display string for workflow duration (e.g., "~45 seconds")
        /// </summary>
        public string DurationDisplay
        {
            get
            {
                var seconds = TotalEstimatedDurationSeconds;
                return seconds >= 60
                    ? $"~{seconds / 60}m {seconds % 60}s"
                    : $"~{seconds}s";
            }
        }

        /// <summary>
        /// Comma-separated display string of tags for UI binding
        /// </summary>
        public string TagsDisplay
        {
            get => string.Join(", ", Tags);
            set
            {
                var newTags = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                Tags.Clear();
                Tags.AddRange(newTags);
                OnPropertyChanged();
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// Validates that this workflow is complete and valid
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (WorkflowId == Guid.Empty)
                errors.Add("WorkflowId cannot be empty");

            if (string.IsNullOrWhiteSpace(Name))
                errors.Add("Name is required");

            if (string.IsNullOrWhiteSpace(Version))
                errors.Add("Version is required");

            if (Steps.Count == 0)
                errors.Add("Workflow must have at least one step");

            // Check for duplicate step IDs
            var duplicateSteps = Steps
                .GroupBy(s => s.StepId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateSteps.Any())
                errors.Add($"Duplicate step IDs found: {string.Join(", ", duplicateSteps)}");

            // Check for duplicate orders
            var duplicateOrders = Steps
                .GroupBy(s => s.Order)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateOrders.Any())
                errors.Add($"Duplicate step orders found: {string.Join(", ", duplicateOrders)}");

            // Validate each step
            foreach (var step in Steps)
            {
                if (!step.Validate(out var stepErrors))
                {
                    errors.Add($"Step '{step.StepId}' validation failed:");
                    errors.AddRange(stepErrors.Select(e => $"  - {e}"));
                }
            }

            return errors.Count == 0;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Creates a deep clone of this workflow with a new ID
        /// </summary>
        public WorkflowDefinition Clone(string? newName = null)
        {
            var cloned = new WorkflowDefinition
            {
                WorkflowId = Guid.NewGuid(),
                Name = newName ?? $"{Name} (Copy)",
                Description = Description,
                Version = Version,
                IsDefault = false,
                IsReadOnly = false,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow,
                Author = Author,
                Tags = new List<string>(Tags),
                Metadata = new Dictionary<string, object>(Metadata)
            };

            foreach (var step in Steps)
            {
                cloned.Steps.Add(step.Clone());
            }

            return cloned;
        }

        /// <summary>
        /// Reorders steps based on their Order property
        /// </summary>
        public void ReorderSteps()
        {
            var ordered = Steps.OrderBy(s => s.Order).ToList();
            Steps.Clear();
            foreach (var step in ordered)
            {
                Steps.Add(step);
            }
        }

        /// <summary>
        /// Gets steps that should execute based on enabled status and conditions.
        /// The optional evaluator can implement snapshot-time skip logic (e.g., SkipIfApplicationRunning),
        /// which is applied during task building.
        /// </summary>
        /// <param name="conditionEvaluator">Function to evaluate conditional logic (receives full step for application-based checks)</param>
        /// <returns>Filtered list of executable steps</returns>
        public List<WorkflowStepDefinition> GetExecutableSteps(Func<WorkflowStepDefinition, bool>? conditionEvaluator = null)
        {
            return Steps
                .Where(s => s.IsEnabled)
                .Where(s => conditionEvaluator?.Invoke(s) ?? true)
                .OrderBy(s => s.Order)
                .ToList();
        }

        /// <summary>
        /// Finds a step by its StepId
        /// </summary>
        public WorkflowStepDefinition? FindStep(string stepId)
        {
            return Steps.FirstOrDefault(s => s.StepId.Equals(stepId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Adds a step and assigns it the next available order
        /// </summary>
        public void AddStep(WorkflowStepDefinition step)
        {
            var maxOrder = Steps.Any() ? Steps.Max(s => s.Order) : -1;
            step.Order = maxOrder + 1;
            Steps.Add(step);
            LastModifiedDate = DateTime.UtcNow;
        }

        /// <summary>
        /// Removes a step by StepId
        /// </summary>
        public bool RemoveStep(string stepId)
        {
            var step = FindStep(stepId);
            if (step == null) return false;

            Steps.Remove(step);
            LastModifiedDate = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Removes a step by reference
        /// </summary>
        public bool RemoveStep(WorkflowStepDefinition step)
        {
            if (step == null) return false;

            var removed = Steps.Remove(step);
            if (removed)
            {
                LastModifiedDate = DateTime.UtcNow;
            }
            return removed;
        }

        /// <summary>
        /// Moves a step to a new order position
        /// </summary>
        public void MoveStep(WorkflowStepDefinition step, int newOrder)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            // Ensure new order is within valid range
            newOrder = Math.Clamp(newOrder, 0, Steps.Count - 1);

            var currentOrder = step.Order;
            if (currentOrder == newOrder) return;

            // Update orders for affected steps
            if (newOrder < currentOrder)
            {
                // Moving up - shift steps down
                foreach (var s in Steps.Where(s => s.Order >= newOrder && s.Order < currentOrder))
                {
                    s.Order++;
                }
            }
            else
            {
                // Moving down - shift steps up
                foreach (var s in Steps.Where(s => s.Order > currentOrder && s.Order <= newOrder))
                {
                    s.Order--;
                }
            }

            step.Order = newOrder;
            ReorderSteps();
            LastModifiedDate = DateTime.UtcNow;
        }

        #endregion
    }
}
