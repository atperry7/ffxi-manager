using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Extensions.Hosting;
using FFXIManager.Infrastructure;
using FFXIManager.Models;
using FFXIManager.Models.Settings;
using FFXIManager.Services;

namespace FFXIManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        private static readonly Uri LightThemeUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkThemeUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        public static IServiceProvider? Services
        {
            get
            {
                // Design-time safety: return null if not our App type
                if (Current is not App app)
                    return null;
                    
                return app._host?.Services;
            }
        }

        /// <summary>
        /// Configure bootstrap Serilog logging before DI container is built
        /// </summary>
        private static void ConfigureBootstrapLogging()
        {
            try
            {
                // Enable self-diagnostics to a fallback file
                var tempPath = Path.GetTempPath();
                var selfLogPath = Path.Combine(tempPath, "FFXIManager-serilog-selflog.txt");
                Serilog.Debugging.SelfLog.Enable(msg => 
                {
                    try { File.AppendAllText(selfLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}"); }
                    catch { /* Ignore self-log failures */ }
                });

                // Create logs directory
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logDirectory = Path.Combine(appDataPath, "FFXIManager", "logs");
                Directory.CreateDirectory(logDirectory);

                // Build configuration from appsettings files
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                // Check if Serilog section exists in configuration
                var serilogSection = configuration.GetSection("Serilog");
                if (serilogSection.Exists() && serilogSection.GetChildren().Any())
                {
                    // Use Serilog configuration from appsettings
                    Log.Logger = new LoggerConfiguration()
                        .ReadFrom.Configuration(configuration)
                        .CreateLogger();
                    
                    Log.Information("Bootstrap Serilog logger configured from appsettings");
                }
                else
                {
                    // Fallback: Use legacy DiagnosticsOptions for backward compatibility
                    Log.Logger = CreateLoggerFromDiagnosticsOptions(logDirectory);
                    Log.Information("Bootstrap Serilog logger configured from legacy DiagnosticsOptions (fallback mode)");
                }
            }
            catch (Exception ex)
            {
                // Fallback to minimal console-only logger if configuration fails
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();
                    
                Log.Warning(ex, "Failed to configure logger from appsettings, using fallback configuration");
            }
        }

        /// <summary>
        /// Creates a Serilog logger based on legacy DiagnosticsOptions for backward compatibility
        /// </summary>
        private static ILogger CreateLoggerFromDiagnosticsOptions(string logDirectory)
        {
            try
            {
                // Try to load settings to get DiagnosticsOptions
                var settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FFXIManager", "settings.json");
                DiagnosticsOptions? diagnostics = null;
                
                if (File.Exists(settingsFilePath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsFilePath);
                        var settings = System.Text.Json.JsonSerializer.Deserialize<Models.Settings.ApplicationSettings>(settingsJson);
                        diagnostics = settings?.Diagnostics;
                    }
                    catch
                    {
                        // Ignore settings loading errors, use defaults
                    }
                }

                // Apply DiagnosticsOptions mapping
                diagnostics ??= new Models.Settings.DiagnosticsOptions();
                
                var logConfig = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId()
                    .Enrich.WithProperty("Application", "FFXIManager");

                // Map EnableDiagnostics and VerboseLogging to minimum level
                if (!diagnostics.EnableDiagnostics)
                {
                    logConfig.MinimumLevel.Warning();
                }
                else if (diagnostics.VerboseLogging)
                {
                    logConfig.MinimumLevel.Debug();
                }
                else
                {
                    logConfig.MinimumLevel.Information();
                }
                
                // Always suppress noisy Microsoft/System logs
                logConfig.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning);
                logConfig.MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning);

                // Add console sink
                logConfig.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

                // Add async file sink
                var logPath = Path.Combine(logDirectory, "log-.json");
                logConfig.WriteTo.Async(a => a.File(
                    new Serilog.Formatting.Compact.CompactJsonFormatter(),
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50 * 1024 * 1024, // 50MB
                    retainedFileCountLimit: 14,
                    shared: true));

                return logConfig.CreateLogger();
            }
            catch (Exception ex)
            {
                // Ultimate fallback - basic console logger
                Log.Warning(ex, "Failed to create logger from DiagnosticsOptions, using minimal fallback");
                return new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();
            }
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Configure early bootstrap Serilog logger
            ConfigureBootstrapLogging();
            
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .UseSerilog() // Use Serilog as the logging provider
                .ConfigureServices(services => services.AddAppServices())
                .Build();

            // Load the initial theme from settings
            try
            {
                var settingsService = Services?.GetRequiredService<ISettingsService>();
                if (settingsService == null)
                {
                    Log.Warning("Services provider not available, using default dark theme");
                    ApplyTheme(true);
                    return;
                }
                var settings = settingsService.LoadSettings();
                ApplyTheme(settings.IsDarkTheme);

                // At this point we know Services is not null since settingsService was resolved
                var services = Services!;
                
                // Centralize global hotkey registration at app startup so it works regardless of UI windows
                services.GetRequiredService<GlobalHotkeyManager>().RegisterHotkeysFromSettings();

                // One-time migration notice: Hybrid-only navigation and template cleanup
                try
                {
                    var logging = services.GetRequiredService<ILoggingService>();
                    await logging.LogInfoAsync("Navigation runtime is Hybrid-only (keyboard-first, click fallback). Please remove legacy absolute coordinates from templates (action.clickOffset, memberSlots/otpField) and rely on navigation.fallback.clickOffset where needed.");
                }
                catch { /* best-effort */ }

                // Ensure PlayOnline monitoring is started regardless of UI windows
                services.GetRequiredService<IPlayOnlineMonitorService>().StartMonitoring();
                
                // Connect the character ordering service to the monitor and wait for completion
                if (services.GetRequiredService<ICharacterOrderingService>() is CharacterOrderingService orderingService)
                {
                    try
                    {
                        await orderingService.ConnectToMonitorAsync(services.GetRequiredService<IPlayOnlineMonitorService>());
                    }
                    catch (Exception ex)
                    {
                        _ = services.GetRequiredService<ILoggingService>().LogErrorAsync("Error connecting character ordering service to monitor", ex, "App");
                    }
                }

                // **GAMING OPTIMIZATION**: Ultra-fast hotkey processing via unified service
                services.GetRequiredService<GlobalHotkeyManager>().HotkeyPressed += async (_, e) =>
                {
                    // Check if this is the cycle hotkey
                    if (e.HotkeyId == HotkeyActivationService.CycleHotkeyId)
                    {
                        // Handle cycle hotkey
                        var cycleResult = await services.GetRequiredService<IHotkeyActivationService>().CycleToNextCharacterAsync();
                        
                        if (!cycleResult.Success && IsUnexpectedHotkeyError(cycleResult.ErrorMessage))
                        {
                            _ = services.GetRequiredService<INotificationServiceEnhanced>()?.ShowToastAsync($"Cycle failed: {cycleResult.ErrorMessage}", NotificationType.Error);
                        }
                    }
                    else
                    {
                        // **UNIFIED PIPELINE**: All hotkey activation through optimized service
                        var result = await services.GetRequiredService<IHotkeyActivationService>().ActivateCharacterByHotkeyAsync(e.HotkeyId);
                        
                        if (!result.Success && IsUnexpectedHotkeyError(result.ErrorMessage))
                        {
                            _ = services.GetRequiredService<INotificationServiceEnhanced>()?.ShowToastAsync($"Hotkey failed: {result.ErrorMessage}", NotificationType.Error);
                        }
                    }
                };
                
                // **PERFORMANCE**: Initialize hotkey mappings at startup
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await services.GetRequiredService<IHotkeyMappingService>().RefreshMappingsAsync();
                    }
                    catch (Exception ex)
                    {
                        _ = services.GetRequiredService<ILoggingService>().LogErrorAsync("Error initializing hotkey mappings", ex, "App");
                    }
                });
                
                // Refresh hotkeys and mappings when settings change
                ViewModels.DiscoverySettingsViewModel.HotkeySettingsChanged += (_, __) =>
                {
                    try
                    {
                        services.GetRequiredService<GlobalHotkeyManager>().RefreshHotkeys();
                        _ = Task.Run(() => services.GetRequiredService<IHotkeyMappingService>().RefreshMappingsAsync());
                    }
                    catch { }
                };
            }
            catch
            {
                // Default to dark theme if settings can't be loaded
                ApplyTheme(true);
            }

            // Show main window via DI
            var window = Services?.GetRequiredService<MainWindow>();
            if (window == null)
            {
                Log.Error("Failed to resolve MainWindow from services provider");
                Shutdown(1);
                return;
            }
            window.Show();
        }
        

        /// <summary>
        /// Determines if a hotkey error is unexpected and should be shown to the user.
        /// </summary>
        private static bool IsUnexpectedHotkeyError(string? errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage)) return false;
            
            // Don't show notifications for expected/common errors
            return !errorMessage.Contains("No character mapped") &&
                   !errorMessage.Contains("Invalid window handle") &&
                   !errorMessage.Contains("Access denied") &&
                   !errorMessage.Contains("out of range");
        }

        public static void ApplyTheme(bool isDarkTheme)
        {
            var app = Application.Current;
            if (app == null) return;

            var themeUri = isDarkTheme ? DarkThemeUri : LightThemeUri;

            // Find and replace the theme dictionary - it should always be at index 0
            ResourceDictionary? oldTheme = null;
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                var firstDict = app.Resources.MergedDictionaries[0];
                if (firstDict.Source != null &&
                    (firstDict.Source.OriginalString.Contains("LightTheme.xaml") ||
                     firstDict.Source.OriginalString.Contains("DarkTheme.xaml")))
                {
                    oldTheme = firstDict;
                }
            }

            var newTheme = new ResourceDictionary { Source = themeUri };

            if (oldTheme != null)
            {
                // Replace the first dictionary (theme)
                app.Resources.MergedDictionaries[0] = newTheme;
            }
            else
            {
                // Insert at the beginning
                app.Resources.MergedDictionaries.Insert(0, newTheme);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Unregister global hotkeys on exit to avoid leaving hooks active
                Services?.GetRequiredService<GlobalHotkeyManager>().UnregisterAllHotkeys();
            }
            catch { }

            _host?.Dispose();
            
            // Ensure all logs are flushed before exit
            try
            {
                Log.CloseAndFlush();
            }
            catch { }
            
            base.OnExit(e);
        }
    }
}
