using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Content.Application.Pages;
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
/// **The one exception to "never edits" is a page that is provably still this seeder's own.** The
/// first version of this page had no hero and no tile images, because there were no images to put
/// in them. A stack seeded back then would otherwise keep that bare page for ever, so a page whose
/// <em>entire</em> history is this seeder's publish note and whose blocks are still exactly the three
/// it wrote gets the hero and the tile pictures added, as one more snapshot. Anything else - an
/// edit, a later publish, a block added or removed, a hero already there - is left alone and logged.
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
/// <param name="media">Finds the demonstration images the hero and the tiles show.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports whether a page was written, and why not when it was not.</param>
internal sealed partial class DemoHomePageSeeder(
    ContentDbContext context,
    IProductProjectionSource catalogue,
    ICatalogTaxonomy taxonomy,
    IDemoMediaCatalogue media,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DemoHomePageSeeder> logger) : IDataSeeder
{
    /// <summary>What the publish snapshot says it was.</summary>
    private const string PublishNote = "Published with the demonstration data.";

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

        // Blocks included: the backfill below snapshots them, and a snapshot of a page whose blocks
        // were never loaded is a version with nothing in it.
        var existing = await context.Pages
            .Include(page => page.Blocks)
            .FirstOrDefaultAsync(page => page.Type == PageType.Home, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // A published page with an empty history is a page that went live without the snapshot
            // a real publish takes - this seeder's own earlier output, which wrote the status and
            // not the version. It read "Published, v0" beside "never been published". Adding the
            // missing snapshot is the only thing done to a page that was not written this run.
            if (existing.Status == PageStatus.Published
                && !await context.PageVersions
                    .AnyAsync(version => version.PageId == existing.Id, cancellationToken)
                    .ConfigureAwait(false))
            {
                TransitionPageCommandHandler.Snapshot(
                    existing,
                    PublishNote,
                    restoredFrom: null,
                    clock.UtcNow,
                    actorId: null,
                    context);

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                HomePageHistoryBackfilled(logger);
                return;
            }

            if (await TryAddImageryAsync(existing, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            HomePageAlreadyAuthored(logger);
            return;
        }

        var images = await media.ListAsync(cancellationToken).ConfigureAwait(false);
        var productIds = await DemoProductIdsAsync(cancellationToken).ConfigureAwait(false);
        var tiles = await DemoTilesAsync(images, cancellationToken).ConfigureAwait(false);

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

        var blocks = new List<BlockDraft>();

        if (HeroDraft(images) is { } hero)
        {
            blocks.Add(hero);
        }

        blocks.AddRange(
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
            ]);

        page.SyncBlocks(blocks, now);

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

        // The snapshot a real publish takes (TransitionPageCommandHandler), so the page is v1 with a
        // history to show rather than "Published, v0" over an empty version list.
        TransitionPageCommandHandler.Snapshot(page, PublishNote, restoredFrom: null, now, actorId: null, context);

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        HomePageSeeded(logger, productIds.Count, tiles.Count);
    }

    /// <summary>
    /// The hero block, or null when there is no hero image to put in it - the block's image is
    /// required, and a hero with none would fail the block catalogue's own validation.
    /// </summary>
    private static BlockDraft? HeroDraft(IReadOnlyDictionary<string, Guid> images)
    {
        if (!images.TryGetValue("demo-hero", out var heroId))
        {
            return null;
        }

        return new BlockDraft(
            null,
            BlockType.Hero,
            Json(new
            {
                headline = "Made by hand, finished for your home",
                subheadline = "Block-printed cushions, hand-quilted throws and wheel-thrown stoneware, "
                              + "made in small batches across India.",
                imageFileId = heroId,
                imageAlt = "A block-printed length of indigo cotton beside a stoneware bowl",
                ctaLabel = "Shop cushion covers",
                ctaHref = "/c/demo-cushion-covers",
                align = "left",
            }),
            IsVisible: true,
            StartsAt: null,
            EndsAt: null);
    }

    /// <summary>
    /// Adds the hero and the tile pictures to a page this seeder wrote and nobody has touched.
    /// Returns false - having changed nothing - for any page that is not provably that.
    /// </summary>
    private async Task<bool> TryAddImageryAsync(ContentPage page, CancellationToken cancellationToken)
    {
        if (page.Status != PageStatus.Published
            || page.Blocks.Any(block => block.Type == BlockType.Hero))
        {
            return false;
        }

        // Still exactly the three blocks this seeder wrote, in its order.
        var shape = page.Blocks.OrderBy(block => block.Position).Select(block => block.Type).ToList();

        if (!shape.SequenceEqual([BlockType.RichText, BlockType.CategoryTiles, BlockType.ProductCarousel]))
        {
            return false;
        }

        // And a history that is nothing but this seeder's publish note: any later publish by a person
        // carries a different note (or none), and so does a restore.
        var notes = await context.PageVersions
            .Where(version => version.PageId == page.Id)
            .Select(version => version.Note)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (notes.Count == 0 || notes.Any(note => note != PublishNote))
        {
            return false;
        }

        var images = await media.ListAsync(cancellationToken).ConfigureAwait(false);
        var hero = HeroDraft(images);
        var tiles = await DemoTilesAsync(images, cancellationToken).ConfigureAwait(false);

        if (hero is null && !images.Keys.Any(key => key.StartsWith("demo-category-", StringComparison.Ordinal)))
        {
            // Nothing to add yet; the next run, after the images exist, will.
            return false;
        }

        var drafts = new List<BlockDraft>();

        if (hero is not null)
        {
            drafts.Add(hero);
        }

        foreach (var block in page.Blocks.OrderBy(block => block.Position))
        {
            var config = block.Type == BlockType.CategoryTiles
                ? Json(new { heading = "Shop by room", columns = "4", items = tiles })
                : block.Config;

            drafts.Add(new BlockDraft(block.Id, block.Type, config, block.IsVisible, block.StartsAt, block.EndsAt));
        }

        var now = clock.UtcNow;
        page.SyncBlocks(drafts, now);

        TransitionPageCommandHandler.Snapshot(page, PublishNote, restoredFrom: null, now, actorId: null, context);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        HomePageImageryAdded(logger);

        return true;
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
    private async Task<List<object>> DemoTilesAsync(
        IReadOnlyDictionary<string, Guid> images,
        CancellationToken cancellationToken)
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
                // The category's own tile image if it has one, else the demonstration picture for it.
                Guid? imageFileId = category.ImageFileId
                                    ?? (images.TryGetValue("demo-category-" + slug, out var imageId) ? imageId : null);

                tiles.Add(new { categoryId = category.Id, label = category.Name, imageFileId });
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

    [LoggerMessage(
        EventId = 9110,
        Level = LogLevel.Information,
        Message = "Added the hero and the category tile pictures to the demonstration home page, which was still untouched.")]
    private static partial void HomePageImageryAdded(ILogger logger);

    [LoggerMessage(
        EventId = 9107,
        Level = LogLevel.Information,
        Message = "The published home page had no version history; recorded the snapshot a publish takes.")]
    private static partial void HomePageHistoryBackfilled(ILogger logger);
}
