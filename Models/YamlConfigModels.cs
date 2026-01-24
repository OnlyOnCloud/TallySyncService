namespace TallySyncService.Models;

/// <summary>
/// Root YAML configuration for Tally export
/// </summary>
public class TallyExportConfig
{
    public List<TableConfig>? Master { get; set; } = new();
    public List<TableConfig>? Transaction { get; set; } = new();
}

/// <summary>
/// Table configuration from YAML
/// </summary>
public class TableConfig
{
    public string Name { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty; // Primary, Derived
    public List<FieldConfig>? Fields { get; set; } = new();
    public List<string>? Fetch { get; set; } = new();
    public List<string>? Filters { get; set; } = new();
    public List<CascadeOperation>? CascadeDelete { get; set; } = new();
    public List<CascadeOperation>? CascadeUpdate { get; set; } = new();
}

/// <summary>
/// Field configuration from YAML
/// </summary>
public class FieldConfig
{
    public string Name { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // text, date, number, logical, amount, quantity, rate
}

/// <summary>
/// Cascade operation configuration (for derived tables)
/// </summary>
public class CascadeOperation
{
    public string Table { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
}
