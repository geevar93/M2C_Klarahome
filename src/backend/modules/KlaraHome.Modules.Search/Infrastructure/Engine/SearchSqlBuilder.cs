using System.Globalization;
using System.Text;
using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Search.Infrastructure.Query;
using Npgsql;

namespace KlaraHome.Modules.Search.Infrastructure.Engine;

/// <summary>
/// Collects the parameters a generated statement needs, and names them.
/// </summary>
/// <remarks>
/// Every value that comes from a caller goes through here and comes back as <c>@p7</c>. Nothing in
/// this module concatenates a value into SQL — the statement's <em>shape</em> is generated from the
/// filters that were asked for, and every value in it is a parameter, which is what keeps a
/// generated query as safe as a written one.
/// </remarks>
internal sealed class SqlParameters
{
    private readonly List<NpgsqlParameter> _parameters = [];

    /// <summary>Every parameter, in the order they were added.</summary>
    public IReadOnlyList<NpgsqlParameter> All => _parameters;

    /// <summary>Adds a value and returns the placeholder that stands for it.</summary>
    /// <param name="value">The value. Null becomes SQL NULL.</param>
    public string Add(object? value)
    {
        var name = "p" + _parameters.Count.ToString(CultureInfo.InvariantCulture);

        _parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));

        return "@" + name;
    }
}

/// <summary>One filter, kept beside the dimension it belongs to.</summary>
/// <remarks>
/// The dimension is the whole point. A facet's counts are only useful if they are computed with
/// that facet's own filter lifted — a shopper who has chosen "beige" still needs to be told how
/// many creams there are, and a count computed with their own choice applied would say zero for
/// every colour but theirs. Keeping the filters labelled is what makes lifting one possible.
/// </remarks>
/// <param name="Dimension">What the filter narrows: <c>brand</c>, <c>price</c>, <c>attr.color</c>.</param>
/// <param name="Sql">The predicate, with its values already parameterised.</param>
internal sealed record SearchPredicate(string Dimension, string Sql);

/// <summary>
/// Builds the statements the PostgreSQL engine runs.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the engine that executes them so the SQL can be read as SQL. It is generated
/// rather than written because the shape genuinely varies — a search with three filters and a search
/// with none are different statements, and a single statement with eight optional predicates would
/// be a query plan chosen for the average case and wrong for every actual one.
/// </para>
/// <para>
/// The dimension names on the predicates are what make the facet counts correct. Each facet is
/// counted over the result set with its own dimension's filter removed and every other filter
/// applied, which is the only definition of a facet count that a shopper's clicking behaviour
/// agrees with.
/// </para>
/// </remarks>
internal static class SearchSqlBuilder
{
    /// <summary>The table every statement here reads.</summary>
    public const string Table = "search.product_search_projection";

    /// <summary>The dimension name the free-text condition is filed under.</summary>
    public const string TextDimension = "text";

    /// <summary>The prefix an attribute dimension carries, as <c>attr.color</c>.</summary>
    public const string AttributeDimensionPrefix = "attr.";

    /// <summary>The review scores the rating facet offers, best first.</summary>
    private static readonly int[] RatingThresholds = [4, 3, 2];

    /// <summary>The discounts the discount facet offers, deepest first.</summary>
    private static readonly int[] DiscountThresholds = [50, 30, 20, 10];

    /// <summary>
    /// Every predicate the request implies, labelled with the dimension it narrows.
    /// </summary>
    /// <param name="request">The parsed search.</param>
    /// <param name="tenantId">The store.</param>
    /// <param name="parameters">Collects the values.</param>
    public static List<SearchPredicate> Predicates(
        SearchRequest request,
        Guid tenantId,
        SqlParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);

        // The tenant and the active flag are dimensions like any other and are never lifted for a
        // facet. Naming them here rather than pasting them into every statement means there is one
        // place where a search can be shown to be confined to one store.
        var predicates = new List<SearchPredicate>
        {
            new("tenant", $"d.tenant_id = {parameters.Add(tenantId)}"),
            new("active", "d.is_active = TRUE"),
        };

        if (request.Query.HasText)
        {
            predicates.Add(new SearchPredicate(TextDimension, TextPredicate(request, parameters)));
        }

        if (request.CategoryId is { } categoryId)
        {
            // Array containment against the GIN index, which is what makes "everything under
            // Furniture" one index lookup from the id the shopper clicked. The alternatives were a
            // prefix match on the materialised path — which needs the caller to know the ancestors —
            // and a substring match, which cannot use an index at all.
            predicates.Add(new SearchPredicate(
                "category",
                $"d.category_ids @> ARRAY[{parameters.Add(categoryId)}]::uuid[]"));
        }

        if (request.BrandIds.Count > 0)
        {
            predicates.Add(new SearchPredicate(
                "brand",
                $"d.brand_id = ANY({parameters.Add(request.BrandIds.ToArray())})"));
        }

        if (request.VendorIds.Count > 0)
        {
            predicates.Add(new SearchPredicate(
                "vendor",
                $"d.vendor_id = ANY({parameters.Add(request.VendorIds.ToArray())})"));
        }

        if (request.MinPrice is { } minPrice)
        {
            predicates.Add(new SearchPredicate("price", $"d.price >= {parameters.Add(minPrice)}"));
        }

        if (request.MaxPrice is { } maxPrice)
        {
            predicates.Add(new SearchPredicate("price", $"d.price <= {parameters.Add(maxPrice)}"));
        }

        if (request.MinRating is { } rating)
        {
            predicates.Add(new SearchPredicate("rating", $"d.rating_average >= {parameters.Add(rating)}"));
        }

        if (request.MinDiscount is { } discount)
        {
            predicates.Add(new SearchPredicate("discount", $"d.discount_percent >= {parameters.Add(discount)}"));
        }

        if (request.InStockOnly)
        {
            predicates.Add(new SearchPredicate("availability", "d.is_available = TRUE"));
        }

        foreach (var attribute in request.Attributes)
        {
            predicates.Add(new SearchPredicate(
                AttributeDimensionPrefix + attribute.Code,
                AttributePredicate(attribute, parameters)));
        }

        return predicates;
    }

    /// <summary>
    /// Joins the predicates into a <c>WHERE</c> clause, optionally lifting one dimension.
    /// </summary>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="lift">The dimension to leave out, or null to apply them all.</param>
    public static string Where(IEnumerable<SearchPredicate> predicates, string? lift = null)
    {
        ArgumentNullException.ThrowIfNull(predicates);

        var applied = predicates
            .Where(predicate => lift is null || !string.Equals(predicate.Dimension, lift, StringComparison.Ordinal))
            .Select(predicate => predicate.Sql)
            .ToList();

        return applied.Count == 0 ? "TRUE" : string.Join(" AND ", applied);
    }

    /// <summary>
    /// The blended score: text relevance, popularity, review score and availability.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A weighted sum of four terms, each normalised into roughly the same range so the weights mean
    /// what an operator thinks they mean. Popularity is damped by <c>x / (1 + x)</c> — it is already
    /// a logarithm of units sold, and this bounds it above by one so that the best-selling product in
    /// the catalogue cannot swamp the other three terms. The review score is divided by five for the
    /// same reason. Availability is a flat bonus rather than a factor, because a product being in
    /// stock should lift it past an equal competitor and not past a far better match.
    /// </para>
    /// <para>
    /// Every cast is to <c>numeric</c>, deliberately. <c>ts_rank_cd</c> answers in <c>real</c>, the
    /// weights arrive as <c>numeric</c>, and leaving PostgreSQL to resolve the mixture would make the
    /// score's type depend on which terms the request happened to include — and the keyset cursor
    /// compares against it.
    /// </para>
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="parameters">Collects the weights.</param>
    public static string Score(SearchRequest request, SqlParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);

        var settings = request.Settings;
        var relevance = RelevanceExpression(request, parameters);

        return $"""
            (
                {parameters.Add(settings.RelevanceWeight)} * {relevance}
              + {parameters.Add(settings.PopularityWeight)} * (d.popularity_score / (1 + d.popularity_score))
              + {parameters.Add(settings.RatingWeight)} * (COALESCE(d.rating_average, 0) / 5.0)
              + CASE WHEN d.is_available THEN {parameters.Add(settings.AvailabilityBoost)} ELSE 0 END
            )
            """;
    }

    /// <summary>The expression a result set is ordered by, and the direction.</summary>
    /// <param name="request">The parsed search.</param>
    /// <param name="score">The score expression, already built.</param>
    public static (string Expression, bool Ascending) Sort(SearchRequest request, string score)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Sort switch
        {
            SearchSorts.PriceAscending => ("d.price", true),
            SearchSorts.PriceDescending => ("d.price", false),
            SearchSorts.Newest => ("COALESCE(EXTRACT(EPOCH FROM d.published_at)::numeric, 0)", false),
            SearchSorts.Discount => ("d.discount_percent::numeric", false),
            SearchSorts.Rating => ("COALESCE(d.rating_average, 0)::numeric", false),
            SearchSorts.Popularity => ("d.popularity_score", false),
            _ => (score, false),
        };
    }

    /// <summary>The columns a result card is built from. Nothing else is read off the disk.</summary>
    public static string HitColumns =>
        """
        d.id, d.variant_id, d.product_id, d.listing_id, d.product_slug, d.sku, d.variant_name,
        d.brand_id, d.brand_name, d.category_id, d.category_name, d.vendor_id, d.vendor_name,
        d.mrp, d.price, d.currency_code, d.discount_percent, d.rating_average, d.rating_count,
        d.is_available, d.is_cod_allowed, d.offer_count, d.primary_image_file_id
        """;

    /// <summary>
    /// The statement that returns one page of results.
    /// </summary>
    /// <remarks>
    /// The sort value is computed in a subquery and filtered in the outer one, because a keyset
    /// cursor has to compare against the value a row sorts by and that value is an expression rather
    /// than a column for the default sort. Wrapping unconditionally keeps one statement shape for
    /// all seven sorts rather than two that have to be kept in step.
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="score">The score expression.</param>
    /// <param name="parameters">Collects the values.</param>
    public static string Hits(
        SearchRequest request,
        IReadOnlyList<SearchPredicate> predicates,
        string score,
        SqlParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(parameters);

        var (sortExpression, ascending) = Sort(request, score);
        var direction = ascending ? "ASC" : "DESC";
        var comparison = ascending ? ">" : "<";

        var cursor = request.Cursor is { } position
            ? $"(s.sort_value {comparison} {parameters.Add(position.SortValue)} "
              + $"OR (s.sort_value = {parameters.Add(position.SortValue)} "
              + $"AND s.id {comparison} {parameters.Add(position.Id)}))"
            : "TRUE";

        return $"""
            SELECT s.* FROM (
                SELECT {HitColumns}, ({sortExpression})::numeric AS sort_value
                FROM {Table} d
                WHERE {Where(predicates)}
            ) s
            WHERE {cursor}
            ORDER BY s.sort_value {direction}, s.id {direction}
            LIMIT {parameters.Add(request.Size)}
            """;
    }

    /// <summary>The statement that counts everything the filters match.</summary>
    /// <param name="predicates">Every predicate.</param>
    public static string Total(IReadOnlyList<SearchPredicate> predicates)
        => $"SELECT COUNT(*)::bigint FROM {Table} d WHERE {Where(predicates)}";

    /// <summary>
    /// The statement that counts every facet, in one round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One statement with a branch per dimension rather than one statement per dimension. Eight
    /// round trips to compute the sidebar of a listing page would be eight times the latency for a
    /// result the shopper reads all at once, and PostgreSQL plans the branches independently anyway.
    /// </para>
    /// <para>
    /// Category is the one dimension counted with its <em>own</em> filter still applied, and that is
    /// deliberate. A category facet is a drill-down — inside Furniture, a shopper wants to see the
    /// sofas and the tables beneath it, not every category in the store — whereas brand, price and
    /// colour are multi-select and have to show what else could be chosen.
    /// </para>
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="parameters">Collects the values.</param>
    public static string Facets(
        SearchRequest request,
        IReadOnlyList<SearchPredicate> predicates,
        SqlParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(predicates);
        ArgumentNullException.ThrowIfNull(parameters);

        var limit = Math.Max(request.Settings.MaxFacetValues, 1);
        var branches = new List<string>
        {
            Grouped(
                "brand",
                "Brand",
                "d.brand_id::text",
                "MAX(d.brand_name)",
                Where(predicates, "brand") + " AND d.brand_id IS NOT NULL",
                "d.brand_id",
                limit,
                parameters),
            Grouped(
                "category",
                "Category",
                "d.category_id::text",
                "MAX(d.category_name)",
                Where(predicates),
                "d.category_id",
                limit,
                parameters),
            Grouped(
                "vendor",
                "Seller",
                "d.vendor_id::text",
                "MAX(d.vendor_name)",
                Where(predicates, "vendor"),
                "d.vendor_id",
                limit,
                parameters),
            Threshold(
                "rating",
                "Customer rating",
                "d.rating_average",
                " & up",
                RatingThresholds,
                Where(predicates, "rating"),
                parameters),
            Threshold(
                "discount",
                "Discount",
                "d.discount_percent",
                "% or more",
                DiscountThresholds,
                Where(predicates, "discount"),
                parameters),
            Availability(Where(predicates, "availability"), parameters),
        };

        if (PriceBands(request, predicates, parameters) is { } priceBranch)
        {
            branches.Add(priceBranch);
        }

        branches.AddRange(AttributeBranches(request, predicates, limit, parameters));

        return string.Join("\nUNION ALL\n", branches);
    }

    /// <summary>
    /// The free-text condition: the exact index, or the trigram one on the fallback pass.
    /// </summary>
    /// <remarks>
    /// The fallback is deliberately narrower than the exact pass — it matches on the product's own
    /// name and nothing else. A trigram match against the whole indexed blob would find a product
    /// because its category name is nearly the misspelling, which reads to a shopper as a search
    /// engine that has stopped listening.
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string TextPredicate(SearchRequest request, SqlParameters parameters)
    {
        if (!request.Fuzzy)
        {
            return $"d.search_vector @@ {parameters.Add(request.Query.TsQuery)}::tsquery";
        }

        // `%` is what uses the trigram index; the explicit similarity is what applies the store's own
        // threshold. The engine sets pg_trgm's session threshold to the same value immediately before
        // running this, so the two agree rather than one silently pruning what the other allows.
        var text = parameters.Add(request.Query.Normalised);

        return $"(d.product_name % {text} AND similarity(d.product_name, {text}) >= "
               + $"{parameters.Add(request.Settings.FuzzyThreshold)})";
    }

    /// <summary>How relevant a row is to the query, on the pass being run.</summary>
    /// <param name="request">The parsed search.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string RelevanceExpression(SearchRequest request, SqlParameters parameters)
    {
        if (!request.Query.HasText)
        {
            // A browse page has nothing to be relevant to. The term is dropped rather than given a
            // constant, so that popularity and rating decide the order — which is what makes a
            // category page open on the things people actually buy.
            return "0";
        }

        if (request.Fuzzy)
        {
            return $"similarity(d.product_name, {parameters.Add(request.Query.Normalised)})::numeric";
        }

        // ts_rank_cd rather than ts_rank: it accounts for how close the matched lexemes are to one
        // another, so "cotton cushion cover" ranks a product with that phrase in its name above one
        // that merely mentions cotton and cushions in different places.
        return $"ts_rank_cd(d.search_vector, {parameters.Add(request.Query.TsQuery)}::tsquery)::numeric";
    }

    /// <summary>The containment test for one attribute filter, ORed across the chosen values.</summary>
    /// <remarks>
    /// Containment rather than the key-exists operators, because containment against the whole
    /// column is what the GIN index answers. <c>attributes -&gt; 'color' ?| array['beige']</c> reads
    /// better and cannot use the index at all, which on a fifty-thousand-row table is the difference
    /// between a facet click and a page reload nobody waits for.
    /// </remarks>
    /// <param name="attribute">The filter.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string AttributePredicate(AttributeFilter attribute, SqlParameters parameters)
    {
        var clauses = new List<string>(attribute.Values.Count);

        foreach (var value in attribute.Values)
        {
            var document = JsonSerializer.Serialize(
                new Dictionary<string, string[]> { [attribute.Code] = [value] });

            clauses.Add($"d.attributes @> {parameters.Add(document)}::jsonb");
        }

        return clauses.Count == 0 ? "TRUE" : "(" + string.Join(" OR ", clauses) + ")";
    }

    /// <summary>A facet branch that groups on a column.</summary>
    /// <param name="key">The filter key.</param>
    /// <param name="label">The dimension's name.</param>
    /// <param name="value">The expression producing the machine value.</param>
    /// <param name="valueLabel">The expression producing the shopper-facing label.</param>
    /// <param name="where">The predicates that apply.</param>
    /// <param name="groupBy">What to group on.</param>
    /// <param name="limit">The most values to return.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string Grouped(
        string key,
        string label,
        string value,
        string valueLabel,
        string where,
        string groupBy,
        int limit,
        SqlParameters parameters)
        => $"""
            (SELECT {parameters.Add(key)}::text AS facet_key,
                    {parameters.Add(label)}::text AS facet_label,
                    {value} AS facet_value,
                    {valueLabel}::text AS value_label,
                    COUNT(*)::bigint AS value_count,
                    NULL::numeric AS band_from,
                    NULL::numeric AS band_to
             FROM {Table} d
             WHERE {where}
             GROUP BY {groupBy}
             ORDER BY value_count DESC
             LIMIT {parameters.Add(limit)})
            """;

    /// <summary>
    /// A facet branch whose values are "this much or more".
    /// </summary>
    /// <remarks>
    /// One branch for every threshold, joined by a <c>VALUES</c> list rather than written out. The
    /// counts overlap on purpose — a product rated 4.5 is counted under both "3 and up" and "4 and
    /// up" — because that is what the filter does, and a facet count that disagreed with the filter
    /// it triggers is worse than no count at all.
    /// </remarks>
    /// <param name="key">The filter key.</param>
    /// <param name="label">The dimension's name.</param>
    /// <param name="column">The column compared against each threshold.</param>
    /// <param name="suffix">What the label reads after the number.</param>
    /// <param name="thresholds">The thresholds offered.</param>
    /// <param name="where">The predicates that apply.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string Threshold(
        string key,
        string label,
        string column,
        string suffix,
        IReadOnlyList<int> thresholds,
        string where,
        SqlParameters parameters)
    {
        var values = string.Join(
            ", ",
            thresholds.Select(threshold => $"({parameters.Add(threshold)}::int)"));

        return $"""
            (SELECT {parameters.Add(key)}::text AS facet_key,
                    {parameters.Add(label)}::text AS facet_label,
                    t.threshold::text AS facet_value,
                    (t.threshold::text || {parameters.Add(suffix)}::text) AS value_label,
                    COUNT(*)::bigint AS value_count,
                    t.threshold::numeric AS band_from,
                    NULL::numeric AS band_to
             FROM {Table} d
             CROSS JOIN (VALUES {values}) AS t(threshold)
             WHERE {where} AND {column} >= t.threshold
             GROUP BY t.threshold
             ORDER BY t.threshold DESC)
            """;
    }

    /// <summary>The in-stock count, which is one value and therefore one branch.</summary>
    /// <param name="where">The predicates that apply.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string Availability(string where, SqlParameters parameters)
        => $"""
            (SELECT {parameters.Add("availability")}::text AS facet_key,
                    {parameters.Add("Availability")}::text AS facet_label,
                    {parameters.Add("in-stock")}::text AS facet_value,
                    {parameters.Add("In stock")}::text AS value_label,
                    COUNT(*)::bigint AS value_count,
                    NULL::numeric AS band_from,
                    NULL::numeric AS band_to
             FROM {Table} d
             WHERE {where} AND d.is_available = TRUE)
            """;

    /// <summary>
    /// The price facet, built from the bands the operator configured.
    /// </summary>
    /// <remarks>
    /// A join against a <c>VALUES</c> list of half-open ranges rather than a <c>CASE</c>, so the
    /// bounds come back on the row and the storefront can render the filter without parsing a label.
    /// The last band is open-ended: five bounds make six bands, the last of which has no upper limit.
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string? PriceBands(
        SearchRequest request,
        IReadOnlyList<SearchPredicate> predicates,
        SqlParameters parameters)
    {
        var bounds = request.Settings.PriceBands;

        if (bounds is null || bounds.Count == 0)
        {
            return null;
        }

        var rows = new List<string>(bounds.Count + 1);
        var lower = 0m;

        for (var index = 0; index <= bounds.Count; index++)
        {
            var upper = index < bounds.Count ? bounds[index] : (decimal?)null;

            var value = upper is { } bound
                ? string.Create(CultureInfo.InvariantCulture, $"{lower:0.##}-{bound:0.##}")
                : string.Create(CultureInfo.InvariantCulture, $"{lower:0.##}+");

            rows.Add(
                $"({parameters.Add(value)}::text, {parameters.Add(lower)}::numeric, "
                + $"{parameters.Add(upper)}::numeric)");

            if (upper is not { } next)
            {
                break;
            }

            lower = next;
        }

        return $"""
            (SELECT {parameters.Add("price")}::text AS facet_key,
                    {parameters.Add("Price")}::text AS facet_label,
                    b.band AS facet_value,
                    b.band AS value_label,
                    COUNT(*)::bigint AS value_count,
                    b.band_from AS band_from,
                    b.band_to AS band_to
             FROM {Table} d
             JOIN (VALUES {string.Join(", ", rows)}) AS b(band, band_from, band_to)
               ON d.price >= b.band_from AND (b.band_to IS NULL OR d.price < b.band_to)
             WHERE {Where(predicates, "price")}
             GROUP BY b.band, b.band_from, b.band_to
             ORDER BY b.band_from)
            """;
    }

    /// <summary>
    /// The attribute facets: one branch for the dimensions nobody has chosen yet, and one more for
    /// each dimension that has been.
    /// </summary>
    /// <remarks>
    /// The split is what makes the counts right when two attributes are filtered at once. A shopper
    /// who has chosen beige and size M must be told how many creams there are <em>in size M</em>, and
    /// how many size Ls there are <em>in beige</em> — which is two different result sets, and a
    /// single grouped query cannot produce both. The number of extra branches is bounded by the
    /// number of attribute filters a request may carry, which the options cap at eight.
    /// </remarks>
    /// <param name="request">The parsed search.</param>
    /// <param name="predicates">Every predicate.</param>
    /// <param name="limit">The most values one facet returns.</param>
    /// <param name="parameters">Collects the values.</param>
    private static IEnumerable<string> AttributeBranches(
        SearchRequest request,
        IReadOnlyList<SearchPredicate> predicates,
        int limit,
        SqlParameters parameters)
    {
        var chosen = request.Attributes.Select(attribute => attribute.Code).ToArray();

        // The dimensions the shopper has not touched: every filter applies, including all of theirs.
        yield return AttributeBranch(
            Where(predicates) + $" AND e.k <> ALL({parameters.Add(chosen)})",
            limit * 4,
            parameters);

        foreach (var attribute in request.Attributes)
        {
            yield return AttributeBranch(
                Where(predicates, AttributeDimensionPrefix + attribute.Code)
                + $" AND e.k = {parameters.Add(attribute.Code)}",
                limit,
                parameters);
        }
    }

    /// <summary>One attribute facet branch.</summary>
    /// <param name="where">The predicates that apply, including the key condition.</param>
    /// <param name="limit">The most rows this branch returns.</param>
    /// <param name="parameters">Collects the values.</param>
    private static string AttributeBranch(string where, int limit, SqlParameters parameters)
        => $"""
            (SELECT ({parameters.Add(AttributeDimensionPrefix)}::text || e.k) AS facet_key,
                    COALESCE(MAX(d.attribute_meta -> e.k ->> 'name'), e.k) AS facet_label,
                    v.value AS facet_value,
                    COALESCE(MAX(d.attribute_meta -> e.k -> 'values' ->> v.value), v.value) AS value_label,
                    COUNT(*)::bigint AS value_count,
                    NULL::numeric AS band_from,
                    NULL::numeric AS band_to
             FROM {Table} d
             CROSS JOIN LATERAL jsonb_each(d.attributes) AS e(k, arr)
             CROSS JOIN LATERAL jsonb_array_elements_text(arr) AS v(value)
             WHERE {where}
             GROUP BY e.k, v.value
             ORDER BY value_count DESC
             LIMIT {parameters.Add(limit)})
            """;

    /// <summary>
    /// The statement behind the autocomplete box: products, brands, categories and past queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four branches, ordered by kind and then by how popular each is. Products first because a
    /// shopper who sees the thing they wanted stops typing; past queries second because they are the
    /// only branch that can suggest a word this shopper has not typed yet; brands and categories
    /// last, as navigation rather than as answers.
    /// </para>
    /// <para>
    /// The past-query branch reads only searches that <em>found</em> something. Suggesting a query
    /// that returned nothing would be the store recommending its own dead ends.
    /// </para>
    /// </remarks>
    /// <param name="query">The parsed partial query.</param>
    /// <param name="tenantId">The store.</param>
    /// <param name="limit">The most suggestions to return.</param>
    /// <param name="parameters">Collects the values.</param>
    public static string Suggestions(
        NormalizedQuery query,
        Guid tenantId,
        int limit,
        SqlParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parameters);

        var tenant = parameters.Add(tenantId);
        var prefix = parameters.Add(query.Normalised + "%");
        var branchLimit = parameters.Add(limit);

        // The text condition is the same one a search uses, ORed with a plain prefix match on the
        // name. The index answers "cushion cover" once the shopper has typed a word; the prefix
        // match answers "cus" before they have.
        var text = query.HasText
            ? $"(d.search_vector @@ {parameters.Add(query.TsQuery)}::tsquery OR d.product_name ILIKE {prefix})"
            : $"d.product_name ILIKE {prefix}";

        var products = $"""
            (SELECT {parameters.Add("product")}::text AS kind,
                    d.variant_name::text AS label,
                    d.product_slug::text AS slug,
                    d.variant_id AS id,
                    d.primary_image_file_id AS image_file_id,
                    d.price::numeric AS price,
                    d.popularity_score::numeric AS rank_value
             FROM {Table} d
             WHERE d.tenant_id = {tenant} AND d.is_active = TRUE AND {text}
             ORDER BY d.popularity_score DESC, d.id DESC
             LIMIT {branchLimit})
            """;

        var brands = $"""
            (SELECT {parameters.Add("brand")}::text,
                    MAX(d.brand_name)::text,
                    MAX(d.brand_slug)::text,
                    d.brand_id,
                    NULL::uuid,
                    NULL::numeric,
                    COUNT(*)::numeric
             FROM {Table} d
             WHERE d.tenant_id = {tenant} AND d.is_active = TRUE
               AND d.brand_id IS NOT NULL AND d.brand_name ILIKE {prefix}
             GROUP BY d.brand_id
             ORDER BY COUNT(*) DESC
             LIMIT {branchLimit})
            """;

        var categories = $"""
            (SELECT {parameters.Add("category")}::text,
                    MAX(d.category_name)::text,
                    MAX(d.category_slug)::text,
                    d.category_id,
                    NULL::uuid,
                    NULL::numeric,
                    COUNT(*)::numeric
             FROM {Table} d
             WHERE d.tenant_id = {tenant} AND d.is_active = TRUE AND d.category_name ILIKE {prefix}
             GROUP BY d.category_id
             ORDER BY COUNT(*) DESC
             LIMIT {branchLimit})
            """;

        var queries = $"""
            (SELECT {parameters.Add("query")}::text,
                    q.normalised_query::text,
                    NULL::text,
                    NULL::uuid,
                    NULL::uuid,
                    NULL::numeric,
                    COUNT(*)::numeric
             FROM search.search_queries q
             WHERE q.tenant_id = {tenant}
               AND q.source = {parameters.Add("search")}
               AND q.result_count > 0
               AND q.created_at >= {parameters.Add(DateTimeOffset.UtcNow.AddDays(-90))}
               AND q.normalised_query LIKE {prefix}
             GROUP BY q.normalised_query
             ORDER BY COUNT(*) DESC
             LIMIT {branchLimit})
            """;

        var union = string.Join("\nUNION ALL\n", [products, queries, brands, categories]);

        return $"""
            SELECT s.* FROM (
            {union}
            ) s
            ORDER BY CASE s.kind
                         WHEN 'product' THEN 0
                         WHEN 'query' THEN 1
                         WHEN 'brand' THEN 2
                         ELSE 3
                     END,
                     s.rank_value DESC NULLS LAST
            LIMIT {parameters.Add(limit)}
            """;
    }

    /// <summary>Renders a statement for a log line, without its parameter values.</summary>
    /// <remarks>
    /// Diagnostics only. The values are deliberately absent: a search query is what a shopper typed,
    /// which is personal data under the DPDP Act, and a log that echoed it would put it in a file
    /// with a different retention policy from the table that is supposed to hold it.
    /// </remarks>
    /// <param name="sql">The statement.</param>
    public static string Flatten(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var builder = new StringBuilder(sql.Length);
        var wasSpace = false;

        foreach (var character in sql)
        {
            var isSpace = char.IsWhiteSpace(character);

            if (isSpace && wasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            wasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }
}
