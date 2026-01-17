using System.Text.Json;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface IConfigurationService
{
    Task<SyncConfiguration> LoadConfigurationAsync();
    Task SaveConfigurationAsync(SyncConfiguration config);
    string GetDataDirectory();
}

public class ConfigurationService : IConfigurationService
{
    private readonly string _dataDirectory;
    private readonly string _configFilePath;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigurationService(ILogger<ConfigurationService> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        // Determine data directory
        var customDataDir = configuration["TallySync:DataDirectory"];
        
        if (!string.IsNullOrEmpty(customDataDir))
        {
            _dataDirectory = customDataDir;
        }
        else
        {
            // Check if running under Wine
            var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
            var isWine = !string.IsNullOrEmpty(winePrefix) || Directory.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine"));
            
            if (isWine)
            {
                // Use Wine's ProgramData directory
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var winePrefixPath = !string.IsNullOrEmpty(winePrefix) ? winePrefix : Path.Combine(homeDir, ".wine");
                _dataDirectory = Path.Combine(winePrefixPath, "drive_c", "ProgramData", "TallySyncService");
            }
            else if (OperatingSystem.IsWindows())
            {
                _dataDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "TallySyncService");
            }
            else
            {
                // For Linux/Mac without Wine, use user's home directory
                var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _dataDirectory = Path.Combine(homeDir, ".tallysync");
            }
        }

        Directory.CreateDirectory(_dataDirectory);
        _configFilePath = Path.Combine(_dataDirectory, "sync-config.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        _logger.LogInformation("Configuration service initialized. Data directory: {Directory}", _dataDirectory);
    }

    public string GetDataDirectory() => _dataDirectory;

    public async Task<SyncConfiguration> LoadConfigurationAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                _logger.LogInformation("No configuration file found. Creating default configuration.");
                return new SyncConfiguration();
            }

            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<SyncConfiguration>(json, _jsonOptions);
            
            _logger.LogInformation("Configuration loaded successfully. Tables configured: {Count}", 
                config?.SelectedTables.Count ?? 0);
            
            return config ?? new SyncConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration. Using default configuration.");
            return new SyncConfiguration();
        }
    }

    public async Task SaveConfigurationAsync(SyncConfiguration config)
    {
        try
        {
            config.LastConfigUpdate = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
            
            _logger.LogInformation("Configuration saved successfully. Tables: {Count}", 
                config.SelectedTables.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
            throw;
        }
    }
}