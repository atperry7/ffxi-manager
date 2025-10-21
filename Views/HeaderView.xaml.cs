using FFXIManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Controls;

namespace FFXIManager.Views
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
            // Resolve ViewModel via DI - skip in design time
            if (!DesignerProperties.GetIsInDesignMode(this) && App.Services != null)
            {
                DataContext = App.Services.GetRequiredService<HeaderViewModel>();
            }
        }
    }
}
