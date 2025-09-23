using System;
using System.Collections.Generic;
using System.Linq;
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

        public QueueStatisticsService()
        {
            _executionStatistics = new QueueExecutionStatistics();
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
                LastExecutionEnd = _executionStatistics.LastExecutionEnd
            };
        }

        public QueueExecutionStatistics GetExecutionStatistics()
        {
            return _executionStatistics;
        }

        #endregion

        #region Execution Tracking

        public void RecordExecutionStart()
        {
            _executionStatistics.UpdateExecutionStart();
        }

        public void RecordExecutionEnd()
        {
            _executionStatistics.UpdateExecutionEnd();
        }

        public void UpdateWithCompletedItem(AutoLoginQueueItem item)
        {
            _executionStatistics.UpdateWithCompletedItem(item);
        }

        public void ResetExecutionStatistics()
        {
            _executionStatistics = new QueueExecutionStatistics();
        }

        public void LoadExecutionStatistics(QueueExecutionStatistics statistics)
        {
            _executionStatistics = statistics ?? new QueueExecutionStatistics();
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
    }
}