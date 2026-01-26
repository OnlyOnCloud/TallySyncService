using System.Text;
using System.Text.RegularExpressions;
using TallySyncService.Models;

namespace TallySyncService.Services;

public class TallyDataExporter
{
    private readonly TallyXmlService _tallyXmlService;
    private readonly XmlGenerator _xmlGenerator;
    private readonly TallyConfig _config;

    public TallyDataExporter(
        TallyXmlService tallyXmlService,
        XmlGenerator xmlGenerator,
        TallyConfig config)
    {
        _tallyXmlService = tallyXmlService;
        _xmlGenerator = xmlGenerator;
        _config = config;
    }

    public async Task<string> ExportTableToCsvAsync(TableDefinition table, string outputDirectory)
    {
        Console.WriteLine($"Exporting table: {table.Name}...");
        
        // Prepare substitutions
        var substitutions = new Dictionary<string, string>
        {
            { "fromDate", ParseDate(_config.FromDate) },
            { "toDate", ParseDate(_config.ToDate) },
            { "targetCompany", _config.Company }
        };

        // Generate XML
        var xml = _xmlGenerator.GenerateXmlFromTableDefinition(table, substitutions);

        // Post to Tally
        var response = await _tallyXmlService.PostTallyXmlAsync(xml);

        // Process TDL output
        var processedData = ProcessTdlOutput(response);

        // Add column headers
        var headers = string.Join("\t", table.Fields.Select(f => f.Name));
        var csvContent = headers + processedData;

        // Write to file
        var outputPath = Path.Combine(outputDirectory, $"{table.Name}.csv");
        await ConvertAndWriteCsvAsync(csvContent, outputPath, table.Fields);

        Console.WriteLine($"  ✓ Exported {table.Name} to {outputPath}");
        return outputPath;
    }

    public async Task<List<string>> ExportMultipleTablesToCsvAsync(
        List<TableDefinition> tables, 
        string outputDirectory)
    {
        var exportedFiles = new List<string>();

        // Create output directory if it doesn't exist
        Directory.CreateDirectory(outputDirectory);

        foreach (var table in tables)
        {
            try
            {
                var filePath = await ExportTableToCsvAsync(table, outputDirectory);
                exportedFiles.Add(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Error exporting {table.Name}: {ex.Message}");
            }
        }

        return exportedFiles;
    }

    public string ProcessTdlOutput(string rawXml)
    {
        var result = rawXml;

        try
        {
            // Eliminate ENVELOPE TAG
            result = result.Replace("<ENVELOPE>", "");
            result = result.Replace("</ENVELOPE>", "");

            // Eliminate blank tag
            result = Regex.Replace(result, @"<FLDBLANK></FLDBLANK>", "");

            // Remove empty lines
            result = Regex.Replace(result, @"\s+\r\n", "");

            // Remove all line breaks
            result = result.Replace("\r\n", "").Replace("\n", "");

            // Replace all tabs with a single space
            result = result.Replace("\t", " ");

            // Trim left space
            result = Regex.Replace(result, @"\s+<F", "<F");

            // Remove XML end tags
            result = Regex.Replace(result, @"</F\d+>", "");

            // Append line break to each row start and remove first field XML start tag
            result = Regex.Replace(result, @"<F01>", "\r\n");

            // Replace XML start tags with tab separator
            result = Regex.Replace(result, @"<F\d+>", "\t");

            // Escape special characters
            result = result.Replace("&amp;", "&");
            result = result.Replace("&lt;", "<");
            result = result.Replace("&gt;", ">");
            result = result.Replace("&quot;", "\"");
            result = result.Replace("&apos;", "'");
            result = result.Replace("&tab;", "");

            // Remove all unreadable character escapes
            result = Regex.Replace(result, @"&#\d+;", "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing TDL output: {ex.Message}");
        }

        return result;
    }

    private async Task ConvertAndWriteCsvAsync(
        string tabDelimitedData, 
        string outputPath,
        List<FieldDefinition> fields)
    {
        var lines = tabDelimitedData.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var csvLines = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split('\t');
            var csvValues = new List<string>();

            for (int i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var fieldType = i < fields.Count ? fields[i].Type : "text";

                // Replace special null character
                value = value.Replace("ñ", "");

                // Escape double quotes
                value = value.Replace("\"", "\"\"");

                // Quote text and date fields
                if (fieldType == "text" || fieldType == "date")
                {
                    csvValues.Add($"\"{value}\"");
                }
                else
                {
                    csvValues.Add(value);
                }
            }

            csvLines.Add(string.Join(",", csvValues));
        }

        await File.WriteAllTextAsync(outputPath, string.Join("\r\n", csvLines), Encoding.UTF8);
    }

    private string ParseDate(string dateStr)
    {
        if (dateStr == "auto")
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        if (DateTime.TryParse(dateStr, out var date))
        {
            return date.ToString("yyyy-MM-dd");
        }

        return dateStr;
    }
}
