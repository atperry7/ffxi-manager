using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base interface for all auto-login task handlers.
    /// Each handler implements the automation logic for workflow-driven subtasks.
    /// </summary>
    public interface ILoginTaskHandler
    {
        /// <summary>
        /// Whether this handler can execute the specified subtask
        /// </summary>
        bool CanHandle(AutoLoginSubtask subtask);

        /// <summary>
        /// Executes the automation logic for the given subtask
        /// </summary>
        /// <param name="subtask">The subtask to execute</param>
        /// <param name="queueItem">The queue item containing account and context information</param>
        /// <param name="context">The context for sharing data between handlers</param>
        /// <param name="cancellationToken">Cancellation token for stopping execution</param>
        /// <returns>Task representing the async operation</returns>
        Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken);
    }
}
