using System.Text.Json;
using System.Runtime.InteropServices;
using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService.Commands;

public class SetupCommand
{
    public static async Task ExecuteAsync()
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally Sync Service - Setup                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = new Dictionary<string, object>();

        // Tally Configuration
        Console.WriteLine("=== Tally Configuration ===");
        Console.Write("Tally Server [localhost]: ");
        var server = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(server)) server = "localhost";

        Console.Write("Tally Port [9000]: ");
        var portStr = Console.ReadLine();
        var port = string.IsNullOrWhiteSpace(portStr) ? 9000 : int.Parse(portStr);

        Console.Write("Company Name (leave empty to select at runtime): ");
        var company = Console.ReadLine() ?? "";

        // Platform-specific default Tally path
        string defaultTallyPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            defaultTallyPath = "C:\\Program Files (x86)\\Tally.ERP 9\\tally.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            defaultTallyPath = $"{homeDir}/.wine/drive_c/Program Files/TallyPrime (3)/tally.exe";
        }
        else
        {
            defaultTallyPath = "";
        }

        Console.Write($"Tally Executable Path [{defaultTallyPath}]: ");
        var tallyPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(tallyPath)) 
            tallyPath = defaultTallyPath;

        config["tally"] = new Dictionary<string, object>
        {
            { "server", server },
            { "port", port },
            { "company", company },
            { "tallyPath", tallyPath }
        };

        // Sync Configuration
        Console.WriteLine("\n=== Sync Configuration ===");
        Console.Write("Sync Interval (minutes) [15]: ");
        var intervalStr = Console.ReadLine();
        var interval = string.IsNullOrWhiteSpace(intervalStr) ? 15 : int.Parse(intervalStr);

        Console.Write("Export Path [./exports]: ");
        var exportPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(exportPath)) exportPath = "./exports";

        config["sync"] = new Dictionary<string, object>
        {
            { "intervalMinutes", interval },
            { "exportPath", exportPath }
        };

        // Backend Configuration
        Console.WriteLine("\n=== Backend Configuration ===");
        Console.Write("Backend Base URL (e.g., http://localhost:3001): ");
        var backendBaseUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(backendBaseUrl)) backendBaseUrl = "http://localhost:3001";
        
        // Remove trailing slash if present
        backendBaseUrl = backendBaseUrl.TrimEnd('/');

        config["backend"] = new Dictionary<string, object>
        {
            { "url", $"{backendBaseUrl}/api/data" }
        };

        // Create TallyConfig for use in custom table selection
        var tallyConfig = new TallyConfig
        {
            Server = server,
            Port = port,
            Company = company,
            TallyPath = tallyPath
        };

        // Table Selection Configuration
        Console.WriteLine("\n=== Table Selection ===");
        Console.WriteLine("Which tables do you want to export?");
        Console.WriteLine("  1. Master Tables only");
        Console.WriteLine("  2. Transaction Tables only");
        Console.WriteLine("  3. All Tables (Master + Transaction)");
        Console.WriteLine("  4. Custom Selection");
        Console.Write("Select option [3]: ");
        
        var tableOption = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(tableOption)) tableOption = "3";

        string tableMode = "all";
        var customTables = new List<string>();

        switch (tableOption)
        {
            case "1":
                tableMode = "master";
                Console.WriteLine("✓ Will export Master tables only");
                break;
            case "2":
                tableMode = "transaction";
                Console.WriteLine("✓ Will export Transaction tables only");
                break;
            case "3":
                tableMode = "all";
                Console.WriteLine("✓ Will export All tables");
                break;
            case "4":
                tableMode = "custom";
                customTables = await SelectCustomTablesAsync(tallyConfig);
                Console.WriteLine($"✓ Will export {customTables.Count} custom table(s)");
                break;
            default:
                tableMode = "all";
                Console.WriteLine("✓ Invalid option, defaulting to All tables");
                break;
        }

        config["tables"] = new Dictionary<string, object>
        {
            { "mode", tableMode },
            { "customTables", customTables }
        };

        // Save configuration
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        await File.WriteAllTextAsync("config.json", json);

        Console.WriteLine("\n✅ Configuration saved to config.json");
        Console.WriteLine();

        // Test Tally connection
        Console.WriteLine("Testing Tally connection...");

        var tallyService = new TallyXmlService(tallyConfig);
        try
        {
            var connected = await tallyService.TestConnectionAsync();
            if (connected)
            {
                Console.WriteLine($"✓ Successfully connected to Tally at {server}:{port}");
                
                var companies = await tallyService.GetCompanyListAsync();
                Console.WriteLine($"✓ Found {companies.Count} company(ies):");
                foreach (var c in companies)
                {
                    Console.WriteLine($"  • {c.Name}");
                }
            }
            else
            {
                Console.WriteLine($"✗ Could not connect to Tally at {server}:{port}");
                Console.WriteLine("  Make sure Tally is running and XML interface is enabled");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Connection error: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Setup complete! You can now:");
        Console.WriteLine($"  1. Login: dotnet run -- --login {backendBaseUrl}");
        Console.WriteLine("  2. Start sync: dotnet run");
        Console.WriteLine();
        Console.WriteLine($"Backend configured: {backendBaseUrl}");
    }

    private static async Task<List<string>> SelectCustomTablesAsync(TallyConfig tallyConfig)
    {
        Console.WriteLine("\n=== Custom Table Selection ===");
        Console.WriteLine("Loading table definitions from YAML...");
        
        try
        {
            var yamlLoader = new YamlConfigLoader(tallyConfig.DefinitionFile);
            await yamlLoader.LoadAsync();
            
            var tablesByCategory = yamlLoader.GetTableNamesByCategory();
            var selectedTables = new List<string>();

            // Display Master tables
            Console.WriteLine("\n--- Master Tables ---");
            var masterTables = tablesByCategory["Master"];
            for (int i = 0; i < masterTables.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {masterTables[i]}");
            }

            Console.Write("\nSelect Master tables (comma-separated numbers, or 'all' for all, or 'none'): ");
            var masterSelection = Console.ReadLine()?.Trim().ToLower();
            
            if (masterSelection == "all")
            {
                selectedTables.AddRange(masterTables);
                Console.WriteLine($"✓ Selected all {masterTables.Count} master tables");
            }
            else if (masterSelection != "none" && !string.IsNullOrWhiteSpace(masterSelection))
            {
                var indices = masterSelection.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(s => int.Parse(s) - 1)
                    .Where(i => i >= 0 && i < masterTables.Count);
                
                foreach (var index in indices)
                {
                    selectedTables.Add(masterTables[index]);
                }
                Console.WriteLine($"✓ Selected {indices.Count()} master table(s)");
            }

            // Display Transaction tables
            Console.WriteLine("\n--- Transaction Tables ---");
            var transactionTables = tablesByCategory["Transaction"];
            for (int i = 0; i < transactionTables.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {transactionTables[i]}");
            }

            Console.Write("\nSelect Transaction tables (comma-separated numbers, or 'all' for all, or 'none'): ");
            var transactionSelection = Console.ReadLine()?.Trim().ToLower();
            
            if (transactionSelection == "all")
            {
                selectedTables.AddRange(transactionTables);
                Console.WriteLine($"✓ Selected all {transactionTables.Count} transaction tables");
            }
            else if (transactionSelection != "none" && !string.IsNullOrWhiteSpace(transactionSelection))
            {
                var indices = transactionSelection.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(s => int.Parse(s) - 1)
                    .Where(i => i >= 0 && i < transactionTables.Count);
                
                foreach (var index in indices)
                {
                    selectedTables.Add(transactionTables[index]);
                }
                Console.WriteLine($"✓ Selected {indices.Count()} transaction table(s)");
            }

            if (selectedTables.Count == 0)
            {
                Console.WriteLine("⚠️  No tables selected, defaulting to all tables");
                selectedTables.AddRange(masterTables);
                selectedTables.AddRange(transactionTables);
            }

            return selectedTables;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error loading tables: {ex.Message}");
            Console.WriteLine("Defaulting to all tables");
            return new List<string>();
        }
    }
}
