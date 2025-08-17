# FFXI Manager - Profile & Application Management Suite

FFXI Manager is a Windows application that simplifies managing multiple Final Fantasy XI accounts and launching related applications. Switch between different account configurations instantly and manage your FFXI tools from a single interface.

<img width="1058" height="605" alt="ffxi-manager-track-characters" src="https://github.com/user-attachments/assets/cbe3b8f7-065f-4569-807d-9fed1af62787" />

## Features

- **Profile Management**: Create unlimited account profile configurations
- **Instant Switching**: Swap between different account sets with one click
- **Global Hotkeys**: Switch between FFXI character windows using customizable keyboard shortcuts
- **Character Monitor**: Floating window to track and switch between active characters
- **Application Launcher**: Launch and monitor FFXI tools (Windower, etc.)
- **Real-time Monitoring**: Track running applications and character instances
- **Automatic Backups**: Protect your configurations with automatic backup creation
- **Clean Interface**: Modern WPF interface with dark/light theme support

## System Requirements

### Essential Requirements
- Windows 10 or Windows 11
- .NET 9 Runtime
- PlayOnline/FFXI installation
- Minimum 100MB free disk space

### Optional (Recommended)
- Windower for enhanced FFXI experience
- Text editor for configuration tweaking (VSCode, Notepad++, etc.)
- Multiple FFXI accounts for multi-boxing

## Installation

1. **Download** the latest FFXIManager.exe from the repository releases
2. **Extract** the application to your preferred location
3. **Run** FFXIManager.exe to launch the application
4. **Configure** your PlayOnline directory (see Configuration section)

## Configuration

### Initial Setup

1. **Set PlayOnline Directory**:
   - Click the "Browse" button for the PlayOnline Directory
   - Navigate to your PlayOnline installation
   - Typical location: `C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all`
   - Verify the directory contains `login_w.bin`

2. **Verify Installation**:
   - The status bar should show successful directory detection
   - Existing account configurations will be automatically detected

### Hotkey Configuration

1. **Access Settings**:
   - Click the Settings (⚙️) button in the header
   - Navigate to "Advanced Settings"

2. **Configure Hotkeys**:
   - Enable "Enable global keyboard shortcuts"
   - Each character slot (1-9) has a configurable hotkey
   - Default shortcuts: Win+F1, Win+F2, Win+F3, etc.
   - Click "Edit" to customize individual hotkeys
   - Toggle "Enabled" checkbox to activate/deactivate specific shortcuts

3. **Hotkey Usage**:
   - Hotkeys work globally (even when other applications have focus)
   - Press the configured key combination to switch to that character slot
   - Only works when FFXI characters are running and detected

## Usage

### Profile Management

#### Creating a New Profile
1. Configure your accounts in PlayOnline as desired
2. Click "Add Profile" button in FFXI Manager
3. Enter a descriptive name (e.g., "Main Party", "Crafting Mules", "Storage Characters")
4. The current account configuration will be saved as a new profile

#### Switching Between Profiles
1. Select the desired profile from the list
2. Double-click the profile or right-click and select "Swap to Profile"
3. The application will backup your current configuration and load the selected profile
4. Status messages will confirm the operation

#### Managing Profiles
- **Rename**: Right-click a profile and select "Rename"
- **Delete**: Right-click a profile and select "Delete" (confirmation required)
- **Backup**: The current active configuration is automatically backed up before each swap

### Character Monitoring

#### Main Window Character List
- View all running FFXI characters in the Character Monitor section
- See character status (Active/Inactive/Not Responding)
- Click the target (🎯) button to switch to a specific character
- Use the refresh (🔄) button to update the character list

#### Character Monitor Window
1. **Opening the Monitor**:
   - Click the "📱" button in the Character Monitor section
   - A compact floating window will open

2. **Window Features**:
   - **Always on Top**: Pin the window above other applications
   - **Draggable**: Click and drag the title bar to move
   - **Resizable**: Adjust window size as needed
   - **Opacity Control**: Use the slider at the bottom to adjust transparency (30%-100%)
   - **Character Switching**: Click "Switch" button to activate each character

3. **Window Controls**:
   - **📌**: Toggle always-on-top mode
   - **🔄**: Refresh character list
   - **—**: Minimize window
   - **✕**: Close window
   - **👁 Slider**: Adjust window opacity

### Global Hotkeys

#### Default Shortcuts
- **Win+F1**: Switch to character slot 1
- **Win+F2**: Switch to character slot 2
- **Win+F3**: Switch to character slot 3
- **Win+F4**: Switch to character slot 4
- **Win+F5**: Switch to character slot 5
- **Win+F6**: Switch to character slot 6
- **Win+F7**: Switch to character slot 7
- **Win+F8**: Switch to character slot 8
- **Win+F9**: Switch to character slot 9

#### Customizing Hotkeys
1. Open Advanced Settings
2. Select a character slot and click "Edit"
3. Record a new key combination using Ctrl, Alt, Shift, or Win modifiers
4. Avoid conflicts with FFXI's built-in shortcuts
5. Save changes and test the new hotkey

### Application Management

#### Adding Applications
1. Click the "+" button in the Application Management panel
2. Configure your application:
   - **Name**: Display name (e.g., "Windower")
   - **Path**: Browse to the executable (e.g., windower.exe)
   - **Arguments**: Command line arguments (optional)
3. Save the configuration

#### Launching Applications
1. Select an application from the list
2. Click the "▶" (Play) button to launch
3. Monitor running status in the application list
4. Use "Stop" button to terminate applications if needed

### Monitoring

The application provides real-time monitoring of:
- Running external applications
- Active FFXI character instances
- Current active profile
- System status and operations
- Character window focus and responsiveness

## Tips and Best Practices

- **Use descriptive profile names** - Avoid generic names like "Test123"
- **Monitor the status bar** - It provides real-time feedback for all operations
- **Create a backup profile** - Save your current setup before experimenting
- **Launch timing** - Switch profiles before launching Windower or other tools
- **Regular backups** - The application creates automatic backups, but consider manual backups for important configurations
- **Hotkey conflicts** - Use Win key combinations to avoid conflicts with FFXI macros
- **Character monitor placement** - Position the floating window where it won't interfere with gameplay
- **Opacity settings** - Adjust transparency so you can see through the monitor window when needed

## Troubleshooting

If you encounter issues:

1. **Check status messages** - Error details appear in the status bar
2. **Verify paths** - Ensure PlayOnline directory is correctly configured
3. **Review active profile** - Check which profile is currently active
4. **Restart application** - Close and restart FFXI Manager if needed
5. **Check file permissions** - Ensure the application has write access to the PlayOnline directory
6. **Hotkey issues** - Verify hotkeys are enabled and not conflicting with other applications
7. **Character detection** - Ensure FFXI is running and characters are logged in for monitoring to work
For additional help, consult the Documentation folder or open an issue in the repository.


## Contributing

We welcome contributions to improve FFXI Manager! Please feel free to:
- Report bugs and request features via GitHub Issues
- Submit pull requests for bug fixes and enhancements
- Share feedback and suggestions for improvements
- Help improve documentation and guides

## License

This project is open source. See the repository for license details.

---

**FFXI Manager** - Command your army like a true Linkshell leader, because /logout and /login is NOT endgame content.

*Made with ❤️ for the FFXI community*

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/L4L21JMRTW)

