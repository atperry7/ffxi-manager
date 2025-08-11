using System.Windows.Controls;
using FFXIManager.Infrastructure;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
            // Ensure DataContext is set to the HeaderViewModel so bindings and commands work
            DataContext = new HeaderViewModel(
                ServiceLocator.UiDispatcher,
                ServiceLocator.SettingsService,
                ServiceLocator.ExternalApplicationService,
                ServiceLocator.PlayOnlineMonitorService);
        }
    }
}
