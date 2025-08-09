using System.Windows;

namespace FFXIManager.Views
{
    public partial class RenameProfileDialog : Window
    {
        public string CurrentName { get; }
        public string NewProfileName { get; set; }
        
        public RenameProfileDialog(string currentName)
        {
            InitializeComponent();
            CurrentName = currentName;
            NewProfileName = currentName;
            DataContext = this;
            
            // Select all text when dialog opens
            Loaded += (s, e) => NewNameTextBox.SelectAll();
            NewNameTextBox.Focus();
        }
        
        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NewProfileName))
            {
                MessageBox.Show("Profile name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
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