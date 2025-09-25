# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

**FFXI Manager** is a Windows desktop application built to simplify managing multiple Final Fantasy XI accounts and launching related applications. It enables instant switching between different account configurations and provides a unified interface for FFXI tools management.

### Key Features
- **Profile Management**: Create and switch between unlimited account configurations
- **Character Monitor**: Real-time tracking and window switching for active FFXI characters  
- **Global Hotkeys**: Customizable keyboard shortcuts for character window switching
- **Auto-Login System**: Automated login sequences with screen detection and UI automation
- **Application Launcher**: Launch and monitor FFXI-related tools (Windower, etc.)

## Technology Stack

- **.NET 9** with Windows-specific features (`net9.0-windows`)
- **WPF** for UI with XAML views and MVVM pattern
- **OpenCV** (OpenCvSharp4) for screenshot detection and template matching
- **Serilog** for comprehensive logging (Console, File, Async sinks)
- **Microsoft.Extensions** for dependency injection and hosting
- **SharpDX.DirectInput** for controller support
- **MSTest** for testing framework

## Essential Commands

### Build and Run
```bash
# Quick build (most common)
dotnet build --configuration Release

# Run the application  
dotnet run --configuration Release

# Clean and rebuild
dotnet clean && dotnet build

# Run tests
dotnet test Testing/FFXIManager.Tests.csproj
```

### Package Creation
```powershell
# Publish for deployment
dotnet publish -c Release -o ./publish

# Create ZIP package (PowerShell)
Compress-Archive -Path "./publish/*" -DestinationPath "FFXIManager-local.zip"
```

*See [CLAUDE.md](CLAUDE.md) for comprehensive development commands and build configuration details.*

## Architecture Overview

### Core Patterns

**MVVM Architecture**
- Clean separation: Views (XAML) ↔ ViewModels (business logic) ↔ Models (data)
- `ViewModelBase` provides common MVVM functionality with `INotifyPropertyChanged`
- Specialized ViewModels: `ProfileManagementViewModel`, `PlayOnlineMonitorViewModel`, etc.

**Dependency Injection**
- All services registered in `Infrastructure/DependencyInjection.cs`
- Interface-first design: Every service has an `I`-prefixed interface
- Singleton registration for application-wide state management
- Constructor injection pattern with null checks

**Service Layer Architecture**
- **Core Services**: Settings, Configuration, Logging, Caching, Notifications
- **UI/Threading**: `WpfUiDispatcher` for thread-safe UI updates
- **Process/Monitoring**: Process management, character detection, window handling
- **Application Logic**: Profile operations, hotkey management, external applications

### Key Modules

**AutoLogin System** (`Services/AutoLogin/`)
- Screen detection using OpenCV template matching
- Context management with `IAutoLoginContextService` 
- Event-driven architecture with security validation
- Handler pattern for different login steps
- Template-based UI automation

**Profile Management**
- PlayOnline account configuration switching
- Automatic backup creation before profile swaps
- Profile validation and persistence

**Character Monitoring**
- Real-time FFXI process and window detection
- Character ordering and activation services  
- Global hotkey integration for window switching

*See [CLAUDE.md](CLAUDE.md) for detailed component listings and architectural patterns.*

## Development Guidelines

### Code Quality Principles
- **SOLID Principles**: Single responsibility, interface segregation, dependency inversion
- **Maintainability Focus**: Avoid workarounds, prefer existing functionality over custom solutions
- **Living Documentation**: Code comments serve as documentation, avoid separate summary documents

### Service Development Pattern
1. **Create interface first** (e.g., `IYourService`)
2. **Implement service class** with constructor injection
3. **Register in DI container** (`Infrastructure/DependencyInjection.cs`)
4. **Add comprehensive logging** using `ILoggingService`
5. **Include null checks** for injected dependencies

### UI Development
- **Theme-friendly colors**: Avoid hardcoded colors, use dynamic theme-aware values
- **MVVM binding**: Use data binding rather than code-behind manipulation
- **Thread safety**: Use `IUiDispatcher` for cross-thread UI updates

### Testing Standards
- **MSTest framework** for all test projects
- **Security validation** required for AutoLogin and sensitive features
- **Integration testing** with quickstart validation patterns

## WARP-Specific Workflow

### Branch and Commit Management
⚠️ **CRITICAL**: Follow these rules strictly:
- **DO NOT create new branches** without explicit confirmation
- **DO NOT create PRs** without explicit confirmation  
- **DO NOT commit changes** until code review is completed
- Always request confirmation before any git operations

### File Navigation
```
├── Services/           # Business logic services (interface-based)
├── ViewModels/         # MVVM ViewModels with ViewModelBase
├── Views/              # WPF XAML views and code-behind  
├── Infrastructure/     # DI, threading, process management
├── Models/             # Data models and DTOs
├── Configuration/      # Application configuration services
├── Testing/            # MSTest-based test projects
└── Templates/          # AutoLogin screen detection templates
```

### Common Tasks

**Adding a New Service:**
1. Create `IYourService` interface in `Services/`
2. Implement service class with constructor injection
3. Register in `Infrastructure/DependencyInjection.cs`
4. Add to relevant ViewModel constructors

**Creating New ViewModels:**
1. Inherit from `ViewModelBase`
2. Use constructor injection for required services
3. Follow property change notification patterns
4. Register in DI container if singleton

**AutoLogin Handler Development:**
1. Inherit from `BaseLoginTaskHandler`
2. Implement `ExecuteHandlerLogicAsync` method
3. Use standardized screen detection patterns
4. Create corresponding template files in `Templates/`

## Troubleshooting

### Strong Name Signing
- Verify assembly signing: Check for `PublicKeyToken` in assembly full name
- Missing .snk file: Strong name key required for build (handled in CI/CD)
- Signing errors: Ensure `ffximanager.snk` is present and valid

### WPF Development in Terminal
- XAML compilation errors: Run `dotnet build` to see detailed diagnostics
- Designer issues: WPF designer not available in terminal, use Visual Studio for XAML editing
- Binding errors: Check Output window in Visual Studio or use debug binding expressions

### Dependency Injection Issues
- Service not registered: Verify registration in `Infrastructure/DependencyInjection.cs`
- Circular dependencies: Check constructor injection chains
- Singleton vs Transient: Most services are Singletons for state management

### Thread Safety
- UI updates from background threads: Always use `IUiDispatcher.BeginInvoke()`
- Service method calls: Most services are thread-safe, but UI operations require dispatcher
- ObservableCollection updates: Must occur on UI thread

### AutoLogin Development
- Template matching: Use full-context screenshots rather than small element crops
- Screen detection: Test confidence thresholds carefully (typically 0.80+)
- Context sharing: Use `IAutoLoginContextService` for data between steps
- Security validation: All credential handling must be validated and tested

## Related Documentation

- **[CLAUDE.md](CLAUDE.md)**: Comprehensive architecture details, service listings, and complete command reference
- **[CONTRIBUTING.md](CONTRIBUTING.md)**: Git workflows, PR process, release procedures, and GitHub Actions
- **[ARCHITECTURE_CONTEXT_MANAGEMENT.md](ARCHITECTURE_CONTEXT_MANAGEMENT.md)**: AutoLogin context service architecture

---

*This documentation follows the project's living documentation principle - detailed implementation patterns and examples are maintained within code comments rather than separate documents.*