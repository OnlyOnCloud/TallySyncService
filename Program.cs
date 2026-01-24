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
    // Check for login mode
    if (args.Contains("--login"))
    {
        await RunLoginModeAsync();
        return 0;
    }

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

    // Check for test sync mode
    if (args.Contains("--test-sync"))
    {
        await RunTestSyncAsync();
        return 0;
    }

    if (args.Contains("--test-companies"))
{
    await TestCompanyListAsync();
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

async Task RunLoginModeAsync()
{
    Log.Information("Running in login mode");
    
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureServices(builder);
    
    var host = builder.Build();
    
    var authService = host.Services.GetRequiredService<IAuthService>();
    var success = await authService.LoginAsync();
    
    Environment.Exit(success ? 0 : 1);
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

async Task RunTestSyncAsync()
{
    Log.Information("Running test sync");
    
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureServices(builder);
    
    var host = builder.Build();
    
    var tallyService = host.Services.GetRequiredService<ITallyService>();
    var backendService = host.Services.GetRequiredService<IBackendService>();
    var configService = host.Services.GetRequiredService<IConfigurationService>();
    
    Console.WriteLine("╔════════════════════════════════════════════╗");
    Console.WriteLine("║   Tally Sync Service - Test Sync         ║");
    Console.WriteLine("╚════════════════════════════════════════════╝");
    Console.WriteLine();
    
    // Check Tally connection
    Console.WriteLine("1. Testing Tally connection...");
    var tallyConnected = await tallyService.CheckConnectionAsync();
    if (tallyConnected)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("   ✓ Tally connection successful");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("   ✗ Cannot connect to Tally");
        Console.ResetColor();
        return;
    }
    Console.WriteLine();
    
    // Check Backend connection
    Console.WriteLine("2. Testing backend connection...");
    var backendConnected = await backendService.CheckConnectionAsync();
    if (backendConnected)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("   ✓ Backend connection successful");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("   ⚠ Backend connection failed (will still test data fetch)");
        Console.ResetColor();
    }
    Console.WriteLine();
    
    // Load configuration
    var config = await configService.LoadConfigurationAsync();
    if (!config.IsConfigured || config.SelectedTables.Count == 0)
    {
        Console.WriteLine("No tables configured. Run --setup first.");
        return;
    }
    
    // Test sync first table
    var testTable = config.SelectedTables.First();
    Console.WriteLine($"3. Testing sync for table: {testTable}");
    
    try
{
    // Fetch from Tally
    Console.WriteLine($"   Fetching data from Tally...");
    var data = await tallyService.FetchTableDataAsync(testTable);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"   ✓ Fetched {data.Length} bytes from Tally");
    Console.ResetColor();
    Console.WriteLine();
    
    // Convert to JSON
    var converter = host.Services.GetRequiredService<IXmlToJsonConverter>();
    var records = converter.ConvertTallyXmlToRecords(data, testTable);
    
    Console.WriteLine($"   ✓ Converted to {records.Count} JSON records");
    Console.WriteLine();
    
    // Show sample of data
    if (records.Count > 0)
    {
        Console.WriteLine("   Sample record (first record):");
        Console.WriteLine("   " + new string('─', 50));
        var sampleJson = Newtonsoft.Json.JsonConvert.SerializeObject(records[0].Data, Newtonsoft.Json.Formatting.Indented);
        var lines = sampleJson.Split('\n');
        foreach (var line in lines.Take(20))
        {
            Console.WriteLine("   " + line);
        }
        if (lines.Length > 20)
            Console.WriteLine("   ... (truncated)");
        Console.WriteLine("   " + new string('─', 50));
        Console.WriteLine();
    }
    
    if (backendConnected)
    {
        // Send to backend
        Console.WriteLine($"   Sending data to backend...");
        var payload = new SyncPayload
        {
            TableName = testTable,
            Records = records.Take(5).ToList(), // Send only first 5 for testing
            Timestamp = DateTime.UtcNow,
            SourceIdentifier = Environment.MachineName,
            TotalRecords = records.Count,
            ChunkNumber = 1,
            TotalChunks = 1,
            SyncMode = "TEST"
        };
        
        var success = await backendService.SendDataAsync(payload);
        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"   ✓ Successfully sent test data to backend");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"   ✗ Failed to send data to backend");
            Console.ResetColor();
        }
    }
    else
    {
        Console.WriteLine($"   Skipping backend send (connection failed)");
    }
}
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"   ✗ Error: {ex.Message}");
        Console.ResetColor();
        Log.Error(ex, "Test sync failed");
    }
    
    Console.WriteLine();
    Console.WriteLine("Test sync completed. Check logs for details.");
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

    // Register ODBC and CSV services
    builder.Services.AddSingleton<IOdbcService, OdbcService>();
    builder.Services.AddSingleton<ICsvExporter, CsvExporter>();
    builder.Services.AddSingleton<IYamlConfigService, YamlConfigService>();

    // Register services
    builder.Services.AddSingleton<ConfigurationService>();
    builder.Services.AddSingleton<IConfigurationService>(sp => sp.GetRequiredService<ConfigurationService>());
    builder.Services.AddSingleton<ITallyService, TallyService>();
    builder.Services.AddSingleton<IAuthService, AuthService>();
    builder.Services.AddSingleton<IBackendService>(sp =>
    {
        var backendService = ActivatorUtilities.CreateInstance<BackendService>(sp);
        var authService = sp.GetRequiredService<IAuthService>();
        backendService.SetAuthService(authService);
        return backendService;
    });
    builder.Services.AddSingleton<IXmlToJsonConverter, XmlToJsonConverter>();
    builder.Services.AddSingleton<ISyncEngine, SyncEngine>();
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
        
        client.BaseAddress = new Uri(options.BackendUrl);
        client.Timeout = TimeSpan.FromSeconds(options.BackendTimeoutSeconds);
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

async Task TestCompanyListAsync()
{
    var builder = Host.CreateApplicationBuilder(args);
    ConfigureServices(builder);
    var host = builder.Build();
    
    var tallyService = host.Services.GetRequiredService<ITallyService>();
    
    Console.WriteLine("Testing Tally Company List...");
    
    // Make raw HTTP request to see what Tally returns
    var client = new HttpClient();
    client.BaseAddress = new Uri("http://localhost:9000");
    
    var xmlPayload = @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Collection</TYPE>
    <ID>ListOfCompanies</ID>
  </HEADER>

  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
      </STATICVARIABLES>

      <TDL>
        <TDLMESSAGE>
          <COLLECTION NAME='ListOfCompanies'>
            <TYPE>Company</TYPE>
            <FETCH>NAME</FETCH>
          </COLLECTION>
        </TDLMESSAGE>
      </TDL>
    </DESC>
  </BODY>
</ENVELOPE>";

    var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
    var response = await client.PostAsync("", content);
    
    var xmlData = await response.Content.ReadAsStringAsync();
    
    // Save to file to inspect
    await File.WriteAllTextAsync("company-list-response.xml", xmlData);
    Console.WriteLine("Response saved to company-list-response.xml");
    Console.WriteLine(xmlData);
}

