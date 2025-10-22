using FFXIManager.Models;
using FFXIManager.Models.Settings;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for calculating and tracking queue execution statistics
    /// </summary>
    public interface IQueueStatisticsService
    {
        #region Statistics Calculation

        /// <summary>
        /// Calculates current queue statistics based on provided items
        /// </summary>
        /// <param name="queueItems">Current queue items</param>
        /// <returns>Current queue statistics</returns>
        QueueStatistics CalculateStatistics(IEnumerable<AutoLoginQueueItem> queueItems);

        /// <summary>
        /// Gets internal execution statistics for persistence (session only)
        /// </summary>
        /// <returns>Session execution statistics</returns>
        QueueExecutionStatistics GetExecutionStatistics();

        /// <summary>
        /// Gets all-time execution statistics (persistent across sessions)
        /// </summary>
        /// <returns>All-time execution statistics</returns>
        QueueExecutionStatistics GetAllTimeStatistics();

        #endregion

        #region Execution Tracking

        /// <summary>
        /// Records the start of queue execution
        /// </summary>
        void RecordExecutionStart();

        /// <summary>
        /// Records the end of queue execution
        /// </summary>
        void RecordExecutionEnd();

        /// <summary>
        /// Updates statistics with a completed queue item
        /// </summary>
        /// <param name="item">The completed queue item</param>
        void UpdateWithCompletedItem(AutoLoginQueueItem item);

        /// <summary>
        /// Resets execution statistics
        /// </summary>
        void ResetExecutionStatistics();

        /// <summary>
        /// Loads session execution statistics from persistence
        /// </summary>
        /// <param name="statistics">Statistics to load</param>
        void LoadExecutionStatistics(QueueExecutionStatistics statistics);

        /// <summary>
        /// Loads all-time execution statistics from persistence
        /// </summary>
        /// <param name="statistics">All-time statistics to load</param>
        void LoadAllTimeStatistics(QueueExecutionStatistics statistics);

        #endregion

        #region Progress Calculation

        /// <summary>
        /// Calculates overall progress percentage based on processed items
        /// </summary>
        /// <param name="totalItems">Total number of items</param>
        /// <param name="processedItems">Number of processed items</param>
        /// <returns>Progress percentage (0-100)</returns>
        int CalculateOverallProgress(int totalItems, int processedItems);

        /// <summary>
        /// Calculates success rate based on completed vs failed items
        /// </summary>
        /// <param name="completedItems">Number of completed items</param>
        /// <param name="failedItems">Number of failed items</param>
        /// <returns>Success rate as a percentage (0.0 to 1.0)</returns>
        double CalculateSuccessRate(int completedItems, int failedItems);

        #endregion

        #region Step-level Metrics

        /// <summary>
        /// Records the start of a workflow step for timing.
        /// </summary>
        void RecordStepStart(AutoLoginQueueItem item, AutoLoginSubtask subtask);

        /// <summary>
        /// Records the completion of a workflow step.
        /// </summary>
        void RecordStepCompleted(AutoLoginQueueItem item, AutoLoginSubtask subtask, bool success);

        /// <summary>
        /// Records the final detection result for a step (confidence and duration seconds).
        /// </summary>
        void RecordDetectionResult(string stepId, string displayName, double confidence, double detectionSeconds);

        #endregion
    }
}
