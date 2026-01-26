using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class YamlConfigLoader
{
    private YamlConfig? _config;
    private readonly string _yamlPath;

    public YamlConfigLoader(string yamlPath)
    {
        _yamlPath = yamlPath;
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_yamlPath))
        {
            throw new FileNotFoundException($"YAML configuration file not found: {_yamlPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(_yamlPath);
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        _config = deserializer.Deserialize<YamlConfig>(yamlContent);
    }

    public List<TableDefinition> GetMasterTables()
    {
        if (_config == null)
            throw new InvalidOperationException("Configuration not loaded. Call LoadAsync() first.");
        
        return _config.Master;
    }

    public List<TableDefinition> GetTransactionTables()
    {
        if (_config == null)
            throw new InvalidOperationException("Configuration not loaded. Call LoadAsync() first.");
        
        return _config.Transaction;
    }

    public List<TableDefinition> GetAllTables()
    {
        var all = new List<TableDefinition>();
        all.AddRange(GetMasterTables());
        all.AddRange(GetTransactionTables());
        return all;
    }

    public TableDefinition? GetTableByName(string tableName)
    {
        return GetAllTables().FirstOrDefault(t => 
            t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    }
}
