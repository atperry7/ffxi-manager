using System.Windows.Controls;
using FFXIManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIManager.Views
{
    public partial class StatusBarView : UserControl
    {
        public StatusBarView()
        {
            InitializeComponent();
            // Resolve ViewModel via DI
            DataContext = App.Services.GetRequiredService<StatusBarViewModel>();
        }
    }
}
