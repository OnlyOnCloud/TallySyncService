using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class BackendUploadService
{
    private readonly string _backendUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public BackendUploadService(string backendUrl, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _backendUrl = backendUrl;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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

            // Create HTTP client from factory
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            // Create request message for per-request headers
            var request = new HttpRequestMessage(HttpMethod.Post, _backendUrl)
            {
                Content = content
            };

            // Add JWT token to request headers (per-request, not default headers)
            var token = AuthService.LoadToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Add("Authorization", token);
            }

            // Add organization ID header
            var orgId = AuthService.LoadOrganisationId();
            if (orgId.HasValue)
            {
                request.Headers.Add("orgid", orgId.Value.ToString());
            }

            // Send to backend
            var response = await httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Uploaded {TableName} ({RecordCount} records)", tableName, recordCount);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to upload {TableName}: {StatusCode} - {Error}", tableName, response.StatusCode, errorContent);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading {TableName}", tableName);
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
