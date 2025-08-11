using System.Windows;
using FFXIManager.ViewModels;

namespace FFXIManager
{
    /// <summary>
    /// MainWindow with simple, clean initialization
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