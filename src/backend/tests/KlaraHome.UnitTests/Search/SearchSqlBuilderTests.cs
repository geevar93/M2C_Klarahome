using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Infrastructure.Engine;
using KlaraHome.Modules.Search.Infrastructure.Query;

namespace KlaraHome.UnitTests.Search;

/// <summary>
/// The statements a search runs, and the one property that makes facet counts mean anything.
/// </summary>
/// <remarks>
/// <para>
/// Tested while writing them under the build sprint's rule 1. These assert the <em>shape</em> of the
/// generated SQL and never that it runs — proving that is Step 29's job, against a real database
/// with real rows. What is worth catching now is the rule the whole facet design rests on: a facet's
/// counts are computed with its own filter lifted and every other filter applied. Get that wrong and
/// every count is plausible, none is right, and the only symptom is a sidebar that says zero next to
/// every colour but the one the shopper already chose.
/// </para>
/// <para>
/// The second thing worth catching now is that no caller-supplied value is ever concatenated into a
/// statement. The shape is generated; the values are parameters. A test that reads the produced text
/// is the only way to say so.
/// </para>
/// </remarks>
public sealed class SearchSqlBuilderTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    /// <summary>
    /// A facet's counts are computed without its own filter, and with everybody else's.
    /// </summary>
    /// <remarks>
    /// The whole reason predicates are labelled with the dimension they narrow. A shopper who has
    /// chosen beige still has to be told how many creams there are — and told it within their chosen
    /// price band, because that filter has not been lifted.
    /// </remarks>
    [Fact]
    public void Lifting_a_dimension_drops_only_that_dimension()
    {
        var parameters = new SqlParameters();
        var request = Request(brandIds: [Guid.CreateVersion7()], minPrice: 500m);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);

        var all = SearchSqlBuilder.Where(predicates);
        var withoutBrand = SearchSqlBuilder.Where(predicates, "brand");

        Assert.Contains("d.brand_id = ANY(", all, StringComparison.Ordinal);
        Assert.DoesNotContain("d.brand_id = ANY(", withoutBrand, StringComparison.Ordinal);

        // Everything else survives, and that is the half people get wrong.
        Assert.Contains("d.price >= ", withoutBrand, StringComparison.Ordinal);
        Assert.Contains("d.tenant_id = ", withoutBrand, StringComparison.Ordinal);
        Assert.Contains("d.is_active = TRUE", withoutBrand, StringComparison.Ordinal);
    }

    /// <summary>Both halves of a price range belong to one dimension.</summary>
    /// <remarks>
    /// They are two predicates and one filter. Lifting only the lower bound for the price facet would
    /// count every band as though the shopper had asked for everything above their minimum, which is
    /// a set of numbers that adds up to more than the result set.
    /// </remarks>
    [Fact]
    public void A_price_range_is_lifted_as_one_dimension()
    {
        var parameters = new SqlParameters();
        var request = Request(minPrice: 500m, maxPrice: 2000m);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var withoutPrice = SearchSqlBuilder.Where(predicates, "price");

        Assert.DoesNotContain("d.price >= ", withoutPrice, StringComparison.Ordinal);
        Assert.DoesNotContain("d.price <= ", withoutPrice, StringComparison.Ordinal);
    }

    /// <summary>Each attribute is its own dimension, so one can be lifted without the others.</summary>
    /// <remarks>
    /// A shopper who has chosen beige and size M must be told how many creams there are <em>in size
    /// M</em>, and how many size Ls there are <em>in beige</em>. That is two different result sets,
    /// and it only works because the two filters are labelled separately.
    /// </remarks>
    [Fact]
    public void Two_attribute_filters_are_two_dimensions()
    {
        var parameters = new SqlParameters();

        var request = Request(attributes:
        [
            new AttributeFilter("color", ["beige"]),
            new AttributeFilter("size", ["m"]),
        ]);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);

        Assert.Equal(2, predicates.Count(predicate => predicate.Dimension.StartsWith("attr.", StringComparison.Ordinal)));

        var withoutColour = SearchSqlBuilder.Where(predicates, "attr.color");

        // The colour clause is gone and the size clause is not. Both are containment tests against
        // the same column, so the only way to tell them apart is by the parameter each carries.
        Assert.Equal(1, Occurrences(withoutColour, "d.attributes @> "));
    }

    /// <summary>Attribute filtering is containment, which is what the GIN index answers.</summary>
    /// <remarks>
    /// <c>attributes -&gt; 'color' ?| array['beige']</c> reads better and cannot use the index at
    /// all. On a fifty-thousand row table that is the difference between a facet click and a page
    /// reload nobody waits for.
    /// </remarks>
    [Fact]
    public void An_attribute_filter_is_a_containment_test()
    {
        var parameters = new SqlParameters();
        var request = Request(attributes: [new AttributeFilter("color", ["beige", "cream"])]);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var clause = predicates.Single(predicate => predicate.Dimension == "attr.color").Sql;

        Assert.Contains("d.attributes @> ", clause, StringComparison.Ordinal);

        // Two chosen values are alternatives, not requirements: a shopper filtering on beige and
        // cream wants both colours, not a product that is somehow both.
        Assert.Contains(" OR ", clause, StringComparison.Ordinal);

        var document = parameters.All.Single(parameter => parameter.Value is string text && text.Contains("beige", StringComparison.Ordinal));
        Assert.Equal("""{"color":["beige"]}""", document.Value);
    }

    /// <summary>A category filter is an array containment against the ancestor list.</summary>
    [Fact]
    public void A_category_filter_matches_the_ancestor_array()
    {
        var parameters = new SqlParameters();
        var categoryId = Guid.CreateVersion7();
        var request = Request(categoryId: categoryId);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var clause = predicates.Single(predicate => predicate.Dimension == "category").Sql;

        Assert.Contains("d.category_ids @> ARRAY[", clause, StringComparison.Ordinal);
        Assert.Contains(categoryId, parameters.All.Select(parameter => parameter.Value));
    }

    /// <summary>
    /// Nothing a caller supplied is concatenated into a statement.
    /// </summary>
    /// <remarks>
    /// The shape is generated from which filters were asked for; every value in it is a parameter.
    /// This is the test that says so, and it is the reason a generated query here is as safe as a
    /// written one.
    /// </remarks>
    [Fact]
    public void Every_caller_value_becomes_a_parameter()
    {
        var parameters = new SqlParameters();
        var brandId = Guid.CreateVersion7();

        var request = Request(
            query: "cushion",
            brandIds: [brandId],
            minPrice: 499m,
            attributes: [new AttributeFilter("color", ["beige"])]);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var where = SearchSqlBuilder.Where(predicates);

        Assert.DoesNotContain("cushion", where, StringComparison.Ordinal);
        Assert.DoesNotContain(brandId.ToString(), where, StringComparison.Ordinal);
        Assert.DoesNotContain("499", where, StringComparison.Ordinal);
        Assert.DoesNotContain("beige", where, StringComparison.Ordinal);

        Assert.Contains(brandId, parameters.All.Select(parameter => parameter.Value).OfType<Guid[]>().SelectMany(values => values));
    }

    /// <summary>A browse with no query has no text condition and scores on popularity alone.</summary>
    /// <remarks>
    /// The relevance term is dropped rather than given a constant, which is what makes a category
    /// page open on the things people actually buy rather than in whatever order the rows were found.
    /// </remarks>
    [Fact]
    public void A_browse_has_no_text_condition()
    {
        var parameters = new SqlParameters();
        var request = Request();

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var score = SearchSqlBuilder.Score(request, parameters);

        Assert.DoesNotContain(predicates, predicate => predicate.Dimension == SearchSqlBuilder.TextDimension);
        Assert.DoesNotContain("ts_rank_cd", score, StringComparison.Ordinal);
        Assert.Contains("popularity_score", score, StringComparison.Ordinal);
    }

    /// <summary>The exact pass uses the full-text index and the fuzzy pass uses the trigram one.</summary>
    /// <remarks>
    /// They match different things on purpose. A misspelling produces no lexeme the exact index could
    /// match — "cushin" and "cushion" share no stem — and a trigram match against the whole indexed
    /// blob would find a product because its category name is nearly the misspelling.
    /// </remarks>
    [Fact]
    public void The_fuzzy_pass_matches_the_product_name_by_trigram()
    {
        var exact = SearchSqlBuilder.Predicates(Request(query: "cushion"), Tenant, new SqlParameters())
            .Single(predicate => predicate.Dimension == SearchSqlBuilder.TextDimension).Sql;

        var fuzzy = SearchSqlBuilder.Predicates(Request(query: "cushin", fuzzy: true), Tenant, new SqlParameters())
            .Single(predicate => predicate.Dimension == SearchSqlBuilder.TextDimension).Sql;

        Assert.Contains("d.search_vector @@ ", exact, StringComparison.Ordinal);
        Assert.DoesNotContain("similarity(", exact, StringComparison.Ordinal);

        Assert.Contains("d.product_name % ", fuzzy, StringComparison.Ordinal);
        Assert.Contains("similarity(d.product_name, ", fuzzy, StringComparison.Ordinal);
        Assert.DoesNotContain("d.search_vector", fuzzy, StringComparison.Ordinal);
    }

    /// <summary>Every sort resolves to a numeric expression the keyset cursor can compare against.</summary>
    /// <remarks>
    /// The cursor holds a decimal. A sort whose expression came back as a timestamp or a real would
    /// page correctly on the first request and silently repeat or skip rows on the second.
    /// </remarks>
    [Theory]
    [InlineData(SearchSorts.Relevance, false)]
    [InlineData(SearchSorts.PriceAscending, true)]
    [InlineData(SearchSorts.PriceDescending, false)]
    [InlineData(SearchSorts.Newest, false)]
    [InlineData(SearchSorts.Discount, false)]
    [InlineData(SearchSorts.Rating, false)]
    [InlineData(SearchSorts.Popularity, false)]
    public void Only_cheapest_first_sorts_ascending(string sort, bool ascending)
    {
        var parameters = new SqlParameters();
        var request = Request(sort: sort);
        var score = SearchSqlBuilder.Score(request, parameters);

        var (expression, isAscending) = SearchSqlBuilder.Sort(request, score);

        Assert.Equal(ascending, isAscending);
        Assert.False(string.IsNullOrWhiteSpace(expression));
    }

    /// <summary>A page is keyset-paged on the sort value and the id, in that order.</summary>
    [Fact]
    public void A_cursor_filters_on_the_sort_value_and_then_the_id()
    {
        var parameters = new SqlParameters();
        var request = Request(cursor: new SearchCursor(0.42m, Guid.CreateVersion7()));

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var score = SearchSqlBuilder.Score(request, parameters);
        var sql = SearchSqlBuilder.Hits(request, predicates, score, parameters);

        Assert.Contains("s.sort_value < ", sql, StringComparison.Ordinal);
        Assert.Contains("AND s.id < ", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY s.sort_value DESC, s.id DESC", SearchSqlBuilder.Flatten(sql), StringComparison.Ordinal);
    }

    /// <summary>The facet statement carries one branch per dimension, and one per chosen attribute.</summary>
    /// <remarks>
    /// Six fixed dimensions, the price bands, the untouched attributes, and one more branch for each
    /// attribute the shopper has narrowed by — because that one's counts need its own filter lifted
    /// and the others left in place.
    /// </remarks>
    [Fact]
    public void The_facet_statement_has_a_branch_for_every_dimension()
    {
        var parameters = new SqlParameters();

        var request = Request(attributes:
        [
            new AttributeFilter("color", ["beige"]),
            new AttributeFilter("size", ["m"]),
        ]);

        var predicates = SearchSqlBuilder.Predicates(request, Tenant, parameters);
        var sql = SearchSqlBuilder.Facets(request, predicates, parameters);

        // brand, category, vendor, rating, discount, availability, price, unfiltered attributes,
        // and one branch for each of the two chosen attributes.
        Assert.Equal(10, Occurrences(sql, "UNION ALL") + 1);
    }

    /// <summary>Builds a request with only the parts a test cares about.</summary>
    private static SearchRequest Request(
        string? query = null,
        Guid? categoryId = null,
        IReadOnlyList<Guid>? brandIds = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        IReadOnlyList<AttributeFilter>? attributes = null,
        string sort = SearchSorts.Relevance,
        SearchCursor? cursor = null,
        bool fuzzy = false)
    {
        var parsed = SearchTextNormalizer.Parse(
            query,
            SearchVocabularySnapshot.Empty,
            maxLength: 120,
            prefixLastTerm: false);

        return new SearchRequest(
            parsed,
            categoryId,
            brandIds ?? [],
            [],
            minPrice,
            maxPrice,
            MinRating: null,
            MinDiscount: null,
            InStockOnly: false,
            attributes ?? [],
            sort,
            cursor,
            Size: 24,
            IncludeFacets: true,
            fuzzy,
            new SearchSettings());
    }

    /// <summary>How many times a fragment appears in a statement.</summary>
    private static int Occurrences(string sql, string fragment)
    {
        var count = 0;
        var index = sql.IndexOf(fragment, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = sql.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
