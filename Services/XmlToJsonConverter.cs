using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TallySyncService.Models;
using System.Text.RegularExpressions; 
namespace TallySyncService.Services;

public interface IXmlToJsonConverter
{
    List<SyncRecord> ConvertTallyXmlToRecords(string xmlData, string tableName);
    string ComputeHash(object data);
}

public class XmlToJsonConverter : IXmlToJsonConverter
{
    private readonly ILogger<XmlToJsonConverter> _logger;

    public XmlToJsonConverter(ILogger<XmlToJsonConverter> logger)
    {
        _logger = logger;
    }

    public List<SyncRecord> ConvertTallyXmlToRecords(string xmlData, string tableName)
    {
        try
        {
            var records = new List<SyncRecord>();
            
            // Remove invalid XML entity references - FIXED PATTERN
            string entityPattern = @"&#x(0[0-8BCEF]|1[0-9A-F]);|&#([0-9]|1[0-2]|1[4-9]|2[0-9]|3[0-1]);";
            xmlData = Regex.Replace(xmlData, entityPattern, " ", RegexOptions.IgnoreCase);
            
            // Clean invalid XML characters
            xmlData = CleanInvalidXmlChars(xmlData);
            
            var doc = XDocument.Parse(xmlData);

            // Extract the collection based on table type
            var collectionElements = tableName switch
            {
                "Ledgers" => doc.Descendants("LEDGER"),
                "Groups" => doc.Descendants("GROUP"),
                "Vouchers" => doc.Descendants("VOUCHER"),
                "StockItems" => doc.Descendants("STOCKITEM"),
                "StockGroups" => doc.Descendants("STOCKGROUP"),
                "Units" => doc.Descendants("UNIT"),
                "CostCentres" => doc.Descendants("COSTCENTRE"),
                "Godowns" => doc.Descendants("GODOWN"),
                "Currencies" => doc.Descendants("CURRENCY"),
                "VoucherTypes" => doc.Descendants("VOUCHERTYPE"),
                _ => Enumerable.Empty<XElement>()
            };

            foreach (var element in collectionElements)
            {
                try
                {
                    // Convert XML element to JSON
                    var json = XmlElementToJson(element);
                    
                    // Extract GUID or create identifier
                    var id = ExtractId(element, tableName);
                    
                    // Extract modified date if available
                    var modifiedDate = ExtractModifiedDate(element);

                    // Compute hash for change detection
                    var hash = ComputeHash(json);

                    records.Add(new SyncRecord
                    {
                        Id = id,
                        Data = json,
                        Hash = hash,
                        ModifiedDate = modifiedDate,
                        Operation = "INSERT" // Will be determined later based on comparison
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert element to record in table {TableName}", tableName);
                }
            }

            _logger.LogInformation("Converted {Count} records from XML for table {TableName}", records.Count, tableName);
            return records;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting XML to records for table {TableName}", tableName);
            throw;
        }
    }

    private string CleanInvalidXmlChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove characters that are NOT allowed in XML 1.0
        // Valid XML chars: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
        // We'll remove control characters (0x00-0x08, 0x0B-0x0C, 0x0E-0x1F)
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == 0x9 || c == 0xA || c == 0xD || // Tab, LF, CR
                (c >= 0x20 && c <= 0xD7FF) ||        // Normal printable chars
                (c >= 0xE000 && c <= 0xFFFD))        // Private use area
            {
                sb.Append(c);
            }
            else
            {
                sb.Append(' '); // Replace invalid chars with space
            }
        }
        return sb.ToString();
    }

    private JObject XmlElementToJson(XElement element)
    {
        var json = new JObject();

        // Process attributes
        foreach (var attr in element.Attributes())
        {
            json[attr.Name.LocalName] = attr.Value;
        }

        // Process child elements
        foreach (var child in element.Elements())
        {
            var childName = child.Name.LocalName;
            
            // If element has children, recurse
            if (child.HasElements)
            {
                // Check if this is a collection (multiple elements with same name)
                var siblings = element.Elements(child.Name);
                if (siblings.Count() > 1)
                {
                    if (json[childName] == null)
                    {
                        json[childName] = new JArray();
                    }
                    ((JArray)json[childName]!).Add(XmlElementToJson(child));
                }
                else
                {
                    json[childName] = XmlElementToJson(child);
                }
            }
            else
            {
                // Simple value
                var value = child.Value;
                
                // Try to parse as number or boolean
                if (decimal.TryParse(value, out var numValue))
                {
                    json[childName] = numValue;
                }
                else if (bool.TryParse(value, out var boolValue))
                {
                    json[childName] = boolValue;
                }
                else
                {
                    json[childName] = value;
                }
            }
        }

        // If element has value and no children, add it
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            json["_value"] = element.Value;
        }

        return json;
    }

    private string ExtractId(XElement element, string tableName)
    {
        // Try to get GUID first
        var guid = element.Element("GUID")?.Value;
        if (!string.IsNullOrEmpty(guid))
            return guid;

        // Try MASTERID
        var masterId = element.Element("MASTERID")?.Value;
        if (!string.IsNullOrEmpty(masterId))
            return masterId;

        // Try NAME
        var name = element.Element("NAME")?.Value;
        if (!string.IsNullOrEmpty(name))
            return $"{tableName}_{name.Replace(" ", "_")}";

        // Try VOUCHERNUMBER for vouchers
        if (tableName == "Vouchers")
        {
            var voucherNumber = element.Element("VOUCHERNUMBER")?.Value;
            var voucherType = element.Element("VOUCHERTYPENAME")?.Value;
            if (!string.IsNullOrEmpty(voucherNumber))
                return $"VOUCHER_{voucherType}_{voucherNumber}";
        }

        // Fallback: generate from content hash
        var contentHash = ComputeHash(element.ToString());
        return $"{tableName}_{contentHash.Substring(0, 16)}";
    }

    private DateTime? ExtractModifiedDate(XElement element)
    {
        // Try ALTERDATE first
        var alterDate = element.Element("ALTERDATE")?.Value;
        if (!string.IsNullOrEmpty(alterDate) && DateTime.TryParse(alterDate, out var date1))
            return date1;

        // Try DATE
        var dateStr = element.Element("DATE")?.Value;
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var date2))
            return date2;

        return null;
    }

    public string ComputeHash(object data)
    {
        var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        });

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}