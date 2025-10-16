using System;
using System.Windows;
using System.Windows.Input;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    public partial class TemplateNavigationTuner : Window
    {
        private TemplateNavigationTunerViewModel? ViewModel => DataContext as TemplateNavigationTunerViewModel;

        public TemplateNavigationTuner(
            ITemplateManagementService templateService,
            ITemplateMatchingService matchingService,
            IUIAutomationService automation,
            IScreenshotCaptureService screenshots,
            ILoggingService log,
            FFXIManager.Infrastructure.IProcessManagementService processes,
            IServiceProvider serviceProvider)
        {
            InitializeComponent();
            this.DataContext = new TemplateNavigationTunerViewModel(templateService, matchingService, automation, screenshots, log, processes, serviceProvider);

            // Ensure cleanup on window closing
            Closing += TemplateNavigationTuner_Closing;
        }

        // Parameterless constructor for designer support only
        public TemplateNavigationTuner()
        {
            InitializeComponent();
        }

        private void TemplateNavigationTuner_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Unsubscribe from event
            Closing -= TemplateNavigationTuner_Closing;

            // Dispose ViewModel if it implements IDisposable
            if (ViewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Clear DataContext to release ViewModel reference
            DataContext = null;
        }

        /// <summary>
        /// Handles clicks on the template canvas for setting click points
        /// </summary>
        private void TemplateCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || !ViewModel.IsClickEditMode)
                return;

            // Get click position relative to the canvas
            var clickPosition = e.GetPosition(TemplateCanvas);

            // Let the ViewModel handle the click (it will convert coordinates)
            ViewModel.HandleCanvasClick(clickPosition.X, clickPosition.Y);

            e.Handled = true;
        }
    }
}
