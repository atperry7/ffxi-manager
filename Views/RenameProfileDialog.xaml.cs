using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FFXIManager.Views
{
    /// <summary>
    /// MVVM-compliant RenameProfileDialog with no code-behind logic
    /// </summary>
    public partial class RenameProfileDialog : Window
    {
        public RenameProfileDialogViewModel ViewModel { get; }

        public string NewProfileName => ViewModel.NewProfileName;

        public RenameProfileDialog(string currentName, bool isSystemFile)
        {
            InitializeComponent();

            ViewModel = new RenameProfileDialogViewModel(currentName, isSystemFile);
            DataContext = ViewModel;

            // Subscribe to ViewModel events
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += (s, e) => NewNameTextBox.SelectAll();
            NewNameTextBox.Focus();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"ViewModel PropertyChanged: {e.PropertyName}");

            if (e.PropertyName == nameof(RenameProfileDialogViewModel.DialogResult))
            {
                System.Diagnostics.Debug.WriteLine($"DialogResult changed to: {ViewModel.DialogResult}");
                DialogResult = ViewModel.DialogResult;
                if (ViewModel.DialogResult.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"Closing dialog with result: {ViewModel.DialogResult}");
                    Close();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            base.OnClosed(e);
        }
    }
}
