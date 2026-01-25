using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TallySyncService.Models;

namespace TallySyncService.Services
{
    public interface ICsvExporter
    {
        Task<string> ExportTableAsync(string tableName, TableDefinition tableConfig, 
            Dictionary<string, string> substitutions);
        string ConvertTdlOutput(string xmlOutput);
        string GenerateXmlFromDefinition(TableDefinition tableConfig, 
            Dictionary<string, string> substitutions);
    }

    public class CsvExporter : ICsvExporter
    {
        private readonly ITallyService _tallyService;
        private readonly ILogger _logger;
        private readonly string _csvDirectory;

        public CsvExporter(ITallyService tallyService, ILogger logger, string csvDir = "./csv")
        {
            _tallyService = tallyService;
            _logger = logger;
            _csvDirectory = csvDir;
        }

        public async Task<string> ExportTableAsync(string tableName, TableDefinition tableConfig,
            Dictionary<string, string> substitutions)
        {
            try
            {
                string xml = GenerateXmlFromDefinition(tableConfig, substitutions);
                string output = await _tallyService.PostTallyXmlAsync(xml);
                output = ConvertTdlOutput(output);

                string columnHeaders = string.Join("\t", tableConfig.Fields.Select(f => f.Name));
                string csvContent = columnHeaders + output;

                // Ensure directory exists
                Directory.CreateDirectory(_csvDirectory);

                string filePath = Path.Combine(_csvDirectory, $"{tableName}.csv");
                File.WriteAllText(filePath, csvContent, Encoding.UTF8);

                _logger.LogMessage("  Exported table: {0}", tableName);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"CsvExporter.ExportTableAsync({tableName})", ex);
                throw;
            }
        }

        public string ConvertTdlOutput(string xmlOutput)
        {
            try
            {
                string result = xmlOutput;
                result = result.Replace("<ENVELOPE>", "");
                result = result.Replace("</ENVELOPE>", "");
                result = Regex.Replace(result, @"\<FLDBLANK\>\<\/FLDBLANK\>", "");
                result = Regex.Replace(result, @"\s+\r\n", "");
                result = result.Replace("\r\n", "");
                result = result.Replace("\t", " ");
                result = Regex.Replace(result, @"\s+\<F", "<F");
                result = Regex.Replace(result, @"\<\/F\d+\>", "");
                result = result.Replace("<F01>", "\r\n");
                result = Regex.Replace(result, @"\<F\d+\>", "\t");
                result = result.Replace("&amp;", "&");
                result = result.Replace("&lt;", "<");
                result = result.Replace("&gt;", ">");
                result = result.Replace("&quot;", "\"");
                result = result.Replace("&apos;", "'");
                result = result.Replace("&tab;", "");
                result = Regex.Replace(result, @"&#\d+;", "");

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("CsvExporter.ConvertTdlOutput()", ex);
                throw;
            }
        }

        public string GenerateXmlFromDefinition(TableDefinition tableConfig,
            Dictionary<string, string> substitutions)
        {
            try
            {
                string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Data</TYPE><ID>TallyDatabaseLoaderReport</ID></HEADER><BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>XML (Data Interchange)</SVEXPORTFORMAT><SVFROMDATE>{fromDate}</SVFROMDATE><SVTODATE>{toDate}</SVTODATE><SVCURRENTCOMPANY>{targetCompany}</SVCURRENTCOMPANY></STATICVARIABLES><TDL><TDLMESSAGE><REPORT NAME=\"TallyDatabaseLoaderReport\"><FORMS>MyForm</FORMS></REPORT><FORM NAME=\"MyForm\"><PARTS>MyPart01</PARTS></FORM>";

                // Extract collection path
                string[] routes = tableConfig.Collection.Split('.');
                string targetCollection = routes[0];
                List<string> lstRoutes = new List<string>(routes.Skip(1));
                lstRoutes.Insert(0, "MyCollection");

                // Append PART XML
                for (int i = 0; i < lstRoutes.Count; i++)
                {
                    string xmlPart = FormatString(i + 1, "MyPart00");
                    string xmlLine = FormatString(i + 1, "MyLine00");
                    xml += $"<PART NAME=\"{xmlPart}\"><LINES>{xmlLine}</LINES><REPEAT>{xmlLine} : {lstRoutes[i]}</REPEAT><SCROLLED>Vertical</SCROLLED></PART>";
                }

                // Append LINE XML
                for (int i = 0; i < lstRoutes.Count - 1; i++)
                {
                    string xmlLine = FormatString(i + 1, "MyLine00");
                    string xmlPart = FormatString(i + 2, "MyPart00");
                    xml += $"<LINE NAME=\"{xmlLine}\"><FIELDS>FldBlank</FIELDS><EXPLODE>{xmlPart}</EXPLODE></LINE>";
                }

                xml += $"<LINE NAME=\"{FormatString(lstRoutes.Count, "MyLine00")}\">";
                xml += "<FIELDS>";

                // Append field declaration
                for (int i = 0; i < tableConfig.Fields.Count; i++)
                    xml += FormatString(i + 1, "Fld00") + ",";
                
                xml = xml.Substring(0, xml.Length - 1); // Remove last comma
                xml += "</FIELDS></LINE>";

                // Append fields
                for (int i = 0; i < tableConfig.Fields.Count; i++)
                {
                    string fieldXml = $"<FIELD NAME=\"{FormatString(i + 1, "Fld00")}\">";
                    Field field = tableConfig.Fields[i];

                    if (Regex.IsMatch(field.FieldName, @"^(\.\.)?[a-zA-Z0-9_]+$"))
                    {
                        switch (field.Type)
                        {
                            case "text":
                                fieldXml += $"<SET>${field.FieldName}</SET>";
                                break;
                            case "logical":
                                fieldXml += $"<SET>if ${field.FieldName} then 1 else 0</SET>";
                                break;
                            case "date":
                                fieldXml += $"<SET>if $$IsEmpty:${field.FieldName} then $$StrByCharCode:241 else $$PyrlYYYYMMDDFormat:${field.FieldName}:\"-\"</SET>";
                                break;
                            case "number":
                                fieldXml += $"<SET>if $$IsEmpty:${field.FieldName} then \"0\" else $$String:${field.FieldName}</SET>";
                                break;
                            case "amount":
                                fieldXml += $"<SET>$$StringFindAndReplace:(if $$IsDebit:${field.FieldName} then -$$NumValue:${field.FieldName} else $$NumValue:${field.FieldName}):\"(-)\": \"-\"</SET>";
                                break;
                            case "quantity":
                                fieldXml += $"<SET>$$StringFindAndReplace:(if $$IsInwards:${field.FieldName} then $$Number:$$String:${field.FieldName}:\"TailUnits\" else -$$Number:$$String:${field.FieldName}:\"TailUnits\"):\"(-)\": \"-\"</SET>";
                                break;
                            case "rate":
                                fieldXml += $"<SET>if $$IsEmpty:${field.FieldName} then 0 else $$Number:${field.FieldName}</SET>";
                                break;
                            default:
                                fieldXml += $"<SET>${field.FieldName}</SET>";
                                break;
                        }
                    }
                    else
                    {
                        fieldXml += $"<SET>{field.FieldName}</SET>";
                    }

                    fieldXml += $"<XMLTAG>{FormatString(i + 1, "F00")}</XMLTAG></FIELD>";
                    xml += fieldXml;
                }

                xml += "<FIELD NAME=\"FldBlank\"><SET>\"\"</SET></FIELD>";
                xml += $"<COLLECTION NAME=\"MyCollection\"><TYPE>{targetCollection}</TYPE>";

                // Fetch list
                if (tableConfig.Fetch != null && tableConfig.Fetch.Count > 0)
                    xml += $"<FETCH>{string.Join(",", tableConfig.Fetch)}</FETCH>";

                // Filters
                if (tableConfig.Filters != null && tableConfig.Filters.Count > 0)
                {
                    xml += "<FILTER>";
                    for (int i = 0; i < tableConfig.Filters.Count; i++)
                        xml += FormatString(i + 1, "Fltr00") + ",";
                    xml = xml.Substring(0, xml.Length - 1); // Remove last comma
                    xml += "</FILTER>";
                }

                xml += "</COLLECTION>";

                // Add filter definitions
                if (tableConfig.Filters != null && tableConfig.Filters.Count > 0)
                {
                    for (int i = 0; i < tableConfig.Filters.Count; i++)
                        xml += $"<SYSTEM TYPE=\"Formulae\" NAME=\"{FormatString(i + 1, "Fltr00")}\">{tableConfig.Filters[i]}</SYSTEM>";
                }

                xml += "</TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>";

                // Apply substitutions
                if (substitutions != null)
                {
                    foreach (var kvp in substitutions)
                    {
                        xml = xml.Replace($"{{{kvp.Key}}}", kvp.Value);
                    }
                }

                return xml;
            }
            catch (Exception ex)
            {
                _logger.LogError("CsvExporter.GenerateXmlFromDefinition()", ex);
                throw;
            }
        }

        private string FormatString(int value, string mask)
        {
            if (mask == "MyPart00")
                return "MyPart" + value.ToString("D2");
            else if (mask == "MyLine00")
                return "MyLine" + value.ToString("D2");
            else if (mask == "Fld00")
                return "Fld" + value.ToString("D2");
            else if (mask == "F00")
                return "F" + value.ToString("D2");
            else if (mask == "Fltr00")
                return "Fltr" + value.ToString("D2");
            return value.ToString();
        }
    }
}
