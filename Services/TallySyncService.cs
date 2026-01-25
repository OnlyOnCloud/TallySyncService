using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TallySyncService.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TallySyncService.Services
{
    public interface ITallySyncService
    {
        Task<int> PerformFullSyncAsync();
        Task<int> PerformIncrementalSyncAsync();
    }

    public class TallySyncServiceImpl : ITallySyncService
    {
        private readonly ITallyService _tallyService;
        private readonly ICsvExporter _csvExporter;
        private readonly ILogger _logger;
        private readonly AppConfiguration _config;
        private readonly string _exportPath;

        private List<TableDefinition> _masterTables;
        private List<TableDefinition> _transactionTables;

        public TallySyncServiceImpl(ITallyService tallyService, ICsvExporter csvExporter, 
            ILogger logger, AppConfiguration config)
        {
            _tallyService = tallyService;
            _csvExporter = csvExporter;
            _logger = logger;
            _config = config;
            _exportPath = config.Sync.ExportPath;
            _masterTables = new List<TableDefinition>();
            _transactionTables = new List<TableDefinition>();
        }

        public async Task<int> PerformFullSyncAsync()
        {
            int totalRowsExported = 0;

            try
            {
                _logger.LogMessage("Tally to CSV Exporter | version: 1.0.0");

                // Load YAML export definition
                if (!LoadTableDefinitions())
                {
                    return 0;
                }

                List<TableDefinition> tablesToExport = new List<TableDefinition>();
                if (_config.Tally.SelectedTables?.Count > 0)
                {
                    tablesToExport.AddRange(_masterTables.Where(t => 
                        _config.Tally.SelectedTables.Contains(t.Name)));
                    tablesToExport.AddRange(_transactionTables.Where(t => 
                        _config.Tally.SelectedTables.Contains(t.Name)));
                }
                else
                {
                    tablesToExport.AddRange(_masterTables);
                    tablesToExport.AddRange(_transactionTables);
                }

                // Create export directory
                Directory.CreateDirectory(_exportPath);

                // Prepare substitutions
                var substitutions = new Dictionary<string, string>
                {
                    { "fromDate", "1900-01-01" },
                    { "toDate", DateTime.Now.ToString("yyyy-MM-dd") },
                    { "targetCompany", string.IsNullOrEmpty(_config.Tally.Company) 
                        ? "##SVCurrentCompany" 
                        : EscapeHtml(_config.Tally.Company) }
                };

                _logger.LogMessage("Generating CSV files from Tally [{0}]", 
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                foreach (var table in tablesToExport)
                {
                    try
                    {
                        var startTime = DateTime.Now;
                        string filePath = await _csvExporter.ExportTableAsync(table.Name, table, substitutions);
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;

                        // Count rows in exported file
                        string[] lines = File.ReadAllLines(filePath);
                        int rowCount = Math.Max(0, lines.Length - 1); // Exclude header

                        totalRowsExported += rowCount;
                        _logger.LogMessage("  {0}: exported {1} rows [{2:F3} sec]", 
                            table.Name, rowCount, elapsed);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error exporting table {table.Name}", ex);
                    }
                }

                _logger.LogMessage("Export completed successfully [{0}]", 
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _logger.LogError("TallySyncService.PerformFullSyncAsync()", ex);
            }

            return totalRowsExported;
        }

        public async Task<int> PerformIncrementalSyncAsync()
        {
            _logger.LogMessage("Incremental sync is not implemented yet. Running full sync instead.");
            return await PerformFullSyncAsync();
        }

        private bool LoadTableDefinitions()
        {
            try
            {
                string definitionFile = "tally-export-config.yaml";
                if (!File.Exists(definitionFile))
                {
                    _logger.LogMessage("Tally export definition file '{0}' not found", definitionFile);
                    return false;
                }

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                string yaml = File.ReadAllText(definitionFile);
                dynamic? config = deserializer.Deserialize<dynamic>(yaml);

                if (config != null)
                {
                    // Parse master tables
                    if (config.ContainsKey("master"))
                    {
                        _masterTables = ParseTableDefinitions(config["master"]);
                    }

                    // Parse transaction tables
                    if (config.ContainsKey("transaction"))
                    {
                        _transactionTables = ParseTableDefinitions(config["transaction"]);
                    }
                }

                _logger.LogMessage("Loaded {0} master tables and {1} transaction tables", 
                    _masterTables.Count, _transactionTables.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("TallySyncService.LoadTableDefinitions()", ex);
                return false;
            }
        }

        private List<TableDefinition> ParseTableDefinitions(dynamic? tableList)
        {
            var result = new List<TableDefinition>();

            if (tableList is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    try
                    {
                        var table = new TableDefinition();

                        if (item is Dictionary<object, object> dict)
                        {
                            table.Name = dict.ContainsKey("name") ? dict["name"]?.ToString() ?? "" : "";
                            table.Collection = dict.ContainsKey("collection") 
                                ? dict["collection"]?.ToString() ?? "" : "";

                            // Parse fields
                            if (dict.ContainsKey("fields") && dict["fields"] is System.Collections.IEnumerable fields)
                            {
                                foreach (var field in fields)
                                {
                                    if (field is Dictionary<object, object> fieldDict)
                                    {
                                        table.Fields.Add(new Field
                                        {
                                            Name = fieldDict.ContainsKey("name") 
                                                ? fieldDict["name"]?.ToString() ?? "" : "",
                                            FieldName = fieldDict.ContainsKey("field") 
                                                ? fieldDict["field"]?.ToString() ?? "" : "",
                                            Type = fieldDict.ContainsKey("type") 
                                                ? fieldDict["type"]?.ToString() ?? "text" : "text"
                                        });
                                    }
                                }
                            }

                            // Parse filters
                            if (dict.ContainsKey("filters") && dict["filters"] is System.Collections.IEnumerable filters)
                            {
                                foreach (var filter in filters)
                                {
                                    if (filter != null)
                                        table.Filters.Add(filter.ToString());
                                }
                            }

                            // Parse fetch
                            if (dict.ContainsKey("fetch") && dict["fetch"] is System.Collections.IEnumerable fetch)
                            {
                                foreach (var f in fetch)
                                {
                                    if (f != null)
                                        table.Fetch.Add(f.ToString());
                                }
                            }

                            if (!string.IsNullOrEmpty(table.Name))
                            {
                                result.Add(table);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("ParseTableDefinitions", ex);
                    }
                }
            }

            return result;
        }

        private string EscapeHtml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
