using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    public class WorkflowProgressService : IWorkflowProgressService
    {
        private readonly ILoggingService _loggingService;

        public WorkflowProgressService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
        }

        public async Task UpdateProgressWithPhaseAsync(AutoLoginSubtask subtask, string phase, int progress, string? detailMessage = null)
        {
            subtask?.UpdateProgressWithPhase(phase, progress, detailMessage);
            await _loggingService.LogInfoAsync($"Phase: {phase} - {progress}%");
            if (!string.IsNullOrEmpty(detailMessage))
            {
                await _loggingService.LogDebugAsync($"Phase details: {detailMessage}");
            }
        }
    }
}

