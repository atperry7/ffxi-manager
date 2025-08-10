using System;
using System.Threading.Tasks;
using System.Windows;
using FFXIManager.Views;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for handling dialog operations in a testable way
    /// </summary>
    public class DialogService : IDialogService
    {
        public async Task<string?> ShowRenameDialogAsync(string currentName, bool isSystemFile)
        {
            return await Task.Run(() =>
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new RenameProfileDialog(currentName, isSystemFile);
                    return dialog.ShowDialog() == true ? dialog.NewProfileName : null;
                });
            });
        }
        
        public async Task<bool> ShowConfirmationDialogAsync(string title, string message)
        {
            return await Task.Run(() =>
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                    return result == MessageBoxResult.Yes;
                });
            });
        }
        
        public async Task ShowMessageDialogAsync(string title, string message)
        {
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
                });
            });
        }
        
        public async Task<string?> ShowFolderBrowserDialogAsync(string title, string initialDirectory)
        {
            return await Task.Run(() =>
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var dialog = new Microsoft.Win32.OpenFolderDialog
                        {
                            Title = title,
                            InitialDirectory = initialDirectory
                        };
                        
                        return dialog.ShowDialog() == true ? dialog.FolderName : null;
                    }
                    catch
                    {
                        return null;
                    }
                });
            });
        }
    }
}