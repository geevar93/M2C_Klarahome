using System.Reflection;
using KlaraHome.Api;
using KlaraHome.Api.Diagnostics;
using KlaraHome.Api.Endpoints;
using KlaraHome.Hosting;
using KlaraHome.Infrastructure;
using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.OpenApi;
using KlaraHome.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

// A chiseled runtime image has no shell and no curl, so the container health probe is the
// application itself: `dotnet KlaraHome.Api.dll --healthcheck` exits 0 when /health/live is 200.
if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    return await ContainerHealthProbe.RunAsync().ConfigureAwait(false);
}

// A bootstrap logger so a failure before the host is built is still reported as structured JSON
// rather than an unhandled exception on stderr.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Assemblies scanned for command/query handlers and FluentValidation validators.
    Assembly[] applicationAssemblies =
    [
        typeof(Program).Assembly,
        .. ModuleAssemblies.All,
    ];

    builder.AddKlaraHomeInfrastructure(applicationAssemblies);

    // Registered before the modules, because each module's AddServices registers its DbContext on
    // top of these. The API attributes writes to the request principal; it does not dispatch the
    // outbox — that is the worker's job.
    builder.Services.AddKlaraHomePersistence(builder.Configuration, httpContextAvailable: true);
    builder.Services.AddModules(builder.Configuration, ModuleAssemblies.All);

    var app = builder.Build();
    var api = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;

    app.UseKlaraHomeInfrastructure();

    app.MapHealthEndpoints();

    var apiGroup = app.MapModules(api.BasePath);
    apiGroup.MapMetaEndpoints();
    apiGroup.MapDiagnosticsEndpoints(app.Environment, api);

    // The OpenAPI document and its reference UI are never served from Production: the contract
    // is published to consumers as a generated client, not as a browsable endpoint.
    if (api.EnableApiReference && !app.Environment.IsProduction())
    {
        app.MapOpenApi(OpenApiExtensions.DocumentRoute).AllowAnonymous();
        app.MapScalarApiReference(api.ApiReferencePath, options => options
                .WithTitle("Klara Home API")
                .AddDocument(api.Version))
            .AllowAnonymous();
    }

    await app.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // Logged here and rethrown, not swallowed: the process must die visibly, with a non-zero
    // exit code, so the orchestrator restarts it rather than keeping a broken container alive.
    Log.Fatal(exception, "The API host terminated unexpectedly");
    throw;
}

// There is deliberately no `finally { Log.CloseAndFlush(); }`. The console sink writes
// synchronously, so nothing is buffered at exit, and the host disposes the logger it owns.
// Closing the static logger here would also break the integration tests, which run several
// hosts inside one process.

/// <summary>Exposed so the integration tests can drive the real host with WebApplicationFactory.</summary>
public partial class Program;
