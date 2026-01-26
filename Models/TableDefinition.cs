namespace TallySyncService.Models;

public class TableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
    public List<FieldDefinition> Fields { get; set; } = new();
    public List<string>? Filters { get; set; }
    public List<string>? Fetch { get; set; }
    public List<CascadeOperation>? CascadeDelete { get; set; }
    public List<CascadeOperation>? CascadeUpdate { get; set; }
}

public class CascadeOperation
{
    public string Table { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
}
