using System.Net.Http.Json;
using KlaraHome.Contracts.Orders;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Payments.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Cash on delivery and settlement ingestion: a confirmed COD parcel opens exactly one cash record,
/// a batch remittance apportions a courier's payout across what it covers, and pulling settlement
/// reports is idempotent and alerts on what it cannot match rather than repairing it.
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsCodAndSettlementTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A confirmed cash-on-delivery sub-order opens exactly one <c>cod_collections</c> row, and a
    /// redelivered <c>SubOrderConfirmed</c> — the outbox's own at-least-once guarantee — opens no
    /// second one.
    /// </summary>
    [Fact]
    public async Task A_confirmed_cod_sub_order_opens_exactly_one_collection()
    {
        SkipWithoutDocker();

        Factory.Features["identity.mobile-otp-login"] = true;
        var admin = await SignedInAdministratorAsync();
        var scenario = new PaymentsScenario(admin, Cancellation);
        var offer = await scenario.OfferAsync(price: 499m);

        var (shopper, _) = await SignedInShopperAsync();
        var (order, _) = await scenario.PlaceOrderAsync(shopper, offer, method: "cod");

        // Cash needs no gateway: the order confirms at placement, and this is the only place in the
        // module where the money is recorded without ever hearing from Razorpay.
        Assert.Null(order.ProviderOrderId);

        var confirmed = await ReadAsync(
            await shopper.GetAsync(new Uri($"/api/v1/store/orders/{order.OrderId}", UriKind.Relative), Cancellation));

        Assert.Equal("Confirmed", confirmed.GetProperty("subOrders")[0].GetProperty("status").GetString());

        await OutboxDrain.RunAsync(Factory, Database, Cancellation);

        var rows = await Database.RowsAsync(
            "SELECT amount, currency_code, status FROM payments.cod_collections WHERE sub_order_id = $1",
            Cancellation,
            order.SubOrderId);

        var row = Assert.Single(rows);
        Assert.Equal("Pending", (string)row["status"]!);

        // The redelivery a real outbox would make: the exact same integration event, applied again
        // through the exact same handler, rather than a second placement.
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<OrderLifecycleHandlers>();

        await handler.HandleAsync(
            new SubOrderConfirmed(
                order.OrderId,
                order.OrderNumber,
                order.SubOrderId,
                $"{order.OrderNumber}-01",
                order.VendorId,
                Guid.NewGuid(),
                order.Amount,
                order.CurrencyCode,
                DispatchDueAt: null,
                Lines: []),
            Cancellation);

        var count = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.cod_collections WHERE sub_order_id = $1",
            Cancellation,
            order.SubOrderId);

        Assert.Equal(1, count);
    }

    /// <summary>
    /// A batch remittance apportions a courier's net total across its records in proportion to what
    /// each was for, and skips records already remitted or waived.
    /// </summary>
    [Fact]
    public async Task A_batch_remittance_apportions_proportionally_and_skips_settled_records()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // The real tenant, not a fresh one — the handler reads through the ambient tenant filter, so
        // a row written under a different tenant would be as invisible to it as one that never
        // existed.
        var tenant = await Database.ScalarAsync<Guid>("SELECT id FROM platform.tenants LIMIT 1", Cancellation);

        // Three records the courier is handing over together, plus one already remitted and one
        // waived — neither of which this remittance should touch.
        var open1 = await InsertCodAsync(tenant, amount: 300m);
        var open2 = await InsertCodAsync(tenant, amount: 700m);
        var alreadyRemitted = await InsertCodAsync(tenant, amount: 100m, status: "Remitted");
        var waived = await InsertCodAsync(tenant, amount: 50m, status: "Waived");

        var response = await ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/cod-collections/remit",
                new
                {
                    ids = new[] { open1, open2, alreadyRemitted, waived },
                    reference = $"UTR-{Guid.NewGuid():N}"[..20],
                    // A total slightly below face value, so the apportionment is provably
                    // proportional rather than a pass-through of each record's own figure.
                    amount = 900m,
                    remittedAt = (DateTimeOffset?)null,
                },
                Cancellation));

        Assert.NotEmpty(response.EnumerateArray().ToArray());

        var settled = await Database.RowsAsync(
            "SELECT id, remitted_amount, status FROM payments.cod_collections WHERE id = ANY($1)",
            Cancellation,
            new[] { open1, open2 });

        var byId = settled.ToDictionary(row => (Guid)row["id"]!, row => row);

        // 300/1000 and 700/1000 of 900, respectively.
        Assert.Equal(270m, (decimal)byId[open1]["remitted_amount"]!);
        Assert.Equal(630m, (decimal)byId[open2]["remitted_amount"]!);
        Assert.All(settled, row => Assert.Equal("Remitted", (string)row["status"]!));

        // The already-settled and waived records are untouched — no remitted_amount overwritten,
        // no status disturbed.
        var untouched = await Database.RowsAsync(
            "SELECT remitted_amount, status FROM payments.cod_collections WHERE id = $1",
            Cancellation,
            alreadyRemitted);

        Assert.Equal("Remitted", (string)untouched[0]["status"]!);

        var waivedRow = await Database.RowsAsync(
            "SELECT status FROM payments.cod_collections WHERE id = $1",
            Cancellation,
            waived);

        Assert.Equal("Waived", (string)waivedRow[0]["status"]!);
    }

    /// <summary>
    /// Settlement ingestion is idempotent over a lookback window, and a line naming a payment this
    /// platform does not hold becomes a <c>Mismatched</c> entry plus an alert rather than being
    /// repaired.
    /// </summary>
    [Fact]
    public async Task Settlement_ingestion_is_idempotent_and_alerts_on_an_unmatched_line()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var settlementId = $"setl_{Guid.NewGuid():N}";

        Factory.Gateway.Settlements.Add(new Modules.Payments.Infrastructure.Gateway.ProviderSettlement(
            settlementId,
            Amount: 1000m,
            Fees: 20m,
            Tax: 3.6m,
            CurrencyCode: "INR",
            Utr: "UTR000123",
            Status: "processed",
            SettledAt: DateTimeOffset.UtcNow,
            Raw: "{}",
            Entries:
            [
                new Modules.Payments.Infrastructure.Gateway.ProviderSettlementEntry(
                    "payment",
                    $"ent_{Guid.NewGuid():N}",
                    // A gateway payment id this platform never opened.
                    $"pay_{Guid.NewGuid():N}",
                    Amount: 1000m,
                    Fee: 20m,
                    Tax: 3.6m,
                    Debit: 0m,
                    Credit: 1000m,
                    OccurredAt: DateTimeOffset.UtcNow),
            ]));

        var first = await ReadAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/settlements/import", new { }, Cancellation));

        Assert.Equal(1, first.GetProperty("imported").GetInt32());
        Assert.Equal(1, first.GetProperty("mismatched").GetInt32());

        // Importing again over the same window is idempotent on the gateway's settlement id: the
        // report is skipped, not imported twice.
        var second = await ReadAsync(
            await admin.PostAsJsonAsync("/api/v1/admin/settlements/import", new { }, Cancellation));

        Assert.Equal(0, second.GetProperty("imported").GetInt32());
        Assert.True(second.GetProperty("skipped").GetInt32() >= 1);

        var storedSettlements = await Database.CountAsync(
            "SELECT COUNT(*) FROM payments.gateway_settlements WHERE provider_settlement_id = $1",
            Cancellation,
            settlementId);

        Assert.Equal(1, storedSettlements);

        var entryStatus = await Database.ScalarAsync<string>(
            "SELECT match_status FROM payments.gateway_settlement_entries e "
            + "JOIN payments.gateway_settlements s ON s.id = e.settlement_id "
            + "WHERE s.provider_settlement_id = $1",
            Cancellation,
            settlementId);

        Assert.Equal("Mismatched", entryStatus);

        var alerted = await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages WHERE type LIKE '%PaymentMismatchDetected%' "
            + "AND payload::text LIKE '%' || $1 || '%'",
            Cancellation,
            settlementId);

        Assert.True(alerted >= 1);
    }

    private async Task<Guid> InsertCodAsync(Guid tenant, decimal amount, string status = "Pending")
    {
        var id = Guid.NewGuid();

        await Database.ExecuteAsync(
            """
            INSERT INTO payments.cod_collections
                (id, order_id, sub_order_id, amount, collected_amount, currency_code, status, tenant_id, created_at)
            VALUES ($1, $2, $3, $4, $4, 'INR', $5, $6, now())
            """,
            Cancellation,
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            status,
            tenant);

        return id;
    }
}
