# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**FFXI Manager** is a WPF desktop application (.NET 9) for managing multiple Final Fantasy XI accounts. It automates character login sequences, manages PlayOnline profile switching, provides global hotkey support for character window switching, and includes controller integration for seamless multi-boxing.

# RULES TO FOLLOW
- ALWAYS follow MVVM + DI + SOLID architecture principles.
- DO NOT CREATE backwards compatibility unless request by the user. 
- PROACTIVELY refactor when changes are needed and utilize powershell commands for mass updates when needed.
- PREFER data-driven approaches (e.g., JSON workflows) over hardcoded logic.
- ALWAYS use async/await for I/O operations and logging.
- ALWAYS use IUiDispatcher for UI updates from background threads.
- ALWAYS use ILoggingService for logging instead of direct Serilog calls.
- ALWAYS register services in Infrastructure/DependencyInjection.cs.
- Provide short and concise summaries when completing tasks.

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

**Templates** (`workflows/defaults/templates/` directory):
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

**Template PNG Files** (`workflows/defaults/templates/` directory):
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

Logs are written to:
- `%APPDATA%/FFXIManager/logs/log-YYYYMMDD.json` (JSON format)
- Console output during development

### Service Registration Pattern

All services registered in `Infrastructure/DependencyInjection.cs`:
- Use `AddSingleton<IInterface, Implementation>()` for stateful services
- Use `AddTransient<T>()` for ViewModels and dialogs
- ViewModels should inject services via constructor

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
- **queue_state.json** - Auto-login queue persistence
- **workflows/*.json** - Workflow definitions

## Security & Safety

- **Memory Safety**: Application does NOT modify game memory or inject code
- **File Operations**: Only swaps PlayOnline configuration files (`login_w.bin`)
- **Input Handling**: Uses Windows API (SendInput) for keyboard/mouse automation
- **Credentials**: Can optionally use Windows Credential Manager for secure storage

## Project History

**Branch**: data-driven-auto-login-test

**Recent Major Refactorings:**

1. **Complete LoginTaskStep Enum Removal - Phase 5** (Latest)
   - Deleted `LoginTaskStep` enum entirely (was obsolete with only `None` value)
   - Removed all step-based progress tracking from `AutoLoginQueueItem` (CurrentStep, CompletedSteps, CompleteStep)
   - Removed TaskStep property from `AutoLoginSubtask` and legacy factory methods
   - Removed AssociatedStep from `UIElementTemplate`
   - Deleted `ScreenState.cs` (unused legacy file ~174 lines)
   - Removed LoadTemplatesForStepAsync from `ITemplateManagementService`
   - Removed LegacyTaskStep from `WorkflowStepDefinition`
   - Removed TaskStep from `ILoginTaskHandler` interface and `BaseLoginTaskHandler`
   - Updated handler resolution to use workflow step presence, not enum matching
   - Cleaned up 60+ obsolete warnings across 17 files
   - **Result**: 100% task-based progress tracking - no enum-based step identification. Progress flows through AutoLoginTask → AutoLoginSubtask → UI in real-time.

2. **Template Metadata Migration to Workflows - Phase 4**
   - Moved template PNG files from `Templates/` to `workflows/defaults/templates/` (flat structure)
   - Removed all template JSON metadata files (14 files)
   - Added `ConfidenceThreshold` and `Tolerance` properties to `WorkflowStepDefinition`
   - Updated workflow JSON to include all template metadata in step definitions
   - Templates are now pure PNG files - all configuration lives in workflows
   - **Result**: Workflows are now the absolute single source of truth for all login configuration

3. **100% Workflow-Driven Architecture - Phase 3**
   - Removed `TemplateNavigationTuner` UI tool (~800 lines) - obsolete after workflow-first migration
   - Removed template navigation fallback from `DynamicWorkflowHandler`
   - Templates now **detection-only** (PNG + confidence threshold) - no navigation metadata
   - Workflows are now the **single source of truth** for all navigation
   - **Result**: Clean architectural separation - Templates = Detection, Workflows = Navigation + Execution

4. **100% Workflow-Driven Architecture - Phase 2**
   - Removed `POLProxyLaunchHandler` and `WindowerLaunchHandler`
   - Removed 4 configuration classes (POLProxyLaunchConfiguration, WindowerLaunchConfiguration, etc.)
   - Simplified `LoginTaskStep` enum from 8 values to 1 (only `None` remains, marked obsolete)
   - Extended `DynamicWorkflowHandler` to handle generic application launches
   - Added `StepType` property to `WorkflowStepDefinition` ("NavigateUI" vs "LaunchApplication")
   - Generic launch pattern: ExternalApplicationService + UnifiedMonitoringService + template confirmation
   - **Result**: DynamicWorkflowHandler is now the ONLY handler - handles ALL UI navigation AND application launches

5. **Complete Migration to Workflow-First Architecture - Phase 1**
   - Removed `PlayOnlineAuthHandler` and `FFXIGameHandler` (~1,800 lines of code)
   - Removed 12 specialized services (authentication, navigation, screen detection)
   - Stripped navigation from all template JSON files (14 templates updated)
   - Consolidated all login logic into workflow JSON definitions
   - Simplified `LoginTaskHandlerResolver` to first-match resolution
   - Created `SharedAutoLoginConfiguration` for minimal shared constants
   - **Result**: Single workflow JSON file (`playonline-standard.json`) defines entire login flow

6. Extracted SOLID-compliant services from monolithic queue service

7. Implemented data-driven workflow system (JSON-based login flows)

8. Migrated from absolute coordinates to hybrid navigation (resolution-independent)
