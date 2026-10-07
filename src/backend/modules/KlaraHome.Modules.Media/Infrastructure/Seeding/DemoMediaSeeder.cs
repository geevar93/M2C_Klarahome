using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Storage;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure.Persistence;
using KlaraHome.Modules.Media.Infrastructure.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Media.Infrastructure.Seeding;

/// <summary>
/// Uploads the demonstration imagery - one hero, one picture per demonstration category and one per
/// demonstration product - into the media library.
/// </summary>
/// <remarks>
/// <para>
/// **It goes through <see cref="MediaStorageService"/>, not around it.** The bytes are validated by
/// content, written to the public bucket and registered in <c>media.files</c> exactly as an admin
/// upload is, so what the storefront resolves and imgproxy serves is the real pipeline and not a
/// row that merely looks like one.
/// </para>
/// <para>
/// **The pictures are generated, not sourced.** <c>tools/demo-media/generate_demo_images.py</c> draws
/// them procedurally (block-print, weave, glaze and wood-grain motifs, with the product name on a
/// caption strip) and the JPEGs are embedded in this assembly, so there is no third-party artwork,
/// no licence to carry, and no network access at seed time.
/// </para>
/// <para>
/// Idempotent on the file name: a key that already has a live demonstration file is left alone, so
/// re-running adds only what is missing and never replaces a picture somebody swapped in. Attaching
/// the files to products, categories, menus and the home page is the job of the seeder in each
/// owning module, through <see cref="IDemoMediaCatalogue"/>.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Media data context.</param>
/// <param name="storage">Object storage, to say plainly when there is none rather than fail a deploy.</param>
/// <param name="uploads">The same storage service the admin upload uses.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="logger">Reports what was stored.</param>
internal sealed partial class DemoMediaSeeder(
    MediaDbContext context,
    IFileStorage storage,
    MediaStorageService uploads,
    IHostEnvironment environment,
    ILogger<DemoMediaSeeder> logger) : IDataSeeder
{
    /// <summary>The <c>OwnerType</c> every demonstration file is registered under.</summary>
    internal const string OwnerType = "DemoSeed";

    private const string ResourcePrefix = "KlaraHome.DemoImages.";

    /// <inheritdoc />
    public string Name => "Media.DemoImages";

    /// <summary>After the demo seller and before the catalogue, whose seeders attach what this stores.</summary>
    public int Order => 905;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        if (!storage.IsAvailable)
        {
            // Not an exception: sample imagery is not worth failing a deploy over, and the
            // storefront renders its placeholder treatment without it.
            StorageUnavailable(logger);
            return;
        }

        var assembly = typeof(DemoMediaSeeder).Assembly;

        var existing = await context.Files
            .AsNoTracking()
            .Where(file => file.OwnerType == OwnerType && file.Status != StoredFileStatus.Deleted)
            .Select(file => file.FileName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var have = existing
            .Select(name => Path.GetFileNameWithoutExtension(name))
            .ToHashSet(StringComparer.Ordinal);

        var stored = 0;

        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            // "KlaraHome.DemoImages.product-DEMO-CC-0001.jpg" -> "demo-product-DEMO-CC-0001":
            // the key the other modules' seeders look up.
            var key = "demo-" + Path.GetFileNameWithoutExtension(resource[ResourcePrefix.Length..]);

            if (have.Contains(key))
            {
                continue;
            }

            await using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            var result = await uploads
                .StoreAsync(
                    buffer.ToArray(),
                    key + ".jpg",
                    MediaIntent.Image,
                    MediaVisibility.Public,
                    OwnerType,
                    ownerId: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                UploadRefused(logger, key, result.Error.Code);
                continue;
            }

            stored++;
        }

        if (stored > 0)
        {
            DemoImagesStored(logger, stored);
        }
    }

    [LoggerMessage(
        EventId = 9201,
        Level = LogLevel.Information,
        Message = "Stored {Count} demonstration image(s) in the media library.")]
    private static partial void DemoImagesStored(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9202,
        Level = LogLevel.Warning,
        Message = "Demonstration image {Key} was refused by the media pipeline ({Code}); skipped.")]
    private static partial void UploadRefused(ILogger logger, string key, string code);

    [LoggerMessage(
        EventId = 9203,
        Level = LogLevel.Warning,
        Message = "Object storage is not configured, so the demonstration images were not stored. "
                  + "Set Storage__Endpoint (and credentials) for the seeder to upload them.")]
    private static partial void StorageUnavailable(ILogger logger);
}
