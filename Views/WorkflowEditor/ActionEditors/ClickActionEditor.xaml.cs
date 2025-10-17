using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;

namespace FFXIManager.Views.WorkflowEditor.ActionEditors
{
    /// <summary>
    /// UserControl for editing click action parameters.
    /// Uses resolution-independent relative coordinates (0.0 - 1.0).
    /// </summary>
    public partial class ClickActionEditor : UserControl, INotifyPropertyChanged
    {
        private KeyboardAction? _action;

        public ClickActionEditor()
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
                OnPropertyChanged(nameof(ClickX));
                OnPropertyChanged(nameof(ClickY));
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(DelayMs));
                OnPropertyChanged(nameof(Description));
            }
        }

        public double ClickX
        {
            get => _action?.ClickX ?? 0.5;
            set
            {
                if (_action != null)
                {
                    _action.ClickX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double ClickY
        {
            get => _action?.ClickY ?? 0.5;
            set
            {
                if (_action != null)
                {
                    _action.ClickY = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Count
        {
            get => _action?.Count ?? 1;
            set
            {
                if (_action != null)
                {
                    _action.Count = value;
                    OnPropertyChanged();
                }
            }
        }

        public int DelayMs
        {
            get => _action?.DelayMs ?? 500;
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

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
