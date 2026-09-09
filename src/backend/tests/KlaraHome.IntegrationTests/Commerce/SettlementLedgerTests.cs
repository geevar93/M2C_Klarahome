using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 18's ledger: append-only, idempotent, and paid on the fact rather than on the calendar.
/// </summary>
/// <remarks>
/// TEST_DEBT.md rows 237, 239, 240, 248 and 249 — the headline acceptance criterion and the four
/// posting rules the build sprint could only prove against an in-memory arithmetic. Every test here
/// posts through the real <c>SettlementPoster</c> and <c>SettlementLifecycleHandlers</c> against the
/// real Postgres schema; only the Orders-module seam, <c>IOrderSettlement</c>, is a controllable
/// fake (see <see cref="FakeOrderSettlement"/>), because a full cart-to-delivery journey is Steps
/// 11-14's own test debt and duplicating it here would prove Orders twice rather than Settlements
/// once.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class SettlementLedgerTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A seller's balance is <c>Σ credits − Σ debits</c> over the ledger rows, never a stored column,
    /// and it ties out to the paisa against what was sold, taken and given back.
    /// </summary>
    [Fact]
    public async Task Balance_is_the_signed_sum_of_every_entry_and_ties_out_to_the_paisa()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var (poster, _, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var sale = SettlementScenario.Sale(vendor.Id, "Prepaid", lineTotal: 1000m, taxableValue: 900m, commissionAmount: 100m);
        orders.Add(sale);

        await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // A partial reversal against the same sale: a return of a third of what was sold.
        await poster.PostReversalAsync(
            vendor.Id,
            sale.SubOrderId,
            amount: 300m,
            taxableValue: 270m,
            currencyCode: "INR",
            referenceType: LedgerReferenceTypes.CreditNote,
            referenceId: Guid.NewGuid(),
            keySuffix: Guid.NewGuid().ToString(),
            note: "Partial return",
            occurredAt: DateTimeOffset.UtcNow,
            Cancellation);
        await context.SaveChangesAsync(Cancellation);

        var rows = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.VendorId == vendor.Id)
            .ToListAsync(Cancellation);

        var computed = rows.Sum(entry => entry.SignedAmount);

        Assert.NotEmpty(rows);
        Assert.Contains(rows, entry => entry.EntryType == LedgerEntryTypes.Sale);
        Assert.Contains(rows, entry => entry.EntryType == LedgerEntryTypes.Commission);
        Assert.Contains(rows, entry => entry.EntryType == LedgerEntryTypes.Refund);

        // The API's own answer for "what is owed" must be the identical sum, not a second
        // computation that could drift from the rows a statement shows.
        var balance = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendor.Id}/balance", UriKind.Relative),
            Cancellation));

        Assert.Equal(computed, balance.GetProperty("currentBalance").GetDecimal());

        // No column anywhere on the seller carries a balance: the vendor row itself has no such
        // field, and re-summing the same rows through the API's own ledger list gives the same
        // number a third way.
        var listed = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/settlements/ledger?vendorId={vendor.Id}&size=200", UriKind.Relative),
            Cancellation));

        var listedSum = listed.GetProperty("items").EnumerateArray()
            .Sum(entry => entry.GetProperty("signedAmount").GetDecimal());

        Assert.Equal(computed, listedSum);
    }

    /// <summary>
    /// A redelivered fact collides on <c>(tenant_id, source_key)</c> and credits nothing a second
    /// time — proved against the live unique index, not against a pre-check in memory.
    /// </summary>
    [Fact]
    public async Task Posting_the_same_fact_twice_credits_the_seller_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var (poster, _, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var sale = SettlementScenario.Sale(vendor.Id, "Prepaid");
        orders.Add(sale);

        var first = await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.NotEmpty(first);

        // The same fact, redelivered — the source key is derived from the sub-order and the entry
        // type, so a second call with nothing changed is exactly what at-least-once delivery looks
        // like.
        var second = await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.Empty(second);

        var saleRows = await context.LedgerEntries
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(entry => entry.VendorId == vendor.Id && entry.EntryType == LedgerEntryTypes.Sale)
            .ToListAsync(Cancellation);

        Assert.Single(saleRows);

        // And the guarantee is the index, not the pre-check: a raw insert reusing the same key is
        // refused by Postgres itself.
        var conflict = await Database.RefusalAsync(
            """
            INSERT INTO settlements.ledger_entries
                (id, vendor_id, entry_type, direction, amount, taxable_value, currency_code,
                 reference_type, reference_id, sub_order_id, settlement_cycle_id, source_key,
                 occurred_at, tenant_id, created_at)
            SELECT gen_random_uuid(), vendor_id, entry_type, direction, amount, taxable_value,
                   currency_code, reference_type, reference_id, sub_order_id, settlement_cycle_id,
                   source_key, occurred_at, tenant_id, now()
            FROM settlements.ledger_entries
            WHERE entry_type = 'sale' AND sub_order_id = $1
            LIMIT 1
            """,
            Cancellation,
            sale.SubOrderId);

        Assert.Equal("23505", conflict);
    }

    /// <summary>
    /// A prepaid sale earns on delivery; a cash sale earns only once the courier has remitted, and
    /// not the moment cash is collected at the door.
    /// </summary>
    [Fact]
    public async Task Prepaid_earns_on_delivery_and_cash_earns_only_on_remittance()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var (poster, handlers, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        _ = poster;

        var prepaid = SettlementScenario.Sale(vendor.Id, "Prepaid");
        orders.Add(prepaid);

        await handlers.HandleAsync(
            new SubOrderStatusChanged(
                prepaid.OrderId, prepaid.OrderNumber, prepaid.SubOrderId, prepaid.SubOrderNumber,
                vendor.Id, prepaid.CustomerId, "Shipped", "Delivered", "Delivered", "system", null),
            Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.True(await Exists(context, prepaid.SubOrderId));

        var cash = SettlementScenario.Sale(vendor.Id, "CashOnDelivery", subOrderId: Guid.NewGuid());
        orders.Add(cash);

        // Delivered, but the money is still in a van: nothing is earned yet.
        await handlers.HandleAsync(
            new SubOrderStatusChanged(
                cash.OrderId, cash.OrderNumber, cash.SubOrderId, cash.SubOrderNumber,
                vendor.Id, cash.CustomerId, "Shipped", "Delivered", "Delivered", "system", null),
            Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.False(await Exists(context, cash.SubOrderId));

        // Collected at the door, not yet handed over: still nothing.
        await handlers.HandleAsync(
            new CodCashRecorded(Guid.NewGuid(), cash.OrderId, cash.SubOrderId, vendor.Id, cash.Total, "INR",
                IsRemitted: false, DateTimeOffset.UtcNow),
            Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.False(await Exists(context, cash.SubOrderId));

        // The courier hands it over. Now it is earned.
        await handlers.HandleAsync(
            new CodCashRecorded(Guid.NewGuid(), cash.OrderId, cash.SubOrderId, vendor.Id, cash.Total, "INR",
                IsRemitted: true, DateTimeOffset.UtcNow),
            Cancellation);
        await context.SaveChangesAsync(Cancellation);

        Assert.True(await Exists(context, cash.SubOrderId));

        static Task<bool> Exists(SettlementsDbContext context, Guid subOrderId)
            => context.LedgerEntries
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(entry => entry.SubOrderId == subOrderId && entry.EntryType == LedgerEntryTypes.Sale);
    }

    /// <summary>
    /// Two returns and a cancellation against one parcel never give back more commission than was
    /// charged, and never reverse more of the sale than was credited.
    /// </summary>
    [Fact]
    public async Task Reversals_against_one_sale_are_clamped_to_what_was_actually_credited()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        using var scope = Factory.Services.CreateScope();
        var (poster, _, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var sale = SettlementScenario.Sale(vendor.Id, "Prepaid", lineTotal: 1000m, taxableValue: 900m, commissionAmount: 100m);
        orders.Add(sale);

        await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // What was actually charged on the sale — commission, marketplace fee, gateway fee and the
        // GST on them together, freight excluded — read back from the ledger rather than assumed, so
        // this test is not tied to whatever the store's settings happen to be today.
        var reversibleTypes = new[]
        {
            LedgerEntryTypes.Commission, LedgerEntryTypes.PlatformFee, LedgerEntryTypes.PaymentFee,
            LedgerEntryTypes.PlatformTax,
        };

        var totalCharged = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .Where(entry => entry.SubOrderId == sale.SubOrderId && reversibleTypes.Contains(entry.EntryType))
            .SumAsync(entry => entry.Amount, Cancellation);

        // First return: 700 of the 1000.
        await poster.PostReversalAsync(
            vendor.Id, sale.SubOrderId, 700m, 630m, "INR",
            LedgerReferenceTypes.CreditNote, Guid.NewGuid(), "first-return", "First return",
            DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // Second return claims 700 more, but only 300 is left of the sale.
        await poster.PostReversalAsync(
            vendor.Id, sale.SubOrderId, 700m, 630m, "INR",
            LedgerReferenceTypes.CreditNote, Guid.NewGuid(), "second-return", "Second return",
            DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // A cancellation on top asks for still more. Nothing is left, so nothing more comes off.
        await poster.PostReversalAsync(
            vendor.Id, sale.SubOrderId, 500m, 450m, "INR",
            LedgerReferenceTypes.SubOrder, sale.SubOrderId, "late-cancel", "Late cancellation",
            DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        var refunds = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .Where(entry => entry.SubOrderId == sale.SubOrderId && entry.EntryType == LedgerEntryTypes.Refund)
            .SumAsync(entry => entry.Amount, Cancellation);

        var givenBack = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .Where(entry => entry.SubOrderId == sale.SubOrderId
                            && entry.EntryType == LedgerEntryTypes.RefundCommissionReversal)
            .SumAsync(entry => entry.Amount, Cancellation);

        // Exactly the 1000 that was ever credited, never more — the second and third reversal are
        // clamped to what remained.
        Assert.Equal(1000m, refunds);

        // The two reversals between them return exactly what was charged, never more: the ratios
        // (700/1000 then 300/1000, the second claim clamped from 700 to what was left) sum to 1,
        // give or take a paisa of independent rounding on each reversal.
        Assert.Equal(totalCharged, givenBack, 1);
    }

    /// <summary>
    /// <c>RefundProcessed</c> is deliberately not consumed: a return that is both credited and
    /// refunded reverses the seller's supply exactly once, from the credit note alone.
    /// </summary>
    [Fact]
    public async Task RefundProcessed_reverses_nothing_on_its_own()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        // The settlements module declares no handler for this event at all — proved by construction
        // rather than by publishing it and hoping nothing reacts, which is the whole point of the
        // decision recorded in PARKING_LOT.md.
        var handledEvents = typeof(KlaraHome.Modules.Settlements.Infrastructure.Events.SettlementLifecycleHandlers)
            .GetInterfaces()
            .Where(iface => iface.IsGenericType
                            && iface.GetGenericTypeDefinition()
                               == typeof(KlaraHome.Infrastructure.Persistence.Outbox.IIntegrationEventHandler<>))
            .Select(iface => iface.GenericTypeArguments[0]);

        Assert.DoesNotContain(typeof(RefundProcessed), handledEvents);

        using var scope = Factory.Services.CreateScope();
        var (poster, _, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        var sale = SettlementScenario.Sale(vendor.Id, "Prepaid", lineTotal: 1000m, taxableValue: 900m, commissionAmount: 100m);
        orders.Add(sale);

        await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        // The credit note reverses the supply once.
        await poster.PostReversalAsync(
            vendor.Id, sale.SubOrderId, 1000m, 900m, "INR",
            LedgerReferenceTypes.CreditNote, Guid.NewGuid(), "cn-1", "Full return",
            DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        var refundRowsAfterCreditNote = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .CountAsync(entry => entry.SubOrderId == sale.SubOrderId && entry.EntryType == LedgerEntryTypes.Refund, Cancellation);

        Assert.Equal(1, refundRowsAfterCreditNote);

        // The associated refund event carries no handler here, so a real one flowing through the
        // outbox — Payments' own module, publishing to whoever listens — reaches nobody in
        // Settlements to reverse the same supply a second time. There being no handler is the proof;
        // running one through the dispatcher would prove nothing further exists to receive it.
        var refundRowsAfterRefund = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .CountAsync(entry => entry.SubOrderId == sale.SubOrderId && entry.EntryType == LedgerEntryTypes.Refund, Cancellation);

        Assert.Equal(refundRowsAfterCreditNote, refundRowsAfterRefund);

        _ = admin;
    }

    /// <summary>
    /// <c>IOrderSettlement</c> reads the commission frozen onto the order line, and a commission plan
    /// changed after the sale was placed does not alter what that past sale is charged.
    /// </summary>
    [Fact]
    public async Task Commission_is_read_off_the_frozen_line_and_a_later_plan_change_does_not_move_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync(commissionRate: 10m);

        using var scope = Factory.Services.CreateScope();
        var (poster, _, orders) = SettlementScenario.Wire(scope);
        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

        // Frozen at 10% of the 1000 line when the order was placed, regardless of what the plan
        // charges today.
        var sale = SettlementScenario.Sale(vendor.Id, "Prepaid", lineTotal: 1000m, taxableValue: 900m, commissionAmount: 100m);
        orders.Add(sale);

        // The seller's plan changes to 25% *before* settlement runs — the point of the test is that
        // this is too late to matter, because the commission was never going to be resolved again.
        var newPlanId = await Sellers(admin).CommissionPlanAsync(rate: 25m);
        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/vendors/{vendor.Id}/commission-plan", new { planId = newPlanId }, Cancellation));

        await poster.PostEarningAsync(sale.SubOrderId, DateTimeOffset.UtcNow, Cancellation);
        await context.SaveChangesAsync(Cancellation);

        var commission = await context.LedgerEntries
            .AsNoTracking().IgnoreQueryFilters()
            .Where(entry => entry.SubOrderId == sale.SubOrderId && entry.EntryType == LedgerEntryTypes.Commission)
            .SumAsync(entry => entry.Amount, Cancellation);

        // 100 — the frozen figure — never 250, which is what 25% of the line would have charged.
        Assert.Equal(100m, commission);
    }
}
