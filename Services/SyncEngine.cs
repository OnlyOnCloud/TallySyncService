
using Microsoft.Extensions.Options;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface ISyncEngine
{
    Task<bool> SyncTableAsync(string tableName, TableSyncState state, CancellationToken cancellationToken);
}

public class SyncEngine : ISyncEngine
{
    private readonly ITallyService _tallyService;
    private readonly IBackendService _backendService;
    private readonly IXmlToJsonConverter _converter;
    private readonly IConfigurationService _configService;
    private readonly ILogger<SyncEngine> _logger;
    private readonly TallySyncOptions _options;

    public SyncEngine(
        ITallyService tallyService,
        IBackendService backendService,
        IXmlToJsonConverter converter,
        IConfigurationService configService,
        ILogger<SyncEngine> logger,
        IOptions<TallySyncOptions> options)
    {
        _tallyService = tallyService;
        _backendService = backendService;
        _converter = converter;
        _configService = configService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> SyncTableAsync(string tableName, TableSyncState state, CancellationToken cancellationToken)
    {
        try
        {
            if (!state.InitialSyncComplete)
            {
                _logger.LogInformation("Starting INITIAL SYNC for table: {TableName}", tableName);
                return await PerformInitialSyncAsync(tableName, state, cancellationToken);
            }
            else
            {
                _logger.LogInformation("Starting INCREMENTAL SYNC for table: {TableName}", tableName);
                return await PerformIncrementalSyncAsync(tableName, state, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing table: {TableName}", tableName);
            return false;
        }
    }

    private async Task<bool> PerformInitialSyncAsync(string tableName, TableSyncState state, CancellationToken cancellationToken)
    {
        try
        {
            // For initial sync, fetch all data going back InitialSyncDaysBack days
            var toDate = DateTime.Now;
            var fromDate = toDate.AddDays(-_options.InitialSyncDaysBack);

            _logger.LogInformation("Initial sync from {From} to {To} for table: {TableName}", 
                fromDate.ToString("yyyy-MM-dd"), toDate.ToString("yyyy-MM-dd"), tableName);

            // Fetch all data
            var xmlData = await _tallyService.FetchTableDataAsync(tableName, fromDate, toDate);

            if (string.IsNullOrEmpty(xmlData))
            {
                _logger.LogWarning("No data returned from Tally for table: {TableName}", tableName);
                state.InitialSyncComplete = true;
                return true;
            }

            // Convert XML to JSON records
            var records = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
            _logger.LogInformation("Converted {Count} records for table: {TableName}", records.Count, tableName);

            if (records.Count == 0)
            {
                _logger.LogInformation("No records found for table: {TableName}", tableName);
                state.InitialSyncComplete = true;
                return true;
            }

            // Store hashes for future change detection
            foreach (var record in records)
            {
                state.RecordHashes[record.Id] = record.Hash;
            }

            // Send in chunks
            var success = await SendRecordsInChunksAsync(tableName, records, "FULL", cancellationToken);

            if (success)
            {
                state.InitialSyncComplete = true;
                state.LastSyncTime = DateTime.UtcNow;
                state.TotalRecordsSynced = records.Count;
                _logger.LogInformation("Initial sync completed for table: {TableName}. Records: {Count}", 
                    tableName, records.Count);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial sync for table: {TableName}", tableName);
            return false;
        }
    }

    private async Task<bool> PerformIncrementalSyncAsync(string tableName, TableSyncState state, CancellationToken cancellationToken)
    {
        try
        {
            // For incremental sync, fetch data since last sync
            var fromDate = state.LastSyncTime?.AddMinutes(-5) ?? DateTime.Now.AddDays(-1); // 5 min overlap for safety
            var toDate = DateTime.Now;

            _logger.LogInformation("Incremental sync from {From} to {To} for table: {TableName}", 
                fromDate.ToString("yyyy-MM-dd HH:mm"), toDate.ToString("yyyy-MM-dd HH:mm"), tableName);

            // Fetch data
            var xmlData = await _tallyService.FetchTableDataAsync(tableName, fromDate, toDate);

            if (string.IsNullOrEmpty(xmlData))
            {
                _logger.LogInformation("No new data for table: {TableName}", tableName);
                state.LastSyncTime = DateTime.UtcNow;
                return true;
            }

            // Convert to records
            var currentRecords = _converter.ConvertTallyXmlToRecords(xmlData, tableName);
            _logger.LogInformation("Fetched {Count} records for incremental sync: {TableName}", 
                currentRecords.Count, tableName);

            if (currentRecords.Count == 0)
            {
                state.LastSyncTime = DateTime.UtcNow;
                return true;
            }

            // Detect changes
            var changedRecords = DetectChanges(currentRecords, state.RecordHashes);
            _logger.LogInformation("Detected changes: {Inserts} inserts, {Updates} updates for table: {TableName}",
                changedRecords.Count(r => r.Operation == "INSERT"),
                changedRecords.Count(r => r.Operation == "UPDATE"),
                tableName);

            // Detect deletions (records that were in previous sync but not in current)
            var deletedRecords = DetectDeletions(currentRecords, state.RecordHashes, tableName);
            if (deletedRecords.Count > 0)
            {
                _logger.LogInformation("Detected {Count} deletions for table: {TableName}", 
                    deletedRecords.Count, tableName);
                changedRecords.AddRange(deletedRecords);
            }

            if (changedRecords.Count == 0)
            {
                _logger.LogInformation("No changes detected for table: {TableName}", tableName);
                state.LastSyncTime = DateTime.UtcNow;
                return true;
            }

            // Update hashes
            foreach (var record in currentRecords)
            {
                state.RecordHashes[record.Id] = record.Hash;
            }

            // Remove deleted record hashes
            foreach (var deletedRecord in deletedRecords)
            {
                state.RecordHashes.Remove(deletedRecord.Id);
            }

            // Send changes
            var success = await SendRecordsInChunksAsync(tableName, changedRecords, "INCREMENTAL", cancellationToken);

            if (success)
            {
                state.LastSyncTime = DateTime.UtcNow;
                state.TotalRecordsSynced += changedRecords.Count;
                _logger.LogInformation("Incremental sync completed for table: {TableName}. Changes: {Count}", 
                    tableName, changedRecords.Count);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during incremental sync for table: {TableName}", tableName);
            return false;
        }
    }

    private List<SyncRecord> DetectChanges(List<SyncRecord> currentRecords, Dictionary<string, string> previousHashes)
    {
        var changedRecords = new List<SyncRecord>();

        foreach (var record in currentRecords)
        {
            if (!previousHashes.TryGetValue(record.Id, out var previousHash))
            {
                // New record
                record.Operation = "INSERT";
                changedRecords.Add(record);
            }
            else if (previousHash != record.Hash)
            {
                // Updated record
                record.Operation = "UPDATE";
                changedRecords.Add(record);
            }
            // else: No change, skip
        }

        return changedRecords;
    }

    private List<SyncRecord> DetectDeletions(List<SyncRecord> currentRecords, Dictionary<string, string> previousHashes, string tableName)
    {
        var deletedRecords = new List<SyncRecord>();
        var currentIds = currentRecords.Select(r => r.Id).ToHashSet();

        // Note: This is a simplified approach. In reality, Tally doesn't easily tell us about deletions.
        // You might need to implement a full table scan periodically or use Tally's audit log.
        // For now, we'll skip deletion detection as it requires more complex logic.

        return deletedRecords;
    }

    private async Task<bool> SendRecordsInChunksAsync(
        string tableName, 
        List<SyncRecord> records, 
        string syncMode,
        CancellationToken cancellationToken)
    {
        var chunks = ChunkRecords(records, _options.ChunkSize);
        var totalChunks = chunks.Count;

        _logger.LogInformation("Sending {Total} records in {Chunks} chunks for table: {TableName}", 
            records.Count, totalChunks, tableName);

        for (int i = 0; i < totalChunks; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Sync cancelled for table: {TableName}", tableName);
                return false;
            }

            var chunk = chunks[i];
            var payload = new SyncPayload
            {
                TableName = tableName,
                Records = chunk,
                Timestamp = DateTime.UtcNow,
                SourceIdentifier = Environment.MachineName,
                TotalRecords = records.Count,
                ChunkNumber = i + 1,
                TotalChunks = totalChunks,
                SyncMode = syncMode
            };

            var success = await _backendService.SendDataAsync(payload);

            if (!success)
            {
                _logger.LogError("Failed to send chunk {Chunk}/{Total} for table: {TableName}", 
                    i + 1, totalChunks, tableName);
                return false;
            }

            // Small delay between chunks to avoid overwhelming backend
            if (i < totalChunks - 1)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return true;
    }

    private List<List<SyncRecord>> ChunkRecords(List<SyncRecord> records, int chunkSize)
    {
        var chunks = new List<List<SyncRecord>>();
        
        for (int i = 0; i < records.Count; i += chunkSize)
        {
            chunks.Add(records.Skip(i).Take(chunkSize).ToList());
        }

        return chunks;
    }
}