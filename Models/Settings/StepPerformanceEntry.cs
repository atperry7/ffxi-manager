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

        // End-to-end step timing
        public System.TimeSpan TotalDuration { get; set; }
        public System.TimeSpan AverageDuration { get; set; }

        // Detection metrics (final detections)
        public int Detections { get; set; }
        public double TotalDetectionSeconds { get; set; }
        public double AverageDetectionSeconds { get; set; }
        public double TotalConfidence { get; set; }
        public double AverageConfidence { get; set; }
    }
}

