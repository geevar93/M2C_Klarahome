using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Options;

/// <summary>Configuration binding that fails at startup rather than at first use.</summary>
public static class OptionsExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to its section, validates its data annotations, and
    /// runs that validation during host start — so a misconfigured container refuses to boot
    /// instead of serving errors.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Root configuration.</param>
    /// <param name="sectionName">Configuration section, e.g. <c>Observability</c>.</param>
    /// <param name="extraValidation">Cross-field rules that data annotations cannot express.</param>
    /// <param name="failureMessage">Message shown when <paramref name="extraValidation"/> fails.</param>
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        Func<TOptions, bool>? extraValidation = null,
        string? failureMessage = null)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var builder = services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations();

        if (extraValidation is not null)
        {
            builder.Validate(
                extraValidation,
                failureMessage ?? $"Configuration section {sectionName} failed validation.");
        }

        builder.ValidateOnStart();
        return builder;
    }
}
