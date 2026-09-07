using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Identity;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Shipping;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure;
using KlaraHome.Modules.Carts.Infrastructure.Carts;
using KlaraHome.Modules.Carts.Infrastructure.Checkout;
using KlaraHome.Modules.Carts.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Application.Checkout;

/// <summary>Opens a checkout against the caller's basket.</summary>
internal sealed record StartCheckoutCommand : ICommand<CheckoutResponse>;

/// <summary>Reads one of the caller's own checkout sessions.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record GetCheckoutQuery(Guid SessionId) : IQuery<CheckoutResponse>;

/// <summary>Re-validates and re-prices a session, immediately before payment.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record ReviewCheckoutQuery(Guid SessionId) : IQuery<CheckoutResponse>;

/// <summary>Chooses where the order goes and who it is billed to.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="ShippingAddressId">One of the shopper's own addresses.</param>
/// <param name="BillingAddressId">Another, or null to bill to the shipping address.</param>
/// <param name="Gstin">The GSTIN to raise the invoice against, for a B2B purchase.</param>
internal sealed record SetCheckoutAddressCommand(
    Guid SessionId,
    Guid ShippingAddressId,
    Guid? BillingAddressId,
    string? Gstin) : ICommand<CheckoutResponse>;

/// <summary>Reads the delivery services available for each seller in the basket.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record GetShippingOptionsQuery(Guid SessionId)
    : IQuery<IReadOnlyList<VendorShippingOptionsResponse>>;

/// <summary>One seller's chosen delivery service.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="OptionCode">The service.</param>
internal sealed record VendorShippingChoice(Guid VendorId, string OptionCode);

/// <summary>Chooses a delivery service for every seller in the basket.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="PerVendor">One choice per seller.</param>
internal sealed record SetCheckoutShippingCommand(
    Guid SessionId,
    IReadOnlyList<VendorShippingChoice> PerVendor) : ICommand<CheckoutResponse>;

/// <summary>Reads what this basket may be paid by, and why it may not be paid by the rest.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record GetPaymentMethodsQuery(Guid SessionId) : IQuery<IReadOnlyList<PaymentMethodResponse>>;

/// <summary>Chooses how the order is paid for.</summary>
/// <param name="SessionId">The session.</param>
/// <param name="Method">Either <c>prepaid</c> or <c>cod</c>.</param>
internal sealed record SetPaymentMethodCommand(Guid SessionId, string Method) : ICommand<CheckoutResponse>;

/// <summary>Closes a session the shopper backed out of.</summary>
/// <param name="SessionId">The session.</param>
internal sealed record AbandonCheckoutCommand(Guid SessionId) : ICommand;

/// <summary>Rejects an address choice that could never be stored.</summary>
internal sealed class SetCheckoutAddressValidator : AbstractValidator<SetCheckoutAddressCommand>
{
    public SetCheckoutAddressValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.ShippingAddressId).NotEmpty();
        RuleFor(command => command.Gstin).Length(15).When(command => !string.IsNullOrWhiteSpace(command.Gstin));
    }
}

/// <summary>Rejects a delivery choice that could never be stored.</summary>
internal sealed class SetCheckoutShippingValidator : AbstractValidator<SetCheckoutShippingCommand>
{
    public SetCheckoutShippingValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.PerVendor).NotEmpty();

        RuleForEach(command => command.PerVendor).ChildRules(choice =>
        {
            choice.RuleFor(payload => payload.VendorId).NotEmpty();
            choice.RuleFor(payload => payload.OptionCode).NotEmpty().MaximumLength(48);
        });
    }
}

/// <summary>Rejects a payment method that could never be stored.</summary>
internal sealed class SetPaymentMethodValidator : AbstractValidator<SetPaymentMethodCommand>
{
    public SetPaymentMethodValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.Method).NotEmpty().MaximumLength(16);
    }
}

/// <summary>
/// Opens a checkout against the caller's basket.
/// </summary>
/// <remarks>
/// It refuses a basket that has a blocking issue, and says which line — because the alternative is
/// letting a shopper walk to the payment screen and discover there that something sold out, which
/// is the most expensive place on the site to find out.
/// </remarks>
/// <param name="context">The Cart data context.</param>
/// <param name="resolver">Finds the caller's basket.</param>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the session lifetime.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class StartCheckoutCommandHandler(
    CartsDbContext context,
    CartResolver resolver,
    CheckoutWorkflow workflow,
    CartsScope scope,
    IOptions<CartsOptions> options,
    IClock clock) : ICommandHandler<StartCheckoutCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        StartCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        // Unconditional, and it used to be a configuration switch. It was not a real one: a
        // checkout session is opened against a customer id, the address step chooses from that
        // customer's address book and the order that comes out belongs to them, so turning the
        // switch off produced a failure rather than a guest checkout. Guest checkout is a feature —
        // a guest identity, an address collected onto the session, an order that belongs to an
        // email address — and it is Phase 2's, not a flag's (PARKING_LOT.md, Step 28B).
        if (scope.CustomerId is not { } customerId || !scope.IsSignedIn)
        {
            return CartsErrors.SignInRequired;
        }

        var cart = await resolver.ResolveAsync(createIfMissing: false, cancellationToken).ConfigureAwait(false);

        if (cart is null || !cart.IsOpen)
        {
            return CartsErrors.CartEmpty;
        }

        if (cart.LineCount == 0)
        {
            return CartsErrors.CartEmpty;
        }

        // One open attempt per basket, matching the partial unique index. Returning the existing
        // one rather than refusing is what makes opening checkout in a second tab harmless.
        var session = await context.CheckoutSessions
            .Include(candidate => candidate.Shipments)
            .FirstOrDefaultAsync(
                candidate => candidate.CartId == cart.Id
                             && (candidate.Status == CheckoutStatus.Draft
                                 || candidate.Status == CheckoutStatus.AddressSet
                                 || candidate.Status == CheckoutStatus.ShippingSet
                                 || candidate.Status == CheckoutStatus.PaymentSet),
                cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            session = CheckoutSession.Open(
                cart.Id,
                customerId,
                cart.CurrencyCode,
                clock.UtcNow.AddMinutes(options.Value.CheckoutSessionMinutes));

            context.CheckoutSessions.Add(session);
        }

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        // Refused here rather than at the payment screen, and with the offending line named on the
        // response: the most expensive place on the site to discover that something sold out is the
        // one immediately after the shopper has entered a card.
        if (!priced.IsReadyForCheckout)
        {
            return CartsErrors.CartNotReady;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, priced));
    }
}

/// <summary>Reads one of the caller's own checkout sessions, re-priced.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">Saves the snapshot the re-price produced.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetCheckoutQueryHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope) : IQueryHandler<GetCheckoutQuery, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        GetCheckoutQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, query.SessionId, requireOpen: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;
        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, priced));
    }
}

/// <summary>
/// Re-validates and re-prices a session immediately before payment.
/// </summary>
/// <remarks>
/// The same work as a read, and deliberately a separate endpoint: the storefront calls it as the
/// last thing before showing the pay button, and the refusal it can return — "something in your
/// basket changed" — is the one the shopper has to see <em>before</em> a gateway is involved rather
/// than after.
/// </remarks>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">Saves the snapshot the re-price produced.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class ReviewCheckoutQueryHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope) : IQueryHandler<ReviewCheckoutQuery, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        ReviewCheckoutQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, query.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        if (session.ShippingAddress is null)
        {
            return CartsErrors.CheckoutIncomplete("a delivery address");
        }

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return priced.IsReadyForCheckout
            ? Result.Success(CheckoutWorkflow.ToResponse(session, priced))
            : CartsErrors.CartNotReady;
    }
}

/// <summary>Chooses where the order goes and who it is billed to.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="customers">Reads the shopper's own addresses. Never a join across a schema.</param>
/// <param name="reference">Checks the state is a real one.</param>
/// <param name="deliveries">Checks the store delivers there, and that a courier will carry it.</param>
internal sealed class SetCheckoutAddressCommandHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope,
    ICustomerDirectory customers,
    IReferenceData reference,
    IShippingOptions deliveries) : ICommandHandler<SetCheckoutAddressCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        SetCheckoutAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, command.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        // Looked up by (customer, address). An id belonging to somebody else simply does not
        // resolve, so no caller can ship an order to an address they do not own.
        var shipping = await customers
            .FindAddressAsync(session.CustomerId, command.ShippingAddressId, cancellationToken)
            .ConfigureAwait(false);

        if (shipping is null)
        {
            return CartsErrors.UnknownAddress;
        }

        var billing = command.BillingAddressId is { } billingId && billingId != command.ShippingAddressId
            ? await customers.FindAddressAsync(session.CustomerId, billingId, cancellationToken).ConfigureAwait(false)
            : shipping;

        if (billing is null)
        {
            return CartsErrors.UnknownAddress;
        }

        if (!await reference.StateExistsAsync(shipping.StateId, cancellationToken).ConfigureAwait(false))
        {
            return CartsErrors.UnknownAddress;
        }

        // The second of the five gates ADR-018 names. Refusing here rather than only at
        // place-order is the whole point: a shopper who cannot be delivered to should find out when
        // they choose the address, not after they have chosen how to pay for it.
        var destination = await deliveries
            .CheckDestinationAsync(shipping.Pincode, isCod: false, cancellationToken)
            .ConfigureAwait(false);

        if (!destination.Deliverable)
        {
            return destination.Refusal == DeliveryRefusal.NotCovered
                ? CartsErrors.NotCovered(destination.Message)
                : CartsErrors.PincodeNotServiceable;
        }

        // The address's own GSTIN wins over the one typed here only if nothing was typed: a shopper
        // buying for a different registration says so on the request, and it is the request they
        // will check on the invoice.
        session.SetAddresses(
            Snapshot(shipping),
            Snapshot(billing),
            command.Gstin ?? billing.Gstin);

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, priced));
    }

    /// <summary>Freezes an account address onto the session.</summary>
    private static AddressSnapshot Snapshot(CustomerAddress address)
        => new()
        {
            SourceAddressId = address.Id,
            RecipientName = address.RecipientName,
            Mobile = address.Mobile,
            Line1 = address.Line1,
            Line2 = address.Line2,
            Landmark = address.Landmark,
            City = address.City,
            StateId = address.StateId,
            Pincode = address.Pincode,
            Gstin = address.Gstin,
            IsBusiness = address.IsBusiness,
        };
}

/// <summary>Reads the delivery services available for each seller in the basket.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="shipping">The logistics seam. Degenerate until Step 16.</param>
/// <param name="catalog">Supplies each line's weight, which is what a courier prices on.</param>
internal sealed class GetShippingOptionsQueryHandler(
    CheckoutWorkflow workflow,
    CartsScope scope,
    IShippingOptions shipping,
    IProductCatalog catalog) : IQueryHandler<GetShippingOptionsQuery, IReadOnlyList<VendorShippingOptionsResponse>>
{
    public async Task<Result<IReadOnlyList<VendorShippingOptionsResponse>>> HandleAsync(
        GetShippingOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, query.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        if (session.ShippingAddress is not { } destination)
        {
            return CartsErrors.CheckoutIncomplete("a delivery address");
        }

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);
        var chosen = session.Shipments.ToDictionary(shipment => shipment.VendorId);

        var listingIds = cart.Lines
            .Where(line => !line.SavedForLater)
            .Select(line => line.ListingId)
            .Distinct()
            .ToArray();

        var listings = listingIds.Length == 0
            ? new Dictionary<Guid, ListingSummary>()
            : await catalog.FindListingsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var results = new List<VendorShippingOptionsResponse>();

        foreach (var group in priced.Groups)
        {
            var weight = cart.Lines
                .Where(line => !line.SavedForLater && line.VendorId == group.VendorId)
                .Sum(line => (listings.GetValueOrDefault(line.ListingId)?.WeightGrams ?? 0) * line.Quantity);

            var options = await shipping.QuoteAsync(
                new ShipmentQuoteRequest(
                    group.VendorId,
                    PickupPincode: null,
                    destination.Pincode,
                    destination.StateId,
                    weight,
                    group.Subtotal,
                    session.PaymentMethod == CheckoutPaymentMethod.CashOnDelivery),
                cancellationToken).ConfigureAwait(false);

            results.Add(new VendorShippingOptionsResponse(
                group.VendorId,
                group.VendorName,
                chosen.GetValueOrDefault(group.VendorId)?.OptionCode,
                [
                    .. options.Select(option => new ShippingOptionResponse(
                        option.Code,
                        option.Name,
                        option.Carrier,
                        option.Amount,
                        option.DispatchSlaHours,
                        option.PromisedMinDays,
                        option.PromisedMaxDays,
                        option.IsCodAvailable)),
                ]));
        }

        return Result.Success<IReadOnlyList<VendorShippingOptionsResponse>>(results);
    }
}

/// <summary>Chooses a delivery service for every seller in the basket.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="shipping">The logistics seam, re-asked so a choice cannot be invented by the client.</param>
/// <param name="catalog">Supplies each line's weight.</param>
internal sealed class SetCheckoutShippingCommandHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope,
    IShippingOptions shipping,
    IProductCatalog catalog) : ICommandHandler<SetCheckoutShippingCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        SetCheckoutShippingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, command.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        if (session.ShippingAddress is not { } destination)
        {
            return CartsErrors.CheckoutIncomplete("a delivery address");
        }

        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);
        var wanted = command.PerVendor.ToDictionary(choice => choice.VendorId, choice => choice.OptionCode);

        var listingIds = cart.Lines
            .Where(line => !line.SavedForLater)
            .Select(line => line.ListingId)
            .Distinct()
            .ToArray();

        var listings = listingIds.Length == 0
            ? new Dictionary<Guid, ListingSummary>()
            : await catalog.FindListingsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        var shipments = new List<CheckoutShipment>();

        foreach (var group in priced.Groups)
        {
            if (!wanted.TryGetValue(group.VendorId, out var code))
            {
                // Every seller must be chosen for. A basket with one parcel decided and one not is
                // a total nobody can quote, and letting it through would show the shopper a price
                // that goes up at the payment screen.
                return CartsErrors.CheckoutIncomplete("a delivery option for every seller");
            }

            var weight = cart.Lines
                .Where(line => !line.SavedForLater && line.VendorId == group.VendorId)
                .Sum(line => (listings.GetValueOrDefault(line.ListingId)?.WeightGrams ?? 0) * line.Quantity);

            // Re-quoted rather than trusted. The price and the promise are the server's to state;
            // a client that could name an amount could name zero.
            var options = await shipping.QuoteAsync(
                new ShipmentQuoteRequest(
                    group.VendorId,
                    PickupPincode: null,
                    destination.Pincode,
                    destination.StateId,
                    weight,
                    group.Subtotal,
                    session.PaymentMethod == CheckoutPaymentMethod.CashOnDelivery),
                cancellationToken).ConfigureAwait(false);

            if (options.FirstOrDefault(option =>
                    string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase)) is not { } chosen)
            {
                return CartsErrors.UnknownShippingOption;
            }

            shipments.Add(CheckoutShipment.Create(
                session.Id,
                group.VendorId,
                chosen.Code,
                chosen.Name,
                chosen.Carrier,
                chosen.Amount,
                chosen.TaxAmount,
                chosen.DispatchSlaHours,
                chosen.PromisedMinDays,
                chosen.PromisedMaxDays));
        }

        if (shipments.Count == 0)
        {
            return CartsErrors.NotServiceable;
        }

        // The old rows are removed rather than updated: a delivery choice is replaced wholesale,
        // and leaving one behind for a seller who is no longer in the basket would charge for a
        // parcel that will never be sent.
        context.CheckoutShipments.RemoveRange(session.Shipments);
        session.SetShipments(shipments);

        var repriced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, repriced));
    }
}

/// <summary>Reads what this basket may be paid by, and why it may not be paid by the rest.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="settings">Supplies the store's cash-on-delivery rules.</param>
internal sealed class GetPaymentMethodsQueryHandler(
    CheckoutWorkflow workflow,
    CartsScope scope,
    IStoreSettings settings) : IQueryHandler<GetPaymentMethodsQuery, IReadOnlyList<PaymentMethodResponse>>
{
    public async Task<Result<IReadOnlyList<PaymentMethodResponse>>> HandleAsync(
        GetPaymentMethodsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, query.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;
        var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);
        var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);

        var refusal = CheckoutWorkflow.CodRefusalReason(priced, session, commerce);

        // The fee is read off a quote rather than off the settings, because this module computes no
        // money. It is the same figure the shopper will be charged for the same reason.
        var codQuote = await workflow
            .QuoteAsCodAsync(session, cart, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PaymentMethodResponse>>(
        [
            new PaymentMethodResponse("prepaid", "Pay now", IsAvailable: true, Fee: 0m, Reason: null),
            new PaymentMethodResponse(
                "cod",
                "Cash on delivery",
                refusal is null,
                codQuote?.CodFee ?? 0m,
                refusal),
        ]);
    }
}

/// <summary>Chooses how the order is paid for.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="settings">Supplies the store's cash-on-delivery rules.</param>
internal sealed class SetPaymentMethodCommandHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope,
    IStoreSettings settings) : ICommandHandler<SetPaymentMethodCommand, CheckoutResponse>
{
    public async Task<Result<CheckoutResponse>> HandleAsync(
        SetPaymentMethodCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await CheckoutLoader
            .LoadAsync(workflow, scope, command.SessionId, requireOpen: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var (session, cart) = loaded.Value;

        if (!TryParseMethod(command.Method, out var method))
        {
            return CartsErrors.UnknownPaymentMethod;
        }

        if (session.Shipments.Count == 0)
        {
            return CartsErrors.CheckoutIncomplete("a delivery option");
        }

        if (method == CheckoutPaymentMethod.CashOnDelivery)
        {
            var priced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);
            var commerce = await settings.GetAsync<CommerceSettings>(cancellationToken).ConfigureAwait(false);

            if (CheckoutWorkflow.CodRefusalReason(priced, session, commerce) is { } refusal)
            {
                return CartsErrors.CodUnavailable(refusal);
            }
        }

        session.SetPaymentMethod(method);

        // Re-priced *after* the method is set, because the fee and the promotions that exclude cash
        // on delivery both depend on it.
        var repriced = await workflow.RepriceAsync(session, cart, cancellationToken).ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CheckoutWorkflow.ToResponse(session, repriced));
    }

    /// <summary>Reads a payment method from what the caller sent.</summary>
    private static bool TryParseMethod(string value, out CheckoutPaymentMethod method)
    {
        if (string.Equals(value, "cod", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, nameof(CheckoutPaymentMethod.CashOnDelivery), StringComparison.OrdinalIgnoreCase))
        {
            method = CheckoutPaymentMethod.CashOnDelivery;
            return true;
        }

        if (string.Equals(value, "prepaid", StringComparison.OrdinalIgnoreCase))
        {
            method = CheckoutPaymentMethod.Prepaid;
            return true;
        }

        method = CheckoutPaymentMethod.Prepaid;
        return false;
    }
}

/// <summary>Closes a session the shopper backed out of.</summary>
/// <param name="workflow">The shared checkout steps.</param>
/// <param name="context">The Cart data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class AbandonCheckoutCommandHandler(
    CheckoutWorkflow workflow,
    CartsDbContext context,
    CartsScope scope) : ICommandHandler<AbandonCheckoutCommand>
{
    public async Task<Result> HandleAsync(AbandonCheckoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.CustomerId is not { } customerId)
        {
            return Result.Failure(CartsErrors.SignInRequired);
        }

        var session = await workflow.FindAsync(command.SessionId, customerId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result.Failure(CartsErrors.NotFound("checkout"));
        }

        if (!session.IsOpen)
        {
            return Result.Failure(CartsErrors.CheckoutClosed);
        }

        // The basket is left alone. A shopper who backs out of paying has not changed their mind
        // about what they wanted, and emptying their cart for them would be an act of vandalism.
        session.MarkAbandoned();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>
/// The load-and-authorise step every checkout handler starts with.
/// </summary>
/// <remarks>
/// Nine handlers need the same four refusals — not signed in, no such session, session closed, cart
/// gone — and a shared step is the only way to be sure all nine give the same answers. The customer
/// id comes from the caller's token and is half the lookup key, so no handler can be written in a
/// way that reads somebody else's checkout.
/// </remarks>
internal static class CheckoutLoader
{
    /// <summary>Loads a session and its basket, or explains why it cannot be acted on.</summary>
    /// <param name="workflow">The shared checkout steps.</param>
    /// <param name="scope">Who is asking.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="requireOpen">Whether a placed or abandoned session is a refusal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<(CheckoutSession Session, Cart Cart)>> LoadAsync(
        CheckoutWorkflow workflow,
        CartsScope scope,
        Guid sessionId,
        bool requireOpen,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.CustomerId is not { } customerId)
        {
            return CartsErrors.SignInRequired;
        }

        var session = await workflow.FindAsync(sessionId, customerId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return CartsErrors.NotFound("checkout");
        }

        if (requireOpen && !session.IsOpen)
        {
            return CartsErrors.CheckoutClosed;
        }

        var cart = await workflow.FindCartAsync(session, cancellationToken).ConfigureAwait(false);

        return cart is null
            ? CartsErrors.NotFound("basket")
            : Result.Success((session, cart));
    }
}
