using FluentValidation;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Pricing.Infrastructure;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Pricing.Application.Quotes;

/// <summary>One line to price, as the caller sends it.</summary>
/// <param name="LineId">
/// The caller's own identifier, echoed back on the quoted line. Optional: a stateless quote from a
/// product page has nothing to identify a line by, and the engine mints one.
/// </param>
/// <param name="ListingId">The offer.</param>
/// <param name="Quantity">How many units.</param>
internal sealed record QuoteLinePayload(Guid? LineId, Guid ListingId, int Quantity);

/// <summary>
/// Prices a basket the server is not holding.
/// </summary>
/// <remarks>
/// The storefront uses it for a product page's price, a quantity-tier preview and a coupon tried
/// before a cart exists; the admin simulator uses it to answer "what would this basket cost". Both
/// are the same calculation, because there is only one.
/// </remarks>
/// <param name="Lines">The lines to price.</param>
/// <param name="CustomerId">
/// The shopper. Taken from the caller's own token on the storefront and named explicitly by staff
/// on the simulator, which is what lets a merchandiser test a segment-scoped campaign.
/// </param>
/// <param name="StateId">The <c>platform.states</c> row of the shipping address.</param>
/// <param name="CouponCode">A code to try.</param>
/// <param name="PaymentMethod">How the basket would be paid for. Prepaid when omitted.</param>
/// <param name="IsFirstOrder">Whether this would be the shopper's first order.</param>
/// <param name="ShippingAmount">What shipping would cost, as the caller knows it.</param>
/// <param name="WalletRedeemRequested">How much store credit to try to apply.</param>
internal sealed record QuoteBasketQuery(
    IReadOnlyList<QuoteLinePayload> Lines,
    Guid? CustomerId,
    Guid? StateId,
    string? CouponCode,
    QuotePaymentMethod? PaymentMethod,
    bool IsFirstOrder,
    decimal ShippingAmount,
    decimal WalletRedeemRequested) : IQuery<QuoteResult>;

/// <summary>Rejects a quote request that could never be priced.</summary>
internal sealed class QuoteBasketValidator : AbstractValidator<QuoteBasketQuery>
{
    public QuoteBasketValidator()
    {
        RuleFor(query => query.Lines).NotEmpty();
        RuleFor(query => query.CouponCode).MaximumLength(48);
        RuleFor(query => query.ShippingAmount).GreaterThanOrEqualTo(0m);
        RuleFor(query => query.WalletRedeemRequested).GreaterThanOrEqualTo(0m);

        RuleForEach(query => query.Lines).ChildRules(line =>
        {
            line.RuleFor(payload => payload.ListingId).NotEmpty();
            line.RuleFor(payload => payload.Quantity).GreaterThan(0);
        });
    }
}

/// <summary>
/// Prices a basket.
/// </summary>
/// <remarks>
/// It does nothing but translate the request and hand it to the engine. That is deliberate: the
/// moment a handler starts adjusting a quote it has become a second pricing implementation, and the
/// one rule this module has is that there is only one.
/// </remarks>
/// <param name="engine">The one calculation engine.</param>
/// <param name="options">Supplies the line ceiling.</param>
internal sealed class QuoteBasketQueryHandler(IPriceQuoteEngine engine, IOptions<PricingOptions> options)
    : IQueryHandler<QuoteBasketQuery, QuoteResult>
{
    public async Task<Result<QuoteResult>> HandleAsync(QuoteBasketQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Lines.Count == 0)
        {
            return PricingErrors.EmptyQuote;
        }

        if (query.Lines.Count > options.Value.MaxQuoteLines)
        {
            return PricingErrors.TooManyItems(options.Value.MaxQuoteLines);
        }

        var lines = query.Lines
            .Select(line => new QuoteLineRequest(line.LineId ?? Guid.CreateVersion7(), line.ListingId, line.Quantity))
            .ToList();

        var request = new QuoteRequest(
            lines,
            query.CustomerId,
            query.StateId,
            query.CouponCode,
            // Prepaid when omitted, because it is the method that charges no fee: defaulting to
            // cash on delivery would quote a handling charge to a shopper who never asked for one.
            query.PaymentMethod ?? QuotePaymentMethod.Prepaid,
            query.IsFirstOrder,
            query.ShippingAmount,
            query.WalletRedeemRequested);

        var quote = await engine.QuoteAsync(request, cancellationToken).ConfigureAwait(false);

        // Nothing priced at all means every offer in the request has gone from the catalogue. That
        // is a client error rather than a zero-value quote, and saying so is what stops a cart
        // rendering a total of zero for a basket full of archived listings.
        return quote.Lines.Count == 0 ? PricingErrors.UnknownListing : Result.Success(quote);
    }

}
