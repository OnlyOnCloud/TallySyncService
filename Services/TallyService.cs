using System.Xml.Linq;
using TallySyncService.Models;

namespace TallySyncService.Services;

public interface ITallyService
{
    Task<bool> CheckConnectionAsync();
    Task<List<TallyTable>> GetAvailableTablesAsync();
    Task<string> FetchTableDataAsync(string tableName);
}

public class TallyService : ITallyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TallyService> _logger;

    public TallyService(IHttpClientFactory httpClientFactory, ILogger<TallyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            // Simple ping request to Tally
            var pingXml = @"<ENVELOPE>
                            <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST></HEADER>
                            <BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES></DESC></BODY>
                          </ENVELOPE>";

            var content = new StringContent(pingXml, System.Text.Encoding.UTF8, "text/xml");
            var response = await client.PostAsync("", content);
            
            var isConnected = response.IsSuccessStatusCode;
            _logger.LogInformation("Tally connection check: {Status}", isConnected ? "Success" : "Failed");
            
            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Tally connection");
            return false;
        }
    }

    public Task<List<TallyTable>> GetAvailableTablesAsync()
    {
        var tables = new List<TallyTable>
        {
            new() { Name = "Ledgers", Description = "Chart of Accounts - Ledgers", CollectionType = "Ledger" },
            new() { Name = "Groups", Description = "Ledger Groups", CollectionType = "Group" },
            new() { Name = "Vouchers", Description = "All Vouchers/Transactions", CollectionType = "Voucher" },
            new() { Name = "StockItems", Description = "Inventory Items", CollectionType = "StockItem" },
            new() { Name = "StockGroups", Description = "Stock Groups", CollectionType = "StockGroup" },
            new() { Name = "Units", Description = "Units of Measure", CollectionType = "Unit" },
            new() { Name = "CostCentres", Description = "Cost Centers", CollectionType = "CostCentre" },
            new() { Name = "Godowns", Description = "Warehouses/Godowns", CollectionType = "Godown" },
            new() { Name = "Currencies", Description = "Currency Masters", CollectionType = "Currency" },
            new() { Name = "VoucherTypes", Description = "Voucher Type Masters", CollectionType = "VoucherType" }
        };

        _logger.LogInformation("Retrieved {Count} available tables from Tally", tables.Count);
        return Task.FromResult(tables);
    }

    public async Task<string> FetchTableDataAsync(string tableName)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            var collectionType = GetCollectionType(tableName);
            var xmlPayload = $@"<ENVELOPE>
                                <HEADER>
                                    <VERSION>1</VERSION>
                                    <TALLYREQUEST>Export</TALLYREQUEST>
                                    <TYPE>Collection</TYPE>
                                    <ID>List of {collectionType}</ID>
                                </HEADER>
                                <BODY>
                                    <DESC>
                                        <STATICVARIABLES>
                                            <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                        </STATICVARIABLES>
                                    </DESC>
                                </BODY>
                              </ENVELOPE>";

            var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
            var response = await client.PostAsync("", content);
            
            response.EnsureSuccessStatusCode();
            
            var xmlData = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Successfully fetched data for table: {TableName}, Size: {Size} bytes", 
                tableName, xmlData.Length);
            
            return xmlData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching data for table: {TableName}", tableName);
            throw;
        }
    }

    private string GetCollectionType(string tableName)
    {
        return tableName switch
        {
            "Ledgers" => "Ledger",
            "Groups" => "Group",
            "Vouchers" => "Voucher",
            "StockItems" => "StockItem",
            "StockGroups" => "StockGroup",
            "Units" => "Unit",
            "CostCentres" => "CostCentre",
            "Godowns" => "Godown",
            "Currencies" => "Currency",
            "VoucherTypes" => "VoucherType",
            _ => tableName
        };
    }
}