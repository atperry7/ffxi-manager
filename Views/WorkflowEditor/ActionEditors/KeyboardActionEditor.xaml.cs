using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using FFXIManager.Models;

namespace FFXIManager.Views.WorkflowEditor.ActionEditors
{
    /// <summary>
    /// UserControl for editing keyboard action parameters.
    /// Handles Tab, Enter, Arrow keys, and other keyboard inputs.
    /// </summary>
    public partial class KeyboardActionEditor : UserControl, INotifyPropertyChanged
    {
        private KeyboardAction? _action;
        private static readonly List<string> _availableKeys = new()
        {
            "Tab",
            "Enter",
            "Escape",
            "Up",
            "Down",
            "Left",
            "Right",
            "Space",
            "Backspace",
            "Delete",
            "Home",
            "End",
            "PageUp",
            "PageDown"
        };

        public KeyboardActionEditor()
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
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(DelayMs));
                OnPropertyChanged(nameof(Description));
            }
        }

        public List<string> AvailableKeys => _availableKeys;

        public string Action
        {
            get => _action?.Action ?? string.Empty;
            set
            {
                if (_action != null)
                {
                    _action.Action = value;
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
            get => _action?.DelayMs ?? 200;
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
