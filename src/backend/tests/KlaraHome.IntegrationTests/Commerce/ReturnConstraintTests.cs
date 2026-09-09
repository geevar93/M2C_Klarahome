using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The <c>returns</c> migration's own <c>CHECK</c> constraints, proved against a live database
/// rather than read off the migration file.
/// </summary>
/// <remarks>
/// The migration itself already applies and re-runs clean on every test in this collection — that is
/// what <c>KlaraHomeSchemaFixture</c> does before a single test runs, and <see cref="SchemaMigrationTests"/>
/// already asserts it generically for every module's schema. What is specific to Step 17 is that its
/// constraints refuse what they are meant to, proved here against real rows a returns flow produced.
/// </remarks>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class ReturnConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    [Fact]
    public async Task A_refunded_return_with_no_mode_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        Factory.Features["identity.mobile-otp-login"] = true;
        var scenario = new ReturnsScenario(Factory, admin, Database, Cancellation);

        var catalogue = await scenario.CatalogueAsync();
        var order = await scenario.DeliveredOrderAsync(catalogue);
        var returnId = await RaiseAndReceiveAsync(scenario, admin, order);

        var refusal = await Database.RefusalAsync(
            "UPDATE returns.returns SET refund_amount = 100, refund_mode = NULL WHERE id = $1",
            Cancellation,
            returnId);

        Assert.Equal("23514", refusal);
    }

    [Fact]
    public async Task An_accepted_quantity_above_the_quantity_sent_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        Factory.Features["identity.mobile-otp-login"] = true;
        var scenario = new ReturnsScenario(Factory, admin, Database, Cancellation);

        var catalogue = await scenario.CatalogueAsync();
        var order = await scenario.DeliveredOrderAsync(catalogue, quantity: 1);
        var returnId = await RaiseAndReceiveAsync(scenario, admin, order);

        var lineId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM returns.return_lines WHERE return_id = $1",
            Cancellation,
            returnId);

        var refusal = await Database.RefusalAsync(
            "UPDATE returns.return_lines SET quantity_accepted = quantity + 1 WHERE id = $1",
            Cancellation,
            lineId);

        Assert.Equal("23514", refusal);
    }

    [Fact]
    public async Task A_credit_note_carrying_both_igst_and_cgst_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        Factory.Features["identity.mobile-otp-login"] = true;
        var scenario = new ReturnsScenario(Factory, admin, Database, Cancellation);

        var catalogue = await scenario.CatalogueAsync();
        var order = await scenario.DeliveredOrderAsync(catalogue);
        var returnId = await RaiseAndReceiveAsync(scenario, admin, order);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/returns/{returnId}/qc",
                new { result = "pass", disposition = (string?)null, notes = (string?)null, lines = Array.Empty<object>() },
                Cancellation),
            Cancellation);

        var creditNoteId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM returns.credit_notes WHERE return_id = $1",
            Cancellation,
            returnId);

        var refusal = await Database.RefusalAsync(
            "UPDATE returns.credit_notes SET igst = 10, cgst = 5 WHERE id = $1",
            Cancellation,
            creditNoteId);

        Assert.Equal("23514", refusal);
    }

    private async Task<Guid> RaiseAndReceiveAsync(ReturnsScenario scenario, HttpClient admin, DeliveredOrder order)
    {
        var client = Factory.CreateClient();

        // A fresh code: the one from placing the order has already been consumed.
        (await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile = order.CustomerMobile },
            Cancellation))
            .EnsureSuccessStatusCode();

        var code = Factory.Otp.Latest(order.CustomerMobile, Modules.Identity.Domain.OtpPurpose.Login);
        await TestSignIn.SignInWithOtpAsync(client, order.CustomerMobile, code, Cancellation);

        var raised = await Rest.ReadAsync(
            await client.PostAsJsonAsync(
                "/api/v1/store/returns",
                new
                {
                    subOrderId = order.SubOrderId,
                    type = (string?)null,
                    reasonCode = "size-issue",
                    reasonNote = (string?)null,
                    lines = new[] { new { orderLineId = order.OrderLineId, quantity = order.Quantity } },
                    evidenceFileIds = (Guid[]?)null,
                    refundMode = (string?)null,
                },
                Cancellation),
            Cancellation);

        var returnId = raised.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/returns/{returnId}/approve",
                new { amount = (decimal?)null, pickupRequired = (bool?)null, note = (string?)null },
                Cancellation),
            Cancellation);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/returns/{returnId}/schedule-pickup",
                new { pickupAt = (DateTimeOffset?)null, manualAwb = (string?)null, manualCourier = (string?)null },
                Cancellation),
            Cancellation);

        var shipmentId = await Database.ScalarAsync<Guid>(
            "SELECT pickup_shipment_id FROM returns.returns WHERE id = $1",
            Cancellation,
            returnId);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/tracking",
                new { status = "PickedUp", remark = (string?)null, occurredAt = (DateTimeOffset?)null },
                Cancellation),
            Cancellation);

        // ShippingLifecycleHandlers moves the return on this scan, but it reacts to
        // ShipmentTrackingUpdated through the outbox, not in-process — the host under test never
        // drains its own outbox.
        await scenario.DrainOutboxAsync();

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/tracking",
                new { status = "Delivered", remark = (string?)null, occurredAt = (DateTimeOffset?)null },
                Cancellation),
            Cancellation);

        await scenario.DrainOutboxAsync();

        return returnId;
    }
}
