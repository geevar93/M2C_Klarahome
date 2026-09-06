using KlaraHome.Modules.Catalog.Domain;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The three life cycles the Catalog module owns (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// A transition table is exactly the kind of algorithm the build sprint's rule 1 says to test while
/// writing it: the whole thing is a few dozen cells, the cost of asserting them is a few lines, and
/// a missing edge is invisible in review — nobody reads a <c>switch</c> and notices the case that is
/// not there.
/// </remarks>
public sealed class CatalogLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Every product transition the life cycle allows, and nothing else.</summary>
    private static readonly (ProductStatus From, ProductStatus To)[] AllowedProduct =
    [
        (ProductStatus.Draft, ProductStatus.PendingApproval),
        (ProductStatus.Draft, ProductStatus.Active),
        (ProductStatus.Draft, ProductStatus.Archived),
        (ProductStatus.PendingApproval, ProductStatus.Draft),
        (ProductStatus.PendingApproval, ProductStatus.Active),
        (ProductStatus.PendingApproval, ProductStatus.Archived),
        (ProductStatus.Active, ProductStatus.Inactive),
        (ProductStatus.Active, ProductStatus.Archived),
        (ProductStatus.Inactive, ProductStatus.Active),
        (ProductStatus.Inactive, ProductStatus.Archived),
    ];

    /// <summary>Every variant transition the life cycle allows, and nothing else.</summary>
    private static readonly (VariantStatus From, VariantStatus To)[] AllowedVariant =
    [
        (VariantStatus.Draft, VariantStatus.Active),
        (VariantStatus.Draft, VariantStatus.Archived),
        (VariantStatus.Active, VariantStatus.Inactive),
        (VariantStatus.Active, VariantStatus.Archived),
        (VariantStatus.Inactive, VariantStatus.Active),
        (VariantStatus.Inactive, VariantStatus.Archived),
    ];

    /// <summary>Every listing transition the life cycle allows, and nothing else.</summary>
    private static readonly (ListingStatus From, ListingStatus To)[] AllowedListing =
    [
        (ListingStatus.Draft, ListingStatus.Active),
        (ListingStatus.Draft, ListingStatus.Archived),
        (ListingStatus.Active, ListingStatus.Inactive),
        (ListingStatus.Active, ListingStatus.Archived),
        (ListingStatus.Inactive, ListingStatus.Active),
        (ListingStatus.Inactive, ListingStatus.Archived),
    ];

    [Fact]
    public void The_product_transition_table_allows_exactly_the_declared_edges_and_no_others()
    {
        foreach (var from in Enum.GetValues<ProductStatus>())
        {
            foreach (var to in Enum.GetValues<ProductStatus>())
            {
                var expected = Array.Exists(AllowedProduct, edge => edge.From == from && edge.To == to);

                Assert.Equal(expected, Product.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void The_variant_transition_table_allows_exactly_the_declared_edges_and_no_others()
    {
        foreach (var from in Enum.GetValues<VariantStatus>())
        {
            foreach (var to in Enum.GetValues<VariantStatus>())
            {
                var expected = Array.Exists(AllowedVariant, edge => edge.From == from && edge.To == to);

                Assert.Equal(expected, Variant.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void The_listing_transition_table_allows_exactly_the_declared_edges_and_no_others()
    {
        foreach (var from in Enum.GetValues<ListingStatus>())
        {
            foreach (var to in Enum.GetValues<ListingStatus>())
            {
                var expected = Array.Exists(AllowedListing, edge => edge.From == from && edge.To == to);

                Assert.Equal(expected, Listing.IsTransitionAllowed(from, to));
            }
        }
    }

    [Fact]
    public void Nothing_can_move_to_the_state_it_is_already_in()
    {
        foreach (var status in Enum.GetValues<ProductStatus>())
        {
            Assert.False(Product.IsTransitionAllowed(status, status));
        }

        foreach (var status in Enum.GetValues<VariantStatus>())
        {
            Assert.False(Variant.IsTransitionAllowed(status, status));
        }

        foreach (var status in Enum.GetValues<ListingStatus>())
        {
            Assert.False(Listing.IsTransitionAllowed(status, status));
        }
    }

    [Fact]
    public void Archived_is_terminal_for_all_three()
    {
        foreach (var to in Enum.GetValues<ProductStatus>())
        {
            Assert.False(Product.IsTransitionAllowed(ProductStatus.Archived, to));
        }

        foreach (var to in Enum.GetValues<VariantStatus>())
        {
            Assert.False(Variant.IsTransitionAllowed(VariantStatus.Archived, to));
        }

        foreach (var to in Enum.GetValues<ListingStatus>())
        {
            Assert.False(Listing.IsTransitionAllowed(ListingStatus.Archived, to));
        }
    }

    [Fact]
    public void A_refused_transition_leaves_the_product_untouched()
    {
        var product = Product.Draft("Cotton Cushion Cover", "cotton-cushion-cover", Guid.CreateVersion7(), null);

        Assert.False(product.TransitionTo(ProductStatus.Inactive, Now));
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Null(product.PublishedAt);
    }

    [Fact]
    public void The_publication_date_is_set_on_the_first_publication_and_never_moved()
    {
        var product = Product.Draft("Cotton Cushion Cover", "cotton-cushion-cover", Guid.CreateVersion7(), null);

        Assert.True(product.TransitionTo(ProductStatus.Active, Now));
        Assert.Equal(Now, product.PublishedAt);

        Assert.True(product.TransitionTo(ProductStatus.Inactive, Now.AddDays(1)));
        Assert.True(product.TransitionTo(ProductStatus.Active, Now.AddDays(2)));

        Assert.Equal(Now, product.PublishedAt);
    }

    [Fact]
    public void An_unpublished_product_can_be_republished_without_going_back_through_moderation()
    {
        // The rule that makes a seasonal withdrawal a merchandising decision rather than a content
        // change: unpublishing and republishing must not queue the product for review again.
        Assert.True(Product.IsTransitionAllowed(ProductStatus.Inactive, ProductStatus.Active));
        Assert.False(Product.IsTransitionAllowed(ProductStatus.Inactive, ProductStatus.PendingApproval));
    }

    [Fact]
    public void A_moderation_submission_can_only_be_decided_once()
    {
        var submission = ProductModeration.Submit(Guid.CreateVersion7(), null, null, Now);

        Assert.True(submission.Decide(ModerationStatus.Rejected, null, "Images are too small.", Now));
        Assert.Equal(ModerationStatus.Rejected, submission.Status);

        // The second moderator, a second later. Their decision is refused rather than overwriting
        // the first, which is what stops two people approving and rejecting the same submission.
        Assert.False(submission.Decide(ModerationStatus.Approved, null, null, Now.AddSeconds(1)));
        Assert.Equal(ModerationStatus.Rejected, submission.Status);
        Assert.Equal("Images are too small.", submission.Notes);
    }
}
