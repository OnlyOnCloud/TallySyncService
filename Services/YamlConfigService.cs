using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using TallySyncService.Models;

namespace TallySyncService.Services;

/// <summary>
/// Loads and parses YAML configuration for Tally export
/// </summary>
public interface IYamlConfigService
{
    Task<TallyExportConfig> LoadConfigAsync(string filePath);
    List<TableConfig> GetMasterTables(TallyExportConfig config);
    List<TableConfig> GetTransactionTables(TallyExportConfig config);
}

public class YamlConfigService : IYamlConfigService
{
    private readonly ILogger<YamlConfigService> _logger;

    public YamlConfigService(ILogger<YamlConfigService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Loads YAML configuration from file
    /// </summary>
    public async Task<TallyExportConfig> LoadConfigAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("YAML configuration file not found: {FilePath}", filePath);
                throw new FileNotFoundException($"YAML configuration file not found: {filePath}");
            }

            var yamlContent = await File.ReadAllTextAsync(filePath);
            
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var config = deserializer.Deserialize<TallyExportConfig>(yamlContent);

            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize YAML configuration");
            }

            _logger.LogInformation("Loaded YAML configuration from {FilePath}", filePath);
            _logger.LogInformation("Master tables: {MasterCount}, Transaction tables: {TransactionCount}",
                config.Master?.Count ?? 0, config.Transaction?.Count ?? 0);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading YAML configuration from {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Gets all master tables from configuration
    /// </summary>
    public List<TableConfig> GetMasterTables(TallyExportConfig config)
    {
        return config.Master ?? new List<TableConfig>();
    }

    /// <summary>
    /// Gets all transaction tables from configuration
    /// </summary>
    public List<TableConfig> GetTransactionTables(TallyExportConfig config)
    {
        return config.Transaction ?? new List<TableConfig>();
    }
}
