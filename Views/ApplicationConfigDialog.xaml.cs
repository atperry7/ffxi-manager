using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using FFXIManager.Models;

namespace FFXIManager.Views
{
    /// <summary>
    /// Simple, reliable ApplicationConfigDialog based on working dialog patterns
    /// </summary>
    public partial class ApplicationConfigDialog : Window
    {
        private readonly ExternalApplication _application;

        public ExternalApplication Application => _application;

        public ApplicationConfigDialog(ExternalApplication application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));

            InitializeComponent();

            // Set window properties
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            // Set DataContext
            DataContext = _application;

            // Focus on name textbox when loaded
            Loaded += (s, e) => NameTextBox.Focus();
        }

        private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Application Executable",
                    Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect = false
                };

                // Set initial directory if we have an existing path
                if (!string.IsNullOrEmpty(_application.ExecutablePath))
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(_application.ExecutablePath);
                        if (Directory.Exists(directory))
                        {
                            openFileDialog.InitialDirectory = directory;
                        }
                        openFileDialog.FileName = Path.GetFileName(_application.ExecutablePath);
                    }
                    catch
                    {
                        // Ignore errors setting initial directory
                    }
                }

                if (openFileDialog.ShowDialog() == true)
                {
                    _application.ExecutablePath = openFileDialog.FileName;

                    // Auto-fill name if it's still "New Application"
                    if (string.IsNullOrEmpty(_application.Name) || _application.Name == "New Application")
                    {
                        _application.Name = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing for executable: {ex.Message}", "Browse Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ValidateApplication())
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving application: {ex.Message}", "Save Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error closing dialog: {ex.Message}", "Close Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateApplication()
        {
            // Check required fields
            if (string.IsNullOrWhiteSpace(_application.Name))
            {
                MessageBox.Show("Please enter an application name.", "Validation Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(_application.ExecutablePath))
            {
                MessageBox.Show("Please select an executable file.", "Validation Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                ExecutablePathTextBox.Focus();
                return false;
            }

            // Warn about non-existent files but allow saving
            if (!File.Exists(_application.ExecutablePath))
            {
                var result = MessageBox.Show(
                    $"The executable file does not exist:\n{_application.ExecutablePath}\n\nDo you want to continue anyway?",
                    "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    ExecutablePathTextBox.Focus();
                    return false;
                }
            }

            return true;
        }
    }
}
