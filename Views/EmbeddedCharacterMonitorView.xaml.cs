using FFXIManager.ViewModels.CharacterMonitor;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Controls;

namespace FFXIManager.Views
{
    /// <summary>
    /// UserControl for embedded Character Monitor view.
    /// Uses the new CharacterMonitor architecture in a lightweight embedded form.
    /// </summary>
    public partial class EmbeddedCharacterMonitorView : UserControl
    {
        public EmbeddedCharacterMonitorView()
        {
            InitializeComponent();

            // Resolve and set the view model - skip in design time
            if (!DesignerProperties.GetIsInDesignMode(this) && App.Services != null)
            {
                DataContext = App.Services.GetRequiredService<EmbeddedCharacterMonitorViewModel>();
            }
        }
    }
}
