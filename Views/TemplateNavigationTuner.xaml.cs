using System;
using System.Windows;
using FFXIManager.Services;
using FFXIManager.Services.AutoLogin.ScreenDetection;
using FFXIManager.ViewModels;

namespace FFXIManager.Views
{
    public partial class TemplateNavigationTuner : Window
    {
        public TemplateNavigationTuner(
            ITemplateManagementService templateService,
            ITemplateMatchingService matchingService,
            IUIAutomationService automation,
            IScreenshotCaptureService screenshots,
            ILoggingService log,
            FFXIManager.Infrastructure.IProcessManagementService processes)
        {
            InitializeComponent();
            this.DataContext = new TemplateNavigationTunerViewModel(templateService, matchingService, automation, screenshots, log, processes);
        }

        // Parameterless constructor for designer support only
        public TemplateNavigationTuner()
        {
            InitializeComponent();
        }
    }
}
