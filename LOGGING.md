# FFXIManager Logging Guide

## Overview
FFXIManager uses Serilog for high-performance structured logging, integrated with Microsoft.Extensions.Logging for compatibility.

## Configuration

### Environment-Specific Settings
- `appsettings.json` - Production configuration (Information level, file + console output)
- `appsettings.Development.json` - Development configuration (Debug level, verbose output)
- `appsettings.Production.json` - Production optimization (minimal logging, file only)

### Log Locations
- **File Location**: `%APPDATA%\FFXIManager\logs\`
- **File Format**: JSON (Compact JSON formatter for structured logs)
- **Rotation**: Daily with size limits (50MB production, 10MB development)
- **Retention**: 14 days (production), 7 days (development), 30 days (production env)

### Configuration Structure
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}" }
      },
      {
        "Name": "Async",
        "Args": {
          "configure": [
            {
              "Name": "File",
              "Args": {
                "path": "%APPDATA%\\FFXIManager\\logs\\log-.json",
                "rollingInterval": "Day",
                "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter"
              }
            }
          ]
        }
      }
    ]
  }
}
```

## Backward Compatibility

### Legacy DiagnosticsOptions Support
The system automatically falls back to legacy `DiagnosticsOptions` if no Serilog configuration is found:
- `EnableDiagnostics: false` → Warning level minimum
- `EnableDiagnostics: true` + `VerboseLogging: false` → Information level  
- `EnableDiagnostics: true` + `VerboseLogging: true` → Debug level
- `MaxLogEntries` → Controls in-memory buffer for `GetRecentLogsAsync()`

### Migration Path
1. **Phase 1** (Current): All services use `ILoggingService` adapter over Serilog
2. **Phase 2** (Future): Gradually migrate services to inject `ILogger<T>` directly

## Usage Guidelines

### Service Logging
```csharp
public class MyService
{
    private readonly ILoggingService _logging;
    
    public MyService(ILoggingService logging)
    {
        _logging = logging;
    }
    
    public async Task DoSomething(string userId)
    {
        await _logging.LogInfoAsync("Processing user operation", "MyService");
        
        try
        {
            // Operation logic
        }
        catch (Exception ex)
        {
            await _logging.LogErrorAsync("Operation failed", ex, "MyService");
            throw;
        }
    }
}
```

### Structured Logging Best Practices
- Use message templates with named placeholders
- Include relevant context (userId, operation, component)
- Use appropriate log levels:
  - **Debug**: Developer diagnostics, verbose tracing
  - **Information**: State changes, successful operations
  - **Warning**: Recoverable issues, configuration problems
  - **Error**: Failures requiring attention

### Performance Considerations
- Async file sinks prevent I/O blocking
- Message templates are compiled for performance
- SerilogAnalyzer catches inefficient patterns at compile time
- Hot paths can use LoggerMessage delegates for zero allocation

## Troubleshooting

### Self-Diagnostics
Serilog self-diagnostics are written to: `%TEMP%\FFXIManager-serilog-selflog.txt`

### Common Issues
1. **Logs not appearing**: Check directory permissions for `%APPDATA%\FFXIManager\logs\`
2. **Performance issues**: Verify async sinks are configured correctly
3. **Missing configuration**: Application falls back to DiagnosticsOptions automatically

### Log Analysis
- Production logs use Compact JSON format for machine parsing
- Development logs use human-readable console output
- Use log aggregation tools (ELK, Seq, etc.) for production analysis

## Features

### Current Features ✅
- High-performance async file logging
- Structured JSON output with enrichers  
- Automatic log rotation and retention
- Environment-specific configuration
- Backward compatibility with DiagnosticsOptions
- Console output with readable formatting
- Exception logging with stack traces
- Scoped logging with categories

### Future Enhancements 🔮
- Direct `ILogger<T>` injection for services
- Custom enrichers for FFXI-specific context
- Performance metrics integration
- Log aggregation service integration
- Advanced filtering and sampling

## Dependencies
- Serilog 4.3.0
- Serilog.Extensions.Logging 9.0.2
- Serilog.Sinks.File 7.0.0
- Serilog.Sinks.Console 6.0.0
- Serilog.Sinks.Async 2.1.0
- Serilog.Formatting.Compact 3.0.0
- SerilogAnalyzer 0.15.0 (compile-time analysis)