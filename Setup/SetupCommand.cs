using TallySyncService.Models;
using TallySyncService.Services;

namespace TallySyncService.Setup;

public class SetupCommand
{
    private readonly ITallyService _tallyService;
    private readonly IConfigurationService _configService;
    private readonly ILogger<SetupCommand> _logger;

    public SetupCommand(
        ITallyService tallyService,
        IConfigurationService configService,
        ILogger<SetupCommand> logger)
    {
        _tallyService = tallyService;
        _configService = configService;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        try
        {
            Console.WriteLine("╔════════════════════════════════════════════╗");
            Console.WriteLine("║   Tally Sync Service - Initial Setup       ║");
            Console.WriteLine("╚════════════════════════════════════════════╝");
            Console.WriteLine();

            // Check Tally connection
            Console.WriteLine("Checking connection to Tally...");
            var isConnected = await _tallyService.CheckConnectionAsync();
            
            if (!isConnected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("X Cannot connect to Tally Prime!");
                Console.ResetColor();
                Console.WriteLine("Please ensure:");
                Console.WriteLine("  1. Tally Prime is running");
                Console.WriteLine("  2. ODBC Server is enabled (Gateway of Tally → F11 → F3)");
                Console.WriteLine("  3. Tally is listening on port 9000");
                Console.WriteLine();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Successfully connected to Tally!");
            Console.ResetColor();
            Console.WriteLine();

            // Get available tables
            Console.WriteLine("Fetching available tables from Tally...");
            var tables = await _tallyService.GetAvailableTablesAsync();
            
            Console.WriteLine();
            Console.WriteLine("Available tables for synchronization:");
            Console.WriteLine("─────────────────────────────────────────────");
            
            for (int i = 0; i < tables.Count; i++)
            {
                Console.WriteLine($"{i + 1,2}. {tables[i].Name,-20} - {tables[i].Description}");
            }
            
            Console.WriteLine();
            Console.WriteLine("Enter the numbers of tables you want to sync (comma-separated).");
            Console.WriteLine("Example: 1,3,4,5 (or 'all' for all tables):");
            Console.Write("> ");
            
            var input = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("No selection made. Setup cancelled.");
                return 1;
            }

            var selectedTables = new List<string>();
            
            if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                selectedTables = tables.Select(t => t.Name).ToList();
            }
            else
            {
                var selections = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim());
                
                foreach (var selection in selections)
                {
                    if (int.TryParse(selection, out int index) && index >= 1 && index <= tables.Count)
                    {
                        selectedTables.Add(tables[index - 1].Name);
                    }
                    else
                    {
                        Console.WriteLine($"Invalid selection: {selection}");
                    }
                }
            }

            if (selectedTables.Count == 0)
            {
                Console.WriteLine("No valid tables selected. Setup cancelled.");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("You have selected the following tables:");
            foreach (var table in selectedTables)
            {
                Console.WriteLine($"  • {table}");
            }
            Console.WriteLine();

            // Confirm
            Console.Write("Proceed with this configuration? (y/n): ");
            var confirm = Console.ReadLine()?.Trim().ToLower();
            
            if (confirm != "y" && confirm != "yes")
            {
                Console.WriteLine("Setup cancelled.");
                return 1;
            }

            // Save configuration
            var config = new SyncConfiguration
            {
                SelectedTables = selectedTables,
                IsConfigured = true,
                LastConfigUpdate = DateTime.UtcNow
            };

            // Initialize table states
            foreach (var table in selectedTables)
            {
                config.TableStates[table] = new TableSyncState
                {
                    TableName = table
                };
            }

            await _configService.SaveConfigurationAsync(config);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✓ Configuration saved successfully!");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"Configuration saved to: {_configService.GetDataDirectory()}");
            Console.WriteLine();
            Console.WriteLine("You can now run the service. The sync will start automatically.");
            Console.WriteLine("To run as Windows Service:");
            Console.WriteLine("  sc create TallySyncService binPath=\"<path-to-exe>\"");
            Console.WriteLine("  sc start TallySyncService");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during setup");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }
}