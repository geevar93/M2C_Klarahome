using KlaraHome.Infrastructure.Observability;
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

    // Hosted services are registered by the steps that introduce them:
    //   Step 4  outbox dispatcher
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
