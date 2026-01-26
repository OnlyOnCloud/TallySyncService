using Microsoft.Extensions.Hosting;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService;

public class TallySyncWorker : BackgroundService
{
    private readonly TallyConfig _config;
    private readonly int _intervalMinutes;
    private readonly string _backendUrl;
    private readonly int _organisationId;

    public TallySyncWorker()
    {
        var (tallyConfig, intervalMinutes, backendUrl, organisationId) = LoadConfiguration();
        _config = tallyConfig;
        _intervalMinutes = intervalMinutes;
        _backendUrl = backendUrl;
        _organisationId = organisationId;
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
            var allTables = yamlLoader.GetAllTables();
            Console.WriteLine($"✓ Loaded {allTables.Count} table definitions");

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

            var uploadedCount = await uploadService.UploadMultipleCsvFilesAsync(
                exportedFiles, 
                _organisationId);

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

    private (TallyConfig, int, string, int) LoadConfiguration()
    {
        var configPath = "config.json";
        var tallyConfig = new TallyConfig();
        var intervalMinutes = 15;
        var backendUrl = "http://localhost:8080/api/data";
        var organisationId = 1;
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine("⚠️  config.json not found, using defaults");
            return (tallyConfig, intervalMinutes, backendUrl, organisationId);
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
                    
                    if (backend.TryGetProperty("organisationId", out var orgId))
                        organisationId = orgId.GetInt32();
                }
            }

            return (tallyConfig, intervalMinutes, backendUrl, organisationId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Error loading config: {ex.Message}, using defaults");
            return (tallyConfig, intervalMinutes, backendUrl, organisationId);
        }
    }
}
