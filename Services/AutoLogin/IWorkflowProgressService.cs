using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services.AutoLogin
{
    public interface IWorkflowProgressService
    {
        Task UpdateProgressWithPhaseAsync(AutoLoginSubtask subtask, string phase, int progress, string? detailMessage = null);
    }
}

