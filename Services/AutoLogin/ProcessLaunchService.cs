using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    /// <summary>
    /// Implementation of process launch, verification, and monitoring operations.
    /// Provides reusable functionality for launching external applications,
    /// verifying process startup, and monitoring process responsiveness.
    /// </summary>
    public class ProcessLaunchService : IProcessLaunchService
    {
        private readonly ILoggingService _loggingService;
        private readonly IProcessUtilityService _processUtilityService;
        private readonly IExternalApplicationService _externalApplicationService;

        public ProcessLaunchService(
            ILoggingService loggingService,
            IProcessUtilityService processUtilityService,
            IExternalApplicationService externalApplicationService)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _processUtilityService = processUtilityService ?? throw new ArgumentNullException(nameof(processUtilityService));
            _externalApplicationService = externalApplicationService ?? throw new ArgumentNullException(nameof(externalApplicationService));
        }

        public async Task<int> GetLaunchedProcessIdAsync(
            ExternalApplication application,
            AutoLoginSubtask subtask,
            string[] processNames,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // First, check if the application already has process IDs from the launch
            if (application.ProcessIds.Any())
            {
                var launchedProcessId = application.ProcessIds.First();
                await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Found launched process ID: {launchedProcessId}");

                // Verify the process is actually running and accessible
                if (_processUtilityService.IsProcessRunning(launchedProcessId))
                {
                    await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Process {launchedProcessId} confirmed running");
                    return launchedProcessId;
                }
                else
                {
                    await _loggingService.LogWarningAsync($"[PROCESS_LAUNCH] Process {launchedProcessId} is not running or not accessible");
                }
            }

            // Fallback: Wait a short time for the process to appear in the application's process list
            var timeoutEnd = DateTime.UtcNow.Add(timeout);

            await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Waiting for process to appear in application tracking...");

            while (DateTime.UtcNow < timeoutEnd && !cancellationToken.IsCancellationRequested)
            {
                // Refresh application status
                await _externalApplicationService.RefreshApplicationStatusAsync(application);

                if (application.ProcessIds.Any())
                {
                    var detectedProcessId = application.ProcessIds.First();

                    // Verify the process is actually running
                    if (_processUtilityService.IsProcessRunning(detectedProcessId))
                    {
                        await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Process {detectedProcessId} detected and confirmed running");
                        return detectedProcessId;
                    }
                }

                // Update progress - use time-based monotonic progress within verification range (60-85%)
                var elapsed = DateTime.UtcNow - (timeoutEnd - timeout);
                var baseProgress = 60;
                var rangeSize = 25;
                var progressPercent = baseProgress + (int)((elapsed.TotalSeconds / timeout.TotalSeconds) * rangeSize);
                subtask.UpdateProgressWithPhase("launch", Math.Min(85, progressPercent), "Verifying launched process");

                await Task.Delay(500, cancellationToken); // Poll every 500ms for process detection
            }

            await _loggingService.LogWarningAsync($"[PROCESS_LAUNCH] Could not verify process startup within {timeout.TotalSeconds}s");
            return 0; // Verification failed
        }

        public async Task WaitForProcessResponsivenessAsync(
            int processId,
            AutoLoginSubtask subtask,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Waiting for process {processId} to become responsive");

            var timeoutEnd = DateTime.UtcNow.Add(timeout);
            var maxAttempts = (int)(timeout.TotalSeconds / checkInterval.TotalSeconds);
            var attempt = 0;

            while (DateTime.UtcNow < timeoutEnd && !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                await _loggingService.LogDebugAsync($"[PROCESS_LAUNCH] Checking process {processId} responsiveness (attempt {attempt}/{maxAttempts})");

                // Check if process is still running
                if (!_processUtilityService.IsProcessRunning(processId))
                {
                    throw new InvalidOperationException($"Process {processId} terminated unexpectedly");
                }

                // Get process info to check responsiveness
                var processInfo = await _processUtilityService.GetProcessInfoAsync(processId);
                if (processInfo?.IsResponding == true)
                {
                    await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Process {processId} is responsive after {attempt} attempts");
                    return; // Success - process is responsive
                }

                await _loggingService.LogDebugAsync($"[PROCESS_LAUNCH] Process {processId} not yet responsive");

                // Update progress
                var progress = (attempt * 100) / maxAttempts;
                subtask.UpdateProgressWithPhase("launch", Math.Min(95, progress), "Waiting for process responsiveness");

                await Task.Delay(checkInterval, cancellationToken);
            }

            throw new TimeoutException($"Process {processId} did not become responsive within {timeout.TotalSeconds}s");
        }

        public int ValidateProcessIdFromContext(IAutoLoginContext context, string contextKey = "ProcessId")
        {
            var processId = context.GetValueData<int>(contextKey);
            if (processId == 0)
            {
                _loggingService.LogWarningAsync($"[PROCESS_LAUNCH] No process ID available in context key '{contextKey}'").GetAwaiter().GetResult();
                return 0;
            }

            _loggingService.LogDebugAsync($"[PROCESS_LAUNCH] Retrieved process ID {processId} from context key '{contextKey}'").GetAwaiter().GetResult();
            return processId;
        }

        public async Task<int> WaitForProcessStartupAsync(
            string[] processNames,
            AutoLoginSubtask subtask,
            TimeSpan timeout,
            TimeSpan checkInterval,
            CancellationToken cancellationToken)
        {
            await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Waiting for process startup: {string.Join(", ", processNames)}");

            var timeoutEnd = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < timeoutEnd && !cancellationToken.IsCancellationRequested)
            {
                var processes = await _processUtilityService.GetProcessesByNamesAsync(processNames);
                if (processes.Any())
                {
                    var process = processes.First();
                    await _loggingService.LogInfoAsync($"[PROCESS_LAUNCH] Process detected: {process.ProcessName} (PID: {process.ProcessId})");
                    return process.ProcessId;
                }

                await Task.Delay(checkInterval, cancellationToken);
            }

            await _loggingService.LogWarningAsync($"[PROCESS_LAUNCH] Process not detected within {timeout.TotalSeconds}s");
            return 0; // Process not found
        }
    }
}
