# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Build and Run
```bash
# Build the solution
dotnet build

# Build for release
dotnet build --configuration Release

# Run the application
dotnet run --configuration Release

# Clean build artifacts
dotnet clean
```

### Testing
```bash
# Run all tests
dotnet test Testing/FFXIManager.Tests.csproj

# Run tests with specific configuration
dotnet test Testing/FFXIManager.Tests.csproj --configuration Release

# Build test project only
dotnet build Testing/FFXIManager.Tests.csproj
```

### Publishing
```bash
# Publish for deployment
dotnet publish -c Release -o ./publish

# Create ZIP package (PowerShell)
Compress-Archive -Path "./publish/*" -DestinationPath "FFXIManager-local.zip"
```

## Architecture Overview

### Core Architecture Patterns
- **MVVM Pattern**: Clean separation between Views (XAML), ViewModels (business logic), and Models (data)
- **Dependency Injection**: All services registered in `Infrastructure/DependencyInjection.cs` using Microsoft.Extensions.DependencyInjection
- **Service Layer**: Business logic separated into focused service interfaces with implementations
- **Interface-based Design**: All major components have interfaces for testability and maintainability

### Key Architectural Components

#### Dependency Injection Container
- Main registration in `Infrastructure/DependencyInjection.cs:AddAppServices()`
- Services registered as Singletons for application-wide state management
- Clear separation of concerns: Core services, UI/Threading, Process/Monitoring, App logic

#### Service Layer Organization
- **Core Services**: Settings, Configuration, Logging, Caching, Notifications, Validation
- **UI/Threading**: WpfUiDispatcher for thread-safe UI updates, WindowEventTracker
- **Process/Monitoring**: Process utilities, unified monitoring, PlayOnline monitoring
- **Application Logic**: External applications, status messages, character ordering, hotkeys

#### ViewModel Architecture
- `MainViewModel` acts as coordinator, injecting dependencies into specialized ViewModels
- Specialized ViewModels: `ProfileManagementViewModel`, `ApplicationManagementViewModel`, `PlayOnlineMonitorViewModel`
- Base class `ViewModelBase` provides common MVVM functionality
- Clear dependency injection pattern with null checks

#### Service Interface Patterns
All services follow consistent interface patterns:
- Interfaces prefixed with `I` (e.g., `ISettingsService`, `IProfileService`)
- Services focused on single responsibility
- Clear separation between interfaces and implementations

### Technology Stack
- **.NET 9** with Windows-specific features
- **WPF** for UI with XAML views
- **Serilog** for comprehensive logging with multiple sinks (Console, File, Async)
- **SharpDX.DirectInput** for controller support
- **System.Management** for system-level operations
- **Microsoft.Extensions** for hosting and dependency injection

### Project Structure
- `Services/`: Business logic services with interface-based design
- `ViewModels/`: MVVM ViewModels with Base classes
- `Views/`: WPF XAML views and code-behind
- `Infrastructure/`: Cross-cutting concerns (DI, threading, process management)
- `Configuration/`: Application configuration services
- `Testing/`: MSTest-based test project

### Auto-Login Module
- Located in `Services/AutoLogin/` with Application layer
- Uses event-driven architecture with EventBus pattern
- Implements comprehensive security validation framework
- Includes integration testing with quickstart validation

## Development Guidelines

### Code Conventions
- Follow `.editorconfig` settings: 4-space indentation, PascalCase for public members
- Use file-scoped namespaces (`csharp_style_namespace_declarations = file_scoped`)
- Prefer explicit types over `var` except when type is apparent
- Enable nullable reference types project-wide

### Service Development
- Always create interface first, then implementation
- Register services in `Infrastructure/DependencyInjection.cs`
- Follow constructor injection pattern with null checks
- Use `ILogger<T>` for logging within services

### Testing
- Use MSTest framework (`MSTest.TestFramework`, `MSTest.TestAdapter`)
- Test project targets same framework as main project (net9.0-windows)
- Comprehensive security testing framework in place for auto-login features

### Version Management
- Version controlled in `FFXIManager.csproj` (currently 1.3.1-beta)
- Do not manually bump version numbers - handled by release workflow
- Strong name signing enabled with `ffximanager.snk`

### Build Configuration
- .NET Analyzers enabled with latest analysis level
- Warnings treated as errors
- Automatic exclusion of Testing files from main build
- Application manifests and configuration files copied to output