using KlaraHome.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace KlaraHome.Infrastructure.Observability;

/// <summary>
/// OpenTelemetry wiring for traces and metrics (docs/09-nfr-testing-observability.md §3.2, §3.3).
/// Instrumentation is always collected; it is only exported when an OTLP endpoint is configured,
/// so local development pays nothing and the VPS collector at Step 31 is a config change.
/// </summary>
public static class TelemetryExtensions
{
    public static IHostApplicationBuilder AddKlaraHomeTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var observability = builder.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: observability.ServiceName,
                    serviceVersion: KlaraHomeTelemetry.Version,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
                ]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(observability.TraceSampleRatio)))
                    .AddSource(KlaraHomeTelemetry.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        // Probes run every few seconds and would drown the useful traces.
                        options.Filter = context => !IsProbe(context.Request.Path);
                    })
                    .AddHttpClientInstrumentation();

                if (observability.ExportsTelemetry)
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(observability.OtlpEndpoint!));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(KlaraHomeTelemetry.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (observability.ExportsTelemetry)
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(observability.OtlpEndpoint!));
                }
            });

        return builder;
    }

    private static bool IsProbe(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
}
