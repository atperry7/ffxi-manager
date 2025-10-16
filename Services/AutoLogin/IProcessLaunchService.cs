using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Interface for process launch, verification, and monitoring operations.
    /// Provides reusable functionality for launching external applications,
    /// verifying process startup, and monitoring process responsiveness.
    /// </summary>
    public interface IProcessLaunchService
    {
        /// <summary>
        /// Gets the process ID from the launched application and verifies it's running.
        /// </summary>
        /// <param name="application">The application that was just launched</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="processNames">Array of possible process names to check</param>
        /// <param name="timeout">Maximum time to wait for process verification</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <returns>The process ID of the launched instance, or 0 if verification fails</returns>
        Task<int> GetLaunchedProcessIdAsync(
            ExternalApplication application,
            AutoLoginSubtask subtask,
            string[] processNames,
            TimeSpan timeout,
            CancellationToken cancellationToken);

        /// <summary>
        /// Waits for a process to become responsive using retry mechanisms.
        /// </summary>
        /// <param name="processId">The process ID to monitor</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="timeout">Maximum time to wait for responsiveness</param>
        /// <param name="checkInterval">Interval between responsiveness checks</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        Task WaitForProcessResponsivenessAsync(
            int processId,
            AutoLoginSubtask subtask,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken);

        /// <summary>
        /// Validates that a process ID is available in the context.
        /// </summary>
        /// <param name="context">The auto-login context containing process information</param>
        /// <param name="contextKey">The key to retrieve the process ID from context</param>
        /// <returns>The process ID, or 0 if not available</returns>
        int ValidateProcessIdFromContext(IAutoLoginContext context, string contextKey = "ProcessId");

        /// <summary>
        /// Waits for a specific process to start by monitoring process names.
        /// </summary>
        /// <param name="processNames">Array of process names to monitor</param>
        /// <param name="subtask">The subtask for progress reporting</param>
        /// <param name="timeout">Maximum time to wait for process startup</param>
        /// <param name="checkInterval">Interval between process detection checks</param>
        /// <param name="cancellationToken">Cancellation token for operation cancellation</param>
        /// <returns>The process ID of the detected process, or 0 if not found</returns>
        Task<int> WaitForProcessStartupAsync(
            string[] processNames,
            AutoLoginSubtask subtask,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken);
    }
}
