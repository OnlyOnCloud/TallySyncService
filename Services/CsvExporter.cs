using System.Data;
using System.Globalization;
using System.Text;
using CsvHelper;
using TallySyncService.Models;

namespace TallySyncService.Services;

/// <summary>
/// Service for exporting data to CSV format
/// </summary>
public interface ICsvExporter
{
    Task<string> ExportDataToCsvAsync(DataTable dataTable, string outputPath);
    Task<List<string>> ExportMultipleTablesToCsvAsync(Dictionary<string, DataTable> tables, string outputDirectory);
    byte[] GetCsvAsBytes(DataTable dataTable);
}

public class CsvExporter : ICsvExporter
{
    private readonly ILogger<CsvExporter> _logger;

    public CsvExporter(ILogger<CsvExporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Exports a DataTable to CSV file
    /// </summary>
    public async Task<string> ExportDataToCsvAsync(DataTable dataTable, string outputPath)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                // Write headers
                foreach (DataColumn column in dataTable.Columns)
                {
                    csv.WriteField(column.ColumnName);
                }
                await csv.NextRecordAsync();

                // Write data rows
                foreach (DataRow row in dataTable.Rows)
                {
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        var value = row[column];
                        
                        if (value == null || value == DBNull.Value)
                        {
                            csv.WriteField("");
                        }
                        else if (value is DateTime dateValue)
                        {
                            // Format dates consistently
                            csv.WriteField(dateValue.ToString("yyyy-MM-dd"));
                        }
                        else if (value is bool boolValue)
                        {
                            // Convert boolean to 0/1
                            csv.WriteField(boolValue ? "1" : "0");
                        }
                        else
                        {
                            csv.WriteField(value.ToString());
                        }
                    }
                    await csv.NextRecordAsync();
                }
            }

            _logger.LogInformation("CSV file exported: {OutputPath}. Rows: {RowCount}", outputPath, dataTable.Rows.Count);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting DataTable to CSV: {OutputPath}", outputPath);
            throw;
        }
    }

    /// <summary>
    /// Exports multiple DataTables to separate CSV files
    /// </summary>
    public async Task<List<string>> ExportMultipleTablesToCsvAsync(Dictionary<string, DataTable> tables, string outputDirectory)
    {
        var exportedFiles = new List<string>();

        try
        {
            // Ensure output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            foreach (var kvp in tables)
            {
                var tableName = kvp.Key;
                var dataTable = kvp.Value;
                var csvPath = Path.Combine(outputDirectory, $"{tableName}.csv");

                var filePath = await ExportDataToCsvAsync(dataTable, csvPath);
                exportedFiles.Add(filePath);
            }

            _logger.LogInformation("Exported {Count} CSV files to {Directory}", exportedFiles.Count, outputDirectory);
            return exportedFiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting multiple tables to CSV");
            throw;
        }
    }

    /// <summary>
    /// Returns CSV content as byte array (for sending to backend)
    /// </summary>
    public byte[] GetCsvAsBytes(DataTable dataTable)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(true)))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                // Write headers
                foreach (DataColumn column in dataTable.Columns)
                {
                    csv.WriteField(column.ColumnName);
                }
                csv.NextRecord();

                // Write data rows
                foreach (DataRow row in dataTable.Rows)
                {
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        var value = row[column];
                        
                        if (value == null || value == DBNull.Value)
                        {
                            csv.WriteField("");
                        }
                        else if (value is DateTime dateValue)
                        {
                            csv.WriteField(dateValue.ToString("yyyy-MM-dd"));
                        }
                        else if (value is bool boolValue)
                        {
                            csv.WriteField(boolValue ? "1" : "0");
                        }
                        else
                        {
                            csv.WriteField(value.ToString());
                        }
                    }
                    csv.NextRecord();
                }

                writer.Flush();
                return memoryStream.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting DataTable to CSV bytes");
            throw;
        }
    }
}
