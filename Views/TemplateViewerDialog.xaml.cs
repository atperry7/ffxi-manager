using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FFXIManager.Models;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for TemplateViewerDialog.xaml
    /// </summary>
    public partial class TemplateViewerDialog : Window
    {
        public TemplateViewerDialog(TemplateViewerDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TemplateCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not TemplateViewerDialogViewModel vm) return;
            if (!vm.IsPickMode) return;

            // Get click position relative to the canvas
            if (sender is not System.Windows.Controls.Canvas canvas) return;
            var p = e.GetPosition(canvas);
            if (canvas.Width <= 0 || canvas.Height <= 0) return;

            // Normalize to 0.0 - 1.0
            var relX = Math.Max(0.0, Math.Min(1.0, p.X / canvas.Width));
            var relY = Math.Max(0.0, Math.Min(1.0, p.Y / canvas.Height));

            vm.PickedX = relX;
            vm.PickedY = relY;

            // Update visual marker to reflect new pick (single marker)
            vm.ClickMarkers.Clear();
            vm.ClickMarkers.Add(new ClickMarker
            {
                X = p.X,
                Y = p.Y,
                Label = "1",
                Description = "Picked position",
                MarkerColor = Brushes.Red,
                StepIndex = 0
            });

            vm.Status = $"Picked: X={relX:F2}, Y={relY:F2}. Click Apply to confirm, or click again to adjust.";
            vm.NotifyPickChanged();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm and close
            try { this.DialogResult = true; } catch { }
            Close();
        }
    }
}
