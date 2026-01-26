using System.Text;
using System.Text.Json;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class BackendUploadService
{
    private readonly HttpClient _httpClient;
    private readonly string _backendUrl;

    public BackendUploadService(string backendUrl)
    {
        _backendUrl = backendUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    public async Task<bool> UploadCsvDataAsync(
        string tableName,
        int organisationId,
        string csvFilePath)
    {
        try
        {
            // Read CSV file
            var csvData = await File.ReadAllTextAsync(csvFilePath);
            var fileInfo = new FileInfo(csvFilePath);
            
            // Count records (lines - 1 for header)
            var recordCount = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

            // Create upload request
            var uploadRequest = new UploadRequest
            {
                TableName = tableName,
                OrganisationId = organisationId,
                CsvData = csvData,
                RecordCount = recordCount,
                FileSize = fileInfo.Length,
                ExportedAt = DateTime.UtcNow
            };

            // Serialize to JSON
            var jsonContent = JsonSerializer.Serialize(uploadRequest);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Send to backend
            var response = await _httpClient.PostAsync(_backendUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  ✓ Uploaded {tableName} ({recordCount} records)");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  ✗ Failed to upload {tableName}: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error uploading {tableName}: {ex.Message}");
            return false;
        }
    }

    public async Task<int> UploadMultipleCsvFilesAsync(
        List<string> csvFiles,
        int organisationId)
    {
        var successCount = 0;

        foreach (var csvFile in csvFiles)
        {
            var tableName = Path.GetFileNameWithoutExtension(csvFile);
            var success = await UploadCsvDataAsync(tableName, organisationId, csvFile);
            
            if (success)
                successCount++;
        }

        return successCount;
    }
}
