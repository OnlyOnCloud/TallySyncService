namespace TallySyncService.Models;

public class CompanyInfo
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BooksFrom { get; set; } = string.Empty;
    public string LastVoucherDate { get; set; } = string.Empty;
    public int LastAlterIdMaster { get; set; }
    public int LastAlterIdTransaction { get; set; }
}
