namespace TallySyncService.Models;

public class TallyConfig
{
    public string Server { get; set; } = "localhost";
    public int Port { get; set; } = 9000;
    public string Company { get; set; } = string.Empty;
    public string FromDate { get; set; } = "auto";
    public string ToDate { get; set; } = "auto";
    public string DefinitionFile { get; set; } = "tally-export-config.yaml";
}
