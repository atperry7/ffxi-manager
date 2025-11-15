using FFXIManager.Models;
using FFXIManager.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

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

            // Listen for property changes to redraw grid when ShowCoordinateGrid changes
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Draw grid after window loads
            Loaded += (s, e) => DrawCoordinateGrid();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TemplateViewerDialogViewModel.ShowCoordinateGrid))
            {
                DrawCoordinateGrid();
            }
            else if (e.PropertyName == nameof(TemplateViewerDialogViewModel.TemplateImageWidth) ||
                     e.PropertyName == nameof(TemplateViewerDialogViewModel.TemplateImageHeight))
            {
                // Redraw grid when template dimensions change
                if (DataContext is TemplateViewerDialogViewModel vm && vm.ShowCoordinateGrid)
                {
                    DrawCoordinateGrid();
                }
            }
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

            // Convert to appropriate coordinate system based on FromCenterMode
            double relX, relY;
            if (vm.FromCenterMode)
            {
                // Center-relative: -0.5 to 0.5
                relX = (p.X / canvas.Width) - 0.5;
                relY = (p.Y / canvas.Height) - 0.5;
            }
            else
            {
                // Template-relative: 0.0 to 1.0
                relX = Math.Max(0.0, Math.Min(1.0, p.X / canvas.Width));
                relY = Math.Max(0.0, Math.Min(1.0, p.Y / canvas.Height));
            }

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
                marker.RelativeX = relX;
                marker.RelativeY = relY;
                marker.Description = $"Slot {idxToSet + 1}";

                var coordMode = vm.FromCenterMode ? "center" : "template";
                vm.Status = $"Picked Slot {idxToSet + 1}: X={relX:F3}, Y={relY:F3} ({coordMode}-relative). Click again to set next slot or Apply to confirm.";
                vm.NotifyPickChanged();

                // Advance to next index (wrap at end)
                vm.CurrentPickIndex = (idxToSet + 1) % vm.MultiPickCount;
            }
            else if (vm.IsMultiPickMode)
            {
                // Append mode: each click adds a new point (no upper bound)
                int idx = vm.ClickMarkers.Count;
                vm.ClickMarkers.Add(new ClickMarker
                {
                    X = p.X,
                    Y = p.Y,
                    RelativeX = relX,
                    RelativeY = relY,
                    Label = (idx + 1).ToString(),
                    Description = $"Click {idx + 1}",
                    MarkerColor = GetColorForIndex(idx),
                    StepIndex = idx
                });

                var coordMode = vm.FromCenterMode ? "center" : "template";
                vm.Status = $"Added point {idx + 1}: X={relX:F3}, Y={relY:F3} ({coordMode}-relative). Click again to add more or Apply to confirm.";
                vm.NotifyPickChanged();
            }
            else
            {
                // Single-pick mode: show one marker
                vm.ClickMarkers.Clear();
                vm.ClickMarkers.Add(new ClickMarker
                {
                    X = p.X,
                    Y = p.Y,
                    RelativeX = relX,
                    RelativeY = relY,
                    Label = "1",
                    Description = "Picked position",
                    MarkerColor = Brushes.Red,
                    StepIndex = 0
                });

                var coordMode = vm.FromCenterMode ? "center" : "template";
                vm.Status = $"Picked: X={relX:F3}, Y={relY:F3} ({coordMode}-relative). Click Apply to confirm, or click again to adjust.";
                vm.NotifyPickChanged();
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm and close
            try { this.DialogResult = true; } catch { }
            Close();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TemplateViewerDialogViewModel vm) return;
            if (!vm.IsPickMode) return;

            vm.ClickMarkers.Clear();
            vm.CurrentPickIndex = 0;
            vm.Status = "Cleared all points. Click the image to add points.";
        }

        private void RemoveClickPoint_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not TemplateViewerDialogViewModel vm) return;
            if (sender is not System.Windows.Controls.Button button) return;
            if (button.Tag is not ClickMarker marker) return;

            vm.ClickMarkers.Remove(marker);
            vm.Status = $"Removed click point. {vm.ClickMarkers.Count} point(s) remaining.";
        }

        private static Brush GetColorForIndex(int index)
        {
            // Extended color palette for up to 16 character slots
            return (index % 16) switch
            {
                0 => Brushes.Red,
                1 => Brushes.DodgerBlue,
                2 => Brushes.Orange,
                3 => Brushes.LimeGreen,
                4 => Brushes.Purple,
                5 => Brushes.DeepPink,
                6 => Brushes.Cyan,
                7 => Brushes.Gold,
                8 => Brushes.Crimson,
                9 => Brushes.RoyalBlue,
                10 => Brushes.DarkOrange,
                11 => Brushes.ForestGreen,
                12 => Brushes.MediumPurple,
                13 => Brushes.HotPink,
                14 => Brushes.Teal,
                15 => Brushes.Yellow,
                _ => Brushes.Gray
            };
        }

        private void TemplateCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (DataContext is not TemplateViewerDialogViewModel vm) return;
            if (sender is not System.Windows.Controls.Canvas canvas) return;

            var p = e.GetPosition(canvas);
            if (canvas.Width <= 0 || canvas.Height <= 0) return;

            // Calculate template-relative coordinates (0.0 to 1.0)
            vm.MouseTemplateRelativeX = Math.Max(0.0, Math.Min(1.0, p.X / canvas.Width));
            vm.MouseTemplateRelativeY = Math.Max(0.0, Math.Min(1.0, p.Y / canvas.Height));

            // Calculate center-relative coordinates (-0.5 to 0.5)
            vm.MouseCenterRelativeX = (p.X / canvas.Width) - 0.5;
            vm.MouseCenterRelativeY = (p.Y / canvas.Height) - 0.5;
        }

        private void TemplateCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is TemplateViewerDialogViewModel vm)
            {
                vm.IsMouseOverTemplate = true;
            }
        }

        private void TemplateCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is TemplateViewerDialogViewModel vm)
            {
                vm.IsMouseOverTemplate = false;
            }
        }

        /// <summary>
        /// Draws the center-relative coordinate grid overlay
        /// </summary>
        private void DrawCoordinateGrid()
        {
            CoordinateGridCanvas.Children.Clear();

            if (DataContext is not TemplateViewerDialogViewModel vm) return;
            if (!vm.ShowCoordinateGrid) return;

            var width = vm.TemplateImageWidth;
            var height = vm.TemplateImageHeight;

            if (width <= 0 || height <= 0) return;

            var centerX = width / 2.0;
            var centerY = height / 2.0;

            // Draw vertical grid lines (every 0.1 units from -0.5 to 0.5)
            for (double coord = -0.5; coord <= 0.5; coord += 0.1)
            {
                var x = centerX + (coord * width);
                var isCenterLine = Math.Abs(coord) < 0.01;

                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = isCenterLine
                        ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 0)) // Center line: yellow
                        : new SolidColorBrush(Color.FromArgb((byte)(Math.Abs(coord) > 0.45 ? 64 : 48), 255, 255, 255)), // Edges brighter
                    StrokeThickness = isCenterLine ? 2 : 1
                };

                if (!isCenterLine)
                {
                    line.StrokeDashArray = new DoubleCollection { 2, 2 };
                }

                CoordinateGridCanvas.Children.Add(line);
            }

            // Draw horizontal grid lines (every 0.1 units from -0.5 to 0.5)
            for (double coord = -0.5; coord <= 0.5; coord += 0.1)
            {
                var y = centerY + (coord * height);
                var isCenterLine = Math.Abs(coord) < 0.01;

                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = width,
                    Y2 = y,
                    Stroke = isCenterLine
                        ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 0)) // Center line: yellow
                        : new SolidColorBrush(Color.FromArgb((byte)(Math.Abs(coord) > 0.45 ? 64 : 48), 255, 255, 255)), // Edges brighter
                    StrokeThickness = isCenterLine ? 2 : 1
                };

                if (!isCenterLine)
                {
                    line.StrokeDashArray = new DoubleCollection { 2, 2 };
                }

                CoordinateGridCanvas.Children.Add(line);
            }

            // Draw center crosshair marker
            var centerMarker = new System.Windows.Shapes.Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromArgb(128, 255, 255, 0)),
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            System.Windows.Controls.Canvas.SetLeft(centerMarker, centerX - 6);
            System.Windows.Controls.Canvas.SetTop(centerMarker, centerY - 6);
            CoordinateGridCanvas.Children.Add(centerMarker);

            // Add center label
            var centerLabel = new System.Windows.Controls.TextBlock
            {
                Text = "(0, 0)",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
                Padding = new Thickness(2)
            };
            System.Windows.Controls.Canvas.SetLeft(centerLabel, centerX + 15);
            System.Windows.Controls.Canvas.SetTop(centerLabel, centerY - 20);
            CoordinateGridCanvas.Children.Add(centerLabel);
        }
    }
}
