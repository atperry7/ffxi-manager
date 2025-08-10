using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// ViewModel for the RenameProfileDialog - following MVVM pattern
    /// </summary>
    public class RenameProfileDialogViewModel : INotifyPropertyChanged
    {
        private string _newProfileName = string.Empty;
        
        public string CurrentName { get; }
        public bool IsSystemFile { get; }
        
        public string NewProfileName
        {
            get => _newProfileName;
            set
            {
                if (SetProperty(ref _newProfileName, value))
                {
                    // Add null check to prevent exception during initialization
                    if (ConfirmCommand is RelayCommand relayCommand)
                    {
                        relayCommand.RaiseCanExecuteChanged();
                    }
                    // Also notify that button status message might have changed
                    OnPropertyChanged(nameof(ButtonStatusMessage));
                }
            }
        }
        
        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
        
        public bool? DialogResult { get; private set; }

        public string ButtonStatusMessage
        {
            get
            {
                if (IsSystemFile)
                    return "Cannot rename system file (login_w.bin)";
                if (string.IsNullOrWhiteSpace(NewProfileName))
                    return "Enter a profile name";
                return "Ready to rename";
            }
        }
        
        public RenameProfileDialogViewModel(string currentName, bool isSystemFile)
        {
            CurrentName = currentName;
            IsSystemFile = isSystemFile;
            
            // Initialize commands BEFORE setting NewProfileName to avoid null reference
            ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
            CancelCommand = new RelayCommand(Cancel);
            
            // Now safe to set NewProfileName (triggers setter that calls ConfirmCommand)
            NewProfileName = currentName;
        }
        
        private void Confirm()
        {
            System.Diagnostics.Debug.WriteLine($"Confirm() called - Setting DialogResult to true");
            DialogResult = true;
            OnPropertyChanged(nameof(DialogResult)); // Explicitly notify
        }
        
        private void Cancel()
        {
            System.Diagnostics.Debug.WriteLine($"Cancel() called - Setting DialogResult to false");
            DialogResult = false;
            OnPropertyChanged(nameof(DialogResult)); // Explicitly notify
        }
        
        private bool CanConfirm()
        {
            // Debug: Let's see what's happening
            var canConfirm = !string.IsNullOrWhiteSpace(NewProfileName) && !IsSystemFile;
            
            // For debugging - you can remove this later
            System.Diagnostics.Debug.WriteLine($"CanConfirm: NewProfileName='{NewProfileName}', IsSystemFile={IsSystemFile}, Result={canConfirm}");
            
            return canConfirm;
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