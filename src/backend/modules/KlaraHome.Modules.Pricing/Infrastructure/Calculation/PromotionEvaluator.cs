using System.Globalization;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Pricing.Domain;

namespace KlaraHome.Modules.Pricing.Infrastructure.Calculation;

/// <summary>One basket line, reduced to the facts a promotion is allowed to see.</summary>
/// <param name="LineId">The caller's identifier for the line.</param>
/// <param name="ListingId">The offer.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="CategoryPath">
/// The product's category path, <c>/id/id/</c>. A promotion scoped to a parent category matches
/// everything beneath it, and this is what makes that a substring test rather than a tree walk.
/// </param>
/// <param name="BrandId">The product's brand, or null.</param>
/// <param name="Quantity">Units.</param>
/// <param name="UnitPrice">The resolved selling price per unit, inclusive of GST.</param>
internal sealed record PromotionLine(
    Guid LineId,
    Guid ListingId,
    Guid VendorId,
    string CategoryPath,
    Guid? BrandId,
    int Quantity,
    decimal UnitPrice)
{
    /// <summary>What the line is worth before any discount.</summary>
    public decimal Gross => GstCalculator.Round(UnitPrice * Quantity);
}

/// <summary>Everything about the shopper and the basket that a promotion may be conditioned on.</summary>
/// <param name="Lines">The basket.</param>
/// <param name="CustomerId">The shopper, or null for an anonymous quote.</param>
/// <param name="Segment">The cohort they belong to, for a segment-scoped campaign.</param>
/// <param name="CouponCode">The code they typed, normalised, or null.</param>
/// <param name="PaymentMethod">How they are paying.</param>
/// <param name="ShippingAmount">What shipping costs before any promotion touches it.</param>
/// <param name="IsFirstOrder">Whether this would be their first order, for a first-order campaign.</param>
/// <param name="CustomerRedemptions">
/// How many times this shopper has already redeemed each promotion, keyed by promotion. Absent
/// means never.
/// </param>
internal sealed record PromotionContext(
    IReadOnlyList<PromotionLine> Lines,
    Guid? CustomerId,
    string? Segment,
    string? CouponCode,
    QuotePaymentMethod PaymentMethod,
    decimal ShippingAmount,
    bool IsFirstOrder,
    IReadOnlyDictionary<Guid, int> CustomerRedemptions);

/// <summary>What the walk decided.</summary>
/// <param name="LineDiscounts">Discount attributed to each line by line-scoped promotions, keyed by line.</param>
/// <param name="OrderDiscounts">Each line's allocated share of order-scoped promotions, keyed by line.</param>
/// <param name="LinePromotions">Which promotions touched each line, keyed by line.</param>
/// <param name="ShippingDiscount">What came off shipping.</param>
/// <param name="Promotions">Every promotion considered, applied or not, with its reason.</param>
/// <param name="CouponRejection">
/// Why the code the shopper typed did nothing, or null. Separate from the promotion list because a
/// code that matches no promotion at all has no promotion to report against.
/// </param>
internal sealed record PromotionOutcome(
    IReadOnlyDictionary<Guid, decimal> LineDiscounts,
    IReadOnlyDictionary<Guid, decimal> OrderDiscounts,
    IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> LinePromotions,
    decimal ShippingDiscount,
    IReadOnlyList<QuotePromotion> Promotions,
    string? CouponRejection);

/// <summary>
/// The promotion engine: which offers apply to this basket, in what order, and for how much.
/// </summary>
/// <remarks>
/// <para>
/// Pure. It is handed the candidate promotions and the facts about the basket, and it returns
/// numbers — it reads no database, no clock and no caller. That is what makes a marketplace's most
/// argued-about arithmetic testable, and under the build sprint's rule 1 it is one of the two
/// things in this module that gets unit tests now rather than at Step 29.
/// </para>
/// <para>
/// Every promotion is reported, including the ones that did nothing and why. A shopper who typed a
/// code needs to be told it was for somebody's first order; a merchandiser looking at a campaign
/// that is not converting needs to see that its minimum basket value is never met. Both questions
/// are answered by the same list.
/// </para>
/// </remarks>
internal static class PromotionEvaluator
{
    /// <summary>The payment-method names <see cref="PromotionConditions.PaymentMethods"/> accepts.</summary>
    public const string PrepaidMethod = "prepaid";

    /// <summary>Cash on delivery, as a condition value.</summary>
    public const string CodMethod = "cod";

    /// <summary>
    /// Walks the candidate promotions in priority order and applies the ones that qualify.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is <see cref="Promotion.Priority"/> ascending, then id, so the walk is
    /// reproducible: two campaigns at the same priority always resolve the same way rather than in
    /// whatever order the database returned them.
    /// </para>
    /// <para>
    /// Stacking is enforced in both directions. An exclusive promotion cannot apply once anything
    /// else has, and nothing may apply after one has — otherwise "exclusive" would mean "first in
    /// the list", which is not what a merchandiser thinks they are choosing.
    /// </para>
    /// </remarks>
    /// <param name="candidates">
    /// The promotions to consider: live at the quote instant, and either automatic or matching the
    /// code the shopper typed. Filtering that is the caller's, because it is a query.
    /// </param>
    /// <param name="context">The basket and the shopper.</param>
    public static PromotionOutcome Evaluate(IReadOnlyList<Promotion> candidates, PromotionContext context)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(context);

        var lineDiscounts = context.Lines.ToDictionary(line => line.LineId, _ => 0m);
        var orderDiscounts = context.Lines.ToDictionary(line => line.LineId, _ => 0m);
        var linePromotions = context.Lines.ToDictionary(line => line.LineId, _ => new List<Guid>());
        var reports = new List<QuotePromotion>(candidates.Count);
        var shippingDiscount = 0m;
        var anythingApplied = false;
        var exclusiveApplied = false;

        var ordered = candidates
            .OrderBy(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Id)
            .ToList();

        foreach (var promotion in ordered)
        {
            if (exclusiveApplied)
            {
                reports.Add(Report(promotion, 0m, "An exclusive offer has already been applied to this order."));
                continue;
            }

            if (promotion.Stacking == StackingMode.Exclusive && anythingApplied)
            {
                reports.Add(Report(promotion, 0m, "This offer cannot be combined with the one already applied."));
                continue;
            }

            var matched = context.Lines.Where(line => Matches(promotion, line)).ToList();

            if (Reject(promotion, context, matched) is { } rejection)
            {
                reports.Add(Report(promotion, 0m, rejection));
                continue;
            }

            var awarded = Award(promotion, context, matched, lineDiscounts, orderDiscounts, shippingDiscount);

            if (awarded.Total <= 0m)
            {
                reports.Add(Report(promotion, 0m, "There is nothing on this order for the offer to discount."));
                continue;
            }

            foreach (var (lineId, amount) in awarded.PerLine)
            {
                if (promotion.AppliesTo == PromotionApplication.Order)
                {
                    orderDiscounts[lineId] += amount;
                }
                else
                {
                    lineDiscounts[lineId] += amount;
                }

                linePromotions[lineId].Add(promotion.Id);
            }

            shippingDiscount += awarded.Shipping;
            anythingApplied = true;
            exclusiveApplied = promotion.Stacking == StackingMode.Exclusive;

            reports.Add(Report(promotion, awarded.Total, reason: null));
        }

        return new PromotionOutcome(
            lineDiscounts,
            orderDiscounts,
            linePromotions.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Guid>)pair.Value),
            GstCalculator.Round(shippingDiscount),
            reports,
            CouponRejection(context, reports));
    }

    /// <summary>Whether a promotion's scope covers one line.</summary>
    /// <remarks>
    /// The dimensions intersect and the values within one are alternatives: a promotion scoped to
    /// a brand <em>and</em> a category covers that brand's products in that category, not either.
    /// An exclusion wins over every inclusion, which is what makes "all of Furniture except the
    /// clearance rail" a thing a merchandiser can express.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="line">The basket line.</param>
    public static bool Matches(Promotion promotion, PromotionLine line)
    {
        ArgumentNullException.ThrowIfNull(promotion);
        ArgumentNullException.ThrowIfNull(line);

        var scope = promotion.Scope;

        if (scope.ExcludedListingIds.Contains(line.ListingId))
        {
            return false;
        }

        if (scope.ListingIds.Count > 0 && !scope.ListingIds.Contains(line.ListingId))
        {
            return false;
        }

        if (scope.VendorIds.Count > 0 && !scope.VendorIds.Contains(line.VendorId))
        {
            return false;
        }

        if (scope.BrandIds.Count > 0 && (line.BrandId is not { } brand || !scope.BrandIds.Contains(brand)))
        {
            return false;
        }

        return scope.CategoryIds.Count == 0 || scope.CategoryIds.Exists(id => CoversCategory(line.CategoryPath, id));
    }

    /// <summary>
    /// Whether a category path passes through one category, which is how a promotion on a parent
    /// reaches every product beneath it.
    /// </summary>
    /// <param name="categoryPath">The line's materialised path, <c>/id/id/</c>.</param>
    /// <param name="categoryId">The scoped category.</param>
    private static bool CoversCategory(string categoryPath, Guid categoryId)
        => !string.IsNullOrEmpty(categoryPath)
           && categoryPath.Contains(
               string.Create(CultureInfo.InvariantCulture, $"/{categoryId}/"),
               StringComparison.OrdinalIgnoreCase);

    /// <summary>The reason a promotion does not apply to this basket, or null when it does.</summary>
    /// <remarks>
    /// Ordered from the cheapest check to the most specific, and — more importantly — from the most
    /// useful message to the least. A shopper told "this code is for first orders" knows what
    /// happened; one told "not eligible" writes to support.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="context">The basket and the shopper.</param>
    /// <param name="matched">The lines its scope covers.</param>
    private static string? Reject(Promotion promotion, PromotionContext context, List<PromotionLine> matched)
    {
        if (!promotion.HasUsesLeft)
        {
            return "This offer has been fully claimed.";
        }

        if (promotion.UsageLimitPerCustomer is { } perCustomer)
        {
            if (context.CustomerId is null)
            {
                return "Sign in to use this offer.";
            }

            var used = context.CustomerRedemptions.TryGetValue(promotion.Id, out var count) ? count : 0;

            if (used >= perCustomer)
            {
                return perCustomer == 1
                    ? "You have already used this offer."
                    : $"You have already used this offer {perCustomer} times.";
            }
        }

        if (promotion.Conditions.FirstOrderOnly && !context.IsFirstOrder)
        {
            return "This offer is for a first order only.";
        }

        if (promotion.Scope.Segments.Count > 0
            && (context.Segment is null
                || !promotion.Scope.Segments.Contains(context.Segment, StringComparer.OrdinalIgnoreCase)))
        {
            return "This offer is not available on your account.";
        }

        if (promotion.Conditions.PaymentMethods.Count > 0)
        {
            var method = context.PaymentMethod == QuotePaymentMethod.CashOnDelivery ? CodMethod : PrepaidMethod;

            if (!promotion.Conditions.PaymentMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                return method == CodMethod
                    ? "This offer is not available on cash on delivery."
                    : "This offer is only available on cash on delivery.";
            }
        }

        // Shipping promotions are the one kind that does not need a matching line: free shipping on
        // orders over a threshold is scoped to the basket, not to what is in it.
        if (matched.Count == 0 && promotion.AppliesTo != PromotionApplication.Shipping)
        {
            return "Nothing in your basket qualifies for this offer.";
        }

        var basketValue = promotion.AppliesTo == PromotionApplication.Shipping
            ? context.Lines.Sum(line => line.Gross)
            : matched.Sum(line => line.Gross);

        if (promotion.MinOrderValue > 0m && basketValue < promotion.MinOrderValue)
        {
            return $"Spend {Amount(promotion.MinOrderValue)} on qualifying items to use this offer.";
        }

        var quantity = matched.Sum(line => line.Quantity);

        return promotion.Conditions.MinQuantity > 1 && quantity < promotion.Conditions.MinQuantity
            ? $"Add {promotion.Conditions.MinQuantity} qualifying items to use this offer."
            : null;
    }

    /// <summary>What one promotion takes off, already allocated to lines and clamped.</summary>
    /// <param name="PerLine">The discount attributed to each line.</param>
    /// <param name="Shipping">The discount taken off shipping.</param>
    private readonly record struct PromotionAward(IReadOnlyList<(Guid LineId, decimal Amount)> PerLine, decimal Shipping)
    {
        /// <summary>Everything this promotion took off.</summary>
        public decimal Total => PerLine.Sum(part => part.Amount) + Shipping;
    }

    /// <summary>Computes one promotion's discount, capped and allocated.</summary>
    /// <param name="promotion">The promotion.</param>
    /// <param name="context">The basket.</param>
    /// <param name="matched">The lines its scope covers.</param>
    /// <param name="lineDiscounts">What each line has been discounted so far.</param>
    /// <param name="orderDiscounts">What each line has been allocated so far.</param>
    /// <param name="shippingDiscount">What shipping has been discounted so far.</param>
    private static PromotionAward Award(
        Promotion promotion,
        PromotionContext context,
        List<PromotionLine> matched,
        Dictionary<Guid, decimal> lineDiscounts,
        Dictionary<Guid, decimal> orderDiscounts,
        decimal shippingDiscount)
    {
        if (promotion.Type == PromotionType.FreeShipping || promotion.AppliesTo == PromotionApplication.Shipping)
        {
            var remaining = Math.Max(0m, context.ShippingAmount - shippingDiscount);
            var off = promotion.Type == PromotionType.FreeShipping
                ? remaining
                : Math.Min(remaining, ValueAgainst(promotion, remaining));

            return new PromotionAward([], GstCalculator.Round(Cap(promotion, off)));
        }

        // What is still discountable per line. A second promotion may not take a line below zero,
        // however generous the two of them are separately.
        decimal Headroom(PromotionLine line)
            => Math.Max(0m, line.Gross - lineDiscounts[line.LineId] - orderDiscounts[line.LineId]);

        var perLine = promotion.Type switch
        {
            PromotionType.Bogo => Bogo(promotion, matched, Headroom),
            PromotionType.Bundle => Bundle(promotion, matched, Headroom),
            _ => Proportional(promotion, matched, Headroom),
        };

        return new PromotionAward(perLine, 0m);
    }

    /// <summary>
    /// Percentage, fixed and tiered discounts, which all reduce to "an amount off these lines".
    /// </summary>
    /// <remarks>
    /// A line-scoped fixed amount comes off <em>each</em> matching line; an order-scoped one comes
    /// off the basket and is then allocated back across the matching lines pro rata, because the
    /// tax on a discount has to land on the lines whose rates it changed.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="matched">The lines its scope covers.</param>
    /// <param name="headroom">How much each line may still be discounted by.</param>
    private static List<(Guid LineId, decimal Amount)> Proportional(
        Promotion promotion,
        List<PromotionLine> matched,
        Func<PromotionLine, decimal> headroom)
    {
        var result = new List<(Guid, decimal)>(matched.Count);

        if (promotion.AppliesTo == PromotionApplication.Line)
        {
            var granted = 0m;

            foreach (var line in matched)
            {
                var room = headroom(line);
                var off = Math.Min(room, ValueAgainst(promotion, room));

                // The cap is on the promotion, not on the line, so it is consumed as the walk goes.
                if (promotion.MaxDiscount is { } max)
                {
                    off = Math.Min(off, Math.Max(0m, max - granted));
                }

                granted += off;

                if (off > 0m)
                {
                    result.Add((line.LineId, GstCalculator.Round(off)));
                }
            }

            return result;
        }

        var basket = matched.Sum(headroom);
        var total = GstCalculator.Round(Cap(promotion, Math.Min(basket, ValueAgainst(promotion, basket))));

        if (total <= 0m)
        {
            return result;
        }

        var shares = GstCalculator.AllocateProportionally(total, [.. matched.Select(headroom)]);

        for (var index = 0; index < matched.Count; index++)
        {
            if (shares[index] > 0m)
            {
                result.Add((matched[index].LineId, shares[index]));
            }
        }

        return result;
    }

    /// <summary>
    /// Buy X, get Y at a discount.
    /// </summary>
    /// <remarks>
    /// The earned units are the <b>cheapest</b> ones in the qualifying set, which is the universal
    /// retail convention and the only one a shopper is not surprised by. Sets are counted across
    /// every matching line together rather than per line, so two of one sofa and two of another
    /// earn what four of either would.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="matched">The lines its scope covers.</param>
    /// <param name="headroom">How much each line may still be discounted by.</param>
    private static List<(Guid LineId, decimal Amount)> Bogo(
        Promotion promotion,
        List<PromotionLine> matched,
        Func<PromotionLine, decimal> headroom)
    {
        var result = new List<(Guid, decimal)>();
        var buy = Math.Max(1, promotion.Conditions.BuyQuantity);
        var get = Math.Max(1, promotion.Conditions.GetQuantity);
        var percent = Math.Clamp(promotion.Conditions.GetDiscountPercent, 0m, 100m);

        var units = matched.Sum(line => line.Quantity);
        var free = units / (buy + get) * get;

        if (promotion.Conditions.MaxQuantityPerOrder is { } cap)
        {
            free = Math.Min(free, Math.Max(0, cap));
        }

        if (free <= 0 || percent <= 0m)
        {
            return result;
        }

        var granted = 0m;

        foreach (var line in matched.OrderBy(line => line.UnitPrice))
        {
            if (free <= 0)
            {
                break;
            }

            var taken = Math.Min(free, line.Quantity);
            var off = Math.Min(headroom(line), GstCalculator.Round(line.UnitPrice * taken * percent / 100m));

            if (promotion.MaxDiscount is { } max)
            {
                off = Math.Min(off, Math.Max(0m, max - granted));
            }

            free -= taken;
            granted += off;

            if (off > 0m)
            {
                result.Add((line.LineId, GstCalculator.Round(off)));
            }
        }

        return result;
    }

    /// <summary>
    /// A named set of offers sold together for one price.
    /// </summary>
    /// <remarks>
    /// Every listed offer must be in the basket, and the number of complete bundles is the smallest
    /// quantity among them. The discount is what the parts would have cost less the bundle price,
    /// allocated across the bundle's lines pro rata — so the tax on the saving lands on the lines
    /// whose rates produced it.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="matched">The lines its scope covers.</param>
    /// <param name="headroom">How much each line may still be discounted by.</param>
    private static List<(Guid LineId, decimal Amount)> Bundle(
        Promotion promotion,
        List<PromotionLine> matched,
        Func<PromotionLine, decimal> headroom)
    {
        var result = new List<(Guid, decimal)>();
        var required = promotion.Conditions.BundleListingIds;

        if (required.Count == 0)
        {
            return result;
        }

        var members = new List<PromotionLine>(required.Count);

        foreach (var listingId in required)
        {
            var line = matched.Find(candidate => candidate.ListingId == listingId);

            if (line is null)
            {
                return result;
            }

            members.Add(line);
        }

        var sets = members.Min(line => line.Quantity);

        if (promotion.Conditions.MaxQuantityPerOrder is { } cap)
        {
            sets = Math.Min(sets, Math.Max(0, cap));
        }

        if (sets <= 0)
        {
            return result;
        }

        var partsPrice = members.Sum(line => line.UnitPrice);
        var saving = GstCalculator.Round((partsPrice - promotion.Conditions.BundlePrice) * sets);

        if (saving <= 0m)
        {
            return result;
        }

        var total = GstCalculator.Round(Math.Min(Cap(promotion, saving), members.Sum(headroom)));
        var shares = GstCalculator.AllocateProportionally(total, [.. members.Select(line => line.UnitPrice * sets)]);

        for (var index = 0; index < members.Count; index++)
        {
            var off = Math.Min(shares[index], headroom(members[index]));

            if (off > 0m)
            {
                result.Add((members[index].LineId, GstCalculator.Round(off)));
            }
        }

        return result;
    }

    /// <summary>
    /// What the promotion is worth against a base, before any cap.
    /// </summary>
    /// <remarks>
    /// A tiered promotion picks the highest step the base clears, best first, so a basket that
    /// clears several gets the best of them rather than the sum. A step is read as a percentage
    /// unless the promotion says otherwise, because "spend more, save a bigger share" is what a
    /// ladder nearly always means.
    /// </remarks>
    /// <param name="promotion">The promotion.</param>
    /// <param name="baseAmount">What it is being computed against.</param>
    private static decimal ValueAgainst(Promotion promotion, decimal baseAmount)
    {
        if (baseAmount <= 0m)
        {
            return 0m;
        }

        if (promotion.Type == PromotionType.Tiered)
        {
            var step = promotion.Conditions.Tiers
                .Where(tier => baseAmount >= tier.MinAmount)
                .OrderByDescending(tier => tier.MinAmount)
                .FirstOrDefault();

            if (step is null)
            {
                return 0m;
            }

            return promotion.Conditions.TiersArePercentage
                ? GstCalculator.Round(baseAmount * Math.Clamp(step.Value, 0m, 100m) / 100m)
                : GstCalculator.Round(step.Value);
        }

        return promotion.Type == PromotionType.Percentage
            ? GstCalculator.Round(baseAmount * Math.Clamp(promotion.Value, 0m, 100m) / 100m)
            : GstCalculator.Round(promotion.Value);
    }

    /// <summary>Applies the promotion's own ceiling to a computed discount.</summary>
    /// <param name="promotion">The promotion.</param>
    /// <param name="amount">What it computed.</param>
    private static decimal Cap(Promotion promotion, decimal amount)
        => promotion.MaxDiscount is { } max ? Math.Min(amount, max) : amount;

    /// <summary>States one promotion's outcome for the caller.</summary>
    /// <param name="promotion">The promotion.</param>
    /// <param name="discount">What it took off.</param>
    /// <param name="reason">Why it did not, or null when it did.</param>
    private static QuotePromotion Report(Promotion promotion, decimal discount, string? reason)
        => new(
            promotion.Id,
            promotion.Code,
            promotion.Name,
            promotion.Type.ToString(),
            reason is null,
            GstCalculator.Round(discount),
            reason);

    /// <summary>
    /// Why the code the shopper typed did nothing, or null when it worked or none was typed.
    /// </summary>
    /// <remarks>
    /// A code that matches no promotion has no promotion to report against, so the reason is
    /// returned separately. That is the case the storefront turns into <c>COUPON_INVALID</c>.
    /// </remarks>
    /// <param name="context">The basket and the shopper.</param>
    /// <param name="reports">What the walk decided about each candidate.</param>
    private static string? CouponRejection(PromotionContext context, List<QuotePromotion> reports)
    {
        if (string.IsNullOrWhiteSpace(context.CouponCode))
        {
            return null;
        }

        var coded = reports.Where(report =>
            string.Equals(report.Code, context.CouponCode, StringComparison.OrdinalIgnoreCase)).ToList();

        if (coded.Count == 0)
        {
            return "That code is not valid.";
        }

        return coded.Exists(report => report.Applied) ? null : coded[0].Reason;
    }

    /// <summary>
    /// Formats an amount for a message a shopper reads. Whole rupees, rounded up, because a
    /// threshold message that says 499 when 499.50 is required is a message that lies.
    /// </summary>
    /// <param name="amount">The figure.</param>
    private static string Amount(decimal amount)
        => string.Create(CultureInfo.InvariantCulture, $"₹{Math.Ceiling(amount):0}");
}
