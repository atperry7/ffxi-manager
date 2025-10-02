using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using FFXIManager.Models;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for CredentialRecoveryDialog.xaml
    /// </summary>
    public partial class CredentialRecoveryDialog : Window, INotifyPropertyChanged
    {
        private ObservableCollection<OrphanedCredential> _orphanedCredentials = new();
        private OrphanedCredential? _selectedOrphanedCredential;

        /// <summary>
        /// Gets the collection of orphaned credentials
        /// </summary>
        public ObservableCollection<OrphanedCredential> OrphanedCredentials
        {
            get => _orphanedCredentials;
            set => SetProperty(ref _orphanedCredentials, value);
        }

        /// <summary>
        /// Gets or sets the selected orphaned credential
        /// </summary>
        public OrphanedCredential? SelectedOrphanedCredential
        {
            get => _selectedOrphanedCredential;
            set
            {
                if (SetProperty(ref _selectedOrphanedCredential, value))
                {
                    OnPropertyChanged(nameof(CanRestore));
                }
            }
        }

        /// <summary>
        /// Gets whether a credential can be restored (one is selected)
        /// </summary>
        public bool CanRestore => SelectedOrphanedCredential != null;

        /// <summary>
        /// Gets whether there are orphaned credentials to display
        /// </summary>
        public bool HasOrphanedCredentials => OrphanedCredentials.Count > 0;

        /// <summary>
        /// Gets whether there are no orphaned credentials
        /// </summary>
        public bool HasNoOrphanedCredentials => OrphanedCredentials.Count == 0;

        /// <summary>
        /// Gets the restored credential if user clicked Restore
        /// </summary>
        public OrphanedCredential? RestoredCredential { get; private set; }

        public CredentialRecoveryDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        /// <summary>
        /// Sets the list of orphaned credentials to display
        /// </summary>
        public void SetOrphanedCredentials(ObservableCollection<OrphanedCredential> credentials)
        {
            OrphanedCredentials = credentials;
            OnPropertyChanged(nameof(HasOrphanedCredentials));
            OnPropertyChanged(nameof(HasNoOrphanedCredentials));
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedOrphanedCredential == null)
            {
                MessageBox.Show(
                    "Please select a credential to restore.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            RestoredCredential = SelectedOrphanedCredential;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
