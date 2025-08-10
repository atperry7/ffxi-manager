using System.Threading.Tasks;

namespace FFXIManager.Services
{
    /// <summary>
    /// Interface for dialog operations
    /// </summary>
    public interface IDialogService
    {
        Task<string?> ShowRenameDialogAsync(string currentName, bool isSystemFile);
        Task<bool> ShowConfirmationDialogAsync(string title, string message);
        Task ShowMessageDialogAsync(string title, string message);
        Task<string?> ShowFolderBrowserDialogAsync(string title, string initialDirectory);
    }
}