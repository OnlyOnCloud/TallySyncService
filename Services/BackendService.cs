using System.Net.Http.Json;
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
    private readonly IConfiguration _configuration;

    public BackendService(
        IHttpClientFactory httpClientFactory, 
        ILogger<BackendService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("BackendClient");
            
            // Try to ping a health endpoint
            var response = await client.GetAsync("/health");
            
            var isConnected = response.IsSuccessStatusCode;
            _logger.LogInformation("Backend connection check: {Status}", isConnected ? "Success" : "Failed");
            
            return isConnected;
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
            
            _logger.LogInformation("Sending data to backend. Table: {TableName}, Size: {Size} bytes", 
                payload.TableName, payload.Data.Length);

            var response = await client.PostAsJsonAsync("/sync", payload);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent data for table: {TableName}", payload.TableName);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to backend for table: {TableName}", payload.TableName);
            return false;
        }
    }
}