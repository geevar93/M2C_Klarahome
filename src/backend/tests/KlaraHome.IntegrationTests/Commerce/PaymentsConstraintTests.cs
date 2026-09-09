using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The guarantees the database itself makes for the Payments schema: a partial unique index and a
/// handful of <c>CHECK</c> constraints that behave perfectly in memory and only differ against
/// PostgreSQL, which is why they were deferred to here (docs/TEST_DEBT.md, Step 15).
/// </summary>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class PaymentsConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Two concurrent retries against one order cannot open two collections that are both still
    /// open — the partial unique index on <c>(tenant_id, order_id)</c> filtered to
    /// <c>status IN ('Created', 'Authorized')</c>.
    /// </summary>
    [Fact]
    public async Task An_order_cannot_have_two_open_collections()
    {
        SkipWithoutDocker();

        var tenant = Guid.NewGuid();
        var order = Guid.NewGuid();

        await InsertPaymentAsync(tenant, order, "Created", idempotencyKey: $"a:{Guid.NewGuid():N}");

        var refusal = await Database.RefusalAsync(
            """
            INSERT INTO payments.payments
                (id, order_id, order_number, customer_id, provider, method, amount, amount_captured,
                 amount_refunded, currency_code, status, idempotency_key, receipt, opened_at, tenant_id, created_at)
            VALUES ($1, $2, 'KH-TEST', $3, 'razorpay', 'Unknown', 100, 0, 0, 'INR', 'Authorized', $4, 'KH-TEST', now(), $5, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            order,
            Guid.NewGuid(),
            $"b:{Guid.NewGuid():N}",
            tenant);

        Assert.Equal("23505", refusal);

        // A second order is unaffected: the index is per order, not a blanket "one open collection".
        var otherOrder = Guid.NewGuid();
        var clean = await InsertPaymentAsync(tenant, otherOrder, "Created", idempotencyKey: $"c:{Guid.NewGuid():N}");
        Assert.Null(clean);

        // And once the first is settled, a fresh attempt against the same order is allowed again —
        // the filter is on status, not on the order id alone.
        await Database.ExecuteAsync(
            "UPDATE payments.payments SET status = 'Captured' WHERE order_id = $1",
            Cancellation,
            order);

        var afterSettlement = await InsertPaymentAsync(tenant, order, "Created", idempotencyKey: $"d:{Guid.NewGuid():N}");
        Assert.Null(afterSettlement);
    }

    /// <summary>
    /// A refund's approver can never be the same person who raised it — refused independently by the
    /// database, not only by the handler.
    /// </summary>
    [Fact]
    public async Task A_refund_cannot_be_approved_by_its_own_initiator()
    {
        SkipWithoutDocker();

        var tenant = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        await InsertPaymentAsync(tenant, Guid.NewGuid(), "Captured", idempotencyKey: $"e:{Guid.NewGuid():N}", id: paymentId);

        var refusal = await Database.RefusalAsync(
            """
            INSERT INTO payments.refunds
                (id, payment_id, order_id, amount, currency_code, reason, status, speed,
                 requires_approval, initiated_by, initiated_at, approved_by, approved_at,
                 idempotency_key, tenant_id, created_at)
            VALUES ($1, $2, $3, 100, 'INR', 'test', 'Approved', 'Normal', true, $4, now(), $4, now(), $5, $6, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            paymentId,
            Guid.NewGuid(),
            actor,
            $"rfnd:{Guid.NewGuid():N}",
            tenant);

        Assert.Equal("23514", refusal);

        // A *different* approver is fine — the constraint is about self-approval, not about approval
        // at all.
        var clean = await Database.RefusalAsync(
            """
            INSERT INTO payments.refunds
                (id, payment_id, order_id, amount, currency_code, reason, status, speed,
                 requires_approval, initiated_by, initiated_at, approved_by, approved_at,
                 idempotency_key, tenant_id, created_at)
            VALUES ($1, $2, $3, 100, 'INR', 'test', 'Approved', 'Normal', true, $4, now(), $5, now(), $6, $7, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            paymentId,
            Guid.NewGuid(),
            actor,
            Guid.NewGuid(),
            $"rfnd:{Guid.NewGuid():N}",
            tenant);

        Assert.Null(clean);
    }

    /// <summary>A payment's refunded total can never exceed what was captured, at the database.</summary>
    [Fact]
    public async Task A_payment_cannot_record_more_refunded_than_captured()
    {
        SkipWithoutDocker();

        var tenant = Guid.NewGuid();

        var refusal = await Database.RefusalAsync(
            """
            INSERT INTO payments.payments
                (id, order_id, order_number, customer_id, provider, method, amount, amount_captured,
                 amount_refunded, currency_code, status, idempotency_key, receipt, opened_at, tenant_id, created_at)
            VALUES ($1, $2, 'KH-TEST', $3, 'razorpay', 'Unknown', 100, 50, 100, 'INR', 'PartiallyRefunded', $4, 'KH-TEST', now(), $5, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"f:{Guid.NewGuid():N}",
            tenant);

        Assert.Equal("23514", refusal);
    }

    /// <summary>The same gateway event id can never be stored twice for one provider — replay
    /// protection at the database, independent of the endpoint's own duplicate check.</summary>
    [Fact]
    public async Task The_same_gateway_event_cannot_be_stored_twice()
    {
        SkipWithoutDocker();

        var tenant = Guid.NewGuid();
        var eventId = $"evt_{Guid.NewGuid():N}";

        await Database.ExecuteAsync(
            """
            INSERT INTO payments.gateway_events
                (id, provider, provider_event_id, event_type, signature_valid, payload, received_at, status, attempts, tenant_id)
            VALUES ($1, 'razorpay', $2, 'payment.captured', true, '{}', now(), 'Pending', 0, $3)
            """,
            Cancellation,
            Guid.NewGuid(),
            eventId,
            tenant);

        var refusal = await Database.RefusalAsync(
            """
            INSERT INTO payments.gateway_events
                (id, provider, provider_event_id, event_type, signature_valid, payload, received_at, status, attempts, tenant_id)
            VALUES ($1, 'razorpay', $2, 'payment.captured', true, '{}', now(), 'Pending', 0, $3)
            """,
            Cancellation,
            Guid.NewGuid(),
            eventId,
            tenant);

        Assert.Equal("23505", refusal);
    }

    /// <summary>A seller's parcel opens at most one cash-on-delivery collection, at the database.</summary>
    [Fact]
    public async Task A_sub_order_cannot_open_two_cod_collections()
    {
        SkipWithoutDocker();

        var tenant = Guid.NewGuid();
        var subOrderId = Guid.NewGuid();

        await Database.ExecuteAsync(
            """
            INSERT INTO payments.cod_collections
                (id, order_id, sub_order_id, amount, currency_code, status, tenant_id, created_at)
            VALUES ($1, $2, $3, 500, 'INR', 'Pending', $4, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            Guid.NewGuid(),
            subOrderId,
            tenant);

        var refusal = await Database.RefusalAsync(
            """
            INSERT INTO payments.cod_collections
                (id, order_id, sub_order_id, amount, currency_code, status, tenant_id, created_at)
            VALUES ($1, $2, $3, 500, 'INR', 'Pending', $4, now())
            """,
            Cancellation,
            Guid.NewGuid(),
            Guid.NewGuid(),
            subOrderId,
            tenant);

        Assert.Equal("23505", refusal);
    }

    private async Task<string?> InsertPaymentAsync(
        Guid tenant,
        Guid orderId,
        string status,
        string idempotencyKey,
        Guid? id = null)
        => await Database.RefusalAsync(
            """
            INSERT INTO payments.payments
                (id, order_id, order_number, customer_id, provider, method, amount, amount_captured,
                 amount_refunded, currency_code, status, idempotency_key, receipt, opened_at, tenant_id, created_at)
            VALUES ($1, $2, 'KH-TEST', $3, 'razorpay', 'Unknown', 100, 0, 0, 'INR', $4, $5, 'KH-TEST', now(), $6, now())
            """,
            Cancellation,
            id ?? Guid.NewGuid(),
            orderId,
            Guid.NewGuid(),
            status,
            idempotencyKey,
            tenant);
}
