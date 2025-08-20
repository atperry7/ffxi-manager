using System.Windows.Controls;
using FFXIManager.ViewModels.CharacterMonitor;

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
            
            // Create and set the view model
            DataContext = new EmbeddedCharacterMonitorViewModel();
        }
    }
}