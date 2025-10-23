# 🧪 FFXI Manager v2.0.0-beta Release Notes

**Release Date**: October 22, 2025
**Branch**: feature/data-driven-auto-login
**Status**: Beta - Public Testing

---

## 🎉 Welcome to FFXI Manager 2.0!

This is a **major architectural release** that fundamentally transforms how FFXI Manager handles auto-login automation. We've rebuilt the system from the ground up with a **workflow-first, data-driven architecture** that puts customization power directly in your hands—no coding required.

> **⚠️ BETA STATUS**: This is a beta release intended for public testing. While we've thoroughly tested the new architecture, we recommend backing up your existing settings before upgrading. Please report any issues on GitHub!

---

## 🚀 What's New

### 🔧 Workflow Editor - The Crown Jewel

The biggest addition in 2.0 is the **Workflow Editor**, a powerful visual tool that lets you customize every aspect of the auto-login process without touching a single line of code.

**What You Can Do:**
- **Visual Customization**: Point, click, and configure—no JSON editing required
- **Template Management**: Capture and assign screen templates with precision
- **Step-by-Step Control**: Add, remove, reorder, and fine-tune login steps
- **Multi-Resolution Support**: Set click positions as percentages for resolution independence
- **Confidence Tuning**: Adjust template matching sensitivity for your screen setup
- **Clone & Customize**: Start with defaults and adapt to your exact needs

**Common Use Cases:**
- Skip OTP entry for accounts without two-factor authentication
- Adjust click positions for different screen resolutions or UI scaling
- Fine-tune timing delays for slower systems or network conditions
- Configure custom application launches (POL Proxy, Windower, Ashita)
- Create workflow variants for different login scenarios

📖 **[Complete Workflow Editor Guide](workflows/README.md)** - Detailed documentation with examples

---

### 🏗️ Complete Architectural Refactor

**Before 2.0**: Login sequences were hardcoded in specialized handler classes. Customization required code changes and recompilation.

**After 2.0**: Login sequences are defined in **JSON workflow files**. Users can customize everything through the Workflow Editor.

**What Changed:**
- **Removed ~2,000 lines of hardcoded logic** (PlayOnlineAuthHandler, FFXIGameHandler, etc.)
- **Single handler architecture**: DynamicWorkflowHandler executes ALL workflow-defined steps
- **100% data-driven**: Every login step, template, navigation sequence, and timing is configured in JSON
- **Resolution-independent**: Works across different screen resolutions and aspect ratios
- **Template-first detection**: OpenCV-powered template matching for reliable screen state detection

**Technical Benefits:**
- **Maintainability**: Changes to login flows don't require code modifications
- **Flexibility**: Users can create unlimited workflow variations
- **Testability**: Workflows can be validated and tested independently
- **Shareability**: Export/import workflows to share with the community
- **Debuggability**: Clear separation between detection logic and navigation execution

---

### 📊 Enhanced Auto-Login Queue System

**New Features:**
- **All-Time Statistics**: Track completion rates, success metrics, and timing data across all sessions
- **Step-Level Performance Tracking**: See which steps take longest and get optimization recommendations
- **Immediate Login**: Priority feature for urgent character switching
- **Improved State Persistence**: Resume queue exactly where you left off
- **Better Error Handling**: Detailed error reporting with actionable retry logic

**UI Improvements:**
- Real-time progress indicators with task/subtask breakdowns
- Color-coded progress bars for visual feedback
- Status messages showing exactly what's happening at each step
- Statistics panel showing success rates and average timing

---

### 🎯 Resolution-Independent Navigation

**Hybrid Navigation System:**
- **Keyboard-first**: Uses keyboard shortcuts when available (fast, reliable)
- **Click fallback**: Falls back to template-relative click positions when needed
- **Percentage-based coordinates**: Click positions use 0.0-1.0 coordinates (resolution-independent)
- **Multi-point click support**: Define multiple click positions for slot navigation (1-4 member slots, 1-16 character slots)

**Template Picker:**
- Visual template preview with click position markers
- 16-point click support for character slot navigation
- Real-time coordinate display
- Easy position adjustment through UI

---

### ⚡ Other Notable Features

- **Canonical Account Model**: Unified account representation across the application
- **Character Ordering Service**: Automatic FFXI instance detection and ordering
- **Improved Logging**: Structured logging with diagnostic context
- **Workflow Validation**: Built-in validation with error detection and warnings
- **Dry-Run Testing**: Test workflows without actually executing login
- **Version Control**: Semantic versioning for workflows with change tracking

---

## 💥 Breaking Changes

### Migration Required

**1. Workflow Assignment**
- Existing profiles/characters will need workflow assignment
- Default workflow will be auto-assigned on first run
- Users with custom setups should review and test workflows

**2. Template System Changes**
- Old template JSON metadata files are no longer used
- All template configuration (confidence, tolerance) moved to workflow definitions
- Template PNG files remain unchanged and are backward compatible

**3. Removed Features**
- **Template Navigation Tuner**: Replaced by Workflow Editor (more powerful and user-friendly)
- **LoginTaskStep Enum**: Removed in favor of task-based progress tracking
- **Specialized Handlers**: Consolidated into single DynamicWorkflowHandler

### Settings Migration

Most settings will migrate automatically, but you should:
1. **Review your accounts**: Ensure each account has a workflow assigned
2. **Test auto-login**: Run a test login for each account to verify workflow compatibility
3. **Customize if needed**: Use Workflow Editor to adjust workflows for your setup

---

## 🛠️ Technical Improvements

### Architecture
- **SOLID Principles**: Complete refactor following Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, and Dependency Inversion
- **Strategy Pattern**: Action executors use strategy pattern for clean extensibility
- **Dependency Injection**: All services registered in DI container
- **Event-Driven**: Improved event handling and async/await patterns

### Code Quality
- **Removed obsolete warnings**: Cleaned up 60+ obsolete warnings across 17 files
- **Removed unused code**: Deleted ~174 lines of legacy ScreenState.cs and other dead code
- **Improved test coverage**: Enhanced validation and testing infrastructure
- **Better error messages**: More descriptive error messages with actionable guidance

### Performance
- **Optimized fullscreen detection**: Faster window state detection
- **Better template matching**: OpenCV optimization for faster screen detection
- **Reduced memory footprint**: Cleanup of resource leaks and collection management
- **Async improvements**: Better async/await usage throughout the codebase

---

## 📋 Known Issues & Limitations

### Beta Limitations

1. **First-Time Setup**: Initial workflow configuration may require trial-and-error for different screen resolutions
2. **Template Matching Sensitivity**: Some users may need to adjust confidence thresholds in Workflow Editor
3. **Migration Path**: No automated migration from 1.x custom configurations (manual workflow creation required)

### Workarounds

- **Auto-Login Fails**: Use Workflow Editor to adjust click positions and timing delays
- **Template Not Detected**: Lower confidence threshold or adjust tolerance in step properties
- **Wrong Button Clicked**: Click template preview in Workflow Editor to set correct position

### Reporting Issues

Please report bugs and issues on [GitHub Issues](https://github.com/your-repo/issues) with:
- FFXI Manager version (2.0.0-beta)
- Windows version and screen resolution
- Log files from `%APPDATA%\FFXIManager\logs\`
- Steps to reproduce the issue

---

## 🎯 What's Next - Roadmap to 2.0 Stable

### Before Stable Release

**Testing & Feedback:**
- [ ] Community testing across different screen resolutions
- [ ] Validation of workflow system with various FFXI setups (vanilla, Windower, Ashita)
- [ ] Performance profiling and optimization
- [ ] Documentation improvements based on user feedback

**Planned Improvements:**
- [ ] Workflow template library (community-contributed workflows)
- [ ] Enhanced error recovery and retry strategies
- [ ] Improved workflow validation and testing tools
- [ ] Migration assistant for 1.x users

**Target Timeline:**
- **Beta Phase**: 2-4 weeks of community testing
- **Release Candidate**: After major issues resolved
- **Stable 2.0**: When no critical issues remain

---

## 📚 Documentation

### New Documentation
- **[Workflow Editor Guide](workflows/README.md)**: Complete guide to creating and customizing workflows
- **[Workflow File Format](workflows/README.md#workflow-file-format)**: Technical reference for workflow JSON structure
- **[CLAUDE.md Updates](CLAUDE.md)**: Development guidelines updated for 2.0 architecture

### Updated Documentation
- **[README.md](README.md)**: Refreshed with 2.0 features and workflow system
- **Project Structure**: Updated architecture overview in CLAUDE.md

---

## 🙏 Acknowledgments

Special thanks to:
- **Beta Testers**: Who will help us find and fix issues before stable release
- **FFXI Community**: For feedback and feature requests that shaped 2.0
- **Multi-Boxing Pioneers**: Who inspired the workflow-first architecture

---

## 🏆 Upgrade Instructions

### From 1.x to 2.0-beta

1. **Backup Current Setup**:
   - Export your current settings
   - Save copies of profile configurations
   - Note your character/account setup

2. **Install 2.0-beta**:
   - Download `FFXIManager-v2.0.0-beta-windows.zip`
   - Extract to a new folder (don't overwrite 1.x installation)
   - Run `FFXIManager.exe`

3. **Configure Workflows**:
   - Open Settings → Workflow Editor
   - Review the default "PlayOnline Standard" workflow
   - Clone and customize for your setup if needed
   - Assign workflows to your accounts

4. **Test Login**:
   - Add a character to the auto-login queue
   - Run a test login to verify workflow compatibility
   - Adjust workflow settings in Workflow Editor if needed

5. **Report Issues**:
   - If you encounter problems, report them on GitHub
   - Include logs from `%APPDATA%\FFXIManager\logs\`
   - Describe your setup (resolution, FFXI version, etc.)

---

## 📄 Full Changelog (Since v1.3.1)

### Major Features
- Add Workflow Editor for visual workflow customization
- Implement 100% workflow-driven auto-login architecture
- Add resolution-independent hybrid navigation system
- Implement all-time statistics tracking for auto-login queue
- Add step-level performance tracking and recommendations
- Add immediate login feature for priority character switching
- Add multi-point click support for slot navigation (16-point template picker)

### Architecture Changes
- Remove PlayOnlineAuthHandler and FFXIGameHandler (~1,800 lines)
- Remove 12 specialized services (consolidated into workflow system)
- Remove POLProxyLaunchHandler and WindowerLaunchHandler
- Remove LoginTaskStep enum (replaced with task-based tracking)
- Remove Template Navigation Tuner (replaced by Workflow Editor)
- Implement Strategy Pattern for action executors
- Migrate template metadata to workflow definitions
- Refactor to SOLID principles throughout codebase

### Improvements
- Optimize fullscreen detection and window state tracking
- Enhance logging with structured diagnostic context
- Improve error handling and retry strategies
- Refactor PlayOnlineMonitorService event handling
- Add canonical account model for consistency
- Integrate CharacterOrderingService for instance detection
- Improve UI feedback and layout throughout application
- Enhance collection management and resource cleanup

### Bug Fixes
- Fix workflow confidence threshold inheritance
- Fix workflow template path resolution for flat directory structure
- Fix XAML bindings for task-based progress tracking
- Fix template navigation fallback logic
- Fix click action coordinate calculation for resolution independence

### Code Quality
- Remove 60+ obsolete warnings across 17 files
- Delete ~174 lines of unused legacy code (ScreenState.cs)
- Clean up unused using statements throughout codebase
- Improve code formatting and consistency
- Enhance test coverage for workflow system

---

## 🔗 Links

- **Download**: [Releases Page](../../releases/tag/v2.0.0-beta)
- **Documentation**: [README.md](README.md)
- **Workflow Guide**: [workflows/README.md](workflows/README.md)
- **Report Issues**: [GitHub Issues](../../issues)
- **Source Code**: [GitHub Repository](../../)

---

**Made with ❤️ for the FFXI community**

*"The real endgame is optimizing your workflow"*
