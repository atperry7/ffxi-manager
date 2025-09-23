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

        private void ActionsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // Show context menu when actions button is clicked
            if (sender is System.Windows.Controls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }
    }
}
