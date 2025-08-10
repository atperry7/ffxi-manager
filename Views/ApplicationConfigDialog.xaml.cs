using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FFXIManager.Models;

namespace FFXIManager.Views
{
    /// <summary>
    /// Interaction logic for ApplicationConfigDialog.xaml
    /// </summary>
    public partial class ApplicationConfigDialog : Window
    {
        private ExternalApplication _application;

        public ExternalApplication Application => _application;

        public ApplicationConfigDialog(ExternalApplication application)
        {
            try
            {
                InitializeComponent();
                
                _application = application ?? throw new ArgumentNullException(nameof(application));
                DataContext = _application;
                
                // Set window properties for better behavior
                this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                this.ShowInTaskbar = false;
                this.Topmost = false;
                
                // Ensure the window is properly sized
                this.SizeToContent = SizeToContent.Manual;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing dialog: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
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
                        // Ignore errors in setting initial directory
                    }
                }

                if (openFileDialog.ShowDialog() == true)
                {
                    _application.ExecutablePath = openFileDialog.FileName;
                    
                    // Auto-fill working directory if not set
                    if (string.IsNullOrEmpty(_application.WorkingDirectory))
                    {
                        var directory = Path.GetDirectoryName(openFileDialog.FileName);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            _application.WorkingDirectory = directory;
                        }
                    }
                    
                    // Auto-fill name if not set
                    if (string.IsNullOrEmpty(_application.Name) || _application.Name == "New Application")
                    {
                        _application.Name = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing for executable: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folderDialog = new OpenFolderDialog
                {
                    Title = "Select Working Directory",
                    Multiselect = false
                };

                if (!string.IsNullOrEmpty(_application.WorkingDirectory))
                {
                    try
                    {
                        if (Directory.Exists(_application.WorkingDirectory))
                        {
                            folderDialog.InitialDirectory = _application.WorkingDirectory;
                        }
                    }
                    catch
                    {
                        // Ignore errors in setting initial directory
                    }
                }
                else if (!string.IsNullOrEmpty(_application.ExecutablePath))
                {
                    try
                    {
                        var directory = Path.GetDirectoryName(_application.ExecutablePath);
                        if (Directory.Exists(directory))
                        {
                            folderDialog.InitialDirectory = directory;
                        }
                    }
                    catch
                    {
                        // Ignore errors in setting initial directory
                    }
                }

                if (folderDialog.ShowDialog() == true)
                {
                    _application.WorkingDirectory = folderDialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing for working directory: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Error saving application: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateApplication()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_application.Name))
                {
                    MessageBox.Show("Please enter an application name.", "Validation Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    try
                    {
                        NameTextBox?.Focus();
                    }
                    catch
                    {
                        // Ignore focus errors
                    }
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_application.ExecutablePath))
                {
                    MessageBox.Show("Please select an executable file.", "Validation Error", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    try
                    {
                        ExecutablePathTextBox?.Focus();
                    }
                    catch
                    {
                        // Ignore focus errors
                    }
                    return false;
                }

                if (!File.Exists(_application.ExecutablePath))
                {
                    var result = MessageBox.Show(
                        $"The executable file does not exist:\n{_application.ExecutablePath}\n\nDo you want to continue anyway?", 
                        "File Not Found", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.No)
                    {
                        try
                        {
                            ExecutablePathTextBox?.Focus();
                        }
                        catch
                        {
                            // Ignore focus errors
                        }
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during validation: {ex.Message}", "Validation Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}