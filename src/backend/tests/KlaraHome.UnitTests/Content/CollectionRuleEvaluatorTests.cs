using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Collections;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// The rule engine behind a rule-based collection.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1. It is a pure function over a projection,
/// and it is the one piece of this module a non-technical person writes input for: a merchandiser
/// composes a rule and trusts the page it produces. A wrong comparison here is not an error anybody
/// sees — it is a campaign page quietly showing the wrong products, and the only symptom is a
/// merchandiser saying the collection "feels off".
/// </remarks>
public sealed class CollectionRuleEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CategoryId = Guid.Parse("0192aaaa-0000-7000-8000-000000000001");
    private static readonly Guid ParentCategoryId = Guid.Parse("0192aaaa-0000-7000-8000-000000000002");
    private static readonly Guid BrandId = Guid.Parse("0192bbbb-0000-7000-8000-000000000001");
    private static readonly Guid VendorId = Guid.Parse("0192cccc-0000-7000-8000-000000000001");

    /// <summary>A rule with no conditions matches nothing, not everything.</summary>
    /// <remarks>
    /// The safe reading rather than the obvious one. An unfinished rule that matched the whole
    /// catalogue would put every product the store sells on whatever page the collection was dropped
    /// onto, the moment somebody saved a draft.
    /// </remarks>
    [Fact]
    public void An_empty_rule_matches_nothing()
        => Assert.False(CollectionRuleEvaluator.Matches(CollectionRuleSet.Empty, Product(), Now));

    /// <summary>A condition on a parent category matches everything beneath it.</summary>
    /// <remarks>
    /// What the materialised path is carried for. A rule that said "furniture" and did not match
    /// sofas would be a rule nobody could write correctly.
    /// </remarks>
    [Fact]
    public void A_category_condition_matches_a_descendant()
    {
        var rule = Rule(new RuleCondition(RuleField.Category, null, RuleOperator.In, [ParentCategoryId.ToString()]));

        Assert.True(CollectionRuleEvaluator.Matches(rule, Product(), Now));
    }

    /// <summary>An unrelated category does not match.</summary>
    [Fact]
    public void An_unrelated_category_does_not_match()
    {
        var rule = Rule(new RuleCondition(RuleField.Category, null, RuleOperator.In, [Guid.NewGuid().ToString()]));

        Assert.False(CollectionRuleEvaluator.Matches(rule, Product(), Now));
    }

    /// <summary>Numeric comparisons are on the winning offer's price.</summary>
    /// <remarks>
    /// The operator is named as a string because the enum is internal to the module: an
    /// <c>[InlineData]</c> of it would make this method more accessible than its parameter type.
    /// </remarks>
    [Theory]
    [InlineData("AtMost", "999", true)]
    [InlineData("LessThan", "999", true)]
    [InlineData("LessThan", "800", false)]
    [InlineData("AtLeast", "800", true)]
    [InlineData("GreaterThan", "999", false)]
    public void Price_comparisons_are_on_the_winning_offer(string op, string bound, bool expected)
    {
        var rule = Rule(new RuleCondition(RuleField.Price, null, Enum.Parse<RuleOperator>(op), [bound]));

        Assert.Equal(expected, CollectionRuleEvaluator.Matches(rule, Product(price: 800m), Now));
    }

    /// <summary>An absent value fails a numeric comparison rather than passing it.</summary>
    /// <remarks>
    /// "Rating above four" cannot be true of a product nobody has reviewed, and a rule engine that
    /// treated null as zero would put every unreviewed product into every "top rated" collection.
    /// </remarks>
    [Fact]
    public void An_absent_rating_fails_a_numeric_comparison()
    {
        var rule = Rule(new RuleCondition(RuleField.Rating, null, RuleOperator.AtLeast, ["4"]));

        Assert.False(CollectionRuleEvaluator.Matches(rule, Product(rating: null), Now));
        Assert.True(CollectionRuleEvaluator.Matches(rule, Product(rating: 4.5m), Now));
    }

    /// <summary>"Not one of these brands" is true of a product with no brand at all.</summary>
    /// <remarks>
    /// What a merchandiser means by it. The opposite reading would silently exclude every unbranded
    /// product from a collection defined by what it is not.
    /// </remarks>
    [Fact]
    public void A_product_with_no_brand_is_not_one_of_them()
    {
        var rule = Rule(new RuleCondition(RuleField.Brand, null, RuleOperator.NotIn, [BrandId.ToString()]));

        Assert.True(CollectionRuleEvaluator.Matches(rule, Product(hasBrand: false), Now));
        Assert.False(CollectionRuleEvaluator.Matches(rule, Product(), Now));
    }

    /// <summary>An attribute condition is about the attribute it names.</summary>
    [Fact]
    public void An_attribute_condition_reads_its_own_attribute()
    {
        var colour = Rule(new RuleCondition(RuleField.Attribute, "color", RuleOperator.In, ["beige"]));
        var size = Rule(new RuleCondition(RuleField.Attribute, "size", RuleOperator.In, ["beige"]));

        Assert.True(CollectionRuleEvaluator.Matches(colour, Product(), Now));
        Assert.False(CollectionRuleEvaluator.Matches(size, Product(), Now));
    }

    /// <summary>The "new in" rule is measured from the offer's publish date.</summary>
    /// <remarks>
    /// The one condition whose truth changes without anything changing, which is why rule-based
    /// collections are swept on a timer as well as refreshed on catalogue events.
    /// </remarks>
    [Theory]
    [InlineData(10, true)]
    [InlineData(40, true)]
    [InlineData(3, false)]
    public void Recency_is_measured_from_the_publish_date(int days, bool expected)
    {
        var rule = Rule(new RuleCondition(RuleField.PublishedWithinDays, null, RuleOperator.AtMost, [days.ToString()]));
        var product = Product(publishedAt: Now.AddDays(-7));

        Assert.Equal(expected, CollectionRuleEvaluator.Matches(rule, product, Now));
    }

    /// <summary>Match-all needs every condition; match-any needs one.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Match_all_and_match_any_differ(bool matchAll, bool expected)
    {
        var rule = new CollectionRuleSet(
            matchAll,
            [
                new RuleCondition(RuleField.Brand, null, RuleOperator.In, [BrandId.ToString()]),
                new RuleCondition(RuleField.Price, null, RuleOperator.AtMost, ["100"]),
            ],
            CollectionSort.Newest,
            100,
            true);

        Assert.Equal(expected, CollectionRuleEvaluator.Matches(rule, Product(price: 800m), Now));
    }

    /// <summary>A comparison that makes no sense for a field matches nothing.</summary>
    /// <remarks>
    /// "Greater than" on a brand id is a rule an editor could not have meant, and answering false is
    /// the only reading that cannot silently include the wrong products.
    /// </remarks>
    [Fact]
    public void A_nonsensical_comparison_matches_nothing()
    {
        var rule = Rule(new RuleCondition(RuleField.Brand, null, RuleOperator.GreaterThan, [BrandId.ToString()]));

        Assert.False(CollectionRuleEvaluator.Matches(rule, Product(), Now));
    }

    /// <summary>Every sort breaks its ties on the product id.</summary>
    /// <remarks>
    /// Without it, a collection's order would depend on the order the catalogue walk happened to
    /// return equal rows in, and the same rule would produce a visibly different page on each refresh.
    /// </remarks>
    [Fact]
    public void Sorting_is_stable_across_equal_rows()
    {
        var first = Product(price: 500m, productId: Guid.Parse("0192dddd-0000-7000-8000-000000000001"));
        var second = Product(price: 500m, productId: Guid.Parse("0192dddd-0000-7000-8000-000000000002"));

        var rule = new CollectionRuleSet(true, [], CollectionSort.PriceAscending, 100, true);

        var forwards = CollectionRuleEvaluator.Sort(rule, [first, second]);
        var backwards = CollectionRuleEvaluator.Sort(rule, [second, first]);

        Assert.Equal(forwards.Select(row => row.ProductId), backwards.Select(row => row.ProductId));
    }

    /// <summary>The cheapest-first sort is cheapest first.</summary>
    [Fact]
    public void Price_ascending_puts_the_cheapest_first()
    {
        var dear = Product(price: 1_200m, productId: Guid.NewGuid());
        var cheap = Product(price: 400m, productId: Guid.NewGuid());

        var rule = new CollectionRuleSet(true, [], CollectionSort.PriceAscending, 100, true);

        Assert.Equal(cheap.ProductId, CollectionRuleEvaluator.Sort(rule, [dear, cheap])[0].ProductId);
    }

    private static CollectionRuleSet Rule(RuleCondition condition)
        => new(true, [condition], CollectionSort.Newest, 100, true);

    private static ProductProjection Product(
        decimal price = 800m,
        decimal? rating = 4.5m,
        bool hasBrand = true,
        DateTimeOffset? publishedAt = null,
        Guid? productId = null)
        => new(
            ListingId: Guid.NewGuid(),
            VendorId: VendorId,
            VendorName: "Klara Living",
            VendorSlug: "klara-living",
            VendorRating: 4.6m,
            VariantId: Guid.NewGuid(),
            ProductId: productId ?? Guid.Parse("0192eeee-0000-7000-8000-000000000001"),
            Sku: "KH-CUSH-001",
            ProductName: "Cotton Cushion Cover",
            VariantName: "Cotton Cushion Cover, Beige",
            ProductSlug: "cotton-cushion-cover",
            ShortDescription: "Soft cotton, 40x40.",
            CategoryId: CategoryId,
            CategoryName: "Cushions",
            CategorySlug: "cushions",
            CategoryPath: $"/{ParentCategoryId}/{CategoryId}/",
            BrandId: hasBrand ? BrandId : null,
            BrandName: hasBrand ? "Klara" : null,
            BrandSlug: hasBrand ? "klara" : null,
            IsPurchasable: true,
            IsBuyBox: true,
            Mrp: 1_000m,
            SellingPrice: price,
            CurrencyCode: "INR",
            RatingAverage: rating,
            RatingCount: rating is null ? 0 : 12,
            IsCodAllowed: true,
            IsReturnable: true,
            PrimaryImageFileId: null,
            PublishedAt: publishedAt ?? Now.AddDays(-30),
            Attributes:
            [
                new ProductAttributeProjection("color", "Colour", "beige", "Beige", true, true),
                new ProductAttributeProjection("size", "Size", "40x40", "40 x 40 cm", true, false),
            ]);
}
