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

            if (vm.IsMultiPickMode && vm.MultiPickCount > 1)
            {
                // Ensure markers up to count exist
                while (vm.ClickMarkers.Count < vm.MultiPickCount)
                {
                    int idx = vm.ClickMarkers.Count;
                    vm.ClickMarkers.Add(new ClickMarker
                    {
                        X = 0,
                        Y = 0,
                        Label = (idx + 1).ToString(),
                        Description = $"Slot {idx + 1}",
                        MarkerColor = GetColorForIndex(idx),
                        StepIndex = idx
                    });
                }

                // Update marker for current index
                var idxToSet = Math.Max(0, Math.Min(vm.MultiPickCount - 1, vm.CurrentPickIndex));
                var marker = vm.ClickMarkers[idxToSet];
                marker.X = p.X;
                marker.Y = p.Y;
                marker.Description = $"Slot {idxToSet + 1}";

                vm.Status = $"Picked Slot {idxToSet + 1}: X={relX:F2}, Y={relY:F2}. Click again to set next slot or Apply to confirm.";
                vm.NotifyPickChanged();

                // Advance to next index (wrap at end)
                vm.CurrentPickIndex = (idxToSet + 1) % vm.MultiPickCount;
            }
            else
            {
                // Single-pick mode: show one marker
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
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm and close
            try { this.DialogResult = true; } catch { }
            Close();
        }

        private static Brush GetColorForIndex(int index)
        {
            switch (index)
            {
                case 0: return Brushes.Red;
                case 1: return Brushes.DodgerBlue;
                case 2: return Brushes.Orange;
                case 3: return Brushes.LimeGreen;
                default: return Brushes.Purple;
            }
        }
    }
}
