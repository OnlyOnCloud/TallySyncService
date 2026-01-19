using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface IBackendService
{
    Task<bool> SendDataAsync(SyncPayload payload);
    Task<bool> CheckConnectionAsync();
}

public class BackendService : IBackendService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackendService> _logger;
    private readonly TallySyncOptions _options;

    public BackendService(
        IHttpClientFactory httpClientFactory, 
        ILogger<BackendService> logger,
        IOptions<TallySyncOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            
            _logger.LogInformation("Checking backend connection at: {Url}{Endpoint}", 
                _options.BackendUrl, _options.BackendHealthEndpoint);
            
            var response = await client.GetAsync(_options.BackendHealthEndpoint);
            
            var isConnected = response.IsSuccessStatusCode;
            _logger.LogInformation("Backend connection check: {Status} (StatusCode: {StatusCode})", 
                isConnected ? "Success" : "Failed", response.StatusCode);
            
            if (!isConnected)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Health check failed. Response: {Response}", errorContent);
            }
            
            return isConnected;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error checking backend connection at {Url}", _options.BackendUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking backend connection");
            return false;
        }
    }

    public async Task<bool> SendDataAsync(SyncPayload payload)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            
            _logger.LogInformation(
                "Sending data to backend: {Url}{Endpoint} | Table: {TableName} | Records: {RecordCount} | Chunk: {Chunk}/{Total} | Mode: {Mode}", 
                _options.BackendUrl, 
                _options.BackendSyncEndpoint,
                payload.TableName, 
                payload.Records.Count,
                payload.ChunkNumber,
                payload.TotalChunks,
                payload.SyncMode);

            var response = await client.PostAsJsonAsync(_options.BackendSyncEndpoint, payload);
            
            _logger.LogInformation("Backend response status: {StatusCode}", response.StatusCode);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Successfully sent chunk {Chunk}/{Total} for table: {TableName}. Response: {Response}", 
                    payload.ChunkNumber, payload.TotalChunks, payload.TableName, responseContent);
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send data for table: {TableName}. Status: {Status}, Error: {Error}", 
                    payload.TableName, response.StatusCode, errorContent);
                return false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error sending data to backend for table: {TableName}. URL: {Url}", 
                payload.TableName, _options.BackendUrl);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to backend for table: {TableName}", payload.TableName);
            return false;
        }
    }
}