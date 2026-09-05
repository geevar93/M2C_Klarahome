using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Observability.Enrichers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace KlaraHome.Infrastructure.Observability;

/// <summary>Serilog wiring: structured JSON to stdout, enriched as the spec requires.</summary>
public static class LoggingExtensions
{
    private const string DeveloperTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} <{correlationId}>{NewLine}{Exception}";

    /// <summary>
    /// Replaces the default logging providers with Serilog. Mandatory enrichers per
    /// docs/09-nfr-testing-observability.md §3.1: correlationId, tenantId, userId, vendorId,
    /// traceId, spanId, module, environment and version.
    /// </summary>
    /// <remarks>
    /// The logger is built here and registered in DI as <see cref="Serilog.ILogger"/>, owned and
    /// disposed by the host. <c>Serilog.Log</c> is deliberately left as the entry point's
    /// bootstrap logger: a host must never dispose a logger it does not own, which is what lets
    /// the integration tests run several hosts inside one process. Anything that logs through
    /// the static API — Serilog request logging included — is handed this logger explicitly.
    /// </remarks>
    public static IHostApplicationBuilder AddKlaraHomeLogging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var observability = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        var tenant = builder.Configuration
            .GetSection(TenantOptions.SectionName)
            .Get<TenantOptions>() ?? new TenantOptions();

        var isDevelopment = builder.Environment.IsDevelopment();
        var asJson = observability.ConsoleJson ?? !isDevelopment;

        // HttpContextAccessor keeps the context in a static AsyncLocal, so the instance the
        // enrichers hold and the one in DI observe the same request either way.
        var accessor = new HttpContextAccessor();
        builder.Services.AddSingleton<IHttpContextAccessor>(accessor);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLevel(observability.MinimumLevel))
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", observability.ServiceName)
            .Enrich.WithProperty("environment", builder.Environment.EnvironmentName)
            .Enrich.WithProperty("version", KlaraHomeTelemetry.Version)
            .Enrich.WithProperty("tenantId", tenant.Code)
            .Enrich.With(new ActivityEnricher())
            .Enrich.With(new ModuleEnricher())
            .Enrich.With(new CorrelationIdEnricher(accessor))
            .Enrich.With(new PrincipalEnricher(accessor))
            .Destructure.With(new PiiMaskingDestructuringPolicy());

        if (asJson)
        {
            configuration.WriteTo.Console(new CompactJsonFormatter());
        }
        else
        {
            configuration.WriteTo.Console(outputTemplate: DeveloperTemplate);
        }

        // Anything under a "Serilog" configuration section wins, so per-module verbosity can be
        // raised in a running deployment without a rebuild.
        configuration.ReadFrom.Configuration(builder.Configuration);

        var logger = configuration.CreateLogger();

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Serilog.ILogger>(logger);
        builder.Services.AddSerilog(logger, dispose: true);

        return builder;
    }

    private static LogEventLevel ParseLevel(string? value)
        => Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
}
