namespace TallySyncService.Models;

public class SyncConfiguration
{
    public List<string> SelectedTables { get; set; } = new();
    public Dictionary<string, TableSyncState> TableStates { get; set; } = new();
    public DateTime? LastConfigUpdate { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsInitialSyncComplete { get; set; }
}

public class TableSyncState
{
    public string TableName { get; set; } = string.Empty;
    public DateTime? LastSyncTime { get; set; }
    public int TotalRecordsSynced { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
    public bool InitialSyncComplete { get; set; }
    public string? LastSyncFromDate { get; set; }
    public string? LastSyncToDate { get; set; }
    public Dictionary<string, string> RecordHashes { get; set; } = new(); // GUID -> Hash for change detection
}

public class TallyTable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CollectionType { get; set; } = string.Empty;
}

public class SyncPayload
{
    public string TableName { get; set; } = string.Empty;
    public List<SyncRecord> Records { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string? SourceIdentifier { get; set; }
    public int TotalRecords { get; set; }
    public int ChunkNumber { get; set; }
    public int TotalChunks { get; set; }
    public string SyncMode { get; set; } = "FULL"; // FULL, INCREMENTAL
}

public class SyncRecord
{
    public string Id { get; set; } = string.Empty; // Tally GUID or unique identifier
    public string Operation { get; set; } = "INSERT"; // INSERT, UPDATE, DELETE
    public object Data { get; set; } = new(); // The actual record data as JSON
    public string Hash { get; set; } = string.Empty; // For change detection
    public DateTime? ModifiedDate { get; set; }
}

public class TallySyncOptions
{
    public string TallyUrl { get; set; } = "http://localhost:9000";
    public string BackendUrl { get; set; } = string.Empty;
    public string BackendSyncEndpoint { get; set; } = "/data";
    public string BackendHealthEndpoint { get; set; } = "/health";
    public int SyncIntervalMinutes { get; set; } = 15;
    public int TallyTimeoutSeconds { get; set; } = 30;
    public int BackendTimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 3;
    public string? DataDirectory { get; set; }
    public int ChunkSize { get; set; } = 100; // Records per chunk
    public int InitialSyncDaysBack { get; set; } = 365; // For initial sync, how far back to go
}