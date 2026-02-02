using Microsoft.Extensions.Hosting;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService;

public class TallySyncWorker : BackgroundService
{
    private readonly TallyConfig _config;
    private readonly int _intervalMinutes;
    private readonly string _backendUrl;
    private readonly string _tableMode;
    private readonly List<string> _customTables;

    public TallySyncWorker()
    {
        var (tallyConfig, intervalMinutes, backendUrl, tableMode, customTables) = LoadConfiguration();
        _config = tallyConfig;
        _intervalMinutes = intervalMinutes;
        _backendUrl = backendUrl;
        _tableMode = tableMode;
        _customTables = customTables;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally CSV Sync Service (Background)         ║");
        Console.WriteLine("║   Syncing every 15 minutes                    ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSyncAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Sync error: {ex.Message}");
            }

            // Wait for the specified interval
            Console.WriteLine($"\n⏱️  Next sync in {_intervalMinutes} minutes...\n");
            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    private async Task PerformSyncAsync()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        Console.WriteLine($"═══════════════════════════════════════════════");
        Console.WriteLine($"🔄 Starting sync at {timestamp}");
        Console.WriteLine($"═══════════════════════════════════════════════");

        try
        {
            // Initialize services
            var tallyXmlService = new TallyXmlService(_config);
            var xmlGenerator = new XmlGenerator();
            var exporter = new TallyDataExporter(tallyXmlService, xmlGenerator, _config);
            var yamlLoader = new YamlConfigLoader(_config.DefinitionFile);
            var uploadService = new BackendUploadService(_backendUrl);

            // Test Tally connection
            Console.WriteLine("Testing connection to Tally...");
            if (!await tallyXmlService.TestConnectionAsync())
            {
                Console.WriteLine($"✗ Unable to connect to Tally at {_config.Server}:{_config.Port}");
                return;
            }
            Console.WriteLine($"✓ Connected to Tally");

            // Load table definitions
            await yamlLoader.LoadAsync();
            var allTables = GetTablesToExport(yamlLoader);
            Console.WriteLine($"✓ Loaded {allTables.Count} table(s) to export (mode: {_tableMode})");

            // Get company
            var companies = await tallyXmlService.GetCompanyListAsync();
            if (companies.Count == 0)
            {
                Console.WriteLine("✗ No companies found");
                return;
            }

            var company = companies.FirstOrDefault(c => c.Name == _config.Company) ?? companies[0];
            _config.Company = company.Name;
            Console.WriteLine($"✓ Using company: {company.Name}");

            // Create temporary export directory
            var tempDir = Path.Combine(Path.GetTempPath(), $"tally_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            Console.WriteLine($"\n📤 Exporting {allTables.Count} tables...");
            Console.WriteLine("─────────────────────────────────────────────────");

            // Export all tables
            var exportedFiles = await exporter.ExportMultipleTablesToCsvAsync(allTables, tempDir);

            Console.WriteLine("─────────────────────────────────────────────────");
            Console.WriteLine($"✓ Exported {exportedFiles.Count} files");

            // Upload to backend
            Console.WriteLine($"\n📡 Uploading to backend ({_backendUrl})...");
            Console.WriteLine("─────────────────────────────────────────────────");

            // Get organisation ID from login (saved in ~/.tally_org)
            var organisationId = AuthService.LoadOrganisationId();
            if (!organisationId.HasValue)
            {
                Console.WriteLine("✗ No organization selected. Please run: dotnet run -- --login");
                return;
            }

            var uploadedCount = await uploadService.UploadMultipleCsvFilesAsync(
                exportedFiles, 
                (int)organisationId.Value);

            Console.WriteLine("─────────────────────────────────────────────────");
            Console.WriteLine($"✓ Uploaded {uploadedCount}/{exportedFiles.Count} files successfully");

            // Cleanup temporary directory
            try
            {
                Directory.Delete(tempDir, true);
                Console.WriteLine($"✓ Cleaned up temporary files");
            }
            catch
            {
                // Ignore cleanup errors
            }

            Console.WriteLine($"\n✅ Sync completed successfully at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Sync failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Details: {ex.InnerException.Message}");
            }
        }
    }

    private List<TableDefinition> GetTablesToExport(YamlConfigLoader yamlLoader)
    {
        return _tableMode switch
        {
            "master" => yamlLoader.GetMasterTables(),
            "transaction" => yamlLoader.GetTransactionTables(),
            "custom" => _customTables.Count > 0 
                ? yamlLoader.GetTablesByNames(_customTables) 
                : yamlLoader.GetAllTables(),
            _ => yamlLoader.GetAllTables()
        };
    }

    private (TallyConfig, int, string, string, List<string>) LoadConfiguration()
    {
        var configPath = "config.json";
        var tallyConfig = new TallyConfig();
        var intervalMinutes = 15;
        var backendUrl = "http://localhost:3001/api/data";
        var tableMode = "all";
        var customTables = new List<string>();
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("⚠️  config.json not found, using defaults");
            return (tallyConfig, intervalMinutes, backendUrl, tableMode, customTables);
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var configData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json);
            
            if (configData != null)
            {
                // Load Tally configuration
                if (configData.ContainsKey("tally"))
                {
                    var tally = configData["tally"];
                    
                    if (tally.TryGetProperty("server", out var server))
                        tallyConfig.Server = server.GetString() ?? "localhost";
                    
                    if (tally.TryGetProperty("port", out var port))
                        tallyConfig.Port = port.GetInt32();
                    
                    if (tally.TryGetProperty("company", out var company))
                        tallyConfig.Company = company.GetString() ?? "";
                }

                // Load sync configuration
                if (configData.ContainsKey("sync"))
                {
                    var sync = configData["sync"];
                    
                    if (sync.TryGetProperty("intervalMinutes", out var interval))
                        intervalMinutes = interval.GetInt32();
                }

                // Load backend configuration
                if (configData.ContainsKey("backend"))
                {
                    var backend = configData["backend"];
                    
                    if (backend.TryGetProperty("url", out var url))
                        backendUrl = url.GetString() ?? backendUrl;
                }

                // Load table selection configuration
                if (configData.ContainsKey("tables"))
                {
                    var tables = configData["tables"];
                    
                    if (tables.TryGetProperty("mode", out var mode))
                        tableMode = mode.GetString() ?? "all";
                    
                    if (tables.TryGetProperty("customTables", out var custom))
                    {
                        customTables = System.Text.Json.JsonSerializer.Deserialize<List<string>>(custom.GetRawText()) ?? new List<string>();
                    }
                }
            }

            return (tallyConfig, intervalMinutes, backendUrl, tableMode, customTables);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Error loading config: {ex.Message}, using defaults");
            return (tallyConfig, intervalMinutes, backendUrl, tableMode, customTables);
        }
    }
}
