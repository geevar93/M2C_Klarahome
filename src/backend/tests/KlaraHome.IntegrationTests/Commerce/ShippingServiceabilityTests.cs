using KlaraHome.IntegrationTests.Database;
using System.Net.Http.Json;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.Modules.Shipping.Infrastructure.Processing;
using KlaraHome.Modules.Shipping.Infrastructure.Serviceability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The three 🟡 TEST_DEBT rows about knowing where a parcel can go, and how a shopper's own tracking
/// stays fresh without a storefront ever calling a courier.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class ShippingServiceabilityTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// <c>IShippingOptions</c> returns priced services at checkout; an unserviceable PIN code returns
    /// none; and a cash-on-delivery basket is offered nothing on a service that refuses cash.
    /// </summary>
    [Fact]
    public async Task Checkout_prices_a_serviceable_pin_offers_nothing_unserviceable_and_refuses_cod_where_cash_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendors = Sellers(admin);
        var catalogue = new CatalogScenario(admin, Cancellation);
        var stateId = await vendors.StateIdAsync();

        var seller = await vendors.ActiveAsync();
        var taxonomy = await catalogue.TaxonomyAsync();
        var product = await catalogue.DraftAsync(taxonomy, seller.Id);
        await catalogue.ActivateVariantAsync(product.VariantId);
        await catalogue.PublishAsync(product.Id);
        var listingId = await catalogue.OfferAsync(seller.Id, product.VariantId, sellingPrice: 999m);
        await catalogue.StockAsync(listingId, 100, seller.Id);

        // A serviceable PIN code, cached by an explicit refresh against the fake sandbox.
        const string ServiceablePincode = "500034";
        const string UnservicePincode = "500061";
        const string NoCodPincode = "500082";

        Factory.Courier.Serviceable = [ServiceablePincode, NoCodPincode];
        Factory.Courier.NoCod.Add(NoCodPincode);

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipping/serviceability/{ServiceablePincode}/refresh", new { }, Cancellation));
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipping/serviceability/{UnservicePincode}/refresh", new { }, Cancellation));
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipping/serviceability/{NoCodPincode}/refresh", new { }, Cancellation));

        var orders = new ShippingOrderScenario(admin, Cancellation);

        // Serviceable: priced options come back.
        {
            var (shopper, _) = await SignedInShopperAsync();
            await orders.AddressAsync(shopper, stateId, ServiceablePincode);
            await ReadAsync(await orders.AddToCartAsync(shopper, listingId));

            var session = await ReadAsync(await shopper.PostAsJsonAsync("/api/v1/store/checkout/", new { }, Cancellation));
            var sessionId = session.GetProperty("id").GetGuid();

            var addressId = (await ReadAsync(await shopper.GetAsync(
                    new Uri("/api/v1/store/me/addresses", UriKind.Relative), Cancellation)))
                .EnumerateArray().First().GetProperty("id").GetGuid();

            await ReadAsync(await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/address",
                new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                Cancellation));

            var options = await ReadAsync(await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative), Cancellation));

            var forVendor = options.EnumerateArray().First(entry => entry.GetProperty("vendorId").GetGuid() == seller.Id);
            Assert.NotEmpty(forVendor.GetProperty("options").EnumerateArray());
        }

        // Unserviceable: the checkout refuses the address itself, before a shipping option is ever
        // asked for — the same PINCODE_NOT_SERVICEABLE the cache exists to answer, straight from
        // SetCheckoutAddressCommand's own destination check.
        {
            var (shopper, _) = await SignedInShopperAsync();
            await orders.AddressAsync(shopper, stateId, UnservicePincode);
            await ReadAsync(await orders.AddToCartAsync(shopper, listingId));

            var session = await ReadAsync(await shopper.PostAsJsonAsync("/api/v1/store/checkout/", new { }, Cancellation));
            var sessionId = session.GetProperty("id").GetGuid();

            var addressId = (await ReadAsync(await shopper.GetAsync(
                    new Uri("/api/v1/store/me/addresses", UriKind.Relative), Cancellation)))
                .EnumerateArray().First().GetProperty("id").GetGuid();

            await RefusedAsync(
                await shopper.PutAsJsonAsync(
                    $"/api/v1/store/checkout/{sessionId}/address",
                    new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                    Cancellation),
                System.Net.HttpStatusCode.UnprocessableEntity,
                "PINCODE_NOT_SERVICEABLE");
        }

        // Serviceable but cash-refusing: a COD basket sees nothing on this seller, once COD is chosen.
        {
            var (shopper, _) = await SignedInShopperAsync();
            await orders.AddressAsync(shopper, stateId, NoCodPincode);
            await ReadAsync(await orders.AddToCartAsync(shopper, listingId));

            var session = await ReadAsync(await shopper.PostAsJsonAsync("/api/v1/store/checkout/", new { }, Cancellation));
            var sessionId = session.GetProperty("id").GetGuid();

            var addressId = (await ReadAsync(await shopper.GetAsync(
                    new Uri("/api/v1/store/me/addresses", UriKind.Relative), Cancellation)))
                .EnumerateArray().First().GetProperty("id").GetGuid();

            await ReadAsync(await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/address",
                new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                Cancellation));

            // A service has to be chosen — prepaid, at this point — before a payment method may be,
            // which is what makes this destination reachable at all: the courier carries a prepaid
            // parcel there and only refuses the cash.
            var prepaidOptions = await ReadAsync(await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative), Cancellation));

            var chosenCode = prepaidOptions.EnumerateArray()
                .First(entry => entry.GetProperty("vendorId").GetGuid() == seller.Id)
                .GetProperty("options").EnumerateArray().First().GetProperty("code").GetString();

            await ReadAsync(await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/shipping",
                new { perVendor = new[] { new { vendorId = seller.Id, optionCode = chosenCode } } },
                Cancellation));

            await ReadAsync(await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{sessionId}/payment-method",
                new { method = "cod" },
                Cancellation));

            var options = await ReadAsync(await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{sessionId}/shipping-options", UriKind.Relative), Cancellation));

            var forVendor = options.EnumerateArray().First(entry => entry.GetProperty("vendorId").GetGuid() == seller.Id);
            Assert.Empty(forVendor.GetProperty("options").EnumerateArray());
        }
    }

    /// <summary>
    /// The serviceability cache is read on the hot path and never calls a courier; the nightly job
    /// refreshes the oldest answers and upserts rather than appending.
    /// </summary>
    [Fact]
    public async Task The_cache_never_calls_a_courier_on_the_hot_path_and_the_refresh_job_upserts()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        const string Pincode = "560001";

        Factory.Courier.Serviceable = [Pincode];

        // The hot-path read, before anybody has ever refreshed this PIN code: it must not have called
        // the courier, and the answer is the optimistic default.
        var beforeAnyRefresh = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipping/serviceability/{Pincode}", UriKind.Relative), Cancellation));

        // Nobody has refreshed this PIN code yet, so the cache is empty and the answer is the
        // optimistic default read straight from it — never from a call to the fake courier.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, beforeAnyRefresh.GetProperty("checkedAt").ValueKind);

        // A refresh calls the courier once and writes one row.
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/shipping/serviceability/{Pincode}/refresh", new { }, Cancellation));

        var rowsAfterFirstRefresh = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.serviceability_cache WHERE pincode = $1",
            Cancellation,
            Pincode);

        Assert.Equal(1, rowsAfterFirstRefresh);

        // A second read, still from the cache: no further call to the courier is needed for it to
        // answer, and the answer now carries a CheckedAt.
        var afterRefresh = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/shipping/serviceability/{Pincode}", UriKind.Relative), Cancellation));

        Assert.NotNull(afterRefresh.GetProperty("checkedAt").GetString());

        // The nightly job: refreshing again upserts the same row rather than appending a second one.
        await RunOnceAsync<ServiceabilityService>(
            (service, ct) => service.RefreshAsync(Pincode, pickupPincode: null, ct));

        var rowsAfterSecondRefresh = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.serviceability_cache WHERE pincode = $1",
            Cancellation,
            Pincode);

        Assert.Equal(1, rowsAfterSecondRefresh);
    }

    /// <summary>
    /// The tracking poll picks up only booked, unfinished parcels silent for the configured window,
    /// and running two workers produces no duplicate scans.
    /// </summary>
    [Fact]
    public async Task The_tracking_poll_finds_only_silent_booked_parcels_and_two_workers_produce_no_duplicates()
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

        var awb = booked.GetProperty("awb").GetString()!;
        var shipmentId = booked.GetProperty("id").GetGuid();

        // Booking stamps LastTrackedAt as "now" (Shipment.Book), not null. Backdated here to outside
        // the default 24-hour window, which is what "silent" means — the alternative is a test that
        // waits on a real clock or a config value below the option's own DataAnnotation minimum.
        await Database.ExecuteAsync(
            "UPDATE shipping.shipments SET last_tracked_at = now() - interval '25 hours' WHERE id = $1",
            Cancellation,
            shipmentId);

        var stale = await RunAsync<TrackingSynchroniser, IReadOnlyList<Guid>>(
            (service, ct) => service.StaleAsync(batchSize: 100, ct));

        Assert.Contains(shipmentId, stale);

        // A scan the fake courier has recorded, so both "workers" see the same tracking history.
        Factory.Courier.Scan(awb, ShipmentStatus.InTransit);

        await RunOnceAsync<TrackingSynchroniser>(async (service, ct) =>
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            var shipment = await context.Shipments.IgnoreQueryFilters()
                .FirstAsync(candidate => candidate.Id == shipmentId, ct);

            await service.SyncAsync(shipment, ct);
            await context.SaveChangesAsync(ct);
        });

        var afterFirstWorker = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.tracking_events WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        Assert.Equal(1, afterFirstWorker);

        // A second "worker" racing the first over the same, unchanged courier history: the scan's own
        // provider-event id is what stops it becoming a second row.
        await RunOnceAsync<TrackingSynchroniser>(async (service, ct) =>
        {
            using var scope = Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            var shipment = await context.Shipments.IgnoreQueryFilters()
                .FirstAsync(candidate => candidate.Id == shipmentId, ct);

            await service.SyncAsync(shipment, ct);
            await context.SaveChangesAsync(ct);
        });

        var afterSecondWorker = await Database.CountAsync(
            "SELECT COUNT(*) FROM shipping.tracking_events WHERE shipment_id = $1",
            Cancellation,
            shipmentId);

        Assert.Equal(1, afterSecondWorker);
    }

    /// <summary>Runs one pass of a hosted service's dependency and answers what it returned.</summary>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    /// <typeparam name="TResult">What the pass answers.</typeparam>
    /// <param name="pass">What one pass means.</param>
    private async Task<TResult> RunAsync<TService, TResult>(Func<TService, CancellationToken, Task<TResult>> pass)
        where TService : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return await pass(scope.ServiceProvider.GetRequiredService<TService>(), Cancellation);
    }
}
