namespace TallySyncService.Models;

public class YamlConfig
{
    public List<TableDefinition> Master { get; set; } = new();
    public List<TableDefinition> Transaction { get; set; } = new();
}
