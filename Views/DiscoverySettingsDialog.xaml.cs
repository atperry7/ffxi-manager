using System.Windows;

namespace FFXIManager.Views
{
    public partial class DiscoverySettingsDialog : Window
    {
        public DiscoverySettingsDialog()
        {
            InitializeComponent();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.DiscoverySettingsViewModel vm)
            {
                vm.Save();
            }
            DialogResult = true;
            Close();
        }
    }
}
