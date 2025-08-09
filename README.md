# FFXI Manager - Login Profile Swapper

A WPF application that allows you to manage and swap FFXI (Final Fantasy XI) login profiles for use with PlayOnline and Windower.

## Overview

This application solves the limitation of the `login_w.bin` file which can only contain 4 accounts. By creating backup profiles, you can manage multiple sets of 4 accounts and easily swap between them.

## Features

- **Profile Management**: View all available login profiles in a user-friendly interface
- **System File Protection**: Automatically excludes system files (login_w.bin, inet_w.bin, noramim.bin) from management
- **Quick Swapping**: Easily swap the active `login_w.bin` file with backup profiles
- **Backup Creation**: Create backup copies of your current active login configuration
- **Profile Details**: View file information including size, last modified date, type, and description
- **Directory Configuration**: Set custom PlayOnline directory paths
- **Safe Operations**: Automatic backup creation before swapping profiles with cleanup of old backups

## Usage

### Initial Setup

1. **Launch the Application**: Run FFXIManager.exe
2. **Configure Directory**: If needed, click "Browse..." to set your PlayOnline directory
   - Default: `C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all`
3. **Refresh**: Click "Refresh" to load existing profile files

### Typical Workflow

1. **Setup Your Primary Accounts**:
   - Configure your first 4 accounts in PlayOnline as usual
   - Launch FFXIManager
   - Create a backup: Enter a name like "Main Characters" and click "Create Backup from Active"

2. **Setup Additional Account Sets**:
   - Configure 4 different accounts in PlayOnline
   - Create another backup: Enter a name like "Alt Characters" and click "Create Backup from Active"

3. **Launching Windower with Different Profiles**:
   - Select the desired profile from the list
   - Click "Swap to Selected Profile"
   - Launch Windower - it will now use the selected account set
   - Repeat for each character you want to launch

### Profile Operations

- **Swap Profile**: Select a profile and click "Swap to Selected Profile" to make it active
- **Create Backup**: Enter a name and click "Create Backup from Active" to save current settings
- **Delete Profile**: Select a profile and click "Delete Selected Profile" (cannot delete active profile)
- **Refresh**: Click "Refresh" to reload the profile list after external changes

## File Structure

- **System File**: `login_w.bin` - The file PlayOnline actually reads (shown as "System File")
- **Active Profile**: The backup profile currently loaded into login_w.bin (shown as "Active Profile") 
- **Backup Files**: `[custom_name].bin` - Your saved profile backups (shown as "Inactive" when not in use)
- **Auto-Backups**: `backup_[timestamp].bin` - Automatic backups created during swaps
- **System Files**: `inet_w.bin`, `noramim.bin` - Excluded from profile management

## Safety Features

- System files (login_w.bin, inet_w.bin, noramim.bin) are automatically excluded from management
- The active login_w.bin file is shown for informational purposes but cannot be swapped or deleted
- Automatic backup creation before each profile swap
- Cannot delete the currently active profile
- Confirmation dialog for profile deletion
- Status messages for all operations
- Error handling with descriptive messages
- Auto-cleanup of old backup files (keeps 10 most recent)

## Technical Details

- **Framework**: .NET 9 WPF Application
- **Architecture**: MVVM pattern for maintainability and extensibility
- **File Operations**: Safe file copying with error handling
- **UI**: Modern WPF interface with data binding

## Future Enhancements

The application is designed with extensibility in mind. Potential future features:

- Profile descriptions and metadata
- Import/Export functionality
- Integration with Windower launcher
- Profile synchronization across multiple installations
- Character information extraction from login files

## Troubleshooting

### Common Issues

1. **"Directory not found" error**: Verify the PlayOnline directory path is correct
2. **"Access denied" error**: Run as administrator or check file permissions
3. **Profiles not showing**: Ensure .bin files exist in the PlayOnline directory

### Support

For issues or feature requests, please check the application's status message for specific error details.

## Requirements

- Windows 10/11
- .NET 9 Runtime
- FFXI/PlayOnline installation
- Appropriate file system permissions for the PlayOnline directory