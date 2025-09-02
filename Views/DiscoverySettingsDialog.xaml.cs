using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FFXIManager.Services;

namespace FFXIManager.Views
{
    public partial class DiscoverySettingsDialog : Window
    {
        private DispatcherTimer? _controllerStatusTimer;

        public DiscoverySettingsDialog()
        {
            InitializeComponent();
            
            // Check controller status after UI loads and start periodic updates
            Loaded += async (s, e) => 
            {
                await UpdateControllerStatusAsync();
                StartControllerStatusTimer();
            };

            // Stop timer when window closes
            Closing += (s, e) => StopControllerStatusTimer();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.DiscoverySettingsViewModel vm)
            {
                vm.Save();
            }
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Updates the controller status indicator based on current controller connection state.
        /// </summary>
        private async Task UpdateControllerStatusAsync()
        {
            try
            {
                // Use dedicated ControllerInputService for detection
                using var controllerService = new ControllerInputService();
                
                // Allow time for XInput detection (controllers can take a moment to enumerate)
                await Task.Delay(500);
                
                var isConnected = controllerService.IsAnyControllerConnected;
                
                ControllerStatusText.Text = isConnected ? 
                    "Controller: Connected ✅" : 
                    "Controller: Not detected";
            }
            catch (Exception ex)
            {
                ControllerStatusText.Text = "Controller: Status unknown";
                System.Diagnostics.Debug.WriteLine($"Controller status check failed: {ex.Message}");
            }
        }


        /// <summary>
        /// Starts periodic controller status checking for hotplug detection
        /// </summary>
        private void StartControllerStatusTimer()
        {
            _controllerStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Check every 2 seconds for changes
            };
            _controllerStatusTimer.Tick += async (s, e) => await UpdateControllerStatusAsync();
            _controllerStatusTimer.Start();
        }

        /// <summary>
        /// Stops the controller status timer
        /// </summary>
        private void StopControllerStatusTimer()
        {
            _controllerStatusTimer?.Stop();
            _controllerStatusTimer = null;
        }

        /// <summary>
        /// Handles the refresh controller button click
        /// </summary>
        private async void RefreshController_Click(object sender, RoutedEventArgs e)
        {
            ControllerStatusText.Text = "Controller: Checking...";
            await Task.Delay(50); // Brief UI update delay
            await UpdateControllerStatusAsync();
        }
    }
}
