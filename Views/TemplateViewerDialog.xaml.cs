using System.Windows;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for TemplateViewerDialog.xaml
    /// </summary>
    public partial class TemplateViewerDialog : Window
    {
        public TemplateViewerDialog(TemplateViewerDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
