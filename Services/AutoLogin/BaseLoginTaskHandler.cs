using System;
using System.Threading;
using System.Threading.Tasks;
using FFXIManager.Models;
using FFXIManager.Services;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Base class for login task handlers providing common functionality and patterns.
    /// Implements the developer guidelines for consistent handler behavior.
    /// </summary>
    public abstract class BaseLoginTaskHandler : ILoginTaskHandler
    {
        protected readonly ILoggingService _loggingService;

        protected BaseLoginTaskHandler(ILoggingService loggingService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        }

        public abstract LoginTaskStep TaskStep { get; }
        public abstract bool CanHandle(AutoLoginSubtask subtask);

        public async Task ExecuteAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken)
        {
            await _loggingService.LogDebugAsync($"Starting execution of {subtask.TaskStep} for {queueItem.DisplayName}");

            try
            {
                // Validate inputs
                ValidateInputs(subtask, queueItem);

                // Execute the handler-specific logic with common patterns applied
                await ExecuteHandlerLogicAsync(subtask, queueItem, context, cancellationToken);

                await _loggingService.LogDebugAsync($"Successfully completed {subtask.TaskStep} for {queueItem.DisplayName}");
            }
            catch (OperationCanceledException)
            {
                await _loggingService.LogDebugAsync($"Execution of {subtask.TaskStep} was cancelled for {queueItem.DisplayName}");
                throw;
            }
            catch (Exception ex)
            {
                await _loggingService.LogErrorAsync($"Failed to execute {subtask.TaskStep} for {queueItem.DisplayName}", ex);
                throw;
            }
        }

        /// <summary>
        /// Implement the specific handler logic in derived classes.
        /// This method will be called with validated inputs and proper error handling in place.
        /// </summary>
        protected abstract Task ExecuteHandlerLogicAsync(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem, IAutoLoginContext context, CancellationToken cancellationToken);

        /// <summary>
        /// Validates common inputs. Override in derived classes for additional validation.
        /// </summary>
        protected virtual void ValidateInputs(AutoLoginSubtask subtask, AutoLoginQueueItem queueItem)
        {
            if (subtask == null)
                throw new ArgumentNullException(nameof(subtask));

            if (queueItem == null)
                throw new ArgumentNullException(nameof(queueItem));

            if (queueItem.Account == null)
                throw new InvalidOperationException($"Account information is required for {subtask.TaskStep}");
        }

        /// <summary>
        /// Executes an operation with retry logic and exponential backoff.
        /// Follows the patterns outlined in the implementation guide.
        /// </summary>
        protected async Task<T> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            string operationName,
            int maxRetries = 3,
            int baseDelayMs = 500,
            CancellationToken cancellationToken = default)
        {
            var delay = TimeSpan.FromMilliseconds(baseDelayMs);

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await _loggingService.LogDebugAsync($"Attempting {operationName} (attempt {attempt}/{maxRetries})");
                    return await operation(cancellationToken);
                }
                catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex))
                {
                    await _loggingService.LogWarningAsync($"{operationName} attempt {attempt} failed, retrying in {delay.TotalMilliseconds}ms: {ex.Message}");
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 1.5); // Exponential backoff
                }
            }

            // This will never be reached due to the exception handling above
            throw new InvalidOperationException($"Retry logic failed unexpectedly for {operationName}");
        }

        /// <summary>
        /// Determines if an exception is retryable. Override in derived classes for specific retry logic.
        /// </summary>
        protected virtual bool IsRetryableException(Exception ex)
        {
            // Common retryable exceptions
            return ex is TimeoutException ||
                   ex is InvalidOperationException ||
                   (ex is System.ComponentModel.Win32Exception win32Ex && IsRetryableWin32Error(win32Ex.NativeErrorCode));
        }

        /// <summary>
        /// Determines if a Win32 error code indicates a retryable condition.
        /// </summary>
        protected virtual bool IsRetryableWin32Error(int errorCode)
        {
            // Common retryable Win32 error codes
            return errorCode == 5 ||    // Access denied (might be temporary)
                   errorCode == 1400 || // Invalid window handle (window might not be ready)
                   errorCode == 1401;   // Invalid menu handle
        }

        /// <summary>
        /// Waits for a condition to be met with timeout and progress reporting.
        /// Respects pause state and cancellation tokens as per guidelines.
        /// </summary>
        protected async Task WaitForConditionAsync(
            Func<Task<bool>> condition,
            string conditionDescription,
            AutoLoginSubtask subtask,
            AutoLoginTask task,
            TimeSpan timeout,
            int progressStart = 0,
            int progressEnd = 100,
            CancellationToken cancellationToken = default)
        {
            var endTime = DateTime.UtcNow.Add(timeout);
            var checkInterval = TimeSpan.FromMilliseconds(200);
            var lastProgressUpdate = DateTime.UtcNow;
            var progressUpdateInterval = TimeSpan.FromSeconds(1);

            while (DateTime.UtcNow < endTime)
            {
                // Respect pause state as per guidelines
                while (task?.Status == AutoLoginTaskStatus.Paused && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(100, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Check the condition
                if (await condition())
                {
                    subtask?.UpdateProgress(progressEnd, $"{conditionDescription} - condition met");
                    return;
                }

                // Update progress periodically
                if (DateTime.UtcNow - lastProgressUpdate > progressUpdateInterval)
                {
                    var elapsed = DateTime.UtcNow - (endTime - timeout);
                    var progressPercent = (int)(progressStart + (progressEnd - progressStart) * (elapsed.TotalMilliseconds / timeout.TotalMilliseconds));
                    subtask?.UpdateProgress(Math.Min(progressPercent, progressEnd - 1), $"{conditionDescription} - waiting...");
                    lastProgressUpdate = DateTime.UtcNow;
                }

                await Task.Delay(checkInterval, cancellationToken);
            }

            throw new TimeoutException($"Timeout waiting for condition: {conditionDescription}");
        }

        /// <summary>
        /// Updates progress with consistent logging.
        /// </summary>
        protected async Task UpdateProgressAsync(AutoLoginSubtask subtask, int progress, string message)
        {
            subtask?.UpdateProgress(progress, message);
            await _loggingService.LogDebugAsync($"Progress {progress}%: {message}");
        }

        /// <summary>
        /// Validates that required account properties are available.
        /// Throws meaningful exceptions when properties are missing.
        /// </summary>
        protected void ValidateAccountProperty(object value, string propertyName, string taskDescription)
        {
            if (value == null || (value is string str && string.IsNullOrEmpty(str)))
            {
                throw new InvalidOperationException($"{propertyName} is required for {taskDescription}. Please ensure the account is properly configured.");
            }
        }

        /// <summary>
        /// Creates a timeout cancellation token source that respects the provided cancellation token.
        /// </summary>
        protected CancellationTokenSource CreateTimeoutCancellationTokenSource(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var timeoutCts = new CancellationTokenSource(timeout);
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        }

        /// <summary>
        /// Logs sensitive operations without exposing sensitive data.
        /// </summary>
        protected async Task LogSecureOperationAsync(string operation, string accountIdentifier)
        {
            await _loggingService.LogDebugAsync($"{operation} for account {accountIdentifier} (sensitive data masked)");
        }
    }
}