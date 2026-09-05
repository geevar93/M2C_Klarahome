using KlaraHome.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using KlaraHome.Migrator;

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

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("KlaraHome.Migrator");

    // Step 4 replaces this with: resolve every module DbContext, apply pending migrations inside
    // a transaction, and fail loudly. The exit-code contract below is already the real one, so
    // the deploy pipeline can be written against it now.
    MigratorLog.NothingToApply(logger);
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Migration run failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
