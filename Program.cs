using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using TallySyncService;
using TallySyncService.Models;
using TallySyncService.Services;
using TallySyncService.Setup;

// Configure Serilog
string logsDirectory;

// Check if running under Wine
var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var isWine = !string.IsNullOrEmpty(winePrefix) || Directory.Exists(Path.Combine(homeDir, ".wine"));

if (isWine)
{
    // Use Wine's ProgramData directory
    var winePrefixPath = !string.IsNullOrEmpty(winePrefix) ? winePrefix : Path.Combine(homeDir, ".wine");
    logsDirectory = Path.Combine(winePrefixPath, "drive_c", "ProgramData", "TallySyncService", "logs");
}
else if (OperatingSystem.IsWindows())
{
    logsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TallySyncService", "logs");
}
else
{
    logsDirectory = Path.Combine(homeDir, ".tallysync", "logs");
}

Directory.CreateDirectory(logsDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(logsDirectory, "tallysync-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    // Check for setup mode
    if (args.Contains("--setup") || args.Contains("--configure"))
    {
        await RunSetupModeAsync();
        return 0;
    }

    // Check for status mode
    if (args.Contains("--status"))
    {
        await ShowStatusAsync();
        return 0;
    }

    // Run as service
    await RunServiceModeAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

async Task RunSetupModeAsync()
{
    Log.Information("Running in setup mode");
    
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureServices(builder);
    
    var host = builder.Build();
    
    var setupCommand = host.Services.GetRequiredService<SetupCommand>();
    var exitCode = await setupCommand.RunAsync();
    
    Environment.Exit(exitCode);
}

async Task ShowStatusAsync()
{
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureServices(builder);
    
    var host = builder.Build();
    
    var configService = host.Services.GetRequiredService<IConfigurationService>();
    var config = await configService.LoadConfigurationAsync();
    
    Console.WriteLine("╔════════════════════════════════════════════╗");
    Console.WriteLine("║   Tally Sync Service - Status             ║");
    Console.WriteLine("╚════════════════════════════════════════════╝");
    Console.WriteLine();
    
    if (!config.IsConfigured)
    {
        Console.WriteLine("Status: Not configured");
        Console.WriteLine("Run with --setup to configure the service");
        return;
    }
    
    Console.WriteLine($"Configured: Yes");
    Console.WriteLine($"Last Updated: {config.LastConfigUpdate:yyyy-MM-dd HH:mm:ss}");
    Console.WriteLine($"Tables: {config.SelectedTables.Count}");
    Console.WriteLine();
    
    if (config.SelectedTables.Count > 0)
    {
        Console.WriteLine("Selected Tables:");
        Console.WriteLine("─────────────────────────────────────────────");
        
        foreach (var tableName in config.SelectedTables)
        {
            Console.WriteLine($"\n{tableName}:");
            
            if (config.TableStates.TryGetValue(tableName, out var state))
            {
                Console.WriteLine($"  Last Sync: {state.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");
                Console.WriteLine($"  Records Synced: {state.TotalRecordsSynced}");
                
                if (!string.IsNullOrEmpty(state.LastError))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  Last Error: {state.LastError}");
                    Console.WriteLine($"  Error Time: {state.LastErrorTime:yyyy-MM-dd HH:mm:ss}");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("  No sync history");
            }
        }
    }
    
    Console.WriteLine();
}

async Task RunServiceModeAsync()
{
    Log.Information("Starting Tally Sync Service");
    
    var builder = Host.CreateApplicationBuilder(args);
    
    // Enable Windows Service support
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Tally Data Sync Service";
    });

    // Use Serilog
    builder.Services.AddSerilog();

    ConfigureServices(builder);
    
    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}

void ConfigureServices(HostApplicationBuilder builder)
{
    // Bind configuration
    builder.Services.Configure<TallySyncOptions>(
        builder.Configuration.GetSection("TallySync"));

    // Register services
    builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
    builder.Services.AddSingleton<ITallyService, TallyService>();
    builder.Services.AddSingleton<IBackendService, BackendService>();
    builder.Services.AddTransient<SetupCommand>();

    // Configure HttpClient for Tally with retry policy
    builder.Services.AddHttpClient("TallyClient", (serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<TallySyncOptions>>().Value;
        client.BaseAddress = new Uri(options.TallyUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TallyTimeoutSeconds);
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

    // Configure HttpClient for Backend with retry policy
    builder.Services.AddHttpClient("BackendClient", (serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<TallySyncOptions>>().Value;
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        
        client.BaseAddress = new Uri(options.BackendUrl);
        client.Timeout = TimeSpan.FromSeconds(options.BackendTimeoutSeconds);
        
        // Add API key if configured (from User Secrets)
        var apiKey = configuration["TallySync:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
}

IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                Log.Warning("Retry {RetryCount} after {Delay}s due to: {Error}",
                    retryCount, timespan.TotalSeconds, outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString());
            });
}

IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Log.Error("Circuit breaker opened for {Duration}s", duration.TotalSeconds);
            },
            onReset: () =>
            {
                Log.Information("Circuit breaker reset");
            });
}