using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Catalog.Infrastructure.BuyBox;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The buy-box rule (docs/03-database-design.md §4.4).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1: this is a comparison over a configured
/// ordering, it decides which seller gets every sale on the platform, and getting a tie-break wrong
/// is invisible until two sellers have identical prices and the storefront starts flickering
/// between them.
/// </remarks>
public sealed class BuyBoxTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Alpha = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    [Fact]
    public void Nothing_wins_when_there_are_no_offers()
        => Assert.Null(BuyBoxResolver.Select([], new BuyBoxSettings()));

    [Fact]
    public void The_cheapest_offer_wins_under_the_default_rule()
    {
        var dear = Offer(Alpha, price: 499m, rating: 5m, dispatch: 12);
        var cheap = Offer(Beta, price: 449m, rating: 2m, dispatch: 48);

        var winner = BuyBoxResolver.Select([dear, cheap], new BuyBoxSettings());

        // Price is the first criterion, so it settles the question before rating or SLA is read.
        Assert.Equal(cheap.ListingId, winner!.ListingId);
    }

    [Fact]
    public void The_better_rated_seller_wins_at_the_same_price()
    {
        var worse = Offer(Alpha, price: 449m, rating: 3.2m, dispatch: 12);
        var better = Offer(Beta, price: 449m, rating: 4.7m, dispatch: 48);

        var winner = BuyBoxResolver.Select([worse, better], new BuyBoxSettings());

        Assert.Equal(better.ListingId, winner!.ListingId);
    }

    [Fact]
    public void The_faster_dispatch_wins_at_the_same_price_and_rating()
    {
        var slow = Offer(Alpha, price: 449m, rating: 4m, dispatch: 72);
        var fast = Offer(Beta, price: 449m, rating: 4m, dispatch: 12);

        var winner = BuyBoxResolver.Select([slow, fast], new BuyBoxSettings());

        Assert.Equal(fast.ListingId, winner!.ListingId);
    }

    [Fact]
    public void An_offer_known_to_be_in_stock_beats_one_known_to_be_out()
    {
        var empty = Offer(Alpha, price: 449m, rating: 4m, dispatch: 24) with { HasStock = false };
        var stocked = Offer(Beta, price: 449m, rating: 4m, dispatch: 24) with { HasStock = true };

        var winner = BuyBoxResolver.Select([empty, stocked], new BuyBoxSettings());

        Assert.Equal(stocked.ListingId, winner!.ListingId);
    }

    [Fact]
    public void Unknown_stock_beats_known_to_be_out_and_loses_to_known_to_be_in()
    {
        var unknown = Offer(Alpha, price: 449m, rating: 4m, dispatch: 24);
        var empty = Offer(Beta, price: 449m, rating: 4m, dispatch: 24) with { HasStock = false };

        Assert.Equal(unknown.ListingId, BuyBoxResolver.Select([unknown, empty], new BuyBoxSettings())!.ListingId);

        var stocked = Offer(Beta, price: 449m, rating: 4m, dispatch: 24) with { HasStock = true };

        Assert.Equal(stocked.ListingId, BuyBoxResolver.Select([unknown, stocked], new BuyBoxSettings())!.ListingId);
    }

    [Fact]
    public void An_unrated_seller_is_treated_as_average_by_default()
    {
        var unrated = Offer(Alpha, price: 449m, rating: null, dispatch: 24);
        var poor = Offer(Beta, price: 449m, rating: 2m, dispatch: 24);

        // The cold-start rule: a new seller must be able to win a first sale.
        Assert.Equal(unrated.ListingId, BuyBoxResolver.Select([unrated, poor], new BuyBoxSettings())!.ListingId);
    }

    [Fact]
    public void An_unrated_seller_ranks_last_when_the_operator_turns_that_off()
    {
        var unrated = Offer(Alpha, price: 449m, rating: null, dispatch: 24);
        var poor = Offer(Beta, price: 449m, rating: 2m, dispatch: 24);

        var settings = new BuyBoxSettings { TreatUnratedAsAverage = false };

        Assert.Equal(poor.ListingId, BuyBoxResolver.Select([unrated, poor], settings)!.ListingId);
    }

    [Fact]
    public void The_configured_order_of_the_criteria_is_what_decides()
    {
        var cheapAndSlow = Offer(Alpha, price: 449m, rating: 4m, dispatch: 96);
        var dearAndFast = Offer(Beta, price: 499m, rating: 4m, dispatch: 6);

        var byPrice = new BuyBoxSettings
        {
            Criteria = [BuyBoxCriteria.LandedPrice, BuyBoxCriteria.DispatchSla],
        };

        var bySla = new BuyBoxSettings
        {
            Criteria = [BuyBoxCriteria.DispatchSla, BuyBoxCriteria.LandedPrice],
        };

        Assert.Equal(cheapAndSlow.ListingId, BuyBoxResolver.Select([cheapAndSlow, dearAndFast], byPrice)!.ListingId);
        Assert.Equal(dearAndFast.ListingId, BuyBoxResolver.Select([cheapAndSlow, dearAndFast], bySla)!.ListingId);
    }

    [Fact]
    public void An_unknown_criterion_in_configuration_is_ignored_rather_than_fatal()
    {
        var dear = Offer(Alpha, price: 499m, rating: 4m, dispatch: 24);
        var cheap = Offer(Beta, price: 449m, rating: 4m, dispatch: 24);

        // A typo in the settings screen must not take the buy box out of the whole storefront.
        var settings = new BuyBoxSettings { Criteria = ["lowest-priec", BuyBoxCriteria.LandedPrice] };

        Assert.Equal(cheap.ListingId, BuyBoxResolver.Select([dear, cheap], settings)!.ListingId);
    }

    [Fact]
    public void The_oldest_offer_wins_when_every_criterion_ties()
    {
        var newer = Offer(Alpha, price: 449m, rating: 4m, dispatch: 24) with { PublishedAt = Morning.AddDays(30) };
        var older = Offer(Beta, price: 449m, rating: 4m, dispatch: 24) with { PublishedAt = Morning };

        // Without this the winner would depend on row order, the price in a search result would
        // disagree with the price on the product page, and nobody could reproduce it.
        Assert.Equal(older.ListingId, BuyBoxResolver.Select([newer, older], new BuyBoxSettings())!.ListingId);
        Assert.Equal(older.ListingId, BuyBoxResolver.Select([older, newer], new BuyBoxSettings())!.ListingId);
    }

    [Fact]
    public void Ranking_puts_the_winner_first_and_agrees_with_selection()
    {
        var offers = new[]
        {
            Offer(Alpha, price: 499m, rating: 4m, dispatch: 24),
            Offer(Beta, price: 449m, rating: 4m, dispatch: 24),
            Offer(Guid.CreateVersion7(), price: 475m, rating: 4m, dispatch: 24),
        };

        var settings = new BuyBoxSettings();
        var ranked = BuyBoxResolver.Rank(offers, settings);
        var winner = BuyBoxResolver.Select(offers, settings);

        // The "other sellers" panel is this list minus its head, so the two must never disagree.
        Assert.Equal(offers.Length, ranked.Count);
        Assert.Equal(winner!.ListingId, ranked[0].ListingId);
        Assert.Equal([449m, 475m, 499m], ranked.Select(offer => offer.SellingPrice));
    }

    /// <summary>An offer with a distinct, time-ordered listing id so the final tie-break is stable.</summary>
    private static BuyBoxCandidate Offer(Guid vendorId, decimal price, decimal? rating, int dispatch)
        => new(Guid.CreateVersion7(), vendorId, price, rating, dispatch, HasStock: null, Morning);
}
