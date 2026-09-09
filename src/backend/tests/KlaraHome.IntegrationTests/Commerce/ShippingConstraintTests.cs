using KlaraHome.IntegrationTests.Database;
using System.Net.Http.Json;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The database-level guarantees behind Shipping: the partitioned, append-only tracking log, the
/// <c>CHECK</c> constraints the schema stands behind, and the outbound client's host allow-list.
/// The migration itself applying and re-running clean is proved once, for every module, by
/// <see cref="SchemaMigrationTests"/> — this is what that cross-cutting test cannot see.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// <c>tracking_events</c> is created partitioned, its append-only trigger refuses <c>UPDATE</c>
    /// and <c>DELETE</c> from raw SQL, and its <c>DEFAULT</c> partition accepts a scan outside every
    /// range a monthly partition covers.
    /// </summary>
    [Fact]
    public async Task Tracking_events_is_partitioned_append_only_and_has_a_default_partition()
    {
        SkipWithoutDocker();

        var partitionType = await Database.ScalarAsync<char>(
            "SELECT relkind FROM pg_class WHERE relname = 'tracking_events' "
            + "AND relnamespace = 'shipping'::regnamespace",
            Cancellation);

        // 'p' is PostgreSQL's own code for a partitioned table.
        Assert.Equal('p', partitionType);

        var defaultPartitionExists = await Database.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_class WHERE relname = 'tracking_events_default' "
            + "AND relnamespace = 'shipping'::regnamespace)",
            Cancellation);

        Assert.True(defaultPartitionExists, "shipping.tracking_events has no DEFAULT partition.");

        // A row far outside any monthly partition this migration created (two years back) must still
        // land somewhere: the DEFAULT partition.
        var shipmentId = Guid.NewGuid();
        var tenantId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM platform.tenants LIMIT 1",
            Cancellation);

        var farPast = DateTimeOffset.UtcNow.AddYears(-5);

        var inserted = await Database.ExecuteAsync(
            "INSERT INTO shipping.tracking_events "
            + "(occurred_at, id, tenant_id, shipment_id, provider_event_id, status, received_at) "
            + "VALUES ($1, $2, $3, $4, 'constraint-test', 'InTransit', now())",
            Cancellation,
            farPast,
            Guid.NewGuid(),
            tenantId,
            shipmentId);

        Assert.Equal(1, inserted);

        var landedInDefault = await Database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM shipping.tracking_events_default WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        Assert.Equal(1, landedInDefault);

        // The append-only trigger: an UPDATE and a DELETE from raw SQL are both refused, not merely
        // discouraged by application code.
        var updateRefusal = await Database.RefusalAsync(
            "UPDATE shipping.tracking_events SET remark = 'edited' WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        Assert.NotNull(updateRefusal);

        var deleteRefusal = await Database.RefusalAsync(
            "DELETE FROM shipping.tracking_events WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        Assert.NotNull(deleteRefusal);
    }

    /// <summary>
    /// Every <c>CHECK</c> constraint refuses what it is meant to: a booked parcel with no waybill, an
    /// inverted weight band, a negative freight rate, and a resolved report with no action.
    /// </summary>
    [Fact]
    public async Task Every_check_constraint_refuses_what_it_is_meant_to()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var orders = new ShippingOrderScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, seller.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        var (shopper, _) = await SignedInShopperAsync();
        var placed = await orders.ConfirmedSubOrderAsync(
            shopper, stateId, seller.Id, listingId, factory: Factory, database: Database);

        var booked = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{placed.SubOrderId}/shipments",
            new
            {
                lines = Array.Empty<object>(),
                weight = 500,
                dimensions = (object?)null,
                courier = "standard",
                pickupLocationId = (Guid?)null,
                manualAwb = (string?)null,
                manualCourier = (string?)null,
            },
            Cancellation));

        var shipmentId = booked.GetProperty("id").GetGuid();

        var zones = await ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/shipping/zones", UriKind.Relative), Cancellation));

        var zoneId = zones.EnumerateArray().First().GetProperty("id").GetGuid();

        var rate = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/shipping/rates",
            new
            {
                zoneId,
                method = "Express",
                vendorId = (Guid?)null,
                terms = new
                {
                    minWeightGrams = 0,
                    maxWeightGrams = 1000,
                    minOrderValue = 0m,
                    maxOrderValue = (decimal?)null,
                    baseRate = 20m,
                    perKgRate = 10m,
                    freeAbove = (decimal?)null,
                    codFee = 5m,
                    isCodAllowed = true,
                    etaMinDays = 1,
                    etaMaxDays = 2,
                },
            },
            Cancellation));

        var rateId = rate.GetProperty("id").GetGuid();

        var scan = Factory.Courier.Scan(
            booked.GetProperty("awb").GetString()!,
            Modules.Shipping.Domain.ShipmentStatus.Exception,
            Modules.Shipping.Domain.NdrReasonCode.CustomerUnavailable);

        var body = Factory.Courier.WebhookBody(booked.GetProperty("awb").GetString()!, scan);
        var signature = FakeShippingProvider.Sign(body);

        (await PostRawAsync(
            CreateClient(),
            "/api/v1/webhooks/shipping/shiprocket",
            body,
            ("X-Api-Key", signature))).EnsureSuccessStatusCode();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider
                .GetRequiredService<Modules.Shipping.Infrastructure.Persistence.ShippingDbContext>();
            var processor = scope.ServiceProvider
                .GetRequiredService<Modules.Shipping.Infrastructure.Processing.CourierEventProcessor>();

            var stored = await context.CourierEvents
                .IgnoreQueryFilters()
                .FirstAsync(entry => entry.ProviderEventId == scan.ProviderEventId, Cancellation);

            await processor.ProcessAsync(stored, Cancellation);
            await context.SaveChangesAsync(Cancellation);
        }

        var ndrId = await Database.ScalarAsync<Guid>(
            "SELECT id FROM shipping.ndr_records WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        (string Constraint, string Statement, object?[] Parameters)[] refusals =
        [
            ("a booked parcel with no waybill",
                "UPDATE shipping.shipments SET awb = NULL WHERE id = $1",
                [shipmentId]),
            ("an inverted weight band",
                "UPDATE shipping.shipping_rates SET max_weight_grams = min_weight_grams - 1 WHERE id = $1",
                [rateId]),
            ("a negative base rate",
                "UPDATE shipping.shipping_rates SET base_rate = -1 WHERE id = $1",
                [rateId]),
            ("a resolved report with no action",
                "UPDATE shipping.ndr_records SET action = 'Resolved', resolved_at = NULL WHERE id = $1",
                [ndrId]),
        ];

        foreach (var (constraint, statement, parameters) in refusals)
        {
            var sqlState = await Database.RefusalAsync(statement, Cancellation, parameters);

            Assert.True(
                sqlState == "23514",
                $"The database accepted {constraint}; it answered {sqlState ?? "nothing at all"} instead of 23514.");
        }
    }

    /// <summary>
    /// The outbound logistics client refuses any host but the one <c>Shipping:BaseUrl</c> names, so a
    /// label link from a third party cannot become a server-side request forgery
    /// (docs/07-security-compliance.md §3).
    /// </summary>
    [Fact]
    public async Task The_outbound_client_refuses_any_host_but_the_configured_one()
    {
        var options = new ShippingOptionsStub("https://apiv2.shiprocket.in");

        var handler = new ShiprocketAllowedHostHandler(options, NullLogger<ShiprocketAllowedHostHandler>.Instance)
        {
            InnerHandler = new RecordingHandler(),
        };

        using var client = new HttpClient(handler);

        var allowed = await client.GetAsync(
            new Uri("https://apiv2.shiprocket.in/v1/external/courier/track"),
            TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, allowed.StatusCode);

        // A URL naming any other host — exactly the shape a forged label link would carry — is
        // refused before the request ever leaves the process, not merely logged.
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(
            new Uri("https://evil.example.test/steal-the-bearer-token"),
            TestContext.Current.CancellationToken));
    }

    /// <summary>An <see cref="IOptionsMonitor{T}"/> stub carrying one fixed base URL.</summary>
    private sealed class ShippingOptionsStub(string baseUrl) : IOptionsMonitor<Modules.Shipping.Infrastructure.ShippingOptions>
    {
        public Modules.Shipping.Infrastructure.ShippingOptions CurrentValue { get; } =
            new() { BaseUrl = baseUrl };

        public Modules.Shipping.Infrastructure.ShippingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<Modules.Shipping.Infrastructure.ShippingOptions, string?> listener) => null;
    }

    /// <summary>Answers 200 to anything that reaches it, so only the allow-list handler is under test.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
