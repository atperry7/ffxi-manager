using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;

namespace FFXIManager.Views.WorkflowEditor.ActionEditors
{
    /// <summary>
    /// UserControl for editing wait/delay action parameters.
    /// </summary>
    public partial class WaitActionEditor : UserControl, INotifyPropertyChanged
    {
        private KeyboardAction? _action;

        public WaitActionEditor()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            this.DataContext = this;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is KeyboardAction action)
            {
                _action = action;
                OnPropertyChanged(nameof(DelayMs));
                OnPropertyChanged(nameof(Description));
            }
        }

        public int DelayMs
        {
            get => _action?.DelayMs ?? 1000;
            set
            {
                if (_action != null)
                {
                    _action.DelayMs = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Description
        {
            get => _action?.Description ?? string.Empty;
            set
            {
                if (_action != null)
                {
                    _action.Description = value;
                    OnPropertyChanged();
                }
            }
        }

        private void SetDelay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagValue)
            {
                if (int.TryParse(tagValue, out var delayValue))
                {
                    DelayMs = delayValue;
                }
            }
        }

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
