using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TallySyncService;
using TallySyncService.Commands;
using TallySyncService.Services;

// Check for command line arguments
if (args.Length > 0)
{
    switch (args[0])
    {
        case "--setup":
            await SetupCommand.ExecuteAsync();
            return;
        
        case "--login":
            var backendUrl = args.Length > 1 ? args[1] : "https://dhub-backend.onlyoncloud.com";
            await LoginCommand.ExecuteAsync(backendUrl);
            return;
    }
}

// Run background service
var host = Host.CreateDefaultBuilder(args)
    .UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/tallysync-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            shared: true))
    .ConfigureServices((context, services) =>
    {
        services.AddHttpClient();
        services.AddHostedService<TallySyncWorker>();
        
        // Register services for dependency injection
        services.AddSingleton<YamlConfigLoader>(sp => 
        {
            var config = context.Configuration;
            var definitionFile = config["Tally:DefinitionFile"] ?? "tally-export-config.yaml";
            return new YamlConfigLoader(definitionFile);
        });
    })
    .Build();

await host.RunAsync();