namespace FFXIManager.Models.Settings
{
    /// Step-level performance aggregation for insights and recommendations.
    public class StepPerformanceEntry
    {
        public string StepId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        // Run outcomes
        public int Runs { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public int Skips { get; set; }

        // End-to-end step timing
        public System.TimeSpan TotalDuration { get; set; }
        public System.TimeSpan AverageDuration { get; set; }

        // Phase-level timing breakdown
        public System.TimeSpan TotalWindowDiscoveryTime { get; set; }
        public System.TimeSpan AverageWindowDiscoveryTime { get; set; }
        public int WindowDiscoveryAttempts { get; set; }
        public int WindowDiscoverySuccessfulFirstAttempts { get; set; }

        public System.TimeSpan TotalDetectionTime { get; set; }
        public System.TimeSpan AverageDetectionTime { get; set; }
        public int DetectionAttempts { get; set; }

        public System.TimeSpan TotalNavigationTime { get; set; }
        public System.TimeSpan AverageNavigationTime { get; set; }

        // Detection metrics (successful detections)
        public int Detections { get; set; }
        public double TotalDetectionSeconds { get; set; }
        public double AverageDetectionSeconds { get; set; }
        public double TotalConfidence { get; set; }
        public double AverageConfidence { get; set; }

        // Detection failure metrics
        public int DetectionFailures { get; set; }
        public int FallbackTemplateUsages { get; set; }

        // Skip tracking
        public Dictionary<string, int> SkipReasons { get; set; } = new();

        // Action-level statistics aggregated at step level
        public Dictionary<string, ActionPerformanceEntry> ActionPerformance { get; set; } = new();
    }
}

