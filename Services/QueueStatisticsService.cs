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

        public void RecordDetectionFailure(string stepId, string displayName, int attemptsMade)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.DetectionFailures++;
                entry.DetectionAttempts += attemptsMade;
            });
        }

        public void RecordFallbackTemplateUsed(string stepId, string displayName)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.FallbackTemplateUsages++;
            });
        }

        public void RecordStepSkipped(string stepId, string displayName, string reason)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.Skips++;
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    if (entry.SkipReasons.TryGetValue(reason, out var count))
                        entry.SkipReasons[reason] = count + 1;
                    else
                        entry.SkipReasons[reason] = 1;
                }
            });
        }

        #endregion

        #region Window Discovery Metrics

        public void RecordWindowDiscovery(string stepId, string displayName, TimeSpan duration, int attempts, bool successOnFirstAttempt)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.TotalWindowDiscoveryTime += duration;
                entry.WindowDiscoveryAttempts += attempts;
                if (successOnFirstAttempt)
                    entry.WindowDiscoverySuccessfulFirstAttempts++;

                // Calculate average window discovery time based on total discoveries
                var totalDiscoveries = entry.WindowDiscoverySuccessfulFirstAttempts +
                                       (entry.WindowDiscoveryAttempts > entry.WindowDiscoverySuccessfulFirstAttempts
                                        ? entry.WindowDiscoveryAttempts - entry.WindowDiscoverySuccessfulFirstAttempts
                                        : 0);
                if (totalDiscoveries > 0)
                {
                    entry.AverageWindowDiscoveryTime = TimeSpan.FromTicks(entry.TotalWindowDiscoveryTime.Ticks / totalDiscoveries);
                }
            });
        }

        #endregion

        #region Action-level Metrics

        public void RecordActionExecution(
            string stepId,
            string displayName,
            string actionType,
            bool success,
            TimeSpan duration,
            int retryCount = 0)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                // Get or create action performance entry
                if (!entry.ActionPerformance.TryGetValue(actionType, out var actionEntry))
                {
                    actionEntry = new Models.Settings.ActionPerformanceEntry
                    {
                        ActionType = actionType
                    };
                    entry.ActionPerformance[actionType] = actionEntry;
                }

                // Update action statistics
                actionEntry.Executions++;
                if (success)
                {
                    actionEntry.Successes++;
                    if (retryCount == 0)
                        actionEntry.FirstAttemptSuccesses++;
                }
                else
                {
                    actionEntry.Failures++;
                }

                actionEntry.TotalDuration += duration;
                actionEntry.AverageDuration = TimeSpan.FromTicks(actionEntry.TotalDuration.Ticks / actionEntry.Executions);

                actionEntry.TotalRetries += retryCount;
                actionEntry.AverageRetries = (double)actionEntry.TotalRetries / actionEntry.Executions;

                // Track min/max duration
                if (duration < actionEntry.MinDuration)
                    actionEntry.MinDuration = duration;
                if (duration > actionEntry.MaxDuration)
                    actionEntry.MaxDuration = duration;
            });
        }

        #endregion

        #region Phase Timing Metrics

        public void RecordDetectionPhaseTime(string stepId, string displayName, TimeSpan duration, int attempts)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.TotalDetectionTime += duration;
                entry.DetectionAttempts += attempts;

                var detectionRuns = entry.Detections + entry.DetectionFailures;
                if (detectionRuns > 0)
                {
                    entry.AverageDetectionTime = TimeSpan.FromTicks(entry.TotalDetectionTime.Ticks / detectionRuns);
                }
            });
        }

        public void RecordNavigationPhaseTime(string stepId, string displayName, TimeSpan duration)
        {
            var key = string.IsNullOrWhiteSpace(stepId) ? displayName : stepId;

            UpdateStepEntry(key, displayName, entry =>
            {
                entry.TotalNavigationTime += duration;

                if (entry.Runs > 0)
                {
                    entry.AverageNavigationTime = TimeSpan.FromTicks(entry.TotalNavigationTime.Ticks / entry.Runs);
                }
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Helper to update step entry in both session and all-time statistics
        /// </summary>
        private void UpdateStepEntry(string key, string displayName, Action<Models.Settings.StepPerformanceEntry> update)
        {
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
            update(entry);

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
            update(allTimeEntry);
        }

        #endregion
    }
}


