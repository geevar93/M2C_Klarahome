using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Search.Infrastructure.Projection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Search.Infrastructure.Seeding;

/// <summary>
/// Builds the search projection over whatever the demonstration seeders just wrote.
/// </summary>
/// <remarks>
/// <para>
/// **Without this the demo catalogue is half-invisible, and confusingly so.** The storefront reads a
/// product page straight from the Catalog schema, but it reads the category pages, the search
/// results and every faceted listing from this module's projection — and that projection is fed by
/// <c>ListingPublished</c> and its siblings arriving through the outbox. A seeder writes rows
/// directly to a <c>DbContext</c>; it raises no integration events, and nothing would ever come to
/// index them. The result would be ten products that each render perfectly on their own URL and
/// appear in no category, no search and no listing — which reads as a bug in browse rather than as
/// missing data.
/// </para>
/// <para>
/// So the projection is built the same way an operator would build it after a bulk import: a full
/// walk of the catalogue through <c>IProductProjectionSource</c>, which is the published contract
/// this module already reads the catalogue through. Rebuilding a row from its sources is naturally
/// idempotent, so a re-run is free, and a deployment with no demo data walks an empty catalogue and
/// writes nothing.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="index">Walks the catalogue and writes the projection.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="logger">Reports what was indexed.</param>
internal sealed partial class DemoSearchIndexSeeder(
    SearchIndexService index,
    IHostEnvironment environment,
    ILogger<DemoSearchIndexSeeder> logger) : IDataSeeder
{
    /// <inheritdoc />
    public string Name => "Search.DemoIndex";

    /// <summary>Last. It reads what every other demonstration seeder has written.</summary>
    public int Order => 920;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        // Null bounds: from the beginning, to the service's own configured ceiling. A demonstration
        // catalogue is ten variants, and the ceiling exists for the case where somebody points this
        // at a real one.
        var result = await index.RebuildAsync(null, null, cancellationToken).ConfigureAwait(false);

        DemoIndexBuilt(logger, result.VariantsWalked, result.RowsWritten);
    }

    [LoggerMessage(
        EventId = 9103,
        Level = LogLevel.Information,
        Message = "Built the search projection over the demonstration catalogue: walked {Walked} variants, wrote {Written} rows.")]
    private static partial void DemoIndexBuilt(ILogger logger, int walked, int written);
}
