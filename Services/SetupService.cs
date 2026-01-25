using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TallySyncService.Models;

namespace TallySyncService.Services
{
    public interface ISetupService
    {
        Task RunInteractiveSetupAsync();
        Task<List<string>> GetAvailableCompaniesAsync();
        Task<List<string>> GetAvailableTablesAsync(string company);
    }

    public class SetupService : ISetupService
    {
        private readonly ITallyService _tallyService;
        private readonly ILogger _logger;
        private readonly ConfigurationService _configService;

        public SetupService(ITallyService tallyService, ILogger logger, ConfigurationService configService)
        {
            _tallyService = tallyService;
            _logger = logger;
            _configService = configService;
        }

        public async Task RunInteractiveSetupAsync()
        {
            try
            {
                _logger.LogMessage("═══════════════════════════════════════════════════════");
                _logger.LogMessage("     TALLY CSV EXPORTER - SETUP WIZARD");
                _logger.LogMessage("═══════════════════════════════════════════════════════");
                _logger.LogMessage("");

                // Step 1: Get Tally Server Details
                _logger.LogMessage("Step 1: Tally Server Connection");
                _logger.LogMessage("─────────────────────────────────");
                
                Console.Write("Enter Tally Server IP/Hostname [localhost]: ");
                string? server = Console.ReadLine();
                server = string.IsNullOrWhiteSpace(server) ? "localhost" : server;

                Console.Write("Enter Tally Port [9000]: ");
                string? portStr = Console.ReadLine();
                int port = string.IsNullOrWhiteSpace(portStr) || !int.TryParse(portStr, out int p) ? 9000 : p;

                _logger.LogMessage("Testing connection to {0}:{1}...", server, port);

                // Test connection
                var testTallyService = new TallyService(server, port, "", _logger);
                try
                {
                    var (masterId, transactionId) = await testTallyService.GetLastAlterIdsAsync();
                    _logger.LogMessage("✓ Connection successful!");
                }
                catch
                {
                    _logger.LogMessage("✗ Connection failed. Please check server and port.");
                    return;
                }

                _logger.LogMessage("");

                // Step 2: Select Company
                _logger.LogMessage("Step 2: Select Company");
                _logger.LogMessage("─────────────────────");

                var companies = await GetAvailableCompaniesAsync();
                if (companies.Count == 0)
                {
                    _logger.LogMessage("✗ No companies found. Please open a company in Tally first.");
                    return;
                }

                _logger.LogMessage("Available companies:");
                for (int i = 0; i < companies.Count; i++)
                {
                    _logger.LogMessage("  {0}. {1}", i + 1, companies[i]);
                }

                Console.Write("Select company number: ");
                string? companyInput = Console.ReadLine();
                if (!int.TryParse(companyInput, out int companyIndex) || companyIndex < 1 || companyIndex > companies.Count)
                {
                    _logger.LogMessage("✗ Invalid selection.");
                    return;
                }

                string selectedCompany = companies[companyIndex - 1];
                _logger.LogMessage("✓ Selected: {0}", selectedCompany);
                _logger.LogMessage("");

                // Step 3: Select Tables
                _logger.LogMessage("Step 3: Select Tables to Export");
                _logger.LogMessage("──────────────────────────────");

                var tables = await GetAvailableTablesAsync(selectedCompany);
                if (tables.Count == 0)
                {
                    _logger.LogMessage("✗ No tables found in configuration.");
                    return;
                }

                _logger.LogMessage("Available tables:");
                for (int i = 0; i < tables.Count; i++)
                {
                    _logger.LogMessage("  {0}. {1}", i + 1, tables[i]);
                }

                _logger.LogMessage("");
                _logger.LogMessage("Enter table numbers to export (comma-separated, e.g., 1,2,5):");
                Console.Write("> ");
                string? tableInput = Console.ReadLine();

                List<string> selectedTables = new List<string>();
                if (!string.IsNullOrWhiteSpace(tableInput))
                {
                    var indices = tableInput.Split(',').Select(s => s.Trim());
                    foreach (var idx in indices)
                    {
                        if (int.TryParse(idx, out int tableIdx) && tableIdx > 0 && tableIdx <= tables.Count)
                        {
                            selectedTables.Add(tables[tableIdx - 1]);
                        }
                    }
                }

                if (selectedTables.Count == 0)
                {
                    _logger.LogMessage("✗ No tables selected.");
                    return;
                }

                _logger.LogMessage("✓ Selected {0} tables:", selectedTables.Count);
                foreach (var table in selectedTables)
                {
                    _logger.LogMessage("  - {0}", table);
                }

                _logger.LogMessage("");

                // Step 4: Set Sync Interval
                _logger.LogMessage("Step 4: Sync Interval");
                _logger.LogMessage("─────────────────────");
                _logger.LogMessage("Enter sync interval in minutes [0 = one-time only]:");
                Console.Write("> ");
                string? intervalStr = Console.ReadLine();
                int syncInterval = string.IsNullOrWhiteSpace(intervalStr) || !int.TryParse(intervalStr, out int intVal) ? 0 : intVal;

                _logger.LogMessage("");

                // Step 5: Export Path
                _logger.LogMessage("Step 5: Export Path");
                _logger.LogMessage("───────────────────");
                Console.Write("Enter export path [./exports]: ");
                string? exportPath = Console.ReadLine();
                exportPath = string.IsNullOrWhiteSpace(exportPath) ? "./exports" : exportPath;

                _logger.LogMessage("");

                // Summary
                _logger.LogMessage("═══════════════════════════════════════════════════════");
                _logger.LogMessage("     CONFIGURATION SUMMARY");
                _logger.LogMessage("═══════════════════════════════════════════════════════");
                _logger.LogMessage("Server:           {0}:{1}", server, port);
                _logger.LogMessage("Company:          {0}", selectedCompany);
                _logger.LogMessage("Tables:           {0}", string.Join(", ", selectedTables));
                 _logger.LogMessage("Sync Interval:    {0} minutes", syncInterval > 0 ? syncInterval : "One-time");
                _logger.LogMessage("Export Path:      {0}", exportPath);
                _logger.LogMessage("");

                Console.Write("Save configuration? (y/n): ");
                string? confirm = Console.ReadLine();

                if (confirm?.ToLower() == "y")
                {
                    var config = new AppConfiguration
                    {
                        Tally = new TallyConfig
                        {
                            Server = server,
                            Port = port,
                            Company = selectedCompany,
                            SelectedTables = selectedTables
                        },
                        Sync = new SyncConfig
                        {
                            IntervalMinutes = syncInterval,
                            ExportPath = exportPath
                        }
                    };

                    await _configService.SaveConfigurationAsync(config);
                    _logger.LogMessage("✓ Configuration saved to config.json");
                    _logger.LogMessage("");
                    _logger.LogMessage("Setup complete! You can now start the service.");
                }
                else
                {
                    _logger.LogMessage("Setup cancelled.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("SetupService.RunInteractiveSetupAsync()", ex);
            }
        }

        public async Task<List<string>> GetAvailableCompaniesAsync()
        {
            var result = new List<string>();
            try
            {
                // XML to get list of companies
                string xmlPayload = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ENVELOPE><HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Data</TYPE><ID>MyReportLedgerTable</ID></HEADER><BODY><DESC><STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT></STATICVARIABLES><TDL><TDLMESSAGE><REPORT NAME=\"MyReportLedgerTable\"><FORMS>MyForm</FORMS></REPORT><FORM NAME=\"MyForm\"><PARTS>MyPart01</PARTS><XMLTAG>DATA</XMLTAG></FORM><PART NAME=\"MyPart01\"><LINES>MyLine01</LINES><REPEAT>MyLine01 : MyCollection</REPEAT><SCROLLED>Vertical</SCROLLED></PART><LINE NAME=\"MyLine01\"><FIELDS>Fld</FIELDS></LINE><FIELD NAME=\"Fld\"><SET>$Name</SET><XMLTAG>ROW</XMLTAG></FIELD><COLLECTION NAME=\"MyCollection\"><TYPE>Company</TYPE><FETCH></FETCH></COLLECTION></TDLMESSAGE></TDL></DESC></BODY></ENVELOPE>";

                string response = await _tallyService.PostTallyXmlAsync(xmlPayload);

                // Parse company names from XML response
                var lines = response.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("<ROW>") && trimmed.EndsWith("</ROW>"))
                    {
                        string company = trimmed.Replace("<ROW>", "").Replace("</ROW>", "").Trim();
                        if (!string.IsNullOrEmpty(company) && company != "&lt;FLDBLANK&gt;")
                        {
                            result.Add(company);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("SetupService.GetAvailableCompaniesAsync()", ex);
            }

            return result.Count == 0 ? new List<string> { "Default" } : result;
        }

        public async Task<List<string>> GetAvailableTablesAsync(string company)
        {
            var result = new List<string>();
            try
            {
                // Load from YAML configuration
                var tables = await _configService.LoadTableDefinitionsAsync();
                result.AddRange(tables.Select(t => t.Name).OrderBy(n => n).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError("SetupService.GetAvailableTablesAsync()", ex);
            }

            return result;
        }
    }
}
