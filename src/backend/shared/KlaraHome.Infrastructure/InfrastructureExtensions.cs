using System.Reflection;
using KlaraHome.Infrastructure.Caching;
using KlaraHome.Infrastructure.Configuration;
using KlaraHome.Infrastructure.Correlation;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Health;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Observability;
using KlaraHome.Infrastructure.OpenApi;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog;

using AspNetCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;
using CorsOptions = KlaraHome.Infrastructure.Configuration.CorsOptions;

namespace KlaraHome.Infrastructure;

/// <summary>
/// One entry point for every cross-cutting concern, so a host reads as a list of intentions
/// rather than three hundred lines of wiring.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Registers configuration, observability, the dispatcher and the HTTP cross-cutting
    /// services. Call before modules are added; modules layer their own services on top.
    /// </summary>
    /// <param name="builder">The web host builder.</param>
    /// <param name="messagingAssemblies">Assemblies scanned for handlers and validators.</param>
    public static WebApplicationBuilder AddKlaraHomeInfrastructure(
        this WebApplicationBuilder builder,
        params Assembly[] messagingAssemblies)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddKlaraHomeOptions();
        builder.AddKlaraHomeLogging();
        builder.AddKlaraHomeTelemetry();

        var api = builder.Configuration.Bound<ApiOptions>(ApiOptions.SectionName);

        builder.Services.TryAddSingleton<IClock>(SystemClock.Instance);
        builder.Services.TryAddScoped<ICorrelationContext, CorrelationContext>();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails =
            context => context.ProblemDetails.Enrich(context.HttpContext));
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        builder.Services.AddMessaging(messagingAssemblies);

        builder.Services.AddCors();
        builder.Services
            .AddOptions<AspNetCorsOptions>()
            .Configure<IOptions<CorsOptions>>((aspNet, ours) =>
                aspNet.AddPolicy(CorsOptions.PolicyName, BuildCorsPolicy(ours.Value)));

        builder.Services.AddResponseCompression(options =>
        {
            // Traefik compresses at the edge; this covers direct calls and internal SSR traffic.
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddKlaraHomeOutputCache();
        builder.Services.AddKlaraHomeRateLimiting();
        builder.Services.AddKlaraHomeHealthChecks(builder.Configuration);
        builder.Services.AddKlaraHomeOpenApi(api);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Traefik on the internal network is the only thing that can reach this container.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    /// <summary>
    /// Translates our CORS settings into an ASP.NET policy. With no configured origin the policy
    /// allows nothing: the API is then reachable server-to-server only, which is the correct
    /// default for a deployment whose frontends are not yet known.
    /// </summary>
    private static CorsPolicy BuildCorsPolicy(CorsOptions cors)
    {
        var policy = new CorsPolicyBuilder();

        if (cors.AllowedOrigins.Count == 0)
        {
            return policy.DisallowCredentials().Build();
        }

        policy
            .WithOrigins([.. cors.AllowedOrigins])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders([.. cors.ExposedHeaders])
            .SetPreflightMaxAge(TimeSpan.FromSeconds(cors.PreflightMaxAgeSeconds));

        if (cors.AllowCredentials)
        {
            policy.AllowCredentials();
        }

        return policy.Build();
    }

    /// <summary>Binds and validates every options type this project owns.</summary>
    private static void AddKlaraHomeOptions(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidatedOptions<ApiOptions>(builder.Configuration, ApiOptions.SectionName);
        builder.Services.AddValidatedOptions<TenantOptions>(builder.Configuration, TenantOptions.SectionName);
        builder.Services.AddValidatedOptions<CorsOptions>(builder.Configuration, CorsOptions.SectionName);
        builder.Services.AddValidatedOptions<RateLimitingOptions>(
            builder.Configuration,
            RateLimitingOptions.SectionName,
            // Data annotations do not walk nested objects, so the buckets are checked here.
            options => options.AllBuckets.All(bucket => bucket.PermitLimit >= 1
                                                        && bucket.WindowSeconds >= 1
                                                        && bucket.QueueLimit >= 0),
            "Every RateLimiting bucket needs PermitLimit >= 1, WindowSeconds >= 1 and QueueLimit >= 0.");
        builder.Services.AddValidatedOptions<ObservabilityOptions>(
            builder.Configuration,
            ObservabilityOptions.SectionName,
            options => !options.ExportsTelemetry || Uri.IsWellFormedUriString(options.OtlpEndpoint, UriKind.Absolute),
            "Observability:OtlpEndpoint must be an absolute URI when set.");
    }

    /// <summary>
    /// Installs the middleware pipeline in the order the cross-cutting concerns require:
    /// exceptions outermost, then correlation (so everything downstream can log it), then the
    /// proxy, transport, policy and caching layers.
    /// </summary>
    public static WebApplication UseKlaraHomeInfrastructure(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;
        var rateLimiting = app.Services.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseMiddleware<CorrelationIdMiddleware>();

        if (api.TrustProxyHeaders)
        {
            app.UseForwardedHeaders();
        }

        app.UseResponseCompression();
        app.UseKlaraHomeRequestLogging();
        app.UseCors(CorsOptions.PolicyName);

        if (rateLimiting.Enabled)
        {
            app.UseRateLimiter();
        }

        app.UseOutputCache();

        return app;
    }

    /// <summary>
    /// One structured summary line per request, replacing the three the framework emits. Probes
    /// are logged at Verbose so they do not bury real traffic.
    /// </summary>
    private static void UseKlaraHomeRequestLogging(this WebApplication app)
        => app.UseSerilogRequestLogging(options =>
        {
            // Handed the host's own logger rather than the static one, so request lines carry
            // the correlation, tenant and trace enrichers like every other log event.
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();

            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.####} ms";

            options.GetLevel = static (httpContext, _, exception) =>
            {
                if (exception is not null || httpContext.Response.StatusCode >= 500)
                {
                    return Serilog.Events.LogEventLevel.Error;
                }

                return httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                    ? Serilog.Events.LogEventLevel.Verbose
                    : Serilog.Events.LogEventLevel.Information;
            };
        });

    private static TOptions Bound<TOptions>(this ConfigurationManager configuration, string sectionName)
        where TOptions : class, new()
        => configuration.GetSection(sectionName).Get<TOptions>() ?? new TOptions();
}
