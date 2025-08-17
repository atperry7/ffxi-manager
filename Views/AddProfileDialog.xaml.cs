using System.Windows;
using System.ComponentModel;

namespace FFXIManager.Views
{
    public partial class AddProfileDialog : Window
    {
        public AddProfileDialog()
        {
            InitializeComponent();
            Loaded += AddProfileDialog_Loaded;
        }

        private void AddProfileDialog_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is INotifyPropertyChanged vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Listen for successful profile creation by checking if NewBackupName is cleared
            if (e.PropertyName == "NewBackupName")
            {
                var vmType = sender?.GetType();
                var prop = vmType?.GetProperty("NewBackupName");
                if (prop != null)
                {
                    var value = prop.GetValue(sender) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        DialogResult = true;
                        Close();
                    }
                }
            }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DataContext is INotifyPropertyChanged vm)
            {
                vm.PropertyChanged -= Vm_PropertyChanged;
            }
            base.OnClosed(e);
        }
    }
}
