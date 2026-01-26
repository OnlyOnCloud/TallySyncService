using System.Text.Json;
using TallySyncService.Models;
using TallySyncService.Services;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally CSV Export Service                    ║");
        Console.WriteLine("║   Export Tally data to CSV format             ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        try
        {
            // Load configuration
            var config = LoadConfig();
            
            // Initialize services
            var tallyXmlService = new TallyXmlService(config);
            var xmlGenerator = new XmlGenerator();
            var exporter = new TallyDataExporter(tallyXmlService, xmlGenerator, config);
            var yamlLoader = new YamlConfigLoader(config.DefinitionFile);

            // Test Tally connection
            Console.WriteLine("Testing connection to Tally...");
            if (!await tallyXmlService.TestConnectionAsync())
            {
                Console.WriteLine("✗ Unable to connect to Tally.");
                Console.WriteLine($"  Please ensure Tally is running at {config.Server}:{config.Port}");
                Console.WriteLine("  and XML/HTTP interface is enabled (F12 > Configure > Enable)");
                return;
            }
            Console.WriteLine($"✓ Connected to Tally at {config.Server}:{config.Port}");
            Console.WriteLine();

            // Load YAML configuration
            Console.WriteLine("Loading table definitions...");
            await yamlLoader.LoadAsync();
            Console.WriteLine($"✓ Loaded {yamlLoader.GetAllTables().Count} table definitions");
            Console.WriteLine();

            // Get company list
            var companies = await tallyXmlService.GetCompanyListAsync();
            if (companies.Count == 0)
            {
                Console.WriteLine("✗ No companies found in Tally.");
                return;
            }

            // Select company
            var selectedCompany = await SelectCompanyAsync(companies, config);
            if (selectedCompany == null)
            {
                Console.WriteLine("No company selected. Exiting.");
                return;
            }

            config.Company = selectedCompany.Name;
            Console.WriteLine($"✓ Selected company: {selectedCompany.Name}");
            Console.WriteLine();

            // Select tables
            var selectedTables = await SelectTablesAsync(yamlLoader);
            if (selectedTables.Count == 0)
            {
                Console.WriteLine("No tables selected. Exiting.");
                return;
            }

            Console.WriteLine($"✓ Selected {selectedTables.Count} table(s) for export");
            Console.WriteLine();

            // Set output directory
            var outputDir = GetOutputDirectory(config);
            Console.WriteLine($"Export directory: {outputDir}");
            Console.WriteLine();

            // Perform export
            Console.WriteLine("Starting export...");
            Console.WriteLine("─────────────────────────────────────────────────");
            
            var exportedFiles = await exporter.ExportMultipleTablesToCsvAsync(selectedTables, outputDir);

            Console.WriteLine("─────────────────────────────────────────────────");
            Console.WriteLine();
            Console.WriteLine($"✓ Export completed successfully!");
            Console.WriteLine($"  Exported {exportedFiles.Count} file(s) to: {outputDir}");
            Console.WriteLine();

            // List exported files
            Console.WriteLine("Exported files:");
            foreach (var file in exportedFiles)
            {
                var fileInfo = new FileInfo(file);
                Console.WriteLine($"  • {Path.GetFileName(file)} ({FormatFileSize(fileInfo.Length)})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Details: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static TallyConfig LoadConfig()
    {
        var configPath = "config.json";
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Configuration file not found: {configPath}");
            Console.WriteLine("Using default configuration.");
            return new TallyConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var configData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            
            var config = new TallyConfig();
            
            if (configData != null && configData.ContainsKey("tally"))
            {
                var tallyConfig = configData["tally"];
                
                if (tallyConfig.TryGetProperty("server", out var server))
                    config.Server = server.GetString() ?? "localhost";
                
                if (tallyConfig.TryGetProperty("port", out var port))
                    config.Port = port.GetInt32();
                
                if (tallyConfig.TryGetProperty("company", out var company))
                    config.Company = company.GetString() ?? "";
            }

            if (configData != null && configData.ContainsKey("sync"))
            {
                var syncConfig = configData["sync"];
                
                if (syncConfig.TryGetProperty("exportPath", out var exportPath))
                {
                    var path = exportPath.GetString();
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Store in a way we can use later
                        Environment.SetEnvironmentVariable("TALLY_EXPORT_PATH", path);
                    }
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
            return new TallyConfig();
        }
    }

    static async Task<CompanyInfo?> SelectCompanyAsync(List<CompanyInfo> companies, TallyConfig config)
    {
        // If company is already specified in config, use it
        if (!string.IsNullOrEmpty(config.Company))
        {
            var existing = companies.FirstOrDefault(c => 
                c.Name.Equals(config.Company, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }
        }

        Console.WriteLine("Available Companies:");
        Console.WriteLine("─────────────────────────────────────────────────");
        for (int i = 0; i < companies.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {companies[i].Name}");
        }
        Console.WriteLine("─────────────────────────────────────────────────");
        Console.Write("Select company number (or 0 to exit): ");

        if (int.TryParse(Console.ReadLine(), out var selection))
        {
            if (selection == 0) return null;
            if (selection > 0 && selection <= companies.Count)
            {
                return companies[selection - 1];
            }
        }

        Console.WriteLine("Invalid selection.");
        return await SelectCompanyAsync(companies, config);
    }

    static async Task<List<TableDefinition>> SelectTablesAsync(YamlConfigLoader yamlLoader)
    {
        Console.WriteLine("Table Categories:");
        Console.WriteLine("─────────────────────────────────────────────────");
        Console.WriteLine("  1. Master Tables (Ledgers, Items, Groups, etc.)");
        Console.WriteLine("  2. Transaction Tables (Vouchers, Accounting, Inventory)");
        Console.WriteLine("  3. All Tables");
        Console.WriteLine("  4. Select Specific Tables");
        Console.WriteLine("─────────────────────────────────────────────────");
        Console.Write("Select option: ");

        var option = Console.ReadLine();

        return option switch
        {
            "1" => yamlLoader.GetMasterTables(),
            "2" => yamlLoader.GetTransactionTables(),
            "3" => yamlLoader.GetAllTables(),
            "4" => await SelectSpecificTablesAsync(yamlLoader),
            _ => new List<TableDefinition>()
        };
    }

    static Task<List<TableDefinition>> SelectSpecificTablesAsync(YamlConfigLoader yamlLoader)
    {
        var allTables = yamlLoader.GetAllTables();
        var selectedTables = new List<TableDefinition>();

        Console.WriteLine();
        Console.WriteLine("Available Tables:");
        Console.WriteLine("─────────────────────────────────────────────────");
        
        var masterTables = yamlLoader.GetMasterTables();
        var transactionTables = yamlLoader.GetTransactionTables();

        Console.WriteLine("Master Tables:");
        for (int i = 0; i < masterTables.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {masterTables[i].Name}");
        }

        Console.WriteLine();
        Console.WriteLine("Transaction Tables:");
        for (int i = 0; i < transactionTables.Count; i++)
        {
            Console.WriteLine($"  {masterTables.Count + i + 1}. {transactionTables[i].Name}");
        }

        Console.WriteLine("─────────────────────────────────────────────────");
        Console.WriteLine("Enter table numbers separated by commas (e.g., 1,3,5)");
        Console.WriteLine("or 'all' for all tables:");
        Console.Write("> ");

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return Task.FromResult(selectedTables);

        if (input.Trim().ToLower() == "all")
            return Task.FromResult(allTables);

        var numbers = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var numStr in numbers)
        {
            if (int.TryParse(numStr.Trim(), out var num))
            {
                if (num > 0 && num <= allTables.Count)
                {
                    selectedTables.Add(allTables[num - 1]);
                }
            }
        }

        return Task.FromResult(selectedTables);
    }

    static string GetOutputDirectory(TallyConfig config)
    {
        var envPath = Environment.GetEnvironmentVariable("TALLY_EXPORT_PATH");
        var basePath = !string.IsNullOrEmpty(envPath) ? envPath : "./csv";
        
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var companyName = string.IsNullOrEmpty(config.Company) 
            ? "export" 
            : config.Company.Replace(" ", "_");
        
        var outputDir = Path.Combine(basePath, $"{companyName}_{timestamp}");
        return Path.GetFullPath(outputDir);
    }

    static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}