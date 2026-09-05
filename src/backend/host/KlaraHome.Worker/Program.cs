using KlaraHome.Hosting;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Observability;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    // Content root is the deployed directory, not the caller's working directory.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    builder.AddKlaraHomeLogging();
    builder.AddKlaraHomeTelemetry();
    builder.Services.AddSingleton<IClock>(SystemClock.Instance);

    // No HTTP context: background work acts as the system, so it attributes nothing to a user.
    builder.Services.AddKlaraHomePersistence(builder.Configuration, httpContextAvailable: false);
    builder.Services.AddModules(builder.Configuration, ModuleAssemblies.All);

    // The outbox is dispatched here and nowhere else. Every API replica running the same loop
    // would multiply the polling and contend for the same rows for no gain.
    builder.Services.AddOutboxDispatcher(
        builder.Configuration,
        [typeof(KlaraHome.Contracts.ContractsAssembly).Assembly, .. ModuleAssemblies.All]);

    // Further hosted services arrive with the steps that introduce them:
    //   Step 8  notification sending
    //   Step 16 shipment polling and reconciliation
    var host = builder.Build();
    await host.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "The worker host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}
