using System.Windows.Controls;
using System.Windows.Input;
using FFXIManager.Models;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    public partial class ProfileListView : UserControl
    {
        public ProfileListView()
        {
            InitializeComponent();
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