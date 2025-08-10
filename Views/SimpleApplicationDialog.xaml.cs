using System.Windows;
using FFXIManager.Models;

namespace FFXIManager.Views
{
    /// <summary>
    /// Simple test dialog for debugging edit crashes
    /// </summary>
    public partial class SimpleApplicationDialog : Window
    {
        public ExternalApplication Application { get; }

        public SimpleApplicationDialog(ExternalApplication application)
        {
            Application = application;
            
            InitializeComponent();
            DataContext = application;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}