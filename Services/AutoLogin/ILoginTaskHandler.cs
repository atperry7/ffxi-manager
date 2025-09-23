using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base interface for all auto-login task handlers.
    /// Each handler implements the automation logic for specific task categories.
    /// </summary>
    public interface ILoginTaskHandler
    {
        /// <summary>
        /// The task step this handler is responsible for
        /// </summary>
        LoginTaskStep TaskStep { get; }

        /// <summary>
        /// Whether this handler can execute the specified subtask
        /// </summary>
        bool CanHandle(AutoLoginSubtask subtask);

        /// <summary>
        /// Executes the automation logic for the given subtask
        /// </summary>
        /// <param name="subtask">The subtask to execute</param>
        /// <param name="queueItem">The queue item containing account and context information</param>
        /// <param name="cancellationToken">Cancellation token for stopping execution</param>
        /// <returns>Task representing the async operation</returns>
        Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Context information passed to task handlers during execution
    /// </summary>
    public class TaskExecutionContext
    {
        /// <summary>
        /// The queue item being processed
        /// </summary>
        public AutoLoginQueueItem QueueItem { get; set; } = null!;

        /// <summary>
        /// The current subtask being executed
        /// </summary>
        public AutoLoginSubtask Subtask { get; set; } = null!;

        /// <summary>
        /// Progress reporting callback
        /// </summary>
        public Action<int, string>? ProgressCallback { get; set; }

        /// <summary>
        /// Cancellation token for the operation
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Additional context data that can be shared between handlers
        /// </summary>
        public Dictionary<string, object> SharedData { get; set; } = new();
    }
}