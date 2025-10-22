using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of queue statistics calculation and tracking
    /// </summary>
    public class QueueStatisticsService : IQueueStatisticsService
    {
        private QueueExecutionStatistics _executionStatistics;
        private QueueExecutionStatistics _allTimeStatistics;
        private readonly Dictionary<Guid, DateTime> _stepStartTimes = new();

        public QueueStatisticsService()
        {
            _executionStatistics = new QueueExecutionStatistics();
            _allTimeStatistics = new QueueExecutionStatistics();
        }

        #region Statistics Calculation

        public QueueStatistics CalculateStatistics(IEnumerable<AutoLoginQueueItem> queueItems)
        {
            var items = queueItems.ToList();
            var totalItems = items.Count;
            var completedItems = items.Count(x => x.Status == AutoLoginQueueStatus.Completed);
            var failedItems = items.Count(x => x.Status == AutoLoginQueueStatus.Failed);
            var pendingItems = items.Count(x => x.Status == AutoLoginQueueStatus.Pending);

            return new QueueStatistics
            {
                TotalItems = totalItems,
                CompletedItems = completedItems,
                FailedItems = failedItems,
                PendingItems = pendingItems,
                TotalExecutionTime = _executionStatistics.TotalExecutionTime,
                AverageItemTime = _executionStatistics.AverageItemTime,
                SuccessRate = _executionStatistics.SuccessRate,
                LastExecutionStart = _executionStatistics.LastExecutionStart,
                LastExecutionEnd = _executionStatistics.LastExecutionEnd,
                StepPerformance = _executionStatistics.StepPerformance.Values
                    .OrderByDescending(s => s.Runs)
                    .ThenByDescending(s => s.Successes)
                    .ToList()
            };
        }

        public QueueExecutionStatistics GetExecutionStatistics()
        {
            return _executionStatistics;
        }

        public QueueExecutionStatistics GetAllTimeStatistics()
        {
            return _allTimeStatistics;
        }

        #endregion

        #region Execution Tracking

        public void RecordExecutionStart()
        {
            _executionStatistics.UpdateExecutionStart();
            _allTimeStatistics.UpdateExecutionStart();
        }

        public void RecordExecutionEnd()
        {
            _executionStatistics.UpdateExecutionEnd();
            _allTimeStatistics.UpdateExecutionEnd();
        }

        public void UpdateWithCompletedItem(AutoLoginQueueItem item)
        {
            _executionStatistics.UpdateWithCompletedItem(item);
            _allTimeStatistics.UpdateWithCompletedItem(item);
        }

        public void ResetExecutionStatistics()
        {
            _executionStatistics = new QueueExecutionStatistics();
        }

        public void LoadExecutionStatistics(QueueExecutionStatistics statistics)
        {
            _executionStatistics = statistics ?? new QueueExecutionStatistics();
        }

        public void LoadAllTimeStatistics(QueueExecutionStatistics statistics)
        {
            _allTimeStatistics = statistics ?? new QueueExecutionStatistics();
        }

        #endregion

        #region Progress Calculation

        public int CalculateOverallProgress(int totalItems, int processedItems)
        {
            if (totalItems == 0) return 0;
            return (int)((double)processedItems / totalItems * 100);
        }

        public double CalculateSuccessRate(int completedItems, int failedItems)
        {
            var totalProcessed = completedItems + failedItems;
            if (totalProcessed == 0) return 1.0; // No items processed yet, assume 100% success
            return (double)completedItems / totalProcessed;
        }

        #endregion

        #region Step-level Metrics

        public void RecordStepStart(AutoLoginQueueItem item, AutoLoginSubtask subtask)
        {
            if (subtask == null) return;
            _stepStartTimes[subtask.Id] = DateTime.UtcNow;
        }

        public void RecordStepCompleted(AutoLoginQueueItem item, AutoLoginSubtask subtask, bool success)
        {
            if (subtask?.WorkflowStep == null) return;
            var key = subtask.WorkflowStep.StepId ?? subtask.Name;

            // Update session statistics
            if (!_executionStatistics.StepPerformance.TryGetValue(key, out var entry))
            {
                entry = new Models.Settings.StepPerformanceEntry
                {
                    StepId = key,
                    DisplayName = subtask.WorkflowStep.DisplayName
                };
                _executionStatistics.StepPerformance[key] = entry;
            }

            entry.Runs++;
            if (success) entry.Successes++; else entry.Failures++;

            // Update all-time statistics
            if (!_allTimeStatistics.StepPerformance.TryGetValue(key, out var allTimeEntry))
            {
                allTimeEntry = new Models.Settings.StepPerformanceEntry
                {
                    StepId = key,
                    DisplayName = subtask.WorkflowStep.DisplayName
                };
                _allTimeStatistics.StepPerformance[key] = allTimeEntry;
            }

            allTimeEntry.Runs++;
            if (success) allTimeEntry.Successes++; else allTimeEntry.Failures++;

            if (_stepStartTimes.TryGetValue(subtask.Id, out var started))
            {
                var dur = DateTime.UtcNow - started;
                entry.TotalDuration += dur;
                entry.AverageDuration = TimeSpan.FromTicks(entry.TotalDuration.Ticks / entry.Runs);
                allTimeEntry.TotalDuration += dur;
                allTimeEntry.AverageDuration = TimeSpan.FromTicks(allTimeEntry.TotalDuration.Ticks / allTimeEntry.Runs);
                _stepStartTimes.Remove(subtask.Id);
            }
        }

        public void RecordDetectionResult(string stepId, string displayName, double confidence, double detectionSeconds)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            // Update session statistics
            if (!_executionStatistics.StepPerformance.TryGetValue(key, out var entry))
            {
                entry = new Models.Settings.StepPerformanceEntry
                {
                    StepId = key,
                    DisplayName = displayName
                };
                _executionStatistics.StepPerformance[key] = entry;
            }

            entry.Detections++;
            entry.TotalDetectionSeconds += Math.Max(0.0, detectionSeconds);
            entry.AverageDetectionSeconds = entry.Detections > 0 ? entry.TotalDetectionSeconds / entry.Detections : 0.0;
            entry.TotalConfidence += Math.Clamp(confidence, 0.0, 1.0);
            entry.AverageConfidence = entry.Detections > 0 ? entry.TotalConfidence / entry.Detections : 0.0;

            // Update all-time statistics
            if (!_allTimeStatistics.StepPerformance.TryGetValue(key, out var allTimeEntry))
            {
                allTimeEntry = new Models.Settings.StepPerformanceEntry
                {
                    StepId = key,
                    DisplayName = displayName
                };
                _allTimeStatistics.StepPerformance[key] = allTimeEntry;
            }

            allTimeEntry.Detections++;
            allTimeEntry.TotalDetectionSeconds += Math.Max(0.0, detectionSeconds);
            allTimeEntry.AverageDetectionSeconds = allTimeEntry.Detections > 0 ? allTimeEntry.TotalDetectionSeconds / allTimeEntry.Detections : 0.0;
            allTimeEntry.TotalConfidence += Math.Clamp(confidence, 0.0, 1.0);
            allTimeEntry.AverageConfidence = allTimeEntry.Detections > 0 ? allTimeEntry.TotalConfidence / allTimeEntry.Detections : 0.0;
        }

        #endregion
    }
}


