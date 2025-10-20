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
        /// Path (filename) of the template PNG used for screen detection.
        /// Location: %APPDATA%/FFXIManager/workflows/templates
        /// Format: "template_name" or "template_name.png" (no application prefix; flat folder)
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

        private string? _skipIfApplicationRunning;

        /// <summary>
        /// Name of external application that, if running, causes this step to be skipped.
        /// Uses same application names from External Applications settings (e.g., "POL Proxy", "Windower").
        /// Null or empty means no application-based skip condition.
        /// </summary>
        /// <remarks>
        /// Example: Steps for "Play Screen" navigation can be skipped if POL Proxy is running,
        /// since POL Proxy bypasses the PlayOnline play screen.
        /// </remarks>
        public string? SkipIfApplicationRunning
        {
            get => _skipIfApplicationRunning;
            set => SetProperty(ref _skipIfApplicationRunning, value);
        }

        /// <remarks>
        /// Evaluation timing: snapshot at task-build time (WorkflowTaskBuilder), not re-evaluated during execution.
        /// </remarks>

        /// <summary>
        /// Estimated duration for this step in seconds.
        /// Used for progress estimation and to size detection timeouts.
        /// </summary>
        /// <remarks>
        /// This is not a hard cap across the entire step. Overall per-subtask timeout
        /// is enforced by AutoLoginTaskExecutor.SubtaskTimeoutSeconds. Detection operations
        /// within the step use this value to derive their own time budgets.
        /// </remarks>
        public int EstimatedDurationSeconds
        {
            get => _estimatedDurationSeconds;
            set => SetProperty(ref _estimatedDurationSeconds, value);
        }

        /// <summary>
        /// Alternative template filenames to try if the primary template fails detection.
        /// Files are resolved in the shared templates folder.
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


        private int _maxRetryAttempts = 3;
        private int? _retryAttempts;
        private int? _retryDelayMs;
        private float _confidenceThreshold = 0.8f;
        private int _tolerance = 5;

        /// <summary>
        /// Maximum number of retry attempts for this step if the subtask fails.
        /// Applied by the task executor across whole-step failures, not per detection attempt.
        /// Default is 3. Set to 0 to disable step-level retries.
        /// </summary>
        public int MaxRetryAttempts
        {
            get => _maxRetryAttempts;
            set => SetProperty(ref _maxRetryAttempts, value);
        }

        /// <summary>
        /// Number of retry attempts for template detection waits.
        /// Used when waiting for a screen to appear or action-level readiness checks.
        /// Default baseline is 30 for step-level detection; action/executor logic may override.
        /// </summary>
        public int? RetryAttempts
        {
            get => _retryAttempts;
            set => SetProperty(ref _retryAttempts, value);
        }

        /// <summary>
        /// Delay in milliseconds between template detection retry attempts.
        /// Default is 500ms. Action-level detection may override per action.
        /// </summary>
        public int? RetryDelayMs
        {
            get => _retryDelayMs;
            set => SetProperty(ref _retryDelayMs, value);
        }

        /// <summary>
        /// Confidence threshold for template matching (0.0 to 1.0).
        /// Determines how closely the screenshot must match the template to be considered a match.
        /// Higher values require more precise matches, lower values are more lenient.
        /// Default: 0.8 (80% confidence)
        /// </summary>
        /// <remarks>
        /// Workflow-first: thresholds are defined in the workflow (can be overridden per action).
        /// </remarks>
        public float ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => SetProperty(ref _confidenceThreshold, value);
        }

        /// <summary>
        /// Position tolerance in pixels for template matching.
        /// Allows for minor UI element position variations between screenshots.
        /// Default: 5 pixels
        /// </summary>
        /// <remarks>
        /// Workflow-first: tolerance is defined in the workflow (can be overridden per action).
        /// </remarks>
        public int Tolerance
        {
            get => _tolerance;
            set => SetProperty(ref _tolerance, value);
        }

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

            // Validate that at least one of the following is present:
            // - TemplatePath (for screen detection)
            // - Navigation (for UI interaction)
            // This allows pure detection steps, pure action steps, or combined detection+action steps
            if (string.IsNullOrWhiteSpace(TemplatePath) && (Navigation == null || Navigation.Sequence == null || Navigation.Sequence.Count == 0))
                errors.Add("Workflow steps must have either TemplatePath (for detection) or Navigation sequence (for actions) or both");

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
                SkipIfApplicationRunning = SkipIfApplicationRunning,
                EstimatedDurationSeconds = EstimatedDurationSeconds,
                FallbackTemplatePaths = new List<string>(FallbackTemplatePaths),
                Navigation = Navigation,
                MaxRetryAttempts = MaxRetryAttempts,
                RetryAttempts = RetryAttempts,
                RetryDelayMs = RetryDelayMs,
                ConfidenceThreshold = ConfidenceThreshold,
                Tolerance = Tolerance,
                Metadata = new Dictionary<string, object>(Metadata)
            };
        }
    }
}
