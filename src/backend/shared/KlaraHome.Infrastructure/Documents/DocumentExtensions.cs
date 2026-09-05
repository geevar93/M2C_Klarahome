using KlaraHome.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Infrastructure.Documents;

/// <summary>Registers the PDF pipeline for a host that generates documents.</summary>
public static class DocumentExtensions
{
    /// <summary>
    /// Registers <see cref="IDocumentRenderer"/>.
    /// </summary>
    /// <remarks>
    /// A singleton: the renderer holds no per-request state, and the font files it loads are read
    /// once and kept. Constructing one per invoice would re-read the font on every order.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Root configuration.</param>
    public static IServiceCollection AddKlaraHomeDocuments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<DocumentOptions>(configuration, DocumentOptions.SectionName);
        services.AddSingleton<IDocumentRenderer, MigraDocRenderer>();

        return services;
    }
}
