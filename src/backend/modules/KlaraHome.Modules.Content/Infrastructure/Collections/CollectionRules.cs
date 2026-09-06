using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;

namespace KlaraHome.Modules.Content.Infrastructure.Collections;

/// <summary>
/// Reads and writes the <c>rules</c> document a rule-based collection carries.
/// </summary>
/// <remarks>
/// One <c>jsonb</c> column rather than a table of conditions, and that is the right shape for once.
/// A rule is read whole, written whole, and never queried into — nobody asks "which collections have
/// a brand condition" — which is exactly the case docs/03-database-design.md §1 allows JSON for. A
/// conditions table would be three rows per collection, a join on every read, and no query anybody
/// would ever run against it.
/// </remarks>
internal static class CollectionRules
{
    private static readonly JsonSerializerOptions Options = new(ContentJson.Options)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serialises a rule.</summary>
    /// <param name="rule">The rule.</param>
    public static string Write(CollectionRuleSet rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return JsonSerializer.Serialize(rule, Options);
    }

    /// <summary>
    /// Reads a rule back, falling through to one that matches nothing.
    /// </summary>
    /// <remarks>
    /// Nothing rather than everything, and it is the same reason <see cref="CollectionRuleSet.Empty"/>
    /// is what it is. A document written by an older build, or corrupted, must not be read as "every
    /// product the store sells" — that would put the whole catalogue on whatever page the collection
    /// was dropped onto, silently, on the next refresh.
    /// </remarks>
    /// <param name="json">The stored document.</param>
    public static CollectionRuleSet Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CollectionRuleSet.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<CollectionRuleSet>(json, Options) ?? CollectionRuleSet.Empty;
        }
        catch (JsonException)
        {
            return CollectionRuleSet.Empty;
        }
    }
}

/// <summary>
/// Decides whether one product satisfies a rule.
/// </summary>
/// <remarks>
/// <para>
/// Evaluated in memory against a <see cref="ProductProjection"/>, one product at a time, during a
/// walk of the catalogue. That is deliberate and is the only shape available: every fact a condition
/// is about belongs to the Catalog, Pricing or Vendors schemas, none of which this module may query,
/// and the projection source is the contract that hands them over.
/// </para>
/// <para>
/// It is also why <see cref="RuleField"/> is a closed list. A condition that named something outside
/// the projection would be a condition nothing could evaluate, and the failure would be a silently
/// empty collection rather than an error anybody could act on.
/// </para>
/// <para>
/// Every comparison is total: an absent value — a product with no brand, no rating, no publish date
/// — fails an <see cref="RuleOperator.In"/> and passes a <see cref="RuleOperator.NotIn"/>, which is
/// what a merchandiser means by "not one of these brands". Numeric comparisons against an absent
/// value fail, because "rating above four" cannot be true of a product nobody has reviewed.
/// </para>
/// </remarks>
internal static class CollectionRuleEvaluator
{
    /// <summary>Whether a product satisfies the rule.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="product">The product's winning offer, as the catalogue projected it.</param>
    /// <param name="now">The instant a "published within" condition is measured from.</param>
    public static bool Matches(CollectionRuleSet rule, ProductProjection product, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(product);

        if (rule.Conditions.Count == 0)
        {
            // A rule with no conditions matches nothing. See CollectionRuleSet.Empty for why that is
            // the safe reading rather than the obvious one.
            return false;
        }

        return rule.MatchAll
            ? rule.Conditions.All(condition => Satisfies(condition, product, now))
            : rule.Conditions.Any(condition => Satisfies(condition, product, now));
    }

    /// <summary>Orders the products a rule found.</summary>
    /// <param name="rule">The rule, which carries the sort.</param>
    /// <param name="matches">What it found.</param>
    public static IReadOnlyList<ProductProjection> Sort(
        CollectionRuleSet rule,
        IReadOnlyList<ProductProjection> matches)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(matches);

        // Every sort breaks its ties on the product id. Without it a collection's order would depend
        // on the order the walk happened to return equal rows in, and the same rule would produce a
        // visibly different page on each refresh.
        return rule.Sort switch
        {
            CollectionSort.PriceAscending =>
                [.. matches.OrderBy(row => row.SellingPrice).ThenBy(row => row.ProductId)],

            CollectionSort.PriceDescending =>
                [.. matches.OrderByDescending(row => row.SellingPrice).ThenBy(row => row.ProductId)],

            CollectionSort.Discount =>
                [.. matches
                    .OrderByDescending(row => Rendering.ContentRenderer.DiscountPercent(row.Mrp, row.SellingPrice))
                    .ThenBy(row => row.ProductId)],

            CollectionSort.Rating =>
                [.. matches
                    .OrderByDescending(row => row.RatingAverage ?? 0m)
                    .ThenByDescending(row => row.RatingCount)
                    .ThenBy(row => row.ProductId)],

            _ =>
                [.. matches
                    .OrderByDescending(row => row.PublishedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(row => row.ProductId)],
        };
    }

    private static bool Satisfies(RuleCondition condition, ProductProjection product, DateTimeOffset now)
        => condition.Field switch
        {
            RuleField.Category => MatchesText(condition, CategoryValues(product)),
            RuleField.Brand => MatchesText(condition, product.BrandId is null ? [] : [product.BrandId.Value.ToString()]),
            RuleField.Vendor => MatchesText(condition, [product.VendorId.ToString()]),
            RuleField.Price => MatchesNumber(condition, product.SellingPrice),
            RuleField.DiscountPercent =>
                MatchesNumber(condition, Rendering.ContentRenderer.DiscountPercent(product.Mrp, product.SellingPrice)),
            RuleField.Rating => MatchesNumber(condition, product.RatingAverage),
            RuleField.PublishedWithinDays => MatchesRecency(condition, product.PublishedAt, now),
            _ => MatchesText(condition, AttributeValues(condition.Key, product)),
        };

    /// <summary>
    /// The category and every ancestor of it, so a condition on a parent matches everything beneath.
    /// </summary>
    /// <remarks>
    /// The materialised path is what makes this a string split rather than a tree walk, and it is why
    /// the projection carries the path at all. A rule that said "furniture" and did not match sofas
    /// would be a rule nobody could write correctly.
    /// </remarks>
    private static List<string> CategoryValues(ProductProjection product)
    {
        var values = new List<string> { product.CategoryId.ToString() };

        foreach (var segment in product.CategoryPath.Split(
                     '/',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!values.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(segment);
            }
        }

        return values;
    }

    /// <summary>Every value the product carries for one attribute code.</summary>
    private static List<string> AttributeValues(string? code, ProductProjection product)
        => string.IsNullOrWhiteSpace(code)
            ? []
            : [.. product.Attributes
                .Where(attribute => string.Equals(attribute.Code, code, StringComparison.OrdinalIgnoreCase))
                .Select(attribute => attribute.Value)];

    private static bool MatchesText(RuleCondition condition, List<string> actual)
    {
        var hit = actual.Any(value => condition.Values.Contains(value, StringComparer.OrdinalIgnoreCase));

        return condition.Operator switch
        {
            RuleOperator.In => hit,
            RuleOperator.NotIn => !hit,

            // A greater-than on a brand id is a rule an editor could not have meant, and answering
            // "false" is the only reading that cannot silently include the wrong products.
            _ => false,
        };
    }

    private static bool MatchesNumber(RuleCondition condition, decimal? actual)
    {
        if (condition.Values.Count == 0)
        {
            return false;
        }

        if (condition.Operator is RuleOperator.In or RuleOperator.NotIn)
        {
            var hit = actual is not null
                      && condition.Values.Any(value =>
                          TryParse(value, out var parsed) && parsed == actual.Value);

            return condition.Operator == RuleOperator.In ? hit : !hit;
        }

        if (actual is null || !TryParse(condition.Values[0], out var bound))
        {
            return false;
        }

        return condition.Operator switch
        {
            RuleOperator.GreaterThan => actual.Value > bound,
            RuleOperator.AtLeast => actual.Value >= bound,
            RuleOperator.LessThan => actual.Value < bound,
            RuleOperator.AtMost => actual.Value <= bound,
            _ => false,
        };
    }

    /// <summary>
    /// Whether an offer went live recently enough.
    /// </summary>
    /// <remarks>
    /// The "new in" rule, and the only condition whose truth changes without anything changing. A
    /// product drifts out of it as the days pass, which is why rule-based collections are swept on a
    /// timer as well as refreshed on catalogue events — no event would ever fire for the product that
    /// merely got older.
    /// </remarks>
    private static bool MatchesRecency(RuleCondition condition, DateTimeOffset? publishedAt, DateTimeOffset now)
    {
        if (publishedAt is null
            || condition.Values.Count == 0
            || !TryParse(condition.Values[0], out var days)
            || days <= 0m)
        {
            return false;
        }

        var age = now - publishedAt.Value;

        return condition.Operator switch
        {
            RuleOperator.AtMost or RuleOperator.LessThan or RuleOperator.In =>
                age <= TimeSpan.FromDays((double)days),
            _ => age > TimeSpan.FromDays((double)days),
        };
    }

    private static bool TryParse(string value, out decimal parsed)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
}
