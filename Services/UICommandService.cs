using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for handling UI-related commands
    /// </summary>
    public interface IUICommandService
    {
        void CopyToClipboard(string text);
        void OpenFileLocation(string filePath);
        bool ShowFolderDialog(string title, string initialDirectory, out string selectedPath);
    }
    
    public class UICommandService : IUICommandService
    {
        public void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to copy to clipboard: {ex.Message}", ex);
            }
        }
        
        public void OpenFileLocation(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");
            
            try
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to open file location: {ex.Message}", ex);
            }
        }
        
        public bool ShowFolderDialog(string title, string initialDirectory, out string selectedPath)
        {
            selectedPath = string.Empty;
            
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = title,
                    InitialDirectory = initialDirectory
                };
                
                if (dialog.ShowDialog() == true)
                {
                    selectedPath = dialog.FolderName;
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to show folder dialog: {ex.Message}", ex);
            }
        }
    }
}