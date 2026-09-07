using System.Globalization;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Identity;
using KlaraHome.Contracts.Inventory;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Orders.Application;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Events;
using KlaraHome.Modules.Orders.Infrastructure.Numbering;
using KlaraHome.Modules.Orders.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Placement;

/// <summary>
/// Turns an agreed checkout into an order (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// The other half of the seam Cart declared at Step 13. Cart owns the conversation with the shopper
/// and has already decided everything — what is in the basket, where it goes, how it is paid for and
/// what it costs. This does not re-decide any of it. In particular <b>it does not re-price</b>: the
/// quote arrives priced, it was shown to the shopper, and a second calculation between the review
/// screen and the order is the one way a confirmation ends up carrying a total nobody consented to.
/// </para>
/// <para>
/// What it does do is <em>split</em>. One quote becomes one order and one sub-order per seller, and
/// every figure is copied down rather than recomputed, so the order's totals are the quote's totals
/// and the sub-orders' add up to them by construction.
/// </para>
/// <para>
/// The order of operations is the design, and it is arranged around one rule: <b>never leave an
/// order nobody can pay for, and never leave money spent on an order that does not exist.</b>
/// </para>
/// <list type="number">
/// <item>Read everything. The shopper, the sellers, the offers, the state code. A refusal here has
/// written nothing.</item>
/// <item>Open a transaction, allocate the number and write the order graph — but do not commit.</item>
/// <item>Spend what the quote said would be spent: the coupon's redemption and the store credit.
/// Both are keyed on the order id and both are reversible, which is what makes step 5 possible.</item>
/// <item>Cash on delivery confirms immediately and commits the stock held against the cart. Prepaid
/// stays awaiting payment and asks Payments to open a collection.</item>
/// <item>Commit. Any failure before this rolls the order back <em>and</em> reverses the redemption
/// and the credit, so the shopper can press the button again with nothing lost.</item>
/// </list>
/// <para>
/// The stock commit is deliberately inside the transaction rather than after it. Committing stock
/// for an order that then fails to save loses units from supply, which an operator can correct;
/// releasing stock for an order that did save would sell the same unit twice, which they cannot.
/// </para>
/// </remarks>
/// <param name="context">The Ordering data context.</param>
/// <param name="numbering">Allocates the order number.</param>
/// <param name="customers">Resolves the shopper for the snapshot.</param>
/// <param name="vendors">Resolves the sellers for the sub-orders and their invoices.</param>
/// <param name="catalog">Resolves the offers for the line snapshots.</param>
/// <param name="commissions">Resolves what the platform charges on each line.</param>
/// <param name="promotions">Commits the coupon the quote applied.</param>
/// <param name="wallet">Spends the store credit the quote clamped.</param>
/// <param name="stock">Commits the units held against the cart.</param>
/// <param name="allocations">Reads which warehouse each held line came out of, to freeze on the line.</param>
/// <param name="reference">Resolves the GST state code of the place of supply.</param>
/// <param name="payments">Opens the collection. Refuses politely until Step 15.</param>
/// <param name="events">Announces the order.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class OrderPlacementService(
    OrdersDbContext context,
    OrderNumbering numbering,
    ICustomerDirectory customers,
    IVendorDirectory vendors,
    IProductCatalog catalog,
    ICommissionResolver commissions,
    IPromotionLedger promotions,
    IStoreCredit wallet,
    IStockAvailability stock,
    IStockAllocation allocations,
    IReferenceData reference,
    IPaymentInitiation payments,
    OrdersEventPublisher events,
    IClock clock) : IOrderPlacement
{
    /// <inheritdoc />
    public async Task<Result<PlacedOrder>> PlaceAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var quote = request.Quote;

        if (quote.Lines.Count == 0 || quote.VendorGroups.Count == 0)
        {
            return OrdersErrors.NothingToOrder;
        }

        var customer = await customers.FindAsync(request.CustomerId, cancellationToken).ConfigureAwait(false);

        if (customer is not { IsActive: true })
        {
            return OrdersErrors.UnknownCustomer;
        }

        var vendorIds = quote.VendorGroups.Select(group => group.VendorId).Distinct().ToArray();
        var sellers = await vendors.FindManyAsync(vendorIds, cancellationToken).ConfigureAwait(false);

        foreach (var vendorId in vendorIds)
        {
            // A seller who stopped trading between the review screen and the button is a refusal, not
            // an order somebody would have to unwind: nothing has been written yet.
            if (!sellers.TryGetValue(vendorId, out var seller) || !seller.IsActive)
            {
                return OrdersErrors.VendorInactive(
                    sellers.TryGetValue(vendorId, out var known) ? known.DisplayName : "One of the sellers");
            }
        }

        var listings = await catalog
            .FindListingsAsync([.. quote.Lines.Select(line => line.ListingId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var shippingStateCode = await reference
            .StateCodeAsync(request.ShippingAddress.StateId, cancellationToken)
            .ConfigureAwait(false);

        var billingStateCode = request.BillingAddress.StateId == request.ShippingAddress.StateId
            ? shippingStateCode
            : await reference.StateCodeAsync(request.BillingAddress.StateId, cancellationToken)
                .ConfigureAwait(false);

        // Where the held units are, read before the commit settles the holds. It is frozen onto each
        // line because it is a fact about the order: holds are swept, and "which shelf did this
        // parcel come off" has to survive that. A single-warehouse deployment records the same id
        // on every line and nothing downstream notices; a second warehouse is what makes a pick
        // list wrong for both pickers without it (Step 28B, deliverable 12).
        var allocated = await allocations
            .GetAllocationsAsync(ReservationReferenceTypes.Cart, request.CartId, cancellationToken)
            .ConfigureAwait(false);

        var warehouses = allocated
            .GroupBy(allocation => allocation.ListingId)
            .ToDictionary(group => group.Key, group => group.First().WarehouseId);

        var now = clock.UtcNow;
        var isCod = request.PaymentMethod == QuotePaymentMethod.CashOnDelivery;

        var strategy = context.Database.CreateExecutionStrategy();
        Result<PlacedOrder> outcome = OrdersErrors.NothingToOrder;

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            var orderNumber = await numbering.NextOrderNumberAsync(now, cancellationToken).ConfigureAwait(false);

            var order = Build(
                request,
                customer,
                sellers,
                listings,
                warehouses,
                orderNumber,
                shippingStateCode,
                billingStateCode,
                now);

            await ResolveCommissionsAsync(order, listings, cancellationToken).ConfigureAwait(false);

            order.Record(OrderEvent.Record(
                order.Id,
                subOrderId: null,
                OrderEventTypes.Placed,
                OrderActor.Customer,
                request.CustomerId,
                $"Order {order.OrderNumber} placed.",
                payload: null,
                visible: true,
                now));

            context.Orders.Add(order);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var spent = await SpendAsync(order, quote, cancellationToken).ConfigureAwait(false);

            try
            {
                PaymentInstruction? instruction = null;

                if (isCod)
                {
                    ConfirmForCashOnDelivery(order, now);

                    // Inside the transaction on purpose: losing units from supply is correctable,
                    // selling the same unit twice is not.
                    await stock.SettleAsync(
                            ReservationReferenceTypes.Cart,
                            order.CartId,
                            ReservationOutcome.Committed,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var opened = await payments.InitiateAsync(
                            new PaymentInitiationRequest(
                                order.Id,
                                order.OrderNumber,
                                order.CustomerId,
                                order.AmountPayable,
                                order.CurrencyCode,
                                customer.DisplayName,
                                customer.Email,
                                customer.Mobile,
                                request.IdempotencyKey),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (opened.IsFailure)
                    {
                        outcome = opened.Error;
                        await UnspendAsync(order, spent, cancellationToken).ConfigureAwait(false);
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    instruction = opened.Value;
                }

                events.Placed(order);

                foreach (var subOrder in order.SubOrders.Where(sub => sub.Status == SubOrderStatus.Confirmed))
                {
                    events.Confirmed(order, subOrder);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                outcome = Result.Success(new PlacedOrder(
                    order.Id,
                    order.OrderNumber,
                    order.Status.ToString(),
                    instruction));
            }
            catch
            {
                // The money is the part that has already left this transaction, so it is the part
                // that has to be put back by hand. Both movers are idempotent and both are keyed on
                // the order, so a reversal that races the failure is a no-op rather than a refund.
                await UnspendAsync(order, spent, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);

        return outcome;
    }

    /// <summary>What was spent against the order, so it can be given back if the order does not stand.</summary>
    private sealed record Spend(decimal Wallet);

    /// <summary>Assembles the order, its sub-orders and its lines from the agreed quote.</summary>
    private static Order Build(
        PlaceOrderRequest request,
        CustomerSummary customer,
        IReadOnlyDictionary<Guid, VendorSummary> sellers,
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        IReadOnlyDictionary<Guid, Guid> warehouses,
        string orderNumber,
        string? shippingStateCode,
        string? billingStateCode,
        DateTimeOffset now)
    {
        var quote = request.Quote;

        var order = Order.Place(
            orderNumber,
            request.CustomerId,
            request.CartId,
            request.CheckoutSessionId,
            quote.CurrencyCode);

        order.Capture(
            new OrderCustomerSnapshot
            {
                DisplayName = customer.DisplayName,
                Email = customer.Email,
                Mobile = customer.Mobile,
                Gstin = request.BillingAddress.Gstin ?? customer.Gstin,
            },
            ToSnapshot(request.ShippingAddress, shippingStateCode),
            ToSnapshot(request.BillingAddress, billingStateCode));

        order.Price(
            request.PaymentMethod == QuotePaymentMethod.CashOnDelivery
                ? OrderPaymentMethod.CashOnDelivery
                : OrderPaymentMethod.Prepaid,
            quote.Subtotal,
            quote.DiscountTotal,
            quote.Shipping,
            quote.TaxTotal,
            quote.CodFee,
            quote.WalletApplied,
            quote.RoundingAdjustment,
            quote.GrandTotal,
            quote.AmountPayable,
            request.CouponCode,
            OrderChannels.Contains(request.Channel) ? request.Channel.ToLowerInvariant() : OrderChannels.Web,
            now);

        var linesById = quote.Lines.ToDictionary(line => line.LineId);
        var shipments = request.Shipments.ToDictionary(shipment => shipment.VendorId);
        var index = 0;

        foreach (var group in quote.VendorGroups)
        {
            index++;

            var subOrder = SubOrder.Open(
                order.Id,
                group.VendorId,
                string.Create(CultureInfo.InvariantCulture, $"{orderNumber}-{index:D2}"),
                quote.CurrencyCode);

            if (sellers.TryGetValue(group.VendorId, out var seller))
            {
                subOrder.CaptureVendor(seller.Code, seller.DisplayName, seller.Gstin);
            }

            subOrder.Price(
                group.Subtotal,
                group.Discount,
                group.TaxableValue,
                group.TaxTotal,
                group.Shipping,
                group.ShippingTax,
                group.Total);

            if (shipments.TryGetValue(group.VendorId, out var shipment))
            {
                subOrder.Promise(
                    shipment.OptionCode,
                    shipment.Carrier,
                    shipment.PromisedMinDays,
                    shipment.PromisedMaxDays,
                    shipment.DispatchSlaHours);
            }
            else if (sellers.TryGetValue(group.VendorId, out var fallback))
            {
                // No shipment plan for this seller means the checkout quoted no delivery service for
                // them, which happens while Shipping is a degenerate quoter. Their own SLA is the
                // honest promise to fall back on rather than zero, which would make every sub-order
                // instantly overdue.
                subOrder.Promise(null, null, 0, 0, fallback.DispatchSlaHours);
            }

            foreach (var lineId in group.LineIds)
            {
                if (!linesById.TryGetValue(lineId, out var quoted))
                {
                    continue;
                }

                subOrder.AddLine(BuildLine(subOrder, quoted, listings, warehouses));
            }

            order.AddSubOrder(subOrder);
        }

        return order;
    }

    /// <summary>Freezes one quoted line onto the order.</summary>
    private static OrderLine BuildLine(
        SubOrder subOrder,
        QuoteLine quoted,
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        IReadOnlyDictionary<Guid, Guid> warehouses)
    {
        var line = OrderLine.Create(
            subOrder.Id,
            subOrder.VendorId ?? Guid.Empty,
            quoted.ListingId,
            quoted.Sku,
            quoted.Quantity);

        listings.TryGetValue(quoted.ListingId, out var listing);

        line.AllocateFrom(
            warehouses.TryGetValue(quoted.ListingId, out var warehouseId) ? warehouseId : null);

        line.Capture(
            listing?.VariantId ?? Guid.Empty,
            new ProductSnapshot
            {
                // The quote's name, not the catalogue's. It is what the shopper was looking at when
                // they agreed, and the two can already differ by the time this runs.
                Name = quoted.Name,
                ProductId = listing?.ProductId ?? Guid.Empty,
                CategoryId = listing?.CategoryId,
                BrandId = listing?.BrandId,
                HsnCode = quoted.HsnCode ?? listing?.HsnCode,
                ImageFileId = listing?.PrimaryImageFileId,
                WeightGrams = listing?.WeightGrams ?? 0,
                IsReturnable = listing?.IsReturnable ?? true,
                ReturnWindowDays = listing?.ReturnWindowDays,
            });

        line.Price(
            quoted.UnitMrp,
            quoted.UnitPrice,
            quoted.LineDiscount + quoted.OrderDiscountAllocated,
            quoted.TaxableValue,
            quoted.GstRate,
            quoted.Cgst,
            quoted.Sgst,
            quoted.Igst,
            quoted.Cess,
            quoted.LineTotal);

        return line;
    }

    /// <summary>
    /// Freezes what the platform charges the seller on every line.
    /// </summary>
    /// <remarks>
    /// Resolved now and stored, never re-resolved at settlement. A commission plan changed in June
    /// must not alter what was earned in April, and the seller's first question about a statement is
    /// always which plan was applied.
    /// </remarks>
    private async Task ResolveCommissionsAsync(
        Order order,
        IReadOnlyDictionary<Guid, ListingSummary> listings,
        CancellationToken cancellationToken)
    {
        foreach (var subOrder in order.SubOrders)
        {
            foreach (var line in subOrder.Lines)
            {
                listings.TryGetValue(line.ListingId, out var listing);

                var quote = await commissions
                    .ResolveAsync(
                        subOrder.VendorId ?? Guid.Empty,
                        listing?.CategoryId,
                        line.UnitPrice,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (quote is null)
                {
                    // A seller with no plan is a configuration gap, not a reason to refuse a sale
                    // the shopper has already paid for. Zero is recorded, and the settlement report
                    // is where an operator sees that nothing was charged.
                    line.SetCommission(null, 0m, 0m);
                    continue;
                }

                var commission = Math.Round(
                    ((line.TaxableValue * quote.RatePercent) / 100m) + (quote.FixedFee * line.Quantity),
                    4,
                    MidpointRounding.AwayFromZero);

                line.SetCommission(quote.PlanId, quote.RatePercent, commission);
            }
        }
    }

    /// <summary>Commits the coupon and the store credit the quote said would be spent.</summary>
    private async Task<Spend> SpendAsync(Order order, QuoteResult quote, CancellationToken cancellationToken)
    {
        var applied = quote.Promotions
            .Where(promotion => promotion.Applied && promotion.DiscountAmount > 0m)
            .Select(promotion => new PromotionRedemptionRequest(promotion.PromotionId, promotion.DiscountAmount))
            .ToArray();

        if (applied.Length > 0)
        {
            // A promotion whose last use was taken between quoting and placing comes back here. The
            // order stands: the shopper agreed to a total, and charging them more than the
            // confirmation says because a counter moved is worse than honouring a coupon once too
            // often. The refusal is recorded so the discrepancy is visible in the campaign report.
            var refused = await promotions
                .RedeemAsync(order.Id, order.CustomerId, applied, cancellationToken)
                .ConfigureAwait(false);

            if (refused.Count > 0)
            {
                order.Record(OrderEvent.Record(
                    order.Id,
                    subOrderId: null,
                    OrderEventTypes.Note,
                    OrderActor.System,
                    actorId: null,
                    $"{refused.Count} promotion(s) could not be redeemed and were honoured on the agreed total.",
                    payload: null,
                    visible: false,
                    order.PlacedAt));
            }
        }

        if (order.WalletApplied <= 0m)
        {
            return new Spend(0m);
        }

        var redeemed = await wallet
            .RedeemAsync(
                order.CustomerId,
                order.WalletApplied,
                StoreCreditReasons.OrderPayment,
                "order",
                order.Id,
                cancellationToken)
            .ConfigureAwait(false);

        return new Spend(redeemed);
    }

    /// <summary>Gives back everything <see cref="SpendAsync"/> spent, for an order that will not stand.</summary>
    private async Task UnspendAsync(Order order, Spend spent, CancellationToken cancellationToken)
    {
        await promotions.ReverseAsync(order.Id, cancellationToken).ConfigureAwait(false);

        if (spent.Wallet > 0m)
        {
            await wallet
                .CreditAsync(
                    order.CustomerId,
                    spent.Wallet,
                    StoreCreditReasons.OrderCancelled,
                    "order",
                    order.Id,
                    expiresAt: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Confirms every sub-order of a cash-on-delivery order.
    /// </summary>
    /// <remarks>
    /// COD is accepted rather than paid, so there is no gateway to wait for and nothing would ever
    /// move the order out of <c>PendingPayment</c>. The payment status stays <c>Pending</c> — the
    /// money genuinely has not been collected, and it is collected at the door.
    /// </remarks>
    private static void ConfirmForCashOnDelivery(Order order, DateTimeOffset now)
    {
        foreach (var subOrder in order.SubOrders)
        {
            if (!subOrder.TransitionTo(SubOrderStatus.Confirmed, OrderActor.System, now, returnWindowDays: 0))
            {
                continue;
            }

            order.Record(OrderEvent.StatusChange(
                order.Id,
                subOrder.Id,
                SubOrderStatus.PendingPayment,
                SubOrderStatus.Confirmed,
                OrderActor.System,
                actorId: null,
                "Order confirmed. Payment will be collected on delivery.",
                visible: true,
                now));
        }

        order.Rederive(now);
    }

    /// <summary>Copies a checkout address onto the order, adding the state code it was taxed against.</summary>
    private static OrderAddressSnapshot ToSnapshot(OrderAddress address, string? stateCode)
        => new()
        {
            RecipientName = address.RecipientName,
            Mobile = address.Mobile,
            Line1 = address.Line1,
            Line2 = address.Line2,
            Landmark = address.Landmark,
            City = address.City,
            StateId = address.StateId,
            StateCode = stateCode,
            Pincode = address.Pincode,
            Gstin = address.Gstin,
        };
}
