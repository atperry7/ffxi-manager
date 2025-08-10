using System.Windows;
using FFXIManager.ViewModels;

namespace FFXIManager
{
    /// <summary>
    /// MainWindow with ZERO business logic - pure MVVM implementation
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}