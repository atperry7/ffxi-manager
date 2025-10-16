using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FFXIManager.Models.AutoLogin
{
    /// <summary>
    /// Defines a single step within an auto-login workflow.
    /// Each step represents an atomic operation in the login sequence (e.g., member selection, password entry).
    /// </summary>
    /// <remarks>
    /// Steps are data-driven and configurable, allowing users to customize the login flow
    /// without modifying code. Each step links to a template that defines the UI detection
    /// and navigation actions to perform.
    /// </remarks>
    public class WorkflowStepDefinition : INotifyPropertyChanged
    {
        private string _stepId = string.Empty;
        private string _displayName = string.Empty;
        private string _description = string.Empty;
        private int _order;
        private string _templatePath = string.Empty;
        private bool _isEnabled = true;
        private bool _isOptional;
        private string? _condition;
        private int _estimatedDurationSeconds = 5;

        /// <summary>
        /// Unique identifier for this step within the workflow.
        /// Used for reference and tracking. Should be lowercase with underscores (e.g., "member_selection").
        /// </summary>
        public string StepId
        {
            get => _stepId;
            set => SetProperty(ref _stepId, value);
        }

        /// <summary>
        /// Human-readable name displayed in UI (e.g., "Member Selection")
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        /// <summary>
        /// Detailed description of what this step accomplishes
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// Execution order within the workflow (0-based).
        /// Steps are executed in ascending order.
        /// </summary>
        public int Order
        {
            get => _order;
            set => SetProperty(ref _order, value);
        }

        /// <summary>
        /// Path to the template image used for screen detection.
        /// Format: "Application/template_name" (e.g., "PlayOnline/member_selection_screen")
        /// OPTIONAL: Can be empty if step uses keyboard-only navigation without screen detection.
        /// </summary>
        public string TemplatePath
        {
            get => _templatePath;
            set => SetProperty(ref _templatePath, value);
        }

        /// <summary>
        /// Whether this step is enabled for execution.
        /// Disabled steps are skipped entirely during workflow execution.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Whether this step is optional and can be skipped if it fails.
        /// If true, step failure won't abort the entire workflow.
        /// </summary>
        public bool IsOptional
        {
            get => _isOptional;
            set => SetProperty(ref _isOptional, value);
        }

        /// <summary>
        /// Optional conditional expression that determines if this step should execute.
        /// Examples: "Account.IsOTPEnabled", "!Account.UseWindower"
        /// Null or empty means always execute (if enabled).
        /// </summary>
        public string? Condition
        {
            get => _condition;
            set => SetProperty(ref _condition, value);
        }

        /// <summary>
        /// Estimated duration for this step in seconds.
        /// Used for progress estimation and timeout calculation.
        /// </summary>
        public int EstimatedDurationSeconds
        {
            get => _estimatedDurationSeconds;
            set => SetProperty(ref _estimatedDurationSeconds, value);
        }

        /// <summary>
        /// Alternative template paths to try if the primary template fails detection.
        /// Provides fallback options for handling UI variations or different resolutions.
        /// </summary>
        public List<string> FallbackTemplatePaths { get; set; } = new();

        /// <summary>
        /// Navigation sequence to execute for this step (keyboard actions, clicks, delays).
        /// **PRIMARY NAVIGATION**: Defines how to interact with the UI once the screen is detected.
        ///
        /// **Use Cases:**
        /// - Navigation + TemplatePath: Wait for screen detection, then execute navigation
        /// - Navigation only (no template): Blind keyboard navigation without detection
        /// - TemplatePath only (no navigation): Passive detection without interaction
        ///
        /// **Workflow-First Architecture:**
        /// Navigation is defined per workflow step, allowing different workflows to use
        /// the same template with different navigation sequences. This eliminates the need
        /// for separate template metadata files.
        /// </summary>
        public NavigationAction? Navigation { get; set; }

        /// <summary>
        /// Associated LoginTaskStep for backward compatibility with existing handlers.
        /// If specified, existing specialized handlers can claim this step.
        /// If null, step uses DynamicWorkflowHandler.
        /// </summary>
        public LoginTaskStep? LegacyTaskStep { get; set; }

        /// <summary>
        /// Maximum number of retry attempts for this step if it fails.
        /// Default is 3. Set to 0 to disable retries.
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Additional metadata for extensibility.
        /// Can store custom properties without modifying the model.
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

        /// <summary>
        /// Validates that this step definition is complete and valid
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(StepId))
                errors.Add("StepId is required");

            if (string.IsNullOrWhiteSpace(DisplayName))
                errors.Add("DisplayName is required");

            // TemplatePath is now optional - step can be navigation-only
            // But if neither TemplatePath nor Navigation is specified, warn
            if (string.IsNullOrWhiteSpace(TemplatePath) && Navigation == null)
                errors.Add("Step must have either TemplatePath (for detection) or Navigation (for interaction) or both");

            if (Order < 0)
                errors.Add("Order must be non-negative");

            if (EstimatedDurationSeconds < 1)
                errors.Add("EstimatedDurationSeconds must be at least 1");

            if (MaxRetryAttempts < 0)
                errors.Add("MaxRetryAttempts must be non-negative");

            return errors.Count == 0;
        }

        /// <summary>
        /// Creates a deep clone of this step definition
        /// </summary>
        public WorkflowStepDefinition Clone()
        {
            return new WorkflowStepDefinition
            {
                StepId = StepId,
                DisplayName = DisplayName,
                Description = Description,
                Order = Order,
                TemplatePath = TemplatePath,
                IsEnabled = IsEnabled,
                IsOptional = IsOptional,
                Condition = Condition,
                EstimatedDurationSeconds = EstimatedDurationSeconds,
                FallbackTemplatePaths = new List<string>(FallbackTemplatePaths),
                Navigation = Navigation,
                LegacyTaskStep = LegacyTaskStep,
                MaxRetryAttempts = MaxRetryAttempts,
                Metadata = new Dictionary<string, object>(Metadata)
            };
        }
    }
}
