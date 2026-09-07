using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Content.Infrastructure.Seeding;

/// <summary>
/// A published home page for the demonstration catalogue.
/// </summary>
/// <remarks>
/// <para>
/// **The front page is the first thing anybody reviewing this sees, and it is CMS-authored.** The
/// storefront's home page is a block document, not a query — which is the right design, and it
/// means a database with a full catalogue and no home document still opens on an empty shop. Every
/// other demonstration seeder could have worked perfectly and the reviewer's first impression would
/// still be a blank page.
/// </para>
/// <para>
/// **It creates a home page only when there is not one already, and never edits one it did not
/// write.** A home page is the single most likely thing for somebody to have hand-authored in a
/// shared development database, and a seeder that reasserted its own version of it every deploy
/// would quietly discard that person's work. If a home page exists, this does nothing at all and
/// says so in the log — which is the outcome an operator can act on, rather than a silent no-op.
/// </para>
/// <para>
/// The product ids in the carousel are resolved from the demonstration SKUs at seed time rather
/// than hard-coded, because ids are minted per database and a literal one here would be a block
/// pointing at nothing on every deployment but the one it was written on.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="catalogue">Resolves the demonstration products the carousel points at.</param>
/// <param name="taxonomy">Resolves the demonstration categories the tiles point at.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports whether a page was written, and why not when it was not.</param>
internal sealed partial class DemoHomePageSeeder(
    ContentDbContext context,
    IProductProjectionSource catalogue,
    ICatalogTaxonomy taxonomy,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DemoHomePageSeeder> logger) : IDataSeeder
{
    /// <summary>The prefix every demonstration SKU carries.</summary>
    private const string DemoSkuPrefix = "DEMO-";

    /// <summary>The demonstration categories the tiles link to, in the order they are shown.</summary>
    private static readonly string[] TileSlugs =
        ["demo-cushion-covers", "demo-throws-and-quilts", "demo-stoneware", "demo-serveware"];

    /// <inheritdoc />
    public string Name => "Content.DemoHomePage";

    /// <summary>After the demonstration catalogue, whose products and categories it points at.</summary>
    public int Order => 918;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        var existing = await context.Pages
            .AnyAsync(page => page.Type == PageType.Home, cancellationToken)
            .ConfigureAwait(false);

        if (existing)
        {
            HomePageAlreadyAuthored(logger);
            return;
        }

        var productIds = await DemoProductIdsAsync(cancellationToken).ConfigureAwait(false);
        var tiles = await DemoTilesAsync(cancellationToken).ConfigureAwait(false);

        if (productIds.Count == 0)
        {
            // Nothing to merchandise. Writing the page anyway would produce an empty carousel,
            // which is a worse first impression than the absence this leaves behind.
            return;
        }

        var now = clock.UtcNow;
        var page = ContentPage.Create("home", PageType.Home, "Klara Home");

        page.Describe(
            "home",
            "Klara Home",
            "Hand-finished textiles, stoneware and lighting for Indian homes.",
            new SeoMetadata
            {
                MetaTitle = "Klara Home — hand-finished textiles and stoneware",
                MetaDescription = "Block-printed cushions, hand-quilted throws and wheel-thrown "
                                  + "stoneware, made in small batches across India.",

                // Demonstration content must never be indexed. It will sit on a staging host with
                // a public URL more often than anybody intends.
                NoIndex = true,
            },
            coverImageFileId: null,
            author: null,
            tags: [],
            now);

        page.SyncBlocks(
            [
                new BlockDraft(
                    null,
                    BlockType.RichText,
                    Json(new
                    {
                        heading = "Made in small batches",
                        body = "<p>Block-printed cottons from Sanganer, hand-quilted throws, and "
                               + "stoneware thrown one piece at a time above Coonoor. Everything "
                               + "here is finished by hand, so no two pieces are quite alike.</p>",
                        width = "wide",
                    }),
                    IsVisible: true,
                    StartsAt: null,
                    EndsAt: null),

                new BlockDraft(
                    null,
                    BlockType.CategoryTiles,
                    Json(new { heading = "Shop by room", columns = "4", items = tiles }),
                    IsVisible: true,
                    StartsAt: null,
                    EndsAt: null),

                new BlockDraft(
                    null,
                    BlockType.ProductCarousel,
                    Json(new
                    {
                        heading = "New this week",
                        limit = 12,
                        showPrice = true,
                        productIds,
                        collectionSlug = (string?)null,
                        viewAllHref = "/search",
                    }),
                    IsVisible: true,
                    StartsAt: null,
                    EndsAt: null),
            ],
            now);

        // Straight to Published, skipping the review. A demonstration home page sitting in Draft is
        // a home page nobody can see, and there is no author here for a reviewer to be a control on.
        //
        // `Publisher`, not `System`: the only edge `System` may take is Scheduled -> Published,
        // because that one belongs to the scheduler (PageLifecycle.Edges). Getting this wrong is
        // silent — `Transition` answers false and leaves the page in Draft rather than throwing —
        // which is exactly why the result is checked rather than discarded.
        if (!page.Transition(PageStatus.Published, PageActor.Publisher, now))
        {
            throw new InvalidOperationException(
                "The demonstration home page could not be published. The page life cycle has "
                + "changed and this seeder now writes a draft nobody can see.");
        }

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        HomePageSeeded(logger, productIds.Count, tiles.Count);
    }

    /// <summary>The product ids behind the demonstration SKUs, newest first.</summary>
    private async Task<List<Guid>> DemoProductIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        Guid? cursor = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await catalogue.EnumerateAsync(cursor, 200, cancellationToken).ConfigureAwait(false);

            ids.AddRange(page.Items
                .Where(offer => offer.Sku.StartsWith(DemoSkuPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(offer => offer.ProductId)
                .Distinct());

            if (page.NextVariantCursor is null)
            {
                break;
            }

            cursor = page.NextVariantCursor;
        }

        return [.. ids.Distinct()];
    }

    /// <summary>The category tiles, skipping any slug the catalogue does not have.</summary>
    /// <remarks>
    /// One listing call and a lookup rather than four calls: the taxonomy contract answers with the
    /// whole active tree, which for this catalogue is smaller than the round trips would be.
    /// Ordered by <see cref="TileSlugs"/> rather than by the tree, because the tiles are a
    /// merchandising sequence and not an alphabet.
    /// </remarks>
    private async Task<List<object>> DemoTilesAsync(CancellationToken cancellationToken)
    {
        var categories = await taxonomy
            .ListCategoriesAsync(activeOnly: true, cancellationToken)
            .ConfigureAwait(false);

        var bySlug = categories.ToDictionary(category => category.Slug, StringComparer.Ordinal);
        var tiles = new List<object>(TileSlugs.Length);

        foreach (var slug in TileSlugs)
        {
            if (bySlug.TryGetValue(slug, out var category))
            {
                tiles.Add(new { categoryId = category.Id, label = category.Name, imageFileId = (Guid?)null });
            }
        }

        return tiles;
    }

    /// <summary>Serialises a block's configuration the way the block catalogue validates it.</summary>
    private static string Json(object config) => JsonSerializer.Serialize(config);

    [LoggerMessage(
        EventId = 9105,
        Level = LogLevel.Information,
        Message = "Seeded the demonstration home page: {Products} product(s) in the carousel, {Tiles} category tile(s).")]
    private static partial void HomePageSeeded(ILogger logger, int products, int tiles);

    [LoggerMessage(
        EventId = 9106,
        Level = LogLevel.Information,
        Message = "A home page already exists, so the demonstration one was not written. "
                  + "Edit it in the back office, or delete it and re-run, if you want the demo front page.")]
    private static partial void HomePageAlreadyAuthored(ILogger logger);
}
