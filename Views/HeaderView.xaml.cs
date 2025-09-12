using System.Windows.Controls;
using FFXIManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FFXIManager.Views
{
    public partial class HeaderView : UserControl
    {
        public HeaderView()
        {
            InitializeComponent();
            // Resolve ViewModel via DI
            DataContext = App.Services.GetRequiredService<HeaderViewModel>();
        }
    }
}
