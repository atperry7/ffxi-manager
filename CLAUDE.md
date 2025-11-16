# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FFXI Manager** is a WPF desktop application (.NET 9) for managing multiple Final Fantasy XI accounts. It automates character login sequences, manages PlayOnline profile switching, provides global hotkey support for character window switching, and includes controller integration for seamless multi-boxing.

## Quick Reference

**Essential Paths:**
- Service registration: `Infrastructure/DependencyInjection.cs`
- Workflow definition: `workflows/playonline-standard.json`
- Template images: `workflows/templates/`
- User settings: `%APPDATA%/FFXIManager/settings.json`
- Application logs: `%APPDATA%/FFXIManager/logs/`

**Common Commands:**
```bash
dotnet build FFXIManager.sln                    # Build solution
dotnet test Testing/FFXIManager.Tests.csproj    # Run tests
```

# RULES TO FOLLOW
- ALWAYS follow MVVM + DI + SOLID architecture principles
- DO NOT CREATE backwards compatibility unless requested by the user
- PROACTIVELY refactor when changes are needed and utilize powershell commands for mass updates when needed
- PREFER data-driven approaches (e.g., JSON workflows) over hardcoded logic
- ALWAYS use IUiDispatcher for UI updates from background threads
- ALWAYS use ILoggingService for logging instead of direct Serilog calls
- ALWAYS register services in Infrastructure/DependencyInjection.cs
- Provide short and concise summaries when completing tasks

## Build Commands

### Building
```bash
# Full solution build
dotnet build FFXIManager.sln

# Build main application only
dotnet build FFXIManager.csproj

# Build for release
dotnet build FFXIManager.sln -c Release
```

### Testing
User performs live testing with actual FFXI accounts. No automated tests exist for auto-login due to complexity.

### MSBuild (Windows)
If using MSBuild on WSL/Linux, use the Windows MSBuild path:
```bash
"/mnt/c/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/amd64/MSBuild.exe" FFXIManager.sln -verbosity:minimal
```

## Architecture Overview

### Architectural Pattern: MVVM + DI + SOLID

The application follows **Model-View-ViewModel (MVVM)** architecture with heavy use of **Dependency Injection** (Microsoft.Extensions.DependencyInjection) and **SOLID principles**. All services are registered in `Infrastructure/DependencyInjection.cs`.

**Key Directories:**
- **Models/** - Domain models and data structures
- **ViewModels/** - MVVM ViewModels, all inherit from `ViewModelBase`
- **Views/** - WPF XAML views
- **Services/** - Business logic services (registered as singletons/transients)
- **Infrastructure/** - Core infrastructure (DI, process management, UI dispatching)

### Critical Architecture: Auto-Login System

The auto-login system is the crown jewel of this application. It went through a major refactoring to support **data-driven workflows** instead of hardcoded logic.

#### Workflow System Architecture

**Key Concept**: Login flows are now defined in JSON workflow files (`workflows/` directory) rather than being hardcoded. Users can customize workflows without modifying code.

**Core Components:**

1. **WorkflowDefinition** (`Models/AutoLogin/WorkflowDefinition.cs`)
   - JSON-serializable workflow with steps, conditions, navigation
   - Supports conditional execution (e.g., "Account.IsOTPEnabled")
   - Validates step definitions and dependencies

2. **WorkflowTaskBuilder** (`Services/AutoLogin/WorkflowTaskBuilder.cs`)
   - Converts workflow definitions into executable `AutoLoginSubtask` sequences
   - Evaluates conditions based on account properties
   - Bridges workflow JSON � executable task infrastructure

3. **DynamicWorkflowHandler** (`Services/AutoLogin/DynamicWorkflowHandler.cs`)
   - Executes workflow steps dynamically using template detection + navigation
   - Resolution-independent (uses hybrid navigation)
   - Fallback handler when no specialized handler claims a subtask

4. **WorkflowService** (`Services/AutoLogin/WorkflowService.cs`)
   - Loads/saves workflows from filesystem
   - Manages default workflows vs user-customized workflows
   - Handles workflow CRUD operations

**Workflow Execution Flow:**
```
1. User adds character to queue � AutoLoginQueueService
2. Queue starts � QueueExecutionOrchestrator orchestrates execution
3. For each queue item � AutoLoginTaskExecutor builds tasks
4. Tasks are built via WorkflowTaskBuilder (converts workflow JSON � subtasks)
5. Subtasks executed by DynamicWorkflowHandler (100% workflow-driven)
6. Each step: Detect screen (template matching) � Navigate (keyboard/click)
7. Progress tracked via AutoLoginTask and AutoLoginSubtask models
8. UI reflects current task/subtask state in real-time
```

#### Auto-Login Service Composition

The auto-login queue system follows **Single Responsibility Principle** with focused services:

- **AutoLoginQueueService** - Coordinating facade, composes all queue services
- **QueueCollectionManager** - Manages ObservableCollection of queue items
- **QueueStateMachine** - Manages execution state (Idle/Running/Paused/etc.)
- **QueuePersistenceService** - Saves/loads queue state to disk
- **QueueStatisticsService** - Calculates queue statistics (completion rates, timing)
- **QueueExecutionOrchestrator** - Orchestrates queue execution loop
- **AutoLoginTaskExecutor** - Executes individual login tasks with handlers

#### Handler System

Login handlers inherit from `BaseLoginTaskHandler` and implement `ILoginTaskHandler`:

**Active Handlers:**
- `DynamicWorkflowHandler` - **ONLY handler** - Executes ALL workflow-defined steps:
  - UI navigation (PlayOnline auth, FFXI character selection)
  - Application launches (POL Proxy, Windower, any external app)
  - Template detection + navigation execution
  - Two-phase launch verification: process detection + UI readiness confirmation

**Handler Resolution:**
- `LoginTaskHandlerResolver` - Routes all subtasks to DynamicWorkflowHandler
- No specialized handlers - 100% workflow-driven architecture
- Handler registered as singleton in DI container

**Generic Application Launch Pattern:**
- WorkflowStepDefinition with `StepType = "LaunchApplication"`
- References external apps from ExternalApplicationData settings
- Process detection via UnifiedMonitoringService (WMI watchers)
- UI readiness confirmed via template matching
- Optional post-launch navigation sequences

### Screen Detection & Template Matching

The application uses **OpenCV (OpenCvSharp4)** for template matching to detect UI screens:

**Services:**
- **IScreenshotCaptureService** - Captures window screenshots
- **ITemplateMatchingService** - Performs template matching (OpenCV)
- **ITemplateManagementService** - Loads/manages template metadata
- **IUIAutomationService** - Executes UI automation (keyboard/mouse)

**Templates** (`workflows/templates/` directory):
- PNG images are the visual templates for template matching (OpenCV)
- **No JSON metadata files** - all metadata (confidenceThreshold, tolerance) is defined in workflow step definitions
- Flat directory structure (not organized by application subdirectories)
- Templates are **detection-only** - all navigation is defined in workflow JSON files
- Example: `member_selection_screen.png`, `login_information_screen.png`

**Action Executor Pattern:**
- **Unified action executor system** - All workflow actions route through `WorkflowActionExecutorFactory`
- **Strategy Pattern** - Each action type has dedicated executor (InputPassword, InputOTP, MemberSlot, CharacterSlot, Click, Keyboard, Launch, Wait)
- **Resolution-independent** - Click actions use template-relative coordinates (0.0-1.0)
- **Data-driven** - Navigation sequences defined in workflow JSON files, executed by action executors

### Profile Management

**ProfileService** (`Services/ProfileService.cs`):
- Swaps PlayOnline `login_w.bin` files to switch accounts
- Maintains backup of original profile before swapping
- Profiles stored in `%APPDATA%/FFXIManager/profiles/`

### Hotkey System

**Global Hotkey Architecture:**
- **GlobalHotkeyManager** - Registers Windows low-level keyboard hooks
- **HotkeyActivationService** - Ultra-fast character window activation
- **HotkeyMappingService** - Maps hotkey IDs to characters
- **ControllerInputService** - Integrates Xbox/PlayStation controllers via DirectInput

**Hotkey Flow:**
```
1. User presses Win+F1 � GlobalHotkeyManager detects
2. HotkeyPressed event fired with hotkey ID
3. App.xaml.cs handler calls HotkeyActivationService.ActivateCharacterByHotkeyAsync()
4. Service uses HotkeyMappingService to resolve character
5. UnifiedMonitoringService activates character window
```

### Character Monitoring

**UnifiedMonitoringService** - Centralized service for monitoring FFXI windows:
- Tracks all active FFXI character windows
- Provides fast window activation (used by hotkeys)
- Polls process list for character discovery

**PlayOnlineMonitorService** - Monitors PlayOnline/FFXI processes:
- Detects new launches
- Tracks character names and window handles
- Provides character list for UI

## Important Development Notes

### Workflow-First Architecture

**CRITICAL PRINCIPLE**: Workflows are the single source of truth for all auto-login configuration including navigation, timing, and template metadata.

**Template PNG Files** (`workflows/templates/` directory):
- Pure PNG images for OpenCV template matching - no metadata files
- Used exclusively for screen detection
- All template configuration (confidenceThreshold, tolerance) is defined in workflow step definitions

**Workflow Files** (`workflows/` directory):
- Define complete login sequences with steps, navigation, timing, retries, and template metadata
- Each step includes: TemplatePath (PNG filename), ConfidenceThreshold, Tolerance, Navigation sequence
- All navigation is **hybrid** (keyboard-first with click fallback)
- Support conditional execution (e.g., "Account.IsOTPEnabled")
- Example: `workflows/defaults/playonline-standard.json`

### Adding New Auto-Login Functionality

**Workflow-first approach** (99% of cases):
1. Edit or create workflow JSON in `workflows/` directory
2. Add new step with TemplatePath and Navigation sequence
3. Test with real login flow
4. **DO NOT create new handlers or services** - use DynamicWorkflowHandler

**Specialized handler** (rare cases only):
- Only needed for process launch logic (e.g., launching applications)
- All UI navigation should use workflow system, not specialized handlers

### Service Registration Pattern

All services registered in `Infrastructure/DependencyInjection.cs`:
- Use `AddSingleton<IInterface, Implementation>()` for stateful services
- Use `AddTransient<T>()` for ViewModels and dialogs
- ViewModels should inject services via constructor

### Logging & Debugging

**Logging Infrastructure:**
- Uses Serilog with JSON structured logging
- Configured via `appsettings.json`
- ALWAYS use `ILoggingService` interface, never direct Serilog calls
- Logs written to: `%APPDATA%/FFXIManager/logs/log-YYYYMMDD.json`

**Logging Patterns:**
```csharp
// Inject the service
private readonly ILoggingService _logger;

public MyService(ILoggingService logger)
{
    _logger = logger;
}

// Log with structured data
_ = _logger.LogInformationAsync("Processing queue item", new { CharacterId = item.Id, ProfileName = profile.Name });
_ = _logger.LogWarningAsync("Template match confidence low", new { Confidence = 0.65, Threshold = 0.75 });
_ = _logger.LogErrorAsync("Failed to launch application", ex, new { AppName = appData.Name });
```

**Debugging Auto-Login:**
1. Enable verbose logging in `appsettings.json` (set MinimumLevel to "Debug")
2. Check workflow execution in real-time via `AutoLoginQueueViewModel` progress updates
3. Review template matching results in log files (includes confidence scores)
4. Use Workflow Editor's dry-run feature to test individual steps

## Common Development Tasks

### Adding a New Service

1. Create interface in `Services/` (e.g., `IMyService.cs`)
2. Create implementation (e.g., `MyService.cs`)
3. Register in `Infrastructure/DependencyInjection.cs`:
   ```csharp
   services.AddSingleton<IMyService, MyService>();
   ```
4. Inject via constructor in consumers:
   ```csharp
   public MyViewModel(IMyService myService)
   {
       _myService = myService;
   }
   ```

## Configuration Files

- **appsettings.json** - Serilog configuration, app settings
- **settings.json** - User settings (`%APPDATA%/FFXIManager/settings.json`)
- **queue_state.json** - Auto-login queue persistence (`%APPDATA%/FFXIManager/queue_state.json`)
- **workflows/playonline-standard.json** - Default workflow definition
- **workflows/templates/** - Template PNG files for screen detection

## Security & Safety

- **Memory Safety**: Application does NOT modify game memory or inject code
- **File Operations**: Only swaps PlayOnline configuration files (`login_w.bin`)
- **Input Handling**: Uses Windows API (SendInput) for keyboard/mouse automation
- **Credentials**: Can optionally use Windows Credential Manager for secure storage

## Project History & Architecture Evolution

The application underwent a major architectural transformation to achieve a **100% workflow-driven, data-first architecture**:

**Key Achievements:**
- **Workflow-Driven**: All login flows defined in JSON (`workflows/playonline-standard.json`) - no hardcoded logic
- **Single Handler**: `DynamicWorkflowHandler` executes ALL steps (UI navigation + application launches)
- **Task-Based Progress**: Progress tracking flows through `AutoLoginTask` → `AutoLoginSubtask` → UI (no legacy enum-based steps)
- **Template Separation**: Templates are pure PNG files for detection - all metadata/navigation lives in workflow JSON
- **Resolution Independence**: Hybrid navigation (keyboard-first, click fallback) with relative coordinates
- **SOLID Refactoring**: Monolithic services extracted into focused, single-responsibility components

**Historical Context:**
The project evolved from hardcoded specialized handlers (`PlayOnlineAuthHandler`, `FFXIGameHandler`, `POLProxyLaunchHandler`) with absolute coordinates and enum-based step tracking to the current data-driven architecture where a single workflow JSON file defines the entire login flow
