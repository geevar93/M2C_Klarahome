using KlaraHome.Modules.Reviews.Domain;

namespace KlaraHome.UnitTests.Reviews;

/// <summary>
/// How a product's average is worked out.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. An average has one right answer and
/// three failure modes that are all silent: a product with no reviews reported as nought out of
/// five, a rounding that disagrees with the half-star widget drawn from it, and a mean computed over
/// the wrong denominator. None of them throws, and all three reach a product page.
/// </remarks>
public sealed class RatingHistogramTests
{
    /// <summary>A product nobody has reviewed has no opinion, not a bad one.</summary>
    /// <remarks>
    /// Null and not zero. Nought out of five would sort every new product below every bad one in a
    /// rating sort, and would render as an empty five-star row on a page that has never been
    /// reviewed.
    /// </remarks>
    [Fact]
    public void An_unreviewed_product_has_no_average()
    {
        Assert.Null(RatingHistogram.Empty.Average);
        Assert.Equal(0, RatingHistogram.Empty.Total);
    }

    /// <summary>The mean is over the reviews, to one decimal place.</summary>
    [Fact]
    public void The_average_is_the_mean_of_every_score()
    {
        // Two fives, one four, one one: 15 over 4 is 3.75, which rounds to 3.8.
        var histogram = new RatingHistogram(One: 1, Two: 0, Three: 0, Four: 1, Five: 2);

        Assert.Equal(4, histogram.Total);
        Assert.Equal(3.8m, histogram.Average);
    }

    /// <summary>
    /// A midpoint rounds away from zero, and to one decimal place.
    /// </summary>
    /// <remarks>
    /// Banker's rounding is .NET's default for <c>Math.Round</c> and is the wrong answer here: 4.25
    /// would become 4.2 and 4.35 would become 4.4, so two products a tenth apart would render
    /// identically. One decimal place is what is stored as well as what is shown, so a page cannot
    /// display 4.2 while a sort orders on 4.1500001.
    /// </remarks>
    [Fact]
    public void A_midpoint_rounds_away_from_zero()
    {
        // Two fours and two fives: 18 over 4 is exactly 4.5.
        Assert.Equal(4.5m, new RatingHistogram(0, 0, 0, 2, 2).Average);

        // One three and one four: 7 over 2 is exactly 3.5.
        Assert.Equal(3.5m, new RatingHistogram(0, 0, 1, 1, 0).Average);

        // Three fours and one five: 17 over 4 is 4.25, which rounds up rather than to even.
        Assert.Equal(4.3m, new RatingHistogram(0, 0, 0, 3, 1).Average);
    }

    /// <summary>A histogram built from scores counts each into its own band.</summary>
    [Fact]
    public void Scores_are_counted_into_their_own_bands()
    {
        var histogram = RatingHistogram.From([5, 5, 4, 1, 3, 5]);

        Assert.Equal(1, histogram.One);
        Assert.Equal(0, histogram.Two);
        Assert.Equal(1, histogram.Three);
        Assert.Equal(1, histogram.Four);
        Assert.Equal(3, histogram.Five);
        Assert.Equal(6, histogram.Total);
    }

    /// <summary>
    /// A score outside one to five is not counted at all.
    /// </summary>
    /// <remarks>
    /// The database refuses one, the domain clamps one, and this is the third guard. A rogue score
    /// reaching the histogram would make the bands stop adding up to the count — which is the
    /// invariant the <c>ck_product_ratings_histogram</c> constraint exists to hold — and a zero or a
    /// six would drag every average on the platform in a direction nobody could explain.
    /// </remarks>
    [Fact]
    public void An_impossible_score_is_ignored()
    {
        var histogram = RatingHistogram.From([0, 5, 6, 4, -3]);

        Assert.Equal(2, histogram.Total);
        Assert.Equal(4.5m, histogram.Average);
    }
}
