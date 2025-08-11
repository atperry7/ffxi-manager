using System.Windows.Controls;
using FFXIManager.Infrastructure;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    public partial class StatusBarView : UserControl
    {
        public StatusBarView()
        {
            InitializeComponent();
            // Set DataContext to StatusBarViewModel for operational status
            DataContext = new StatusBarViewModel(
                ServiceLocator.StatusMessageService,
                ServiceLocator.SettingsService,
                ServiceLocator.ProfileService,
                ServiceLocator.LoggingService);
        }
    }
}
