using System.Collections.Generic;

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
            ["TemplatePath"] = "Path to template PNG image for screen detection\nLeave empty for keyboard-only navigation",
            ["EstimatedDurationSeconds"] = "Expected time for this step to complete\nUsed for timeout calculation and progress estimation",

            // Step Properties - Retry Configuration
            ["MaxRetryAttempts"] = "Number of detection attempts before giving up\nExample: 15 attempts = check screen 15 times at 1-second intervals\n\nRecommended: 10-15 for stable screens, 20-30 for slow transitions",

            ["ScreenshotRetryCount"] = "Screenshot capture retries per detection attempt\nEach detection attempt will retry screenshot capture this many times\n\nDefault: Auto-calculated (MaxRetryAttempts ÷ 3, minimum 5)\nIncrease if screenshots fail due to slow window rendering\n\nExample with MaxRetryAttempts=15, ScreenshotRetryCount=5:\n- 15 detection attempts × 6 screenshot attempts = 90 total captures",

            ["RetryAttempts"] = "Template detection attempts (LaunchApplication steps only)\nUsed when waiting for application UI to become ready\n\nDefault: 30 attempts × 500ms = 15 seconds",

            ["RetryDelayMs"] = "Delay between template detection retry attempts\nDefault: 500ms (half second)",

            // Step Properties - Template Matching
            ["ConfidenceThreshold"] = "How closely screenshot must match template (0.0-1.0)\n\n• 0.90+  = Very strict (exact match required)\n• 0.80-0.85 = Balanced (recommended)\n• 0.70-0.75 = Lenient (handles minor variations)\n• < 0.70 = Very lenient (may match incorrectly)\n\nDefault: 0.80 (80% confidence)",

            ["Tolerance"] = "Position tolerance in pixels for template matching\nAllows template to shift slightly between screenshots\n\nDefault: 5 pixels\nIncrease if UI elements move slightly during animation",

            // Step Properties - Conditional Execution
            ["Condition"] = "Optional condition for step execution\n\nExamples:\n• Account.IsOTPEnabled - only if OTP is enabled\n• !Account.UseWindower - only if Windower is disabled\n• POLProxyDetected - only if POL Proxy is detected\n\nLeave empty to always execute",

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
            public static string ScreenshotRetryCount => Get("ScreenshotRetryCount");
            public static string EstimatedDurationSeconds => Get("EstimatedDurationSeconds");
            public static string ConfidenceThreshold => Get("ConfidenceThreshold");
            public static string Tolerance => Get("Tolerance");
            public static string TemplatePath => Get("TemplatePath");
            public static string Condition => Get("Condition");
        }
    }
}
