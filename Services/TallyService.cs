using System.Xml.Linq;
using TallySyncService.Models;

using System.Text.RegularExpressions; 
using System.Security.Cryptography;
using System.Text; 
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; 
namespace TallySyncService.Services;

public interface ITallyService
{
    Task<bool> CheckConnectionAsync();
    Task<List<TallyTable>> GetAvailableTablesAsync();
    Task<string> FetchTableDataAsync(string tableName, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<TallyCompany>> GetCompanyListAsync();
    void SetActiveCompany(string companyName);
    string? GetActiveCompany();
}

public class TallyService : ITallyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TallyService> _logger;
    private string? _activeCompany;

    public TallyService(IHttpClientFactory httpClientFactory, ILogger<TallyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public void SetActiveCompany(string companyName)
    {
        _activeCompany = companyName;
        _logger.LogInformation("Active company set to: {CompanyName}", companyName);
    }

    public string? GetActiveCompany()
    {
        return _activeCompany;
    }

    public async Task<List<TallyCompany>> GetCompanyListAsync()
{
    try
    {
        var client = _httpClientFactory.CreateClient("TallyClient");
        
        var xmlPayload = @"<ENVELOPE>
	<HEADER>
		<VERSION>1</VERSION>
		<TALLYREQUEST>Export</TALLYREQUEST>
		<TYPE>Collection</TYPE>
		<ID>List of Companies</ID>
	</HEADER>
	<BODY>
		<DESC>
			<STATICVARIABLES>
            <SVIsSimpleCompany>No</SVIsSimpleCompany>
            </STATICVARIABLES>
			<TDL>
				<TDLMESSAGE>
					<COLLECTION ISMODIFY='No' ISFIXED='No' ISINITIALIZE='Yes' ISOPTION='No' ISINTERNAL='No' NAME='List of Companies'>
                    
						<TYPE>Company</TYPE>
						<NATIVEMETHOD>Name</NATIVEMETHOD>
					</COLLECTION>
                    <ExportHeader>EmpId:5989</ExportHeader>
				</TDLMESSAGE>
			</TDL>
		</DESC>
	</BODY>
</ENVELOPE>";
    
        var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
        var response = await client.PostAsync("", content);
        
        response.EnsureSuccessStatusCode();
        
        var xmlData = await response.Content.ReadAsStringAsync();
        
        // Remove raw invalid control characters
        xmlData = CleanInvalidXmlChars(xmlData);
        
        // Now parse the cleaned company list
        var doc = XDocument.Parse(xmlData);
        var companies = new List<TallyCompany>();
        
        foreach (var companyElement in doc.Descendants("COMPANY"))
        {
            var name = companyElement.Element("NAME")?.Value;
            var guid = companyElement.Element("GUID")?.Value;
            
            if (!string.IsNullOrEmpty(name))
            {
                companies.Add(new TallyCompany
                {
                    Name = name,
                    GUID = guid ?? ""
                });
            }
        }
        
        _logger.LogInformation("Found {Count} companies in Tally", companies.Count);
        return companies;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error fetching company list from Tally");
        throw;
    }
}
// private string CleanInvalidXmlChars(string text)
// {
//     if (string.IsNullOrEmpty(text)) return text;
    
//     // Remove all control characters except tab, newline, and carriage return
//     var cleaned = new StringBuilder(text.Length);
    
//     foreach (char c in text)
//     {
//         // Valid XML 1.0 characters:
//         // #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
//         if (c == 0x9 || c == 0xA || c == 0xD || 
//             (c >= 0x20 && c <= 0xD7FF) || 
//             (c >= 0xE000 && c <= 0xFFFD))
//         {
//             cleaned.Append(c);
//         }
//         // Skip invalid characters (including 0x04 that's causing the error)
//     }
    
//     return cleaned.ToString();
// }

    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            // Simple ping request to Tally
            var pingXml = @"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>";


            _logger.LogInformation("Attempting to connect to Tally at {Url}...", client.BaseAddress);
            
            var content = new StringContent(pingXml, System.Text.Encoding.UTF8, "text/xml");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.PostAsync("", content, cts.Token);
            
            var isConnected = response.IsSuccessStatusCode;
            if (isConnected)
            {
                _logger.LogInformation("Tally connection successful");
            }
            else
            {
                _logger.LogWarning("Tally connection returned status {StatusCode}: {StatusDescription}", 
                    response.StatusCode, response.ReasonPhrase);
            }
            
            return isConnected;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Tally connection timeout - server at {Url} is not responding. Ensure Tally is running and accessible.", 
                _httpClientFactory.CreateClient("TallyClient").BaseAddress);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Tally connection failed - unable to reach {Url}. Check if Tally server is running.", 
                _httpClientFactory.CreateClient("TallyClient").BaseAddress);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking Tally connection");
            return false;
        }
    }

    public async Task<List<TallyTable>> GetAvailableTablesAsync()
{
    try
    {
        var client = _httpClientFactory.CreateClient("TallyClient");
        
        var xmlPayload = @"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>
  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>List of Accounts</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        </STATICVARIABLES>
      </REQUESTDESC>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>";

        var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
        var response = await client.PostAsync("", content);
        
        response.EnsureSuccessStatusCode();
        
        var xmlData = await response.Content.ReadAsStringAsync();
        
        // Clean invalid XML characters
        xmlData = RemoveInvalidXmlEntities(xmlData);
        xmlData = CleanInvalidXmlChars(xmlData);
        
        var doc = XDocument.Parse(xmlData);
        
        // Define all known Tally master types
        var knownMasterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Accounting
            "LEDGER", "GROUP", "VOUCHERTYPE", "CURRENCY", "COSTCENTRE", "COSTCATEGORY", "BUDGETS",
            
            // Inventory
            "STOCKITEM", "STOCKGROUP", "STOCKCATEGORY", "UNIT", "GODOWN", "BATCHMASTER",
            
            // Payroll
            "EMPLOYEE", "EMPLOYEEGROUP", "ATTENDANCETYPE", "LEAVETYPE", "PAYHEAD", "PAYHEADGROUP",
            
            // GST/Tax
            "GSTRATES", "TDSNATURE", "TCSNATURE",
            
            // Others
            "COMPANY", "SCENARIO", "NARRATION"
        };
        
        // Detect which masters are present in the response
        var foundMasterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var element in doc.Descendants())
        {
            var elementName = element.Name.LocalName.ToUpper();
            if (knownMasterTypes.Contains(elementName))
            {
                foundMasterTypes.Add(elementName);
            }
        }
        
        // Convert to TallyTable objects
        var tables = new List<TallyTable>();
        foreach (var masterType in foundMasterTypes.OrderBy(t => t))
        {
            tables.Add(new TallyTable
            {
                Name = GetPluralName(masterType),
                Description = GetMasterDescription(masterType),
                CollectionType = masterType
            });
        }
        
        // Add Vouchers separately
        tables.Add(new TallyTable
        {
            Name = "Vouchers",
            Description = "All Vouchers/Transactions",
            CollectionType = "Voucher"
        });
        
        var result = tables.OrderBy(t => t.Name).ToList();
        _logger.LogInformation("Dynamically retrieved {Count} unique tables from Tally", result.Count);
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error fetching available tables from Tally, falling back to defaults");
        return GetDefaultTables();
    }
}

private string GetPluralName(string masterType)
{
    return masterType.ToUpper() switch
    {
        "LEDGER" => "Ledgers",
        "GROUP" => "Groups",
        "CURRENCY" => "Currencies",
        "STOCKITEM" => "StockItems",
        "STOCKGROUP" => "StockGroups",
        "STOCKCATEGORY" => "StockCategories",
        "UNIT" => "Units",
        "GODOWN" => "Godowns",
        "BATCHMASTER" => "Batches",
        "COSTCENTRE" => "CostCentres",
        "COSTCATEGORY" => "CostCategories",
        "VOUCHERTYPE" => "VoucherTypes",
        "ATTENDANCETYPE" => "AttendanceTypes",
        "LEAVETYPE" => "LeaveTypes",
        "EMPLOYEEGROUP" => "EmployeeGroups",
        "EMPLOYEE" => "Employees",
        "PAYHEAD" => "PayHeads",
        "PAYHEADGROUP" => "PayHeadGroups",
        "BUDGETS" => "Budgets",
        "GSTRATES" => "GSTRates",
        "TDSNATURE" => "TDSNatures",
        "TCSNATURE" => "TCSNatures",
        "COMPANY" => "Companies",
        "SCENARIO" => "Scenarios",
        "NARRATION" => "Narrations",
        _ => masterType + "s"
    };
}

private string GetMasterDescription(string masterType)
{
    return masterType.ToUpper() switch
    {
        "LEDGER" => "Chart of Accounts - Ledgers",
        "GROUP" => "Ledger Groups",
        "CURRENCY" => "Currency Masters",
        "STOCKITEM" => "Inventory Items",
        "STOCKGROUP" => "Stock Groups",
        "STOCKCATEGORY" => "Stock Categories",
        "UNIT" => "Units of Measure",
        "GODOWN" => "Warehouses/Godowns",
        "BATCHMASTER" => "Batch Masters",
        "COSTCENTRE" => "Cost Centers",
        "COSTCATEGORY" => "Cost Categories",
        "VOUCHERTYPE" => "Voucher Type Masters",
        "ATTENDANCETYPE" => "Attendance Types",
        "LEAVETYPE" => "Leave Types",
        "EMPLOYEEGROUP" => "Employee Groups",
        "EMPLOYEE" => "Employees",
        "PAYHEAD" => "Salary/Pay Heads",
        "PAYHEADGROUP" => "Pay Head Groups",
        "BUDGETS" => "Budget Masters",
        "GSTRATES" => "GST Rate Masters",
        "TDSNATURE" => "TDS Nature of Payment",
        "TCSNATURE" => "TCS Nature of Goods",
        "COMPANY" => "Company Information",
        "SCENARIO" => "Scenarios",
        "NARRATION" => "Standard Narrations",
        _ => masterType + " Masters"
    };
}

private string RemoveInvalidXmlEntities(string text)
{
    if (string.IsNullOrEmpty(text)) return text;
    
    // Remove hexadecimal entities (&#x00; to &#x1F; except 09, 0A, 0D)
    text = Regex.Replace(text, @"&#x0*([0-8BCEF]|1[0-9A-F]);", " ", RegexOptions.IgnoreCase);
    
    // Remove decimal entities (&#0; to &#31; except 9, 10, 13)
    text = Regex.Replace(text, @"&#0*([0-8]|1[0-24-9]|2[0-9]|3[01]);", " ", RegexOptions.IgnoreCase);
    
    return text;
}

private string CleanInvalidXmlChars(string text)
{
    if (string.IsNullOrEmpty(text)) return text;
    
    var cleaned = new StringBuilder(text.Length);
    
    foreach (char c in text)
    {
        if (c == 0x9 || c == 0xA || c == 0xD || 
            (c >= 0x20 && c <= 0xD7FF) || 
            (c >= 0xE000 && c <= 0xFFFD))
        {
            cleaned.Append(c);
        }
    }
    
    return cleaned.ToString();
}
 

private List<TallyTable> GetDefaultTables()
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

    _logger.LogInformation("Using default tables list with {Count} tables", tables.Count);
    return tables;
}

    public async Task<string> FetchTableDataAsync(string tableName, DateTime? fromDate = null, DateTime? toDate = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("TallyClient");
            
            // Build proper TDL request based on table type with date filters and company
            var xmlPayload = GetTallyXmlRequest(tableName, fromDate, toDate);

            var content = new StringContent(xmlPayload, System.Text.Encoding.UTF8, "text/xml");
            var response = await client.PostAsync("", content);
            
            response.EnsureSuccessStatusCode();
            
            var xmlData = await response.Content.ReadAsStringAsync();
            
            var dateInfo = fromDate.HasValue || toDate.HasValue 
                ? $" (From: {fromDate:yyyy-MM-dd}, To: {toDate:yyyy-MM-dd})" 
                : "";
            
            var companyInfo = !string.IsNullOrEmpty(_activeCompany) ? $" [Company: {_activeCompany}]" : "";
            
            _logger.LogInformation("Successfully fetched data for table: {TableName}{DateInfo}{CompanyInfo}, Size: {Size} bytes", 
                tableName, dateInfo, companyInfo, xmlData.Length);
            
            return xmlData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching data for table: {TableName}", tableName);
            throw;
        }
    }

     private string GetTallyXmlRequest(string tableName, DateTime? fromDate, DateTime? toDate)
{
    var fromDateStr = fromDate?.ToString("yyyyMMdd") ?? "";
    var toDateStr = toDate?.ToString("yyyyMMdd") ?? "";
    
   var dateFilter = "";
if (tableName == "Vouchers" &&
    !string.IsNullOrEmpty(fromDateStr) &&
    !string.IsNullOrEmpty(toDateStr))
{
    dateFilter = $@"<SVFROMDATE>{fromDateStr}</SVFROMDATE>
                   <SVTODATE>{toDateStr}</SVTODATE>";
}


    var companyFilter = "";
    if (!string.IsNullOrEmpty(_activeCompany))
    {
        companyFilter = $"<SVCURRENTCOMPANY>{_activeCompany}</SVCURRENTCOMPANY>";
    }

    return tableName switch
    {
        "Ledgers" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Ledger Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Ledger Collection'>
              <TYPE>Ledger</TYPE>
              <FETCH>NAME,PARENT,GUID,ALTERID,OPENINGBALANCE</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "Groups" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Group Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Group Collection'>
              <TYPE>Group</TYPE>
              <FETCH>NAME,PARENT,PRIMARYGROUP,ISREVENUE,ISDEEMEDPOSITIVE,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "Vouchers" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
          {dateFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Voucher Collection'>
              <TYPE>Voucher</TYPE>
              <FETCH>
                DATE,
                VOUCHERTYPENAME,
                VOUCHERNUMBER,
                NARRATION,
                PARTYLEDGERNAME,
                GUID,
                ALTERID,
                ALLLEDGERENTRIES.LIST
              </FETCH>
              <FILTER>DateRangeFilter</FILTER>
            </COLLECTION>

            <SYSTEM TYPE='Formulae' NAME='DateRangeFilter'>
              $$IsBetween:DATE:##SVFROMDATE:##SVTODATE
            </SYSTEM>

          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "StockItems" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Stock Item Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Stock Item Collection'>
              <TYPE>StockItem</TYPE>
              <FETCH>
                NAME,
                PARENT,
                BASEUNITS,
                OPENINGBALANCE,
                CLOSINGBALANCE,
                OPENINGVALUE,
                CLOSINGVALUE,
                GUID,
                ALTERID
              </FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",

        "StockGroups" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Stock Group Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Stock Group Collection'>
              <TYPE>StockGroup</TYPE>
              <FETCH>NAME,PARENT,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


       "Units" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Unit Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Unit Collection'>
              <TYPE>Unit</TYPE>
              <FETCH>NAME,FORMALNAME,ISSIMPLEUNIT,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "VoucherTypes" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Voucher Type Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Voucher Type Collection'>
              <TYPE>VoucherType</TYPE>
              <FETCH>NAME,PARENT,NUMBERINGMETHOD,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",

        "CostCentres" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Cost Centre Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Cost Centre Collection'>
              <TYPE>CostCentre</TYPE>
              <FETCH>NAME,PARENT,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "Godowns" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Godown Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
          {companyFilter}
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Godown Collection'>
              <TYPE>Godown</TYPE>
              <FETCH>NAME,PARENT,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        "Currencies" => $@"<ENVELOPE>
  <HEADER>
    <TALLYREQUEST>Export Data</TALLYREQUEST>
  </HEADER>

  <BODY>
    <EXPORTDATA>
      <REQUESTDESC>
        <REPORTNAME>Currency Collection</REPORTNAME>
        <STATICVARIABLES>
          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        </STATICVARIABLES>
      </REQUESTDESC>

      <REQUESTDATA>
        <TDL>
          <TDLMESSAGE>
            <COLLECTION NAME='Currency Collection'>
              <TYPE>Currency</TYPE>
              <FETCH>NAME,SYMBOL,GUID,ALTERID</FETCH>
            </COLLECTION>
          </TDLMESSAGE>
        </TDL>
      </REQUESTDATA>
    </EXPORTDATA>
  </BODY>
</ENVELOPE>",


        _ => throw new ArgumentException($"Unknown table name: {tableName}")
    };
}

public async Task<string> TestFetchWithLogging(string tableName)
{
    try
    {
        var xmlData = await FetchTableDataAsync(tableName);
        
        _logger.LogInformation("=== Raw XML Response ===");
        _logger.LogInformation(xmlData);
        
        // Parse and count records
        var doc = XDocument.Parse(xmlData);
        var records = doc.Descendants(GetCollectionType(tableName)).Count();
        
        _logger.LogInformation("Found {Count} records for {TableName}", records, tableName);
        
        return xmlData;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Test fetch failed for {TableName}", tableName);
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