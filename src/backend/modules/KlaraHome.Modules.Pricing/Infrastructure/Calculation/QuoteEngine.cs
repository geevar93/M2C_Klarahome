using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Pricing.Infrastructure.Calculation;

/// <summary>
/// The one calculation engine (docs/01-architecture.md §2.1, docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// It assembles the pieces and does no arithmetic of its own worth arguing about: the price walk is
/// <see cref="PriceResolver"/>'s, the rate lookup is <see cref="TaxRateResolver"/>'s, the discount
/// decisions are <see cref="PromotionEvaluator"/>'s and the GST split is
/// <see cref="GstCalculator"/>'s. What lives here is the order those happen in, which is the part
/// that has to be right for the total to mean anything: price, then promotions, then tax on what is
/// left, then shipping and the COD fee, then rounding, and store credit last of all.
/// </para>
/// <para>
/// That order is the whole design. Tax computed before a discount overstates what a customer owes;
/// store credit applied before rounding makes the rounding line lie; and a promotion evaluated
/// after the tax split cannot change the taxable value it should have changed.
/// </para>
/// <para>
/// Quoting writes nothing. It evaluates promotions without redeeming them and clamps a wallet
/// request without debiting it, because a cart is rendered many times and bought once.
/// </para>
/// </remarks>
/// <param name="context">The Pricing data context, for the promotions and their redemption counts.</param>
/// <param name="catalog">Resolves the offers in the basket.</param>
/// <param name="vendors">Supplies each seller's GSTIN, which decides their place of supply.</param>
/// <param name="referenceData">Turns the shipping address's state id into a GST state code.</param>
/// <param name="settings">The commercial levers: the COD fee, the tax on delivery, the wallet ceiling.</param>
/// <param name="features">Gates coupons and store credit.</param>
/// <param name="storeCredit">Reads the shopper's balance, so the engine can clamp their request.</param>
/// <param name="prices">The price-list walk.</param>
/// <param name="taxes">The HSN rate lookup.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="options">This module's limits.</param>
internal sealed class QuoteEngine(
    PricingDbContext context,
    IProductCatalog catalog,
    IVendorDirectory vendors,
    IReferenceData referenceData,
    IStoreSettings settings,
    IFeatureFlags features,
    IStoreCredit storeCredit,
    PriceResolver prices,
    TaxRateResolver taxes,
    IClock clock,
    IOptions<PricingOptions> options) : IPriceQuoteEngine
{
    /// <inheritdoc />
    public async ValueTask<QuoteResult> QuoteAsync(
        QuoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var asOf = request.QuotedAt ?? clock.UtcNow;
        var limits = options.Value;

        var pricingSettings = await settings.GetAsync<PricingSettings>(cancellationToken).ConfigureAwait(false);
        var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);
        var localization = await settings.GetAsync<LocalizationSettings>(cancellationToken).ConfigureAwait(false);
        var currency = string.IsNullOrWhiteSpace(localization.CurrencyCode) ? Money.Inr : localization.CurrencyCode;

        var requested = request.Lines
            .Where(line => line.Quantity > 0)
            .Take(limits.MaxQuoteLines)
            .ToList();

        if (requested.Count == 0)
        {
            return Empty(currency, placeOfSupply: null, isIntraState: true);
        }

        var listings = await catalog
            .FindListingsAsync([.. requested.Select(line => line.ListingId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        // An offer the catalogue no longer knows about is dropped rather than priced at zero. The
        // caller sees a line missing from the quote, which is a case a cart already has to render;
        // a zero-priced line would be a case nobody has to render and everybody would ship.
        var priceable = requested.Where(line => listings.ContainsKey(line.ListingId)).ToList();

        if (priceable.Count == 0)
        {
            return Empty(currency, placeOfSupply: null, isIntraState: true);
        }

        var quantities = priceable
            .GroupBy(line => line.ListingId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var resolved = await prices.ResolveAsync(listings, quantities, asOf, cancellationToken).ConfigureAwait(false);

        var rates = await taxes
            .ResolveAsync(
                [.. listings.Values.Select(listing => listing.HsnCode ?? string.Empty)],
                DateOnly.FromDateTime(asOf.UtcDateTime),
                cancellationToken)
            .ConfigureAwait(false);

        var sellers = await vendors
            .FindManyAsync([.. listings.Values.Select(listing => listing.VendorId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var placeOfSupply = await PlaceOfSupplyAsync(request, cancellationToken).ConfigureAwait(false);
        var storeStateCode = await StoreStateCodeAsync(cancellationToken).ConfigureAwait(false);
        var storeIsIntraState = GstCalculator.IsIntraState(placeOfSupply, storeStateCode);

        var promotionLines = priceable.ConvertAll(line =>
        {
            var listing = listings[line.ListingId];

            return new PromotionLine(
                line.LineId,
                line.ListingId,
                listing.VendorId,
                listing.CategoryPath,
                listing.BrandId,
                line.Quantity,
                resolved[line.ListingId].UnitPrice);
        });

        var outcome = await EvaluatePromotionsAsync(request, promotionLines, asOf, limits, cancellationToken)
            .ConfigureAwait(false);

        var lines = new List<QuoteLine>(priceable.Count);

        foreach (var line in promotionLines)
        {
            var listing = listings[line.ListingId];
            var tax = TaxRateResolver.For(rates, listing.HsnCode, listing.GstRate);

            var lineDiscount = outcome.LineDiscounts[line.LineId];
            var orderDiscount = outcome.OrderDiscounts[line.LineId];
            var lineTotal = GstCalculator.Round(line.Gross - lineDiscount - orderDiscount);

            // Each seller invoices under their own registration, so the split is decided against
            // their state and not the store's. A basket spanning two states produces one CGST/SGST
            // line and one IGST line, which is exactly what the two invoices will say.
            var supplierState = GstCalculator.StateCodeOf(
                sellers.TryGetValue(listing.VendorId, out var seller) ? seller.Gstin : null);

            var split = GstCalculator.SplitInclusive(
                lineTotal,
                tax.Rate,
                tax.CessRate,
                GstCalculator.IsIntraState(placeOfSupply, supplierState));

            lines.Add(new QuoteLine(
                line.LineId,
                line.ListingId,
                listing.VendorId,
                listing.Sku,
                listing.Name,
                line.Quantity,
                listing.Mrp,
                line.UnitPrice,
                line.Gross,
                lineDiscount,
                orderDiscount,
                split.TaxableValue,
                listing.HsnCode,
                tax.Rate,
                tax.CessRate,
                split.Cgst,
                split.Sgst,
                split.Igst,
                split.Cess,
                lineTotal,
                outcome.LinePromotions[line.LineId]));
        }

        return Assemble(request, lines, outcome, pricingSettings, commerce, currency, placeOfSupply, storeIsIntraState,
            await WalletBalanceAsync(request, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Finds the promotions worth considering and hands them to the evaluator.
    /// </summary>
    /// <remarks>
    /// The candidate query is deliberately narrow — live at the quote instant, and either automatic
    /// or matching the code the shopper typed — because everything after it is linear in what comes
    /// back and runs on every cart render.
    /// </remarks>
    /// <param name="request">The quote request.</param>
    /// <param name="lines">The basket, already priced.</param>
    /// <param name="asOf">The quote instant.</param>
    /// <param name="limits">This module's caps.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<PromotionOutcome> EvaluatePromotionsAsync(
        QuoteRequest request,
        List<PromotionLine> lines,
        DateTimeOffset asOf,
        PricingOptions limits,
        CancellationToken cancellationToken)
    {
        var couponsOn = await features
            .IsEnabledAsync(
                PricingFeatureFlags.Coupons,
                new FeatureAudience(request.CustomerId),
                cancellationToken)
            .ConfigureAwait(false);

        var code = couponsOn ? Promotion.NormalizeCode(request.CouponCode) : null;

        var candidates = await context.Promotions
            .AsNoTracking()
            .Where(promotion => promotion.IsActive
                                && promotion.StartsAt <= asOf
                                && (promotion.EndsAt == null || promotion.EndsAt > asOf)
                                && (promotion.Code == null || promotion.Code == code))
            .OrderBy(promotion => promotion.Priority)
            .ThenBy(promotion => promotion.Id)
            .Take(limits.MaxPromotionCandidates)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var redemptions = await CustomerRedemptionsAsync(request.CustomerId, candidates, cancellationToken)
            .ConfigureAwait(false);

        var outcome = PromotionEvaluator.Evaluate(
            candidates,
            new PromotionContext(
                lines,
                request.CustomerId,
                Segment: null,
                code,
                request.PaymentMethod,
                request.ShippingAmount,
                request.IsFirstOrder,
                redemptions));

        // A code typed while the switch is off is neither valid nor invalid, and saying "not valid"
        // would send a shopper to support with a code that works again tomorrow.
        return !couponsOn && !string.IsNullOrWhiteSpace(request.CouponCode)
            ? outcome with { CouponRejection = "Coupon codes are not being accepted at the moment." }
            : outcome;
    }

    /// <summary>How many times this shopper has already used each candidate promotion.</summary>
    /// <remarks>
    /// Only the promotions that actually carry a per-customer limit are counted. Most do not, and
    /// counting redemptions for all of them would be a join nobody needed on every cart render.
    /// </remarks>
    /// <param name="customerId">The shopper, or null.</param>
    /// <param name="candidates">The promotions being considered.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<IReadOnlyDictionary<Guid, int>> CustomerRedemptionsAsync(
        Guid? customerId,
        List<Promotion> candidates,
        CancellationToken cancellationToken)
    {
        var limited = candidates
            .Where(promotion => promotion.UsageLimitPerCustomer is not null)
            .Select(promotion => promotion.Id)
            .ToList();

        if (customerId is not { } customer || limited.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await context.PromotionRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.CustomerId == customer
                                 && limited.Contains(redemption.PromotionId)
                                 && redemption.Status == RedemptionStatus.Redeemed)
            .GroupBy(redemption => redemption.PromotionId)
            .Select(group => new { PromotionId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return counts.ToDictionary(row => row.PromotionId, row => row.Count);
    }

    /// <summary>The shopper's store-credit balance, or zero when the wallet is off or unused.</summary>
    /// <param name="request">The quote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<decimal> WalletBalanceAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId is not { } customer || request.WalletRedeemRequested <= 0m)
        {
            return 0m;
        }

        var enabled = await features
            .IsEnabledAsync(PricingFeatureFlags.StoreCredit, new FeatureAudience(customer), cancellationToken)
            .ConfigureAwait(false);

        if (!enabled)
        {
            return 0m;
        }

        var balance = await storeCredit.GetBalanceAsync(customer, cancellationToken).ConfigureAwait(false);

        return balance.IsActive ? balance.Balance : 0m;
    }

    /// <summary>The GST state code the split is decided against.</summary>
    /// <param name="request">The quote request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask<string?> PlaceOfSupplyAsync(QuoteRequest request, CancellationToken cancellationToken)
        => request.PlaceOfSupplyStateId is { } stateId
            ? await referenceData.StateCodeAsync(stateId, cancellationToken).ConfigureAwait(false)
            : await StoreStateCodeAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>The store's own GST state, from its registered address.</summary>
    /// <remarks>
    /// Read from the legal settings rather than configured separately: the registered address is
    /// already the authoritative one — it is what is printed on the invoice — and a second copy of
    /// the state code would be a second answer to which state the store supplies from.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async ValueTask<string?> StoreStateCodeAsync(CancellationToken cancellationToken)
    {
        var legal = await settings.GetAsync<LegalSettings>(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(legal.RegisteredAddress.StateCode)
            ? GstCalculator.StateCodeOf(legal.Gstin)
            : legal.RegisteredAddress.StateCode;
    }

    /// <summary>
    /// Totals the lines, applies shipping, the COD fee, rounding and store credit, and groups by
    /// seller.
    /// </summary>
    /// <param name="request">The quote request.</param>
    /// <param name="lines">The priced lines.</param>
    /// <param name="outcome">What the promotion walk decided.</param>
    /// <param name="pricing">The commercial levers.</param>
    /// <param name="commerce">The store's own free-shipping threshold.</param>
    /// <param name="currency">ISO 4217 code every amount is in.</param>
    /// <param name="placeOfSupply">The GST state code the splits were decided against.</param>
    /// <param name="storeIsIntraState">Whether the store's own supply is intra-state.</param>
    /// <param name="walletBalance">What the shopper has to spend, already gated by the flag.</param>
    private static QuoteResult Assemble(
        QuoteRequest request,
        List<QuoteLine> lines,
        PromotionOutcome outcome,
        PricingSettings pricing,
        CommerceSettings commerce,
        string currency,
        string? placeOfSupply,
        bool storeIsIntraState,
        decimal walletBalance)
    {
        var subtotal = GstCalculator.Round(lines.Sum(line => line.Gross));
        var lineDiscount = GstCalculator.Round(lines.Sum(line => line.LineDiscount));
        var orderDiscount = GstCalculator.Round(lines.Sum(line => line.OrderDiscountAllocated));
        var netTotal = GstCalculator.Round(lines.Sum(line => line.LineTotal));

        // The store's own free-shipping threshold, on top of any free-shipping promotion. It is
        // measured on the discounted total, because a shopper who used a coupon to get there has
        // not actually spent the threshold.
        var shippingDiscount = outcome.ShippingDiscount;

        if (commerce.FreeShippingThreshold > 0m && netTotal >= commerce.FreeShippingThreshold)
        {
            shippingDiscount = request.ShippingAmount;
        }

        shippingDiscount = GstCalculator.Round(Math.Min(shippingDiscount, request.ShippingAmount));

        var shipping = GstCalculator.Round(request.ShippingAmount - shippingDiscount);
        var shippingSplit = GstCalculator.SplitInclusive(shipping, pricing.ShippingTaxRate, 0m, storeIsIntraState);

        var codFee = request.PaymentMethod == QuotePaymentMethod.CashOnDelivery
            ? GstCalculator.Round(Math.Max(0m, pricing.CodHandlingFee))
            : 0m;

        var codSplit = GstCalculator.SplitInclusive(codFee, pricing.ShippingTaxRate, 0m, storeIsIntraState);

        var beforeRounding = GstCalculator.Round(netTotal + shipping + codFee);

        var (grandTotal, roundingAdjustment) = pricing.RoundToNearestRupee
            ? GstCalculator.RoundToRupee(beforeRounding)
            : (beforeRounding, 0m);

        // Store credit is applied to the rounded total and never before it: applying it first would
        // round the residue instead of the price, and the rounding line on the invoice would be a
        // number the shopper could not reconcile against anything.
        var ceiling = pricing.WalletMaxRedeemPercent >= 100m
            ? grandTotal
            : GstCalculator.Round(grandTotal * Math.Max(0m, pricing.WalletMaxRedeemPercent) / 100m);

        var walletApplied = GstCalculator.Round(
            Math.Max(0m, Math.Min(Math.Min(request.WalletRedeemRequested, walletBalance), Math.Min(ceiling, grandTotal))));

        return new QuoteResult(
            lines,
            GroupByVendor(lines, shipping, shippingSplit.TaxTotal),
            outcome.Promotions,
            currency,
            storeIsIntraState,
            placeOfSupply,
            subtotal,
            lineDiscount,
            orderDiscount,
            GstCalculator.Round(lineDiscount + orderDiscount),
            GstCalculator.Round(lines.Sum(line => line.TaxableValue) + shippingSplit.TaxableValue
                                                                    + codSplit.TaxableValue),
            GstCalculator.Round(lines.Sum(line => line.Cgst) + shippingSplit.Cgst + codSplit.Cgst),
            GstCalculator.Round(lines.Sum(line => line.Sgst) + shippingSplit.Sgst + codSplit.Sgst),
            GstCalculator.Round(lines.Sum(line => line.Igst) + shippingSplit.Igst + codSplit.Igst),
            GstCalculator.Round(lines.Sum(line => line.Cess)),
            GstCalculator.Round(lines.Sum(line => line.Cgst + line.Sgst + line.Igst + line.Cess)
                                + shippingSplit.TaxTotal + codSplit.TaxTotal),
            shipping,
            shippingDiscount,
            shippingSplit.TaxTotal,
            codFee,
            walletApplied,
            roundingAdjustment,
            grandTotal,
            GstCalculator.Round(grandTotal - walletApplied),
            outcome.CouponRejection);
    }

    /// <summary>
    /// Groups the lines into the sub-orders Orders will create, and splits shipping across them.
    /// </summary>
    /// <remarks>
    /// Shipping is allocated on each seller's share of the net total rather than split evenly,
    /// because a seller's settlement is computed from their group and an even split would move
    /// money between sellers whenever a basket was lopsided.
    /// </remarks>
    /// <param name="lines">The priced lines.</param>
    /// <param name="shipping">Shipping charged for the whole basket.</param>
    /// <param name="shippingTax">The tax inside it.</param>
    private static List<QuoteVendorGroup> GroupByVendor(List<QuoteLine> lines, decimal shipping, decimal shippingTax)
    {
        var groups = lines
            .GroupBy(line => line.VendorId)
            .Select(group => new
            {
                VendorId = group.Key,
                Lines = group.ToList(),
                Net = group.Sum(line => line.LineTotal),
            })
            .ToList();

        var weights = groups.ConvertAll(group => group.Net);
        var shippingShares = GstCalculator.AllocateProportionally(shipping, weights);
        var taxShares = GstCalculator.AllocateProportionally(shippingTax, weights);

        var result = new List<QuoteVendorGroup>(groups.Count);

        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];

            result.Add(new QuoteVendorGroup(
                group.VendorId,
                group.Lines.ConvertAll(line => line.LineId),
                GstCalculator.Round(group.Lines.Sum(line => line.Gross)),
                GstCalculator.Round(group.Lines.Sum(line => line.LineDiscount + line.OrderDiscountAllocated)),
                GstCalculator.Round(group.Lines.Sum(line => line.TaxableValue)),
                GstCalculator.Round(group.Lines.Sum(line => line.Cgst + line.Sgst + line.Igst + line.Cess)),
                shippingShares[index],
                taxShares[index],
                GstCalculator.Round(group.Net + shippingShares[index])));
        }

        return result;
    }

    /// <summary>A quote for nothing. Every figure is zero and no promotion was considered.</summary>
    /// <param name="currency">ISO 4217 code.</param>
    /// <param name="placeOfSupply">The GST state code, when one was resolved.</param>
    /// <param name="isIntraState">Whether the store's own supply would be intra-state.</param>
    private static QuoteResult Empty(string currency, string? placeOfSupply, bool isIntraState)
        => new(
            [],
            [],
            [],
            currency,
            isIntraState,
            placeOfSupply,
            0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m,
            CouponRejection: null);
}
