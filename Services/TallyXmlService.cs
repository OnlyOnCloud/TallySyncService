using System.Text;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class TallyXmlService
{
    private readonly TallyConfig _config;
    private readonly HttpClient _httpClient;

    public TallyXmlService(TallyConfig config)
    {
        _config = config;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<string> PostTallyXmlAsync(string xmlPayload)
    {
        try
        {
            var content = new StringContent(xmlPayload, Encoding.Unicode, "text/xml");
            var url = $"http://{_config.Server}:{_config.Port}";
            
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            
            // Read response as UTF-16 LE (Unicode)
            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            var responseText = Encoding.Unicode.GetString(responseBytes);
            
            return responseText;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Unable to connect to Tally. Ensure Tally is running and XML port is enabled at {_config.Server}:{_config.Port}", ex);
        }
    }

    public async Task<List<CompanyInfo>> GetCompanyListAsync()
    {
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?><ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Data</TYPE><ID>TallyDatabaseLoaderReport</ID></HEADER><BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>ASCII (Comma Delimited)</SVEXPORTFORMAT></STATICVARIABLES><TDL><TDLMESSAGE><REPORT NAME=""TallyDatabaseLoaderReport""><FORMS>MyForm</FORMS></REPORT><FORM NAME=""MyForm""><PARTS>MyPart</PARTS></FORM><PART NAME=""MyPart""><LINES>MyLine</LINES><REPEAT>MyLine : MyCollection</REPEAT><SCROLLED>Vertical</SCROLLED></PART><LINE NAME=""MyLine""><FIELDS>FldGuid,FldName,FldBooksFrom,FldLastVoucherDate,FldLastAlterIdMaster,FldLastAlterIdTransaction,FldEOL</FIELDS></LINE><FIELD NAME=""FldGuid""><SET>$Guid</SET></FIELD><FIELD NAME=""FldName""><SET>$$StringFindAndReplace:$Name:'""':'""'</SET></FIELD><FIELD NAME=""FldBooksFrom""><SET>(($$YearOfDate:$BooksFrom)*10000)+(($$MonthOfDate:$BooksFrom)*100)+(($$DayOfDate:$BooksFrom)*1)</SET></FIELD><FIELD NAME=""FldLastVoucherDate""><SET>(($$YearOfDate:$LastVoucherDate)*10000)+(($$MonthOfDate:$LastVoucherDate)*100)+(($$DayOfDate:$LastVoucherDate)*1)</SET></FIELD><FIELD NAME=""FldLastAlterIdMaster""><SET>$AltMstId</SET></FIELD><FIELD NAME=""FldLastAlterIdTransaction""><SET>$AltVchId</SET></FIELD><FIELD NAME=""FldEOL""><SET>†</SET></FIELD><COLLECTION NAME=""MyCollection""><TYPE>Company</TYPE></COLLECTION></TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>";

        var response = await PostTallyXmlAsync(xml);
        return ParseCompanyList(response);
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var companies = await GetCompanyListAsync();
            return companies.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private List<CompanyInfo> ParseCompanyList(string csvData)
    {
        var companies = new List<CompanyInfo>();
        
        if (string.IsNullOrWhiteSpace(csvData))
            return companies;

        // Parse CSV format: "guid","name","booksFrom","lastVoucherDate","altMstId","altVchId","†",
        var lines = csvData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.Contains(","))
                continue;

            // Simple CSV parsing - split by comma and remove quotes
            var parts = line.Split(',');
            
            if (parts.Length >= 2)
            {
                var guid = parts[0].Trim('"', ' ');
                var name = parts[1].Trim('"', ' ');
                
                if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(name))
                {
                    companies.Add(new CompanyInfo 
                    { 
                        Guid = guid, 
                        Name = name 
                    });
                }
            }
        }
        
        return companies;
    }
}
