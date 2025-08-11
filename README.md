# FFXI Manager - Profile & Application Management Suite

A modern WPF application that provides comprehensive management for FFXI (Final Fantasy XI) login profiles and external applications, designed for use with PlayOnline, Windower, and related tools.

## Overview

FFXI Manager solves multiple challenges for FFXI players:

- **Profile Management**: Overcome the 4-account limitation of `login_w.bin` by managing unlimited profile sets
- **Application Management**: Launch, monitor, and control external FFXI applications from a centralized interface
- **Streamlined Workflow**: Seamlessly switch between different character sets and launch associated tools

## Key Features

### 🔐 **Advanced Profile Management**

- **Unlimited Profiles**: Create and manage unlimited sets of 4-account login configurations
- **Smart Profile Tracking**: Automatically tracks which profile is currently active
- **Safe Swapping**: Secure profile switching with automatic backup creation
- **Profile Operations**: Create, rename, delete, and organize login profiles
- **System Protection**: Automatic exclusion of critical system files from management
- **Auto-Cleanup**: Intelligent cleanup of old backup files

### 🚀 **External Application Management**

- **Application Launcher**: Launch and monitor external FFXI applications
- **Process Monitoring**: Real-time status tracking of running applications
- **Application Control**: Start, stop, and manage application lifecycles
- **Configuration Management**: Store and manage application settings and paths
- **Multi-Instance Support**: Configure applications for single or multiple instances
- **Persistent Settings**: Application configurations saved between sessions

### 🎯 **Integrated User Experience**

- **Unified Interface**: Single application for all FFXI management needs
- **Real-Time Status**: Live status indicators for profiles and applications
- **Modern UI**: Clean, responsive WPF interface with intuitive controls
- **Status Feedback**: Comprehensive status messages and operation feedback
- **Error Handling**: Robust error handling with descriptive messages

## Typical Workflow

### Profile Setup & Management

1. **Initial Configuration**:
   - Launch FFXI Manager
   - Configure your PlayOnline directory path
   - Set up your first 4 accounts in PlayOnline

2. **Create Profile Sets**:
   - Create backup: "Main Characters" → Click "Add Profile"
   - Configure different accounts in PlayOnline
   - Create backup: "Alt Characters" → Click "Add Profile"

3. **Switch Between Profiles**:
   - Select desired profile from the list
   - Click "Swap to Selected Profile"
   - Launch Windower with the selected account set

### Application Management

1. **Add Applications**:
   - Click "➕" to add new external applications
   - Configure paths for Windower, POL Proxy, or custom tools
   - Set launch parameters and working directories

2. **Launch & Monitor**:
   - Click "▶️" to launch applications
   - Monitor real-time status (Running/Stopped)
   - Click "⏹️" to stop running applications

3. **Configuration**:
   - Click "⚙️" to edit application settings
   - Update paths, arguments, and options
   - Configure multi-instance behavior

## Application Features

### Profile Operations

- **Swap Profile**: Make any backup profile the active login configuration
- **Create Backup**: Save current login settings as a named profile
- **Rename Profile**: Change profile names for better organization
- **Delete Profile**: Remove unused profiles (with safety checks)
- **Copy Names**: Copy profile names to clipboard
- **Open Location**: Open file location in Windows Explorer

### Application Operations

- **Launch**: Start external applications with configured settings
- **Stop**: Terminate running applications safely
- **Edit**: Modify application configuration and settings
- **Add**: Register new external applications
- **Remove**: Unregister applications from management
- **Monitor**: Real-time process status monitoring

### Built-in Applications

The application comes pre-configured with common FFXI tools:

- **Windower**: FFXI game launcher with addon support
- **POL Proxy**: PlayOnline proxy server
- **Silmaril**: FFXI utility tool

## File Structure & Safety

### Profile Files

- **Active System File**: `login_w.bin` - Current active login configuration
- **Profile Backups**: `[profile_name].bin` - Your saved profile sets
- **Auto-Backups**: `backup_[timestamp].bin` - Automatic safety backups
- **Protected Files**: `inet_w.bin`, `noramim.bin` - Excluded from management

### Application Data

- **Settings Storage**: `%AppData%\FFXIManager\FFXIManagerSettings.json`
- **Profile Configurations**: Stored within PlayOnline directory
- **Application Configs**: Persisted application settings and paths

## Safety & Reliability Features

### Profile Safety

- **System File Protection**: Critical files automatically excluded
- **Active Profile Protection**: Cannot delete currently active profiles
- **Automatic Backups**: Safety backups created before all swaps
- **Confirmation Dialogs**: Verification for destructive operations
- **Status Tracking**: Always know which profile is active

### Application Safety

- **Process Monitoring**: Real-time application status tracking
- **Safe Termination**: Graceful application shutdown procedures
- **Configuration Validation**: Verify executable paths before launch
- **Error Recovery**: Robust error handling and status reporting

## Technical Specifications

- **Framework**: .NET 9 WPF Application
- **Architecture**: Clean MVVM pattern with specialized ViewModels
- **UI Technology**: Modern WPF with data binding and responsive design
- **File Operations**: Safe file copying with comprehensive error handling
- **Process Management**: Advanced process monitoring and lifecycle management
- **Data Persistence**: JSON-based settings with automatic backup

## System Requirements

- **Operating System**: Windows 10/11
- **Runtime**: .NET 9 Runtime
- **Game**: FFXI/PlayOnline installation
- **Permissions**: Appropriate file system access to PlayOnline directory
- **Memory**: Minimal system resources required

## Installation & Setup

1. **Download**: Get the latest release from the repository
2. **Install**: Extract and run FFXIManager.exe
3. **Configure**: Set your PlayOnline directory path on first launch
4. **Setup**: Add your external applications and create your first profiles

## Advanced Features

### Smart Profile Detection

- **Auto-Detection**: Automatically detects currently active profile
- **Persistence Tracking**: Remembers last used profile across sessions
- **Conflict Resolution**: Handles orphaned profile references

### Application Monitoring

- Process discovery settings are fully configurable via Settings → Discovery Settings (Include/Exclude patterns with * wildcard).

- **Real-Time Status**: 3-second polling for application status changes
- **External Detection**: Detects when applications are closed externally
- **Multi-Instance Tracking**: Monitors multiple instances of the same application

### Clean Architecture

- **Modular Design**: Separate ViewModels for different concerns
- **Extensible**: Easy to add new features and capabilities
- **Maintainable**: Well-organized codebase following SOLID principles

## Troubleshooting

### Common Issues

1. **Directory Access**: Ensure proper permissions for PlayOnline directory
2. **Application Paths**: Verify executable paths in application configuration
3. **Profile Loading**: Check PlayOnline directory contains valid .bin files
4. **Application Status**: Applications may need configuration before first launch

### Support & Debugging

- **Status Messages**: Check status bar for detailed operation feedback
- **Debug Logging**: Application provides comprehensive logging for troubleshooting
- **Configuration Validation**: Built-in validation prevents common configuration errors

## Future Roadmap

The application is designed for extensibility and continuous improvement:

- **Profile Metadata**: Enhanced profile descriptions and organization
- **Import/Export**: Profile and configuration sharing capabilities
- **Integration APIs**: Deeper integration with Windower and other tools

---

**FFXI Manager** - Streamlining your Final Fantasy XI experience with modern tools and intelligent automation.