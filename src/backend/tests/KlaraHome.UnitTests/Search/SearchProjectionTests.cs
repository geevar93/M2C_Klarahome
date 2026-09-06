using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Logging;

namespace KlaraHome.UnitTests.Search;

/// <summary>
/// The arithmetic and the parsing on an index row.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. All four are pure functions with one
/// right answer and no observable failure: a wrong discount is a badge that disagrees with the price
/// beside it, a wrong popularity reorders the whole catalogue, a mis-parsed category path silently
/// removes a product from its own filter, and a click handle that does not round-trip loses the one
/// number that says whether the ranking is any good.
/// </remarks>
public sealed class SearchProjectionTests
{
    /// <summary>The discount badge is the saving as a whole percentage of MRP.</summary>
    /// <remarks>
    /// The same arithmetic as <c>EffectivePrice.DiscountPercent</c> in the Pricing contract, and
    /// deliberately so: a badge that disagreed between a search result and the product page it links
    /// to is the kind of defect nobody reports and everybody notices.
    /// </remarks>
    [Theory]
    [InlineData(1000, 800, 20)]
    [InlineData(1499, 999, 33)]
    [InlineData(999, 999, 0)]
    [InlineData(0, 0, 0)]
    public void A_discount_is_the_saving_as_a_percentage_of_mrp(decimal mrp, decimal price, int expected)
        => Assert.Equal(expected, ProductSearchDocument.DiscountOf(mrp, price));

    /// <summary>An offer priced above its MRP shows no discount rather than a negative one.</summary>
    /// <remarks>
    /// It should never happen — the catalogue refuses one and so does the law — but a projection must
    /// not be the thing that produces "-15% off".
    /// </remarks>
    [Fact]
    public void A_price_above_mrp_is_not_a_negative_discount()
        => Assert.Equal(0, ProductSearchDocument.DiscountOf(500m, 600m));

    /// <summary>
    /// Popularity is damped, so a runaway best-seller cannot flatten the ranking.
    /// </summary>
    /// <remarks>
    /// Sales follow a power law: a marketplace's best seller outsells its median by three or four
    /// orders of magnitude. A raw count as a ranking term would mean nothing but that one product
    /// ever appeared first. Ten thousand units is worth roughly three times ten.
    /// </remarks>
    [Fact]
    public void Popularity_grows_with_the_logarithm_of_sales()
    {
        var ten = ProductSearchDocument.PopularityOf(10);
        var thousand = ProductSearchDocument.PopularityOf(1_000);
        var tenThousand = ProductSearchDocument.PopularityOf(10_000);

        Assert.True(ten < thousand);
        Assert.True(thousand < tenThousand);

        // The whole point: four orders of magnitude in sales is under four times the score.
        Assert.True(tenThousand < ten * 4m);
    }

    /// <summary>Nothing sold is a score of zero, not a negative number.</summary>
    [Fact]
    public void Popularity_of_nothing_sold_is_zero()
    {
        Assert.Equal(0m, ProductSearchDocument.PopularityOf(0));
        Assert.Equal(0m, ProductSearchDocument.PopularityOf(-5));
    }

    /// <summary>A sale adds to the counter and moves the score.</summary>
    [Fact]
    public void Recording_a_sale_adds_to_what_was_already_sold()
    {
        var document = ProductSearchDocument.Open(Guid.CreateVersion7());

        document.RecordSale(3);
        var afterThree = document.PopularityScore;

        document.RecordSale(7);

        Assert.Equal(10, document.UnitsSold);
        Assert.True(document.PopularityScore > afterThree);
        Assert.Equal(ProductSearchDocument.PopularityOf(10), document.PopularityScore);
    }

    /// <summary>A cancellation that arrives as a zero or negative quantity changes nothing.</summary>
    [Fact]
    public void A_sale_of_no_units_is_ignored()
    {
        var document = ProductSearchDocument.Open(Guid.CreateVersion7());

        document.RecordSale(0);
        document.RecordSale(-4);

        Assert.Equal(0, document.UnitsSold);
    }

    /// <summary>
    /// The category filter's ancestor list is parsed out of the materialised path.
    /// </summary>
    /// <remarks>
    /// This is what makes "everything under Furniture" one index lookup from the id a shopper
    /// clicked. A path that parsed to the wrong set would silently remove a product from a filter it
    /// belongs in, and nothing would report it.
    /// </remarks>
    [Fact]
    public void Ancestors_are_every_id_in_the_path()
    {
        var furniture = Guid.CreateVersion7();
        var seating = Guid.CreateVersion7();
        var sofas = Guid.CreateVersion7();

        var ancestors = ProductSearchDocument.AncestorsOf($"/{furniture}/{seating}/{sofas}/");

        Assert.Equal([furniture, seating, sofas], ancestors);
    }

    /// <summary>A malformed path yields what it can rather than throwing.</summary>
    /// <remarks>
    /// A path that is not a list of identifiers is a catalogue defect, and the right answer to one is
    /// a product that is harder to filter to — not a rebuild that stops half-way through the store.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/not-a-guid/")]
    public void A_malformed_path_yields_no_ancestors(string? path)
        => Assert.Empty(ProductSearchDocument.AncestorsOf(path));

    /// <summary>
    /// The click handle carries the partition key as well as the id, and reads back exactly.
    /// </summary>
    /// <remarks>
    /// The reason it exists rather than the bare id being returned: the log is partitioned by month,
    /// and a lookup without the timestamp would search every month the table holds to find one row.
    /// </remarks>
    [Fact]
    public void A_click_handle_round_trips()
    {
        var createdAt = new DateTimeOffset(2026, 9, 6, 11, 42, 13, TimeSpan.Zero);
        var id = Guid.CreateVersion7();

        var token = SearchQueryRecorder.Token(createdAt, id);

        Assert.True(SearchQueryRecorder.TryReadToken(token, out var readCreatedAt, out var readId));
        Assert.Equal(createdAt, readCreatedAt);
        Assert.Equal(id, readId);
    }

    /// <summary>A handle that is not one answers false rather than throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64url")]
    public void A_malformed_click_handle_is_refused(string? token)
        => Assert.False(SearchQueryRecorder.TryReadToken(token, out _, out _));
}
