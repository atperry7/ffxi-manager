# ⚔️ FFXI Manager - The Ultimate Linkshell Leader's Arsenal

*"Because managing 18 mules shouldn't be harder than beating Absolute Virtue pre-nerf"*

Welcome, Adventurer! Are you tired of manually logging in your army of characters like some Level 1 noob? Does switching between your main, crafting mules, and storage characters feel like grinding merit points in 2004? **FFXI Manager** is here to turn you into the most efficient Taru overlord Vana'diel has ever seen!

<img width="1058" height="605" alt="ffxi-manager-track-characters" src="https://github.com/user-attachments/assets/cbe3b8f7-065f-4569-807d-9fed1af62787" />

## 🎯 What This Bad Boy Does

### 🚀 **Auto-Login System** *(The Crown Jewel)*
### 🏠 **Profile Management** *(login_w.bin Swapping Made Easy)*
### 🎮 **Controller Support** *(Now We're Talking)*
### 👑 **Character Hotkeys** *(Win+F1 to Win+F9, Baby!)*
### 📱 **Character Monitor Window** *(The Command Center)*
### 🛠️ **Application Management** *(Who has time to find those?)*

## 🎮 System Requirements

### Essential (The Bare Minimum)
- **Windows 10/11** (because let's be real, it's 2024)
- **.NET 9 Runtime** (the latest and greatest)
- **PlayOnline/FFXI Installation** (obviously)
- **100MB disk space** (less than a single Dynamis run's screenshots)

### Recommended (For the Full Experience)
- **Windower** (for the enhanced FFXI experience we all know and love)
- **Multiple FFXI accounts** (for maximum efficiency and/or addiction)
- **Controller** (Xbox or PlayStation - your choice, champion)
- **Multiple monitors** (because who plays FFXI on just one screen anymore?)

## 🚀 Quick Start Guide

### Installation (Easier than learning to play BRD)
1. **Download** the latest `FFXIManager-vX.X.X.zip` from [Releases](../../releases)
2. **Extract** to your preferred location (Desktop works fine)
3. **Run** `FFXIManager.exe` (no installation required - portable like a good old-school app)
4. **Configure** your PlayOnline directory (usually somewhere deep in Program Files)

### First-Time Setup
1. **Point to PlayOnline**: Browse to your PlayOnline directory
   - Usually: `C:\Program Files (x86)\PlayOnline\SquareEnix\PlayOnlineViewer\usr\all`
   - Look for the sacred `login_w.bin` file
2. **Create Your First Profile**: Save your current account setup as "Main Party" or "Mule Brigade"
3. **Test the Magic**: Try switching to another profile and back
4. **Setup Auto-Login**: Add your characters to the queue and watch the automation magic happen

## 🎯 Features Deep Dive

### 🤖 Auto-Login Queue System
The crown jewel of FFXI Manager - because manually logging in 6+ characters is content nobody asked for.

**How It Works:**
- Add characters to the queue with their profile settings
- Hit "Start" and watch the magic unfold
- The system uses advanced screen recognition to navigate PlayOnline
- Automatically handles POL Proxy launching, login screens, and character selection
- Integrates with Windower to launch your characters directly into the game

**Queue Management:**
- **Pause/Resume**: Need to take a break? Pause the queue anytime
- **Skip Items**: That one mule can wait - skip and keep going
- **Batch Operations**: Add multiple characters at once, modify queue order, queue state preserved between sessions

### 📊 Profile System (login_w.bin Management)
The backbone of multi-account management - handles the tedious file swapping so you don't have to.

**Profile Features:**
- **Unlimited Profiles**: Create as many setups as you need
- **Automatic Backups**: Your current setup is always preserved before switching
- **Descriptive Names**: "Crafting Mules", "Endgame Linkshell", "Storage Army" - name them whatever makes sense
- **Quick Switching**: Double-click or right-click context menu for instant swapping
- **Safety Checks**: Validates profiles before switching to prevent disasters

### 🎮 Controller & Hotkey Mastery
Turn your gamepad into the ultimate character-switching tool.

**Controller Support:**
- **Xbox Controllers**: Native XInput support for Xbox 360, Xbox One, Xbox Series controllers
- **PlayStation Controllers**: Full DirectInput support for DualShock 3, DualShock 4, DualSense
- **Button Mapping**: Assign any controller button to switch to specific characters
- **Trigger Support**: Even analog triggers work as digital buttons

**Hotkey Options:**
- **Individual Character Hotkeys**: Win+F1 through Win+F9 for specific characters
- **Character Cycle**: One button to cycle through all active characters
- **Controller + Keyboard**: Mix and match input methods however you want

### 🎛️ Character Monitor
Your mission control for keeping track of all active characters.

**Main Window Monitor:**
- Real-time character list with status indicators
- Quick-switch buttons for each character
- Refresh functionality to detect newly launched characters
- Integration with profile system

**Floating Monitor Window:**
- Always-on-top overlay for continuous monitoring
- Adjustable transparency (30-100%) to suit your preference
- Fully draggable and resizable
- Minimal resource usage - won't impact game performance

## ⚙️ Configuration & Advanced Settings

### Hotkey Customization
1. **Access Settings**: Click the ⚙️ button → Advanced Settings
2. **Enable Global Shortcuts**: Toggle the master switch
3. **Customize Individual Hotkeys**: Click "Edit" on any character slot
4. **Record New Combinations**: Use Ctrl, Alt, Shift, Win + any key
5. **Controller Mapping**: Assign controller buttons to any character slot

### Auto-Login Configuration
1. **Template Management**: Customize screen recognition templates for different resolutions
2. **Timing Settings**: Adjust delays between actions for optimal reliability
3. **Error Handling**: Configure retry attempts and failure recovery behavior
4. **POL Proxy**: Launch POL Proxy automatically if needed

## 🐛 Troubleshooting

### Common Issues & Solutions

**"Auto-Login isn't working"**
- Check that templates match your screen resolution
- Verify PlayOnline directory is correctly configured
- Try adjusting timing settings for slower systems
- Make sure POL Proxy is properly configured if using

**"Controller not detected"**
- Check Windows Gaming Services are running (Win11)
- Try unplugging and reconnecting the controller
- Check Windows Device Manager for driver issues
- Both wired and wireless controllers are supported

**"Hotkeys not working"**
- Verify global shortcuts are enabled in Advanced Settings
- Check for conflicts with other applications
- Make sure FFXI Manager is running as Administrator if needed
- Test individual hotkeys to isolate the issue

**"Profile switching failed"**
- Check file permissions on PlayOnline directory
- Ensure no FFXI instances are currently running
- Verify the profile backup completed successfully
- Try manually browsing to a working profile

### Getting Help
- **Status Messages**: Check the status bar for detailed error information
- **Log Files**: Application logs are saved for debugging complex issues
- **GitHub Issues**: Report bugs or request features via GitHub Issues
- **Documentation**: Check the Documentation folder for detailed guides

## 🛡️ Security & Safety

FFXI Manager only swaps configuration files. It:
- ✅ **Never modifies game files or memory**
- ✅ **Only swaps PlayOnline configurations**
- ✅ **Uses standard Windows APIs for input handling**
- ✅ **Operates entirely outside the game process**
- ✅ **Creates automatic backups before any changes**

*This tool is designed to enhance your multi-boxing experience within FFXI's Terms of Service.*

## 🤝 Contributing

Want to help make FFXI Manager even more awesome? We'd love your help!

- **Bug Reports**: Found something broken? Let us know via [GitHub Issues](../../issues)
- **Feature Requests**: Have an idea for improvement? Share it!
- **Code Contributions**: Check [CONTRIBUTING.md](./CONTRIBUTING.md) for development guidelines
- **Documentation**: Help improve guides and documentation
- **Testing**: Try new features and provide feedback

## 🏆 Credits & Thanks

- **FFXI Community**: For feedback and feature requests
- **Multi-boxing Pioneers**: Who showed us the way to efficiency
- **Beta Testers**: Who suffered through early versions so you don't have to

## 📄 License

This project is open source under the terms specified in the repository. Use responsibly and in accordance with FFXI's Terms of Service.

---

## 🌟 Final Words

**FFXI Manager** isn't just another tool - it's your ticket to playing FFXI the way it was meant to be played in 2025: efficiently, comfortably, and without repetitive strain injury from constant character switching.

Whether you're managing a small family of characters or commanding an empire that would make even Shantotto jealous, FFXI Manager has your back.

*Now get out there and show Vana'diel what peak efficiency looks like!*

---

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/L4L21JMRTW)

**Made with ❤️ for the FFXI community**
*"Because the real endgame is optimizing your workflow"*