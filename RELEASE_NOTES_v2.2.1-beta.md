# FFXI Manager v2.2.1-beta

## 🎯 Highlights

### Always-On-Top Auto-Login Progress Window
A compact floating window that provides real-time visibility of the auto-login queue without requiring users to switch back to the main application.

**Key Features:**
- **Always visible** - Stays on top of all windows, positioned in top-right corner
- **Real-time progress** - Shows current account, step, progress bar, and queue status
- **Emergency controls** - Includes Stop button for immediate queue halt
- **Custom title bar** - Draggable window with modern design matching app theme
- **Auto lifecycle** - Opens when queue starts, closes when complete
- **Manual control** - Close button (×) allows dismissal at any time

**Why This Matters:**
When the auto-login process takes control of mouse input, finding and accessing the main window becomes challenging. This progress window provides quick, always-accessible controls without hunting for the main application window.

---

## 🚀 New Features

### Auto-Login Progress Window
- Added compact floating window (350×210px) for queue monitoring
- Custom draggable title bar with pin indicator and close button
- Real-time display: account name, profile, current step, progress percentage
- Emergency Stop button for immediate queue termination
- Transparent background with rounded corners for modern appearance
- Window automatically positions in top-right corner of primary screen

---

## 🏗️ Technical Changes

### Architecture
- **AutoLoginProgressViewModel** - Wraps AutoLoginQueueViewModel for progress data binding
- **AutoLoginProgressWindow** - Custom WPF window with title bar and modern styling
- **Lifecycle Management** - Integrated into AutoLoginQueueViewModel event system
- **Service Registration** - Added transient services to DI container

### Files Modified
- `ViewModels/AutoLoginQueueViewModel.cs` - Added progress window lifecycle management
- `Infrastructure/DependencyInjection.cs` - Registered new window and ViewModel

### Files Added
- `ViewModels/AutoLoginProgressViewModel.cs` - Progress window ViewModel
- `Views/AutoLoginProgressWindow.xaml` - Progress window UI definition
- `Views/AutoLoginProgressWindow.xaml.cs` - Progress window code-behind

---

## 📦 Installation

### Requirements
- Windows 10/11
- .NET 9.0 Runtime
- Final Fantasy XI installed

### Download
Download `FFXIManager-v2.2.1-beta.zip` from the release assets below.

### First-Time Setup
1. Extract the ZIP file to your preferred location
2. Run `FFXIManager.exe`
3. Configure your PlayOnline profiles and accounts
4. Set up auto-login workflows in the Workflow Editor

### Upgrade from Previous Version
Simply extract and run - your settings, profiles, and workflows are preserved in `%APPDATA%/FFXIManager/`.

---

## 🐛 Known Issues

None specific to this release. See [GitHub Issues](https://github.com/your-repo/FFXIManager/issues) for ongoing tracked issues.

---

## 🔄 What's Next?

This is a beta pre-release for testing the new progress window feature. Feedback is welcome!

**Upcoming:**
- Additional workflow customization options
- Enhanced error recovery and diagnostics
- Performance optimizations

---

## 🙏 Acknowledgments

This feature was developed in response to user feedback about the difficulty of stopping auto-login when the process is actively controlling mouse input. Thank you to our community for your valuable input!

---

**Full Changelog**: [v2.2.0-beta...v2.2.1-beta](https://github.com/your-repo/FFXIManager/compare/v2.2.0-beta...v2.2.1-beta)
