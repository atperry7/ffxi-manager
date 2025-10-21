namespace FFXIManager.Views
{
    /// <summary>
    /// Centralized tooltip and help text for Workflow Editor UI fields.
    /// Makes it easy to maintain consistent help text across the application.
    /// </summary>
    public static class WorkflowEditorTooltips
    {
        /// <summary>
        /// Tooltip dictionary for all workflow editor fields.
        /// Key: Field identifier (e.g., "MaxRetryAttempts")
        /// Value: Tooltip help text
        /// </summary>
        public static readonly Dictionary<string, string> FieldHelp = new()
        {
            // Step Properties - Basic
            ["DisplayName"] = "Human-readable name shown in the UI during execution",
            ["Description"] = "Detailed description of what this step accomplishes",
            ["TemplatePath"] = "Template PNG filename for detection\nFolder: %APPDATA%/FFXIManager/workflows/templates (flat)\nFormat: name or name.png\nLeave empty for keyboard-only navigation",
            ["EstimatedDurationSeconds"] = "Estimated time for this step\nSizes detection timeouts and progress\nNot a hard cap (SubtaskTimeoutSeconds applies)",

            // Step Properties - Retry Configuration
            ["MaxRetryAttempts"] = "Step-level retry budget if the subtask fails\nApplied by the task executor (not per detection attempt)\nDefault: 3 (0 to disable)",

            ["RetryAttempts"] = "Template detection attempts (wait loops)\nUsed when waiting for a screen or action readiness\nDefault baseline: 30",

            ["RetryDelayMs"] = "Delay between template detection attempts\nDefault: 500ms (half second)",

            // Step Properties - Template Matching
            ["ConfidenceThreshold"] = "How closely screenshot must match template (0.0-1.0)\n\n• 0.90+  = Very strict (exact match required)\n• 0.80-0.85 = Balanced (recommended)\n• 0.70-0.75 = Lenient (handles minor variations)\n• < 0.70 = Very lenient (may match incorrectly)\n\nDefault: 0.80 (80% confidence)",

            ["Tolerance"] = "Position tolerance in pixels for template matching\nAllows template to shift slightly between screenshots\n\nDefault: 5 pixels\nIncrease if UI elements move slightly during animation",

            // Step Properties - Conditional Execution
            ["SkipIfApplicationRunning"] = "Skip this step if the application is already running\nName must match External Applications settings\nEvaluated at task-build time (snapshot), not re-checked during execution",

            // Step Properties - Flags
            ["IsEnabled"] = "Uncheck to skip this step entirely\nDisabled steps are never executed",

            ["IsOptional"] = "Check if step failure should not abort the workflow\nOptional steps can be skipped on failure",

            // Step Properties - Application Launch
            ["ApplicationName"] = "Name of external application to launch\nMust match entry in External Applications settings\n\nExamples: POL Proxy, Windower, Ashita",

            ["AllowSkipIfNotConfigured"] = "Skip step if application not found in settings\nUncheck to make application required (workflow fails if not configured)",

            ["AllowSkipIfRunning"] = "Skip step if application already running\nUncheck to force relaunch even if already running",

            // Navigation Properties
            ["PostNavigationDelayMs"] = "Delay after completing navigation sequence\nAllows UI to settle before next step\n\nRecommended: 500-1500ms",

            ["NavigationDescription"] = "Human-readable description of navigation actions\nHelps understand what the navigation sequence does",

            // Workflow Properties
            ["WorkflowName"] = "Unique name for this workflow\nWill be shown in workflow selection dropdown",

            ["WorkflowDescription"] = "Detailed description of workflow purpose\nHelps users understand when to use this workflow",

            ["WorkflowVersion"] = "Semantic version (e.g., 1.0.0, 2.1.3)\nIncrement when making changes for tracking",

            ["WorkflowIsDefault"] = "Mark as default workflow\nDefault workflow is pre-selected when adding characters to queue",

            ["WorkflowTags"] = "Comma-separated tags for organization\nExamples: playonline, windower, otp, custom"
        };

        /// <summary>
        /// Gets tooltip text for a specific field.
        /// Returns empty string if field not found.
        /// </summary>
        public static string Get(string fieldName)
        {
            return FieldHelp.TryGetValue(fieldName, out var tooltip) ? tooltip : string.Empty;
        }

        /// <summary>
        /// Quick access properties for common tooltips
        /// </summary>
        public static class Step
        {
            public static string MaxRetryAttempts => Get("MaxRetryAttempts");
            public static string EstimatedDurationSeconds => Get("EstimatedDurationSeconds");
            public static string ConfidenceThreshold => Get("ConfidenceThreshold");
            public static string Tolerance => Get("Tolerance");
            public static string TemplatePath => Get("TemplatePath");
        }
    }
}
