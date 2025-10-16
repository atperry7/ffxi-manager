using System.Windows;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for WorkflowEditor.xaml
    /// </summary>
    public partial class WorkflowEditor : Window
    {
        public WorkflowEditor(WorkflowEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Check for unsaved changes before closing
            if (DataContext is WorkflowEditorViewModel viewModel && viewModel.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Dispose the ViewModel if it implements IDisposable
            if (DataContext is WorkflowEditorViewModel viewModel)
            {
                viewModel.Dispose();
            }

            base.OnClosing(e);
        }
    }
}
