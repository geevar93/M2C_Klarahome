using System.Text;
using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Search.Infrastructure.Projection;

/// <summary>What one refresh did.</summary>
/// <param name="Written">How many rows were created or refreshed.</param>
/// <param name="Retired">How many were taken out of results because no offer survived.</param>
internal readonly record struct ProjectionOutcome(int Written, int Retired)
{
    /// <summary>Adds two outcomes, so a batched rebuild can total them.</summary>
    /// <param name="left">One outcome.</param>
    /// <param name="right">Another.</param>
    public static ProjectionOutcome operator +(ProjectionOutcome left, ProjectionOutcome right)
        => new(left.Written + right.Written, left.Retired + right.Retired);

    /// <summary>Adds two outcomes. The named form of <c>operator +</c>.</summary>
    /// <param name="left">One outcome.</param>
    /// <param name="right">Another.</param>
    public static ProjectionOutcome Add(ProjectionOutcome left, ProjectionOutcome right) => left + right;
}

/// <summary>
/// Builds and writes index rows from what the other modules say.
/// </summary>
/// <remarks>
/// <para>
/// The single place a row in this schema is written. Every route into it — an integration event, the
/// staleness sweep, an operator's rebuild — comes through here, so none of them can disagree about
/// what a row means. That matters more here than in most modules: three different writers would
/// eventually produce three subtly different definitions of "available", and the difference would
/// only show up as a search result a shopper cannot buy.
/// </para>
/// <para>
/// It reads three contracts and owns none of the facts. The catalogue says what the thing is and
/// which offer won its buy box; Pricing says what that offer actually costs today, which is not the
/// same as the price on the listing once a price list is running; Inventory says whether there is
/// any. Each is asked once per batch rather than once per row, which is what makes a two-hundred
/// variant rebuild three round trips instead of six hundred.
/// </para>
/// <para>
/// A variant with no purchasable offer is <em>deactivated</em> rather than deleted. Deleting it
/// would discard the sales history that orders the whole catalogue, and an offer paused for a week
/// would come back at the bottom of every result set it used to lead.
/// </para>
/// </remarks>
/// <param name="context">The Search data context.</param>
/// <param name="catalogue">Says what the thing is, and which offer won.</param>
/// <param name="prices">Says what that offer costs today.</param>
/// <param name="stock">Says whether there is any.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was rebuilt.</param>
internal sealed partial class SearchProjectionWriter(
    SearchDbContext context,
    IProductProjectionSource catalogue,
    IPriceCatalog prices,
    IStockAvailability stock,
    IClock clock,
    ILogger<SearchProjectionWriter> logger)
{
    /// <summary>
    /// How the two <c>jsonb</c> columns are written.
    /// </summary>
    /// <remarks>
    /// camelCase, because the SQL that reads them back names the keys — <c>-&gt; 'name'</c>,
    /// <c>-&gt; 'values'</c> — and a serialiser default that changed would break a query rather than
    /// a compile. Named here so the writer and the reader cannot drift.
    /// </remarks>
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.General) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Rebuilds the index rows for a set of variants.</summary>
    /// <param name="variantIds">The variants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ProjectionOutcome> RefreshVariantsAsync(
        IReadOnlyCollection<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(variantIds);

        if (variantIds.Count == 0)
        {
            return default;
        }

        var offers = await catalogue.FindByVariantsAsync(variantIds, cancellationToken).ConfigureAwait(false);

        return await ApplyAsync(variantIds, offers, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds the index rows for a page of the catalogue that has already been read.
    /// </summary>
    /// <remarks>
    /// The rebuild walks the catalogue itself, so it already holds the offers and asking for them a
    /// second time would double the cost of the one operation that touches every product in the
    /// store.
    /// </remarks>
    /// <param name="variantIds">The variants the page covered.</param>
    /// <param name="offers">Their offers, as the catalogue returned them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ProjectionOutcome> RefreshAsync(
        IReadOnlyCollection<Guid> variantIds,
        IReadOnlyList<ProductProjection> offers,
        CancellationToken cancellationToken)
        => ApplyAsync(variantIds, offers, cancellationToken);

    /// <summary>Turns a batch of offers into rows, and saves them.</summary>
    /// <param name="variantIds">Every variant this batch is about, including those with no offers.</param>
    /// <param name="offers">The offers the catalogue returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<ProjectionOutcome> ApplyAsync(
        IReadOnlyCollection<Guid> variantIds,
        IReadOnlyList<ProductProjection> offers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offers);

        var now = clock.UtcNow;
        var ids = variantIds.Distinct().ToList();

        var existing = await context.Documents
            .Where(document => ids.Contains(document.VariantId))
            .ToDictionaryAsync(document => document.VariantId, cancellationToken)
            .ConfigureAwait(false);

        var winners = offers.Where(offer => offer.IsBuyBox).ToList();
        var listingIds = winners.ConvertAll(offer => offer.ListingId);

        // One call each, for the whole batch. A row that asked Pricing and Inventory for itself
        // would make a rebuild of a fifty-thousand-product catalogue a hundred thousand round trips.
        var effective = listingIds.Count == 0
            ? new Dictionary<Guid, EffectivePrice>()
            : await prices.FindManyAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var availability = listingIds.Count == 0
            ? new Dictionary<Guid, StockAvailability>()
            : await stock.FindManyAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var byVariant = offers
            .GroupBy(offer => offer.VariantId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var written = 0;
        var retired = 0;

        foreach (var variantId in ids)
        {
            var group = byVariant.GetValueOrDefault(variantId) ?? [];
            var winner = group.FirstOrDefault(offer => offer.IsBuyBox);

            if (winner is null)
            {
                // Either the catalogue no longer has the variant, or every offer for it is paused.
                // Both mean the same thing to a shopper and neither is a reason to forget what it
                // has sold.
                if (existing.TryGetValue(variantId, out var stale) && stale.IsActive)
                {
                    stale.Deactivate(now);
                    retired++;
                }

                continue;
            }

            if (!existing.TryGetValue(variantId, out var document))
            {
                document = ProductSearchDocument.Open(variantId);
                context.Documents.Add(document);
            }

            var purchasable = group.Count(offer => offer.IsPurchasable);

            document.Describe(Content(winner, group, purchasable, effective), now);

            var held = availability.GetValueOrDefault(winner.ListingId);

            document.RecordAvailability(
                held?.QuantityAvailable ?? 0,
                held?.CanFulfil(1) ?? false,
                now);

            written++;
        }

        if (written > 0 || retired > 0)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            Rebuilt(logger, written, retired);
        }

        return new ProjectionOutcome(written, retired);
    }

    /// <summary>Assembles the snapshot one row is written from.</summary>
    /// <param name="winner">The offer that won the buy box.</param>
    /// <param name="group">Every offer for the variant, for the offer count.</param>
    /// <param name="purchasable">How many of those can actually be bought.</param>
    /// <param name="effective">What Pricing says the winning offer costs.</param>
    private static SearchDocumentContent Content(
        ProductProjection winner,
        List<ProductProjection> group,
        int purchasable,
        IReadOnlyDictionary<Guid, EffectivePrice> effective)
    {
        var price = effective.TryGetValue(winner.ListingId, out var quoted)
            ? quoted.UnitPrice
            : winner.SellingPrice;

        var mrp = effective.TryGetValue(winner.ListingId, out var declared)
            ? declared.Mrp
            : winner.Mrp;

        // The catalogue refuses an offer above its own MRP and so does the law, but this row carries
        // a CHECK saying the same thing and a projection must not be the thing that fails. Clamping
        // rather than throwing: an index that refused to build because one product's price list was
        // misconfigured would take the whole storefront's search down with it.
        if (mrp < price)
        {
            mrp = price;
        }

        var filterable = winner.Attributes.Where(attribute => attribute.IsFilterable).ToList();

        return new SearchDocumentContent(
            winner.ProductId,
            winner.ListingId,
            winner.VendorId,
            winner.Sku,
            winner.ProductName,
            winner.VariantName,
            winner.ProductSlug,
            winner.BrandId,
            winner.BrandName,
            winner.BrandSlug,
            winner.CategoryId,
            winner.CategoryName,
            winner.CategorySlug,
            winner.CategoryPath,
            winner.VendorName,
            winner.VendorSlug,
            winner.VendorRating,
            Keywords(winner),
            AttributesJson(filterable),
            AttributeMetaJson(filterable),
            mrp,
            price,
            winner.CurrencyCode,
            winner.RatingAverage,
            winner.RatingCount,
            winner.IsCodAllowed,
            winner.IsReturnable,
            Math.Max(purchasable, 1),
            winner.PrimaryImageFileId,
            winner.PublishedAt,
            IsActive: true);
    }

    /// <summary>
    /// The low-weight text: the summary, plus every searchable attribute's label.
    /// </summary>
    /// <remarks>
    /// Labels rather than machine values, because a shopper searches for "beige" and not for the
    /// option code that happens to spell it. Where the two are the same word this costs nothing;
    /// where they differ, the label is the one that was written for a human to read.
    /// </remarks>
    /// <param name="offer">The winning offer.</param>
    private static string? Keywords(ProductProjection offer)
    {
        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(offer.ShortDescription))
        {
            builder.Append(offer.ShortDescription);
        }

        foreach (var attribute in offer.Attributes.Where(candidate => candidate.IsSearchable))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(attribute.ValueLabel);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>The filter document: <c>{"color": ["beige"]}</c>.</summary>
    /// <param name="attributes">The filterable attributes.</param>
    private static string AttributesJson(List<ProductAttributeProjection> attributes)
    {
        var map = attributes
            .GroupBy(attribute => attribute.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(attribute => attribute.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return JsonSerializer.Serialize(map, Json);
    }

    /// <summary>
    /// The display document: <c>{"color": {"name": "Colour", "values": {"beige": "Beige"}}}</c>.
    /// </summary>
    /// <remarks>
    /// Separate from the filter document so that stays a plain map of arrays the GIN index can match
    /// containment against. One structure carrying both would have to be walked rather than indexed,
    /// which is the difference between a facet click and a page reload.
    /// </remarks>
    /// <param name="attributes">The filterable attributes.</param>
    private static string AttributeMetaJson(List<ProductAttributeProjection> attributes)
    {
        var map = new Dictionary<string, AttributeMeta>(StringComparer.Ordinal);

        foreach (var attribute in attributes)
        {
            if (!map.TryGetValue(attribute.Code, out var meta))
            {
                meta = new AttributeMeta(attribute.Name, new Dictionary<string, string>(StringComparer.Ordinal));
                map[attribute.Code] = meta;
            }

            meta.Values[attribute.Value] = attribute.ValueLabel;
        }

        return JsonSerializer.Serialize(map, Json);
    }

    /// <summary>The shape of one attribute inside the display document.</summary>
    /// <param name="Name">The attribute's shopper-facing name.</param>
    /// <param name="Values">Each machine value's label.</param>
    private sealed record AttributeMeta(string Name, Dictionary<string, string> Values);

    [LoggerMessage(
        EventId = 7920,
        Level = LogLevel.Debug,
        Message = "Search index: {Written} row(s) rebuilt, {Retired} retired")]
    private static partial void Rebuilt(ILogger logger, int written, int retired);
}
