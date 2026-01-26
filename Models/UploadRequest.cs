namespace TallySyncService.Models;

public class UploadRequest
{
    public string TableName { get; set; } = string.Empty;
    public int OrganisationId { get; set; }
    public string CsvData { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public long FileSize { get; set; }
    public DateTime ExportedAt { get; set; }
}
