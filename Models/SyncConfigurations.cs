namespace TallySyncService.Models;

public class SyncConfiguration
{
    public List<string> SelectedTables { get; set; } = new();
    public Dictionary<string, TableSyncState> TableStates { get; set; } = new();
    public DateTime? LastConfigUpdate { get; set; }
    public bool IsConfigured { get; set; }
}

public class TableSyncState
{
    public string TableName { get; set; } = string.Empty;
    public DateTime? LastSyncTime { get; set; }
    public int TotalRecordsSynced { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
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
    public string Data { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? SourceIdentifier { get; set; }
}

public class TallySyncOptions
{
    public string TallyUrl { get; set; } = "http://localhost:9000";
    public string BackendUrl { get; set; } = string.Empty;
    public int SyncIntervalMinutes { get; set; } = 15;
    public int TallyTimeoutSeconds { get; set; } = 30;
    public int BackendTimeoutSeconds { get; set; } = 60;
    public int MaxRetryAttempts { get; set; } = 3;
    public string? DataDirectory { get; set; }
}