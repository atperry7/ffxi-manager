using System.Windows.Controls;

namespace FFXIManager.Views
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
        }

        private void OpenDiscoverySettings_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dlg = new DiscoverySettingsDialog();
            dlg.Owner = System.Windows.Window.GetWindow(this);
            dlg.DataContext = new ViewModels.DiscoverySettingsViewModel();
            dlg.ShowDialog();
        }
    }
}