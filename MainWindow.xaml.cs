using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FFXIManager.Models;
using FFXIManager.ViewModels;

namespace FFXIManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
        
        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dataGrid && dataGrid.SelectedItem is ProfileInfo profile)
            {
                // Double-click to swap profile (if it's not the active system file)
                if (!profile.IsActive && DataContext is MainViewModel viewModel)
                {
                    viewModel.SwapProfileCommand.Execute(null);
                }
            }
        }
        
        private void CopyProfileName_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && viewModel.SelectedProfile != null)
            {
                Clipboard.SetText(viewModel.SelectedProfile.Name);
                viewModel.StatusMessage = $"📋 Copied profile name: {viewModel.SelectedProfile.Name}";
            }
        }
        
        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel && viewModel.SelectedProfile != null)
            {
                try
                {
                    var filePath = viewModel.SelectedProfile.FilePath;
                    if (File.Exists(filePath))
                    {
                        // Open file explorer and select the file
                        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                        viewModel.StatusMessage = $"📁 Opened file location for: {viewModel.SelectedProfile.Name}";
                    }
                    else
                    {
                        viewModel.StatusMessage = $"❌ File not found: {filePath}";
                    }
                }
                catch (Exception ex)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.StatusMessage = $"❌ Error opening file location: {ex.Message}";
                    }
                }
            }
        }
        
        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ProfileInfo profile && DataContext is MainViewModel viewModel)
            {
                // Temporarily select this profile and trigger rename
                var originalSelection = viewModel.SelectedProfile;
                viewModel.SelectedProfile = profile;
                
                try
                {
                    viewModel.RenameProfileCommand.Execute(null);
                }
                finally
                {
                    // Only restore original selection if the rename didn't change it
                    if (viewModel.SelectedProfile == profile && originalSelection != profile)
                    {
                        viewModel.SelectedProfile = originalSelection;
                    }
                }
            }
        }
    }
}