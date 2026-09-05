using KlaraHome.Hosting;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Observability;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.Migrator;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    // Content root is the deployed directory, not the caller's working directory: a one-shot
    // job container is started from wherever the orchestrator happens to be, and it must still
    // find its own appsettings.json.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    builder.AddKlaraHomeLogging();
    builder.Services.AddSingleton<IClock>(SystemClock.Instance);

    // No HTTP context here: the migrator acts as the system, so audit columns on seeded rows are
    // correctly left unattributed rather than blamed on whoever last logged in.
    builder.Services.AddKlaraHomePersistence(builder.Configuration, httpContextAvailable: false);
    builder.Services.AddModules(builder.Configuration, ModuleAssemblies.All);
    builder.Services.AddSingleton<MigrationRunner>();

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("KlaraHome.Migrator");

    var result = await host.Services
        .GetRequiredService<MigrationRunner>()
        .RunAsync(CancellationToken.None)
        .ConfigureAwait(false);

    MigratorLog.RunCompleted(
        logger,
        result.ContextsInspected,
        result.MigrationsApplied,
        result.SeedersRun);

    return 0;
}
catch (Exception exception)
{
    // Exit code 1 is the deploy pipeline's signal to stop before the new application version
    // starts against a schema that was never brought up to date.
    Log.Fatal(exception, "Migration run failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
