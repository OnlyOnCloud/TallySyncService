using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService;

public class TallySyncWorker : BackgroundService
{
    private readonly ILogger<TallySyncWorker> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly YamlConfigLoader _yamlLoader;
    private readonly TallyConfig _config;
    private readonly int _intervalMinutes;
    private readonly string _backendUrl;
    private readonly string _tableMode;
    private readonly List<string> _customTables;

    public TallySyncWorker(
        ILogger<TallySyncWorker> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        YamlConfigLoader yamlLoader)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _yamlLoader = yamlLoader;
        
        // Load configuration from appsettings.json
        _config = new TallyConfig
        {
            Server = _configuration["Tally:Server"] ?? "localhost",
            Port = int.Parse(_configuration["Tally:Port"] ?? "9000"),
            Company = _configuration["Tally:Company"] ?? "",
            TallyPath = _configuration["Tally:TallyPath"] ?? "",
            DefinitionFile = _configuration["Tally:DefinitionFile"] ?? "tally-export-config.yaml"
        };
        
        _intervalMinutes = int.Parse(_configuration["Sync:IntervalMinutes"] ?? "15");
        _backendUrl = (_configuration["Backend:Url"] ?? "https://dhub-backend.onlyoncloud.com/api/data").Trim();
        _tableMode = _configuration["Tables:Mode"] ?? "all";
        _customTables = _configuration.GetSection("Tables:CustomTables").Get<List<string>>() ?? new List<string>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("╔═══════════════════════════════════════════════╗");
        _logger.LogInformation("║   Tally CSV Sync Service (Background)         ║");
        _logger.LogInformation("║   Syncing every {IntervalMinutes} minutes                    ║", _intervalMinutes);
        _logger.LogInformation("╚═══════════════════════════════════════════════╝");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSyncAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync error occurred");
            }

            // Wait for the specified interval
            _logger.LogInformation("Next sync in {IntervalMinutes} minutes...", _intervalMinutes);
            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    private async Task PerformSyncAsync()
    {
        _logger.LogInformation("═══════════════════════════════════════════════");
        _logger.LogInformation("Starting sync at {Timestamp}", DateTime.Now);
        _logger.LogInformation("═══════════════════════════════════════════════");

        try
        {
            // Initialize services with dependency injection
            var httpClient = _httpClientFactory.CreateClient();
            var tallyXmlService = new TallyXmlService(_config, _httpClientFactory, _logger);
            var xmlGenerator = new XmlGenerator();
            var exporter = new TallyDataExporter(tallyXmlService, xmlGenerator, _config);
            await _yamlLoader.LoadAsync();
            var uploadService = new BackendUploadService(_backendUrl, _httpClientFactory, _logger);

            // Test Tally connection
            _logger.LogInformation("Testing connection to Tally...");
            if (!await tallyXmlService.TestConnectionAsync())
            {
                _logger.LogWarning("Unable to connect to Tally at {Server}:{Port}", _config.Server, _config.Port);
                
                // Try to auto-start Tally
                _logger.LogInformation("Attempting to start Tally automatically...");
                
                var notificationService = new NotificationService();
                var tallyProcessService = new TallyProcessService(_config.TallyPath);
                
                // Send notification
                notificationService.SendNotification(
                    "Tally Sync Service",
                    "Tally server not running. Please select the company within 15 seconds to complete the sync."
                );
                _logger.LogInformation("Notification sent to user");
                
                // Launch Tally if not already running
                 
                    if (!tallyProcessService.LaunchTally())
                    {
                        _logger.LogWarning("Failed to launch Tally. Skipping this sync cycle.");
                        return;
                    }
                 
                
                // Wait 15 seconds for user to select company
                _logger.LogInformation("Waiting 15 seconds for user to select company...");
                await Task.Delay(TimeSpan.FromSeconds(15));
                
                // Test connection again
                _logger.LogInformation("Testing connection to Tally again...");
                if (!await tallyXmlService.TestConnectionAsync())
                {
                    _logger.LogWarning("Still unable to connect to Tally. Skipping this sync cycle.");
                    _logger.LogInformation("Will retry in {IntervalMinutes} minutes.", _intervalMinutes);
                    return;
                }
                
                _logger.LogInformation("Successfully connected to Tally after auto-start");
            }
            else
            {
                _logger.LogInformation("Connected to Tally");
            }

            // Load table definitions
            var allTables = GetTablesToExport(_yamlLoader);
            _logger.LogInformation("Loaded {TableCount} table(s) to export (mode: {TableMode})", allTables.Count, _tableMode);

            // Get company
            var companies = await tallyXmlService.GetCompanyListAsync();
            if (companies.Count == 0)
            {
                _logger.LogWarning("No companies found in Tally. Please open a company in Tally and try again.");
                return;
            }

            // Use the currently open company in Tally directly.
            // In educational mode only one company can be open at a time,
            // so always use whichever is active — don't override with a saved name.
            var company = companies[0];

            if (!string.IsNullOrWhiteSpace(_config.Company) &&
                !company.Name.Equals(_config.Company, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Configured company '{ConfiguredCompany}' does not match the open Tally company '{OpenCompany}'. " +
                    "Using the currently open company. Please open '{ConfiguredCompany}' in Tally if that is intended.",
                    _config.Company, company.Name, _config.Company);
            }

            _config.Company = company.Name;
            _logger.LogInformation("Using company: {CompanyName}", company.Name);

            // Create temporary export directory
            var tempDir = Path.Combine(Path.GetTempPath(), $"tally_export_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            _logger.LogInformation("Exporting {TableCount} tables...", allTables.Count);
            _logger.LogInformation("─────────────────────────────────────────────────");

            // Export all tables
            var exportedFiles = await exporter.ExportMultipleTablesToCsvAsync(allTables, tempDir);

            _logger.LogInformation("─────────────────────────────────────────────────");
            _logger.LogInformation("Exported {FileCount} files", exportedFiles.Count);

            // Upload to backend
            _logger.LogInformation("Uploading to backend ({BackendUrl})...", _backendUrl);
            _logger.LogInformation("─────────────────────────────────────────────────");

            // Get organisation ID from login (saved in ~/.tally_org)
            var organisationId = AuthService.LoadOrganisationId();
            if (!organisationId.HasValue)
            {
                _logger.LogWarning("No organization selected. Please run: dotnet run -- --login");
                return;
            }

            var uploadedCount = await uploadService.UploadMultipleCsvFilesAsync(
                exportedFiles, 
                (int)organisationId.Value);

            _logger.LogInformation("─────────────────────────────────────────────────");
            _logger.LogInformation("Uploaded {UploadedCount}/{TotalCount} files successfully", uploadedCount, exportedFiles.Count);

            // Cleanup temporary directory
            try
            {
                Directory.Delete(tempDir, true);
                _logger.LogInformation("Cleaned up temporary files");
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to cleanup temporary files");
            }

            _logger.LogInformation("Sync completed successfully at {Timestamp}", DateTime.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
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
}
