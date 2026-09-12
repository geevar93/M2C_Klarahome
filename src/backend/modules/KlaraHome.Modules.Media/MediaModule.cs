using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Documents;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Media.Endpoints;
using KlaraHome.Modules.Media.Infrastructure;
using KlaraHome.Modules.Media.Infrastructure.Imaging;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using KlaraHome.Modules.Media.Infrastructure.Scanning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Media;

/// <summary>
/// Stored files: the registry, validation by content, the virus-scan seam, responsive image
/// derivatives, signed access to private documents, and the PDF pipeline (ADR-016).
/// </summary>
/// <remarks>
/// <para>
/// Registered before every module that stores a file id, which is nearly all of them. It owns no
/// business meaning: a file here is bytes, a type, a size and a checksum, and what the file
/// <em>is</em> — a product photo, a KYC scan, an invoice — is known only to the module that kept
/// its id.
/// </para>
/// <para>
/// The S3 client and the PDF renderer are registered here rather than in the host, because this is
/// the only module that uses either. A host that never maps this module never constructs them.
/// </para>
/// </remarks>
public sealed class MediaModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "media";

    /// <inheritdoc />
    public string Name => "Media";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// After Platform and Identity — it audits, and it records who uploaded — and before every
    /// module that will store a file id.
    /// </summary>
    public int Order => 30;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<MediaDbContext>(configuration, this);
        services.AddValidatedOptions<MediaOptions>(configuration, MediaOptions.SectionName);

        services.AddKlaraHomeStorage(configuration);
        services.AddKlaraHomeDocuments(configuration);

        services.AddSingleton<ImgproxyUrlBuilder>();

        // The concrete service is registered and the published contract forwarded to it, so this
        // module's own handlers can use the projection helper while every other module sees only
        // the read contract — the same arrangement StoreSettingsService uses.
        services.AddScoped<MediaLibrary>();
        services.AddScoped<IMediaLibrary>(provider => provider.GetRequiredService<MediaLibrary>());

        services.AddScoped<MediaStorageService>();
        services.AddScoped<IDocumentStore, DocumentStore>();

        // The seam, with the implementation that is honest about scanning nothing. A ClamAV adapter
        // replaces this registration and nothing else (docs/08-integrations.md §4).
        services.AddScoped<IVirusScanner, NoOpVirusScanner>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapAdminMediaEndpoints();

        endpoints.MapGroup("/store").MapStoreMediaEndpoints();
    }
}
