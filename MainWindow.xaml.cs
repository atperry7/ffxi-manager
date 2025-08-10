using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FFXIManager.Models;
using FFXIManager.ViewModels;

namespace FFXIManager
{
    /// <summary>
    /// MainWindow with ZERO business logic - pure MVVM implementation
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
            // Pure UI interaction - no business logic
            if (sender is DataGrid dataGrid && 
                dataGrid.SelectedItem is ProfileInfo profile && 
                !profile.IsSystemFile && 
                DataContext is MainViewModel viewModel)
            {
                viewModel.SwapProfileCommand.Execute(null);
            }
        }
    }
}