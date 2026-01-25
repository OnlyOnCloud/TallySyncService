using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TallySyncService.Models;
using TallySyncService.Services;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            // Check for command-line arguments
            if (args.Length > 0)
            {
                switch (args[0].ToLower())
                {
                    case "--setup":
                        await RunSetup();
                        return;
                    case "--install":
                        InstallWindowsService();
                        return;
                    case "--uninstall":
                        UninstallWindowsService();
                        return;
                    case "--debug":
                        await RunConsoleMode();
                        return;
                    default:
                        ShowHelp();
                        return;
                }
            }

            // Default: Run as Windows Service
            await RunAsWindowsService();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");
            Environment.Exit(1);
        }
    }

    static async Task RunSetup()
    {
        var logger = new FileLogger(".");
        var configService = new ConfigurationService(logger);

        // Load existing config for server/port defaults
        var existingConfig = await configService.LoadConfigurationAsync();
        
        var tallyService = new TallyService(
            existingConfig.Tally.Server,
            existingConfig.Tally.Port,
            "",
            logger
        );

        var setupService = new SetupService(tallyService, logger, configService);
        await setupService.RunInteractiveSetupAsync();

        logger.Close();
    }

    static async Task RunConsoleMode()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("config.json", optional: false, reloadOnChange: false)
            .Build();

        var appConfig = new AppConfiguration();
        configuration.GetSection("tally").Bind(appConfig.Tally);
        configuration.GetSection("sync").Bind(appConfig.Sync);

        var logger = new FileLogger(".");
        var tallyService = new TallyService(appConfig.Tally.Server, appConfig.Tally.Port, appConfig.Tally.Company, logger);
        var csvExporter = new CsvExporter(tallyService, logger, "./csv");
        var syncService = new TallySyncServiceImpl(tallyService, csvExporter, logger, appConfig);

        logger.LogMessage("Starting Tally CSV Exporter (Console Mode)");
        logger.LogMessage("  Server: {0}:{1}", appConfig.Tally.Server, appConfig.Tally.Port);
        logger.LogMessage("  Company: {0}", appConfig.Tally.Company ?? "All");
        logger.LogMessage("  Export Path: {0}", appConfig.Sync.ExportPath);

        int rowCount = await syncService.PerformFullSyncAsync();
        logger.LogMessage("Export completed. Total rows: {0}", rowCount);

        logger.Close();
    }

    static async Task RunAsWindowsService()
    {
        var host = Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureServices((context, services) =>
            {
                // Load configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("config.json", optional: false, reloadOnChange: false)
                    .Build();

                var appConfig = new AppConfiguration();
                configuration.GetSection("tally").Bind(appConfig.Tally);
                configuration.GetSection("sync").Bind(appConfig.Sync);

                // Register services
                services.AddSingleton<TallySyncService.Services.ILogger>(sp => new FileLogger("."));
                
                services.AddSingleton<ITallyService>(sp =>
                    new TallyService(
                        appConfig.Tally.Server,
                        appConfig.Tally.Port,
                        appConfig.Tally.Company,
                        sp.GetRequiredService<TallySyncService.Services.ILogger>()
                    )
                );

                services.AddSingleton<ICsvExporter>(sp =>
                    new CsvExporter(
                        sp.GetRequiredService<ITallyService>(),
                        sp.GetRequiredService<TallySyncService.Services.ILogger>(),
                        "./csv"
                    )
                );

                services.AddSingleton<ITallySyncService>(sp =>
                    new TallySyncServiceImpl(
                        sp.GetRequiredService<ITallyService>(),
                        sp.GetRequiredService<ICsvExporter>(),
                        sp.GetRequiredService<TallySyncService.Services.ILogger>(),
                        appConfig
                    )
                );

                services.AddSingleton(sp => appConfig);

                // Add background service
                services.AddHostedService(sp =>
                    new TallySyncService.SyncWorker(
                        sp.GetRequiredService<ITallySyncService>(),
                        sp.GetRequiredService<TallySyncService.Services.ILogger>(),
                        appConfig.Sync.IntervalMinutes
                    )
                );
            })
            .Build();

        await host.RunAsync();
    }

    static void InstallWindowsService()
    {
        try
        {
            string serviceName = "TallySyncService";
            string displayName = "Tally CSV Exporter Service";

            Console.WriteLine("Installing Windows Service...");
            Console.WriteLine($"Service Name: {serviceName}");
            Console.WriteLine($"Display Name: {displayName}");

            // Get the current executable path
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            
            // Check if service already exists
            bool serviceExists = ServiceExists(serviceName);

            if (serviceExists)
            {
                Console.WriteLine("Service already exists. Uninstalling first...");
                UninstallWindowsService();
            }

            // Create the service using sc.exe
            string command = $"create {serviceName} binPath=\"{exePath}\" DisplayName=\"{displayName}\" start=auto";
            
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                process?.WaitForExit();
                if (process?.ExitCode == 0)
                {
                    Console.WriteLine("✓ Service installed successfully!");
                    Console.WriteLine("You can now:");
                    Console.WriteLine("  1. Start the service: net start TallySyncService");
                    Console.WriteLine("  2. Stop the service:  net stop TallySyncService");
                    Console.WriteLine("  3. Run setup:         TallySyncService.exe --setup");
                }
                else
                {
                    Console.WriteLine("✗ Failed to install service. Run as Administrator.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error installing service: {ex.Message}");
        }
    }

    static void UninstallWindowsService()
    {
        try
        {
            string serviceName = "TallySyncService";

            Console.WriteLine("Uninstalling Windows Service...");

            // Stop the service first
            var stopPsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "net.exe",
                Arguments = $"stop {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var stopProcess = System.Diagnostics.Process.Start(stopPsi))
            {
                stopProcess?.WaitForExit();
            }

            // Delete the service
            var deletePsi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var deleteProcess = System.Diagnostics.Process.Start(deletePsi))
            {
                deleteProcess?.WaitForExit();
                if (deleteProcess?.ExitCode == 0)
                {
                    Console.WriteLine("✓ Service uninstalled successfully!");
                }
                else
                {
                    Console.WriteLine("✗ Failed to uninstall service. Run as Administrator.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error uninstalling service: {ex.Message}");
        }
    }

    static bool ServiceExists(string serviceName)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("  TALLY CSV EXPORTER - Windows Service");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  TallySyncService.exe [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  (no args)      Run as Windows Service (default)");
        Console.WriteLine("  --setup        Interactive setup wizard");
        Console.WriteLine("  --install      Install as Windows Service");
        Console.WriteLine("  --uninstall    Uninstall Windows Service");
        Console.WriteLine("  --debug        Run in console mode (for testing)");
        Console.WriteLine("  --help         Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TallySyncService.exe --setup");
        Console.WriteLine("    Run interactive setup to configure company and tables");
        Console.WriteLine();
        Console.WriteLine("  TallySyncService.exe --install");
        Console.WriteLine("    Install as Windows Service (run as Administrator)");
        Console.WriteLine();
        Console.WriteLine("  net start TallySyncService");
        Console.WriteLine("    Start the Windows Service");
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine();
    }
}
