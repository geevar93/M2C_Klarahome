using System.Diagnostics;
using System.Globalization;
using System.Net;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The ceiling on a basket, and what a full one costs to render.
/// </summary>
/// <remarks>
/// <para>
/// A cart render prices every line through the quote engine, which reads the catalogue, the price
/// lists and every live promotion. Without a ceiling a basket is a denial-of-service primitive with
/// a friendly name — and the render is the hottest authenticated read the storefront has, so what it
/// costs as the basket grows is the number that matters.
/// </para>
/// <para>
/// The ceiling is turned down to six for this host so a full basket can be built in a few seconds
/// rather than in fifty round trips. The behaviour is the same at any ceiling; what is being asserted
/// is that there is one and that it counts what it claims to.
/// </para>
/// </remarks>
public sealed class CartLimitTests : CommerceTestBase
{
    private const int MaxLines = 6;
    private const int Renders = 7;

    /// <summary>
    /// A generous absolute ceiling. The real target is <c>09-nfr</c>'s 300 ms p95 for a read, which
    /// belongs to k6 against a provisioned environment; this is here to catch a render that has
    /// become seconds rather than to certify one that has not.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);

    /// <param name="fixture">The migrated database.</param>
    public CartLimitTests(KlaraHomeSchemaFixture fixture)
        : base(fixture)
        => Factory.Overrides["Carts:MaxLines"] = MaxLines.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A basket fills to its ceiling and refuses the next distinct offer, while more units of
    /// something already in it are still welcome.
    /// </summary>
    /// <remarks>
    /// The ceiling counts distinct offers, not units, and the difference is the whole of its
    /// usability: a shopper who has reached the limit must still be able to buy two of what they
    /// already chose.
    /// </remarks>
    [Fact]
    public async Task A_basket_fills_to_its_ceiling_and_still_takes_more_of_what_is_in_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var offers = await AcrossSellersAsync(scenario, sellers: 3, each: 3);

        var (shopper, _) = await SignedInShopperAsync();

        for (var index = 0; index < MaxLines; index++)
        {
            var filling = await scenario.AddAsync(shopper, offers[index]);
            Assert.Equal(index + 1, filling.GetProperty("lineCount").GetInt32());
        }

        var problem = await RefusedAsync(
            await scenario.TryAddAsync(shopper, offers[MaxLines].ListingId),
            HttpStatusCode.UnprocessableEntity,
            "CART_TOO_MANY_LINES");

        Assert.Contains(
            MaxLines.ToString(CultureInfo.InvariantCulture),
            problem.GetProperty("detail").GetString() ?? problem.GetRawText(),
            StringComparison.Ordinal);

        // A full basket still takes more of something it already holds.
        var deeper = await scenario.AddAsync(shopper, offers[0], quantity: 2);

        Assert.Equal(MaxLines, deeper.GetProperty("lineCount").GetInt32());

        Assert.Equal(
            3,
            deeper.GetProperty("lines").EnumerateArray()
                .First(line => line.GetProperty("listingId").GetGuid() == offers[0].ListingId)
                .GetProperty("quantity")
                .GetInt32());
    }

    /// <summary>
    /// A full basket across three sellers renders in about what a single-line basket costs, which is
    /// what "three contract calls for the whole basket" has to mean in practice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The assertion that carries the weight is the ratio, not the clock: it is the same host, the
    /// same database and the same moment, so a render that walked the lines — a catalogue call per
    /// line, a stock call per line, a serviceability call per line — would show up as a multiple
    /// however slow or fast the machine is. An absolute budget sits beside it as a smoke check.
    /// </para>
    /// <para>
    /// Medians rather than means, and a warm-up first, because the first render of the process pays
    /// for compilation that no shopper ever will.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_full_basket_renders_in_about_what_a_single_line_basket_costs()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var offers = await AcrossSellersAsync(scenario, sellers: 3, each: 2);

        var (full, _) = await SignedInShopperAsync();
        var (single, _) = await SignedInShopperAsync();

        foreach (var offer in offers)
        {
            await scenario.AddAsync(full, offer, quantity: 2);
        }

        var rendered = await scenario.CartAsync(full);

        Assert.Equal(MaxLines, rendered.GetProperty("lineCount").GetInt32());
        Assert.Equal(3, rendered.GetProperty("groups").EnumerateArray().Count());
        Assert.True(rendered.GetProperty("quote").GetProperty("grandTotal").GetDecimal() > 0m);

        await scenario.AddAsync(single, offers[0], quantity: 2);

        // Warm up both paths before either is timed.
        await MedianAsync(scenario, full, 3);
        await MedianAsync(scenario, single, 3);

        var fullest = await MedianAsync(scenario, full, Renders);
        var slightest = await MedianAsync(scenario, single, Renders);

        var ratio = fullest.TotalMilliseconds / Math.Max(slightest.TotalMilliseconds, 0.5);

        Assert.True(
            ratio < 4d,
            $"a {MaxLines}-line basket across three sellers took {fullest.TotalMilliseconds:F0} ms against "
            + $"{slightest.TotalMilliseconds:F0} ms for one line ({ratio:F1}x). The render is supposed to make "
            + "the same three batch calls whatever the basket holds, so a multiple here means it is walking "
            + "the lines.");

        Assert.True(
            fullest < Budget,
            $"a full basket rendered in {fullest.TotalMilliseconds:F0} ms, past the {Budget.TotalMilliseconds:F0} ms "
            + "this test treats as obviously broken.");
    }

    /// <summary>The median wall-clock cost of rendering one shopper's basket.</summary>
    private static async Task<TimeSpan> MedianAsync(CartScenario scenario, HttpClient shopper, int samples)
    {
        var measured = new List<TimeSpan>(samples);

        for (var sample = 0; sample < samples; sample++)
        {
            var started = Stopwatch.GetTimestamp();
            await scenario.CartAsync(shopper);
            measured.Add(Stopwatch.GetElapsedTime(started));
        }

        measured.Sort();

        return measured[measured.Count / 2];
    }

    /// <summary>Enough stocked offers, spread across several sellers, to fill a basket and overflow it.</summary>
    private static async Task<IReadOnlyList<SellableOffer>> AcrossSellersAsync(
        CartScenario scenario,
        int sellers,
        int each)
    {
        var offers = new List<SellableOffer>(sellers * each);

        for (var index = 0; index < sellers; index++)
        {
            var seller = await scenario.SellerAsync();

            for (var offer = 0; offer < each; offer++)
            {
                offers.Add(await scenario.OfferAsync(seller, sellingPrice: 149m + (offer * 50m)));
            }
        }

        return offers;
    }
}
