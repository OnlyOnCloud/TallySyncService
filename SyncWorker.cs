using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using TallySyncService.Services;

namespace TallySyncService
{
    public class SyncWorker : BackgroundService
    {
        private readonly ITallySyncService _syncService;
        private readonly TallySyncService.Services.ILogger _logger;
        private readonly int _intervalMinutes;

        public SyncWorker(ITallySyncService syncService, TallySyncService.Services.ILogger logger, int intervalMinutes = 0)
        {
            _syncService = syncService;
            _logger = logger;
            _intervalMinutes = intervalMinutes;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogMessage("Sync Worker Started");
            _logger.LogMessage("Interval: {0} minutes", _intervalMinutes > 0 ? _intervalMinutes : "One-time");

            // Run immediately on start
            await PerformSync();

            // If interval is set, run periodically
            if (_intervalMinutes > 0)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_intervalMinutes * 60 * 1000, stoppingToken);
                    
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await PerformSync();
                    }
                }
            }
            else
            {
                // For one-time sync, wait a bit then stop the service
                _logger.LogMessage("One-time sync completed. Stopping service...");
                await Task.Delay(2000, stoppingToken);
            }
        }

        private async Task PerformSync()
        {
            try
            {
                _logger.LogMessage("Starting sync at {0}", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                int rowsExported = await _syncService.PerformFullSyncAsync();
                _logger.LogMessage("Sync completed. Total rows: {0}", rowsExported);
            }
            catch (Exception ex)
            {
                _logger.LogError("SyncWorker.PerformSync()", ex);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogMessage("Sync Worker Stopping");
            await base.StopAsync(cancellationToken);
        }
    }
}
