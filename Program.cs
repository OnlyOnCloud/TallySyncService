using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TallySyncService;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddHostedService<TallySyncWorker>();
    })
    .Build();

await host.RunAsync();