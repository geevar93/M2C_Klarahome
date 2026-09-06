using KlaraHome.Modules.Reviews.Infrastructure;

namespace KlaraHome.UnitTests.Reviews;

/// <summary>
/// What name a review is signed with.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. The shortening is what stands between an
/// account's full name and a public product page (docs/07-security-compliance.md §4), and the
/// failure mode is a name published in full — which cannot be taken back once a crawler has it.
/// </remarks>
public sealed class DisplayNameTests
{
    /// <summary>A given name and a surname become a given name and an initial.</summary>
    [Fact]
    public void A_full_name_keeps_its_given_name_and_one_initial()
        => Assert.Equal("Priya S.", DisplayNames.Shorten("Priya Sharma"));

    /// <summary>
    /// The initial comes from the last part, not the second.
    /// </summary>
    /// <remarks>
    /// "Priya Ramesh Kumar" is called Priya Kumar, not Priya Ramesh. Taking the second part would
    /// quietly rename a large fraction of Indian customers on their own reviews.
    /// </remarks>
    [Fact]
    public void The_initial_comes_from_the_last_part_of_the_name()
        => Assert.Equal("Priya K.", DisplayNames.Shorten("Priya Ramesh Kumar"));

    /// <summary>A single name is left whole. There is nothing to shorten.</summary>
    [Fact]
    public void A_single_name_is_left_alone()
        => Assert.Equal("Anjali", DisplayNames.Shorten("Anjali"));

    /// <summary>Extra spacing does not produce an empty initial.</summary>
    [Fact]
    public void Untidy_spacing_is_ignored()
        => Assert.Equal("Ravi V.", DisplayNames.Shorten("  Ravi   Verma  "));

    /// <summary>
    /// An account that is gone is not named at all.
    /// </summary>
    /// <remarks>
    /// A review outlives the account that wrote it, and the fallback is what stops a deleted
    /// customer's review rendering with a blank byline that reads as a bug.
    /// </remarks>
    [Fact]
    public void An_absent_name_falls_back_to_the_anonymous_label()
    {
        Assert.Equal(DisplayNames.Anonymous, DisplayNames.Shorten(null));
        Assert.Equal(DisplayNames.Anonymous, DisplayNames.Shorten("   "));
    }
}
