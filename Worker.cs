using Microsoft.Extensions.Options;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ITallyService _tallyService;
    private readonly IBackendService _backendService;
    private readonly IConfigurationService _configService;
    private readonly ISyncEngine _syncEngine;
    private readonly TallySyncOptions _options;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public Worker(
        ILogger<Worker> logger,
        ITallyService tallyService,
        IBackendService backendService,
        IConfigurationService configService,
        ISyncEngine syncEngine,
        IOptions<TallySyncOptions> options)
    {
        _logger = logger;
        _tallyService = tallyService;
        _backendService = backendService;
        _configService = configService;
        _syncEngine = syncEngine;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tally Sync Service started at: {Time}", DateTimeOffset.Now);

        // Check if service is configured
        var config = await _configService.LoadConfigurationAsync();
        if (!config.IsConfigured || config.SelectedTables.Count == 0)
        {
            _logger.LogWarning("Service is not configured. Please run the service with --setup to configure tables.");
            _logger.LogWarning("Service will check for configuration every 5 minutes...");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                config = await _configService.LoadConfigurationAsync();
                
                if (config.IsConfigured && config.SelectedTables.Count > 0)
                {
                    _logger.LogInformation("Configuration detected. Starting sync process...");
                    break;
                }
            }
        }

        // Main sync loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSyncAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync cycle");
            }

            // Wait for the configured interval
            var delay = TimeSpan.FromMinutes(_options.SyncIntervalMinutes);
            _logger.LogInformation("Next sync in {Minutes} minutes", _options.SyncIntervalMinutes);
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task PerformSyncAsync(CancellationToken stoppingToken)
    {
        // Ensure only one sync runs at a time
        if (!await _syncLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogWarning("Previous sync still in progress. Skipping this cycle.");
            return;
        }

        try
        {
            _logger.LogInformation("=================================================");
            _logger.LogInformation("Starting sync cycle at: {Time}", DateTimeOffset.Now);
            _logger.LogInformation("=================================================");

            // Check Tally connection
            if (!await _tallyService.CheckConnectionAsync())
            {
                _logger.LogError("Cannot connect to Tally. Ensure Tally is running on {Url}", _options.TallyUrl);
                return;
            }

            // Check Backend connection
            if (!await _backendService.CheckConnectionAsync())
            {
                _logger.LogWarning("Cannot connect to backend. Will retry next cycle.");
                return;
            }

            // Load configuration
            var config = await _configService.LoadConfigurationAsync();
            
            if (config.SelectedTables.Count == 0)
            {
                _logger.LogWarning("No tables configured for sync");
                return;
            }

            // Sync each table
            int successCount = 0;
            int failureCount = 0;

            foreach (var tableName in config.SelectedTables)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                try
                {
                    // Get or create table state
                    if (!config.TableStates.ContainsKey(tableName))
                        config.TableStates[tableName] = new TableSyncState { TableName = tableName };
                    
                    var state = config.TableStates[tableName];

                    _logger.LogInformation("───────────────────────────────────────────────");
                    _logger.LogInformation("Syncing table: {TableName} (Mode: {Mode})", 
                        tableName, 
                        state.InitialSyncComplete ? "INCREMENTAL" : "INITIAL");
                    _logger.LogInformation("───────────────────────────────────────────────");

                    // Use the sync engine
                    var success = await _syncEngine.SyncTableAsync(tableName, state, stoppingToken);

                    if (success)
                    {
                        successCount++;
                        state.LastError = null;
                        state.LastErrorTime = null;
                        _logger.LogInformation("✓ Successfully synced table: {TableName}", tableName);
                    }
                    else
                    {
                        failureCount++;
                        state.LastError = "Sync failed";
                        state.LastErrorTime = DateTime.UtcNow;
                        _logger.LogError("✗ Failed to sync table: {TableName}", tableName);
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogError(ex, "Error syncing table: {TableName}", tableName);
                    
                    if (!config.TableStates.ContainsKey(tableName))
                        config.TableStates[tableName] = new TableSyncState { TableName = tableName };
                    
                    config.TableStates[tableName].LastError = ex.Message;
                    config.TableStates[tableName].LastErrorTime = DateTime.UtcNow;
                }
            }

            // Save updated configuration
            await _configService.SaveConfigurationAsync(config);

            _logger.LogInformation("=================================================");
            _logger.LogInformation("Sync cycle completed at: {Time}", DateTimeOffset.Now);
            _logger.LogInformation("Summary - Success: {Success}, Failures: {Failures}", 
                successCount, failureCount);
            _logger.LogInformation("=================================================");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tally Sync Service is stopping...");
        
        // Wait for current sync to complete (with timeout)
        await _syncLock.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
        
        await base.StopAsync(stoppingToken);
        _logger.LogInformation("Tally Sync Service stopped");
    }
}