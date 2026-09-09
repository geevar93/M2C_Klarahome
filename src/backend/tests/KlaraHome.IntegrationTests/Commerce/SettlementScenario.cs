using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Events;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// A controllable stand-in for <see cref="IOrderSettlement"/>, the one seam Step 18's tests do not
/// drive through a real order.
/// </summary>
/// <remarks>
/// <para>
/// Building a full cart-to-delivery order journey belongs to Steps 11-14's own test debt, and
/// duplicating it here would prove Orders' correctness a second time rather than Settlements'. What
/// Step 18 owns is what happens to a <see cref="SubOrderSettlementView"/> once Settlements has one —
/// the frozen commission, the earning timing, the reversal clamp — and this fake supplies exactly
/// that shape, under the test's own control, so <c>SettlementPoster</c> and
/// <c>SettlementLifecycleHandlers</c> are exercised as the real, unmodified production types.
/// </para>
/// <para>
/// Never registered in DI. Each row is set explicitly by the test that needs it, which is what
/// makes "the commission on the line is frozen and does not move when the plan does" provable: the
/// fake never re-resolves anything, exactly as the real Orders implementation must not either.
/// </para>
/// </remarks>
internal sealed class FakeOrderSettlement : IOrderSettlement
{
    private readonly Dictionary<Guid, SubOrderSettlementView> _views = [];

    public void Add(SubOrderSettlementView view) => _views[view.SubOrderId] = view;

    public Task<Result<SubOrderSettlementView>> GetAsync(Guid subOrderId, CancellationToken cancellationToken = default)
        => Task.FromResult(
            _views.TryGetValue(subOrderId, out var view)
                ? Result.Success(view)
                : Result.Failure<SubOrderSettlementView>(Error.NotFound("SUB_ORDER_NOT_FOUND", "No such sub-order.")));

    public Task<IReadOnlyList<SubOrderSettlementView>> GetManyAsync(
        IReadOnlyCollection<Guid> subOrderIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SubOrderSettlementView>>(
            [.. subOrderIds.Where(_views.ContainsKey).Select(id => _views[id])]);
}

/// <summary>Builds the fixtures the settlements test classes share.</summary>
internal static class SettlementScenario
{
    /// <summary>A minimal, one-line prepaid or COD sale, already carrying a frozen commission.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="paymentMethod"><c>Prepaid</c> or <c>CashOnDelivery</c>.</param>
    /// <param name="lineTotal">What the shopper paid for the line, inclusive of tax.</param>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="commissionAmount">What the platform froze onto the line at placement.</param>
    /// <param name="subOrderId">The id to key the view under, or a new one.</param>
    /// <param name="quantity">How many units, all delivered by default.</param>
    /// <param name="quantityCancelled">How many of those were cancelled.</param>
    public static SubOrderSettlementView Sale(
        Guid vendorId,
        string paymentMethod,
        decimal lineTotal = 1000m,
        decimal taxableValue = 900m,
        decimal commissionAmount = 100m,
        Guid? subOrderId = null,
        int quantity = 1,
        int quantityCancelled = 0)
    {
        var id = subOrderId ?? Guid.NewGuid();
        var lineId = Guid.NewGuid();

        var line = new SettleableLine(
            OrderLineId: lineId,
            ListingId: Guid.NewGuid(),
            CategoryId: null,
            Sku: "SKU-1",
            Name: "Test item",
            Quantity: quantity,
            QuantityCancelled: quantityCancelled,
            QuantityReturned: 0,
            UnitPrice: lineTotal / quantity,
            TaxableValue: taxableValue,
            TaxAmount: lineTotal - taxableValue,
            LineTotal: lineTotal,
            CommissionRate: 10m,
            CommissionAmount: commissionAmount,
            CommissionPlanId: Guid.NewGuid());

        return new SubOrderSettlementView(
            OrderId: Guid.NewGuid(),
            OrderNumber: "ORD-TEST",
            SubOrderId: id,
            SubOrderNumber: "SUB-TEST",
            VendorId: vendorId,
            VendorCode: "SEL-TEST",
            VendorName: "Test Seller",
            VendorGstin: null,
            CustomerId: Guid.NewGuid(),
            Status: "Delivered",
            PaymentMethod: paymentMethod,
            IsPaid: true,
            ItemsTotal: lineTotal,
            DiscountTotal: 0m,
            ShippingTotal: 0m,
            ShippingTax: 0m,
            TaxableValue: taxableValue,
            TaxTotal: lineTotal - taxableValue,
            Total: lineTotal,
            CurrencyCode: "INR",
            PlacedAt: DateTimeOffset.UtcNow.AddDays(-10),
            DeliveredAt: DateTimeOffset.UtcNow.AddDays(-3),
            ReturnWindowEndsAt: DateTimeOffset.UtcNow.AddDays(4),
            InvoiceId: null,
            InvoiceNumber: null,
            FinancialYear: null,
            Lines: [line]);
    }

    /// <summary>
    /// A real <see cref="SettlementPoster"/> and <see cref="SettlementLifecycleHandlers"/> wired to a
    /// scope's real <see cref="SettlementsDbContext"/>, real <see cref="ICommissionResolver"/>, real
    /// settings and real clock, but reading sales through a <see cref="FakeOrderSettlement"/> the test
    /// controls.
    /// </summary>
    /// <param name="scope">A DI scope over the host under test.</param>
    public static (SettlementPoster Poster, SettlementLifecycleHandlers Handlers, FakeOrderSettlement Orders) Wire(
        IServiceScope scope)
    {
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IStoreSettings>();
        var commissions = scope.ServiceProvider.GetRequiredService<ICommissionResolver>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var orders = new FakeOrderSettlement();
        var poster = new SettlementPoster(
            context,
            orders,
            commissions,
            settings,
            NullLogger<SettlementPoster>.Instance);

        var handlers = new SettlementLifecycleHandlers(
            context,
            poster,
            orders,
            clock,
            NullLogger<SettlementLifecycleHandlers>.Instance);

        return (poster, handlers, orders);
    }
}
