using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Payments.Domain;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>A seller, a listing and stock behind it, ready for an order to be placed against it.</summary>
/// <param name="Vendor">The seller.</param>
/// <param name="ProductId">The product.</param>
/// <param name="VariantId">Its variant.</param>
/// <param name="ListingId">The offer sold.</param>
/// <param name="WarehouseId">Where its stock sits.</param>
/// <param name="UnitPrice">What one unit sells for.</param>
internal sealed record ReturnableCatalogue(
    OnboardedVendor Vendor,
    Guid ProductId,
    Guid VariantId,
    Guid ListingId,
    Guid WarehouseId,
    decimal UnitPrice);

/// <summary>An order placed, paid (unless cash on delivery) and delivered, ready for a return.</summary>
/// <param name="OrderId">The order.</param>
/// <param name="OrderNumber">Its number.</param>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="SubOrderNumber">Its number.</param>
/// <param name="OrderLineId">The line the shopper bought.</param>
/// <param name="Quantity">How many units were bought.</param>
/// <param name="ShipmentId">The forward parcel.</param>
/// <param name="CustomerMobile">The shopper's mobile, to sign back in as them.</param>
internal sealed record DeliveredOrder(
    Guid OrderId,
    string OrderNumber,
    Guid SubOrderId,
    string SubOrderNumber,
    Guid OrderLineId,
    int Quantity,
    Guid ShipmentId,
    string CustomerMobile);

/// <summary>
/// Builds the seller, the catalogue and the delivered order that every Step 17 test stands on, and
/// drives the returns surface itself.
/// </summary>
/// <remarks>
/// <para>
/// A return proves nothing on its own — every acceptance criterion in
/// <c>step-17-returns-refunds-and-rma-module.md</c> is about what happens to a line that was really
/// sold, really paid for (or really not, for cash on delivery) and really delivered. So this walks
/// the whole commerce pipeline through the API a shopper and an operator would use: a basket, a
/// checkout, a gateway capture confirmed the way a webhook confirms it, a parcel packed, booked,
/// dispatched and scanned delivered. Only then does a return exist to test.
/// </para>
/// <para>
/// Every helper here is deliberately generous with what it lets a test override — the unit price,
/// the returnability of the product, the reason code, the disposition — because the eligibility and
/// disposition rows this step exists to close are exactly the rows that need those to vary.
/// </para>
/// </remarks>
/// <param name="factory">The host under test, for its gateway and courier fakes.</param>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="database">Direct SQL, for the assertions the API cannot make.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class ReturnsScenario(
    CommerceApiFactory factory,
    HttpClient admin,
    Sql database,
    CancellationToken cancellationToken)
{
    private readonly VendorScenario _vendors = new(admin, cancellationToken);
    private readonly CatalogScenario _catalog = new(admin, cancellationToken);

    /// <summary>Onboards a seller, drafts a returnable product and stocks fifty units of it.</summary>
    /// <param name="isReturnable">Whether the product may be sent back at all.</param>
    /// <param name="returnWindowDays">
    /// The product's own return window, or null to leave the question to the seller's or the store's
    /// policy — which is what the eligibility fallback rows need.
    /// </param>
    /// <param name="unitPrice">What one unit sells for.</param>
    public async Task<ReturnableCatalogue> CatalogueAsync(
        bool isReturnable = true,
        int? returnWindowDays = 7,
        decimal unitPrice = 1200m)
    {
        var vendor = await _vendors.ActiveAsync();
        var taxonomy = await _catalog.TaxonomyAsync();

        var drafted = await _catalog.DraftAsync(taxonomy);
        await _catalog.ActivateVariantAsync(drafted.VariantId);
        await _catalog.PublishAsync(drafted.Id);

        if (returnWindowDays != 7 || !isReturnable)
        {
            await OverrideReturnPolicyAsync(drafted.Id, isReturnable, returnWindowDays);
        }

        var warehouseId = await WarehouseAsync(vendor.Id);

        // The variant's own MRP is fixed at 1299 by CatalogScenario; a listing may never sell above
        // its MRP, so a test asking for a higher price needs the listing's own, higher one.
        var listingId = await _catalog.OfferAsync(
            vendor.Id,
            drafted.VariantId,
            unitPrice,
            mrp: Math.Max(unitPrice, 1299m));

        await StockUpAsync(listingId, warehouseId, 50);

        return new ReturnableCatalogue(vendor, drafted.Id, drafted.VariantId, listingId, warehouseId, unitPrice);
    }

    /// <summary>Places, pays for (unless cash on delivery) and delivers an order for one listing.</summary>
    /// <param name="catalogue">What is being bought.</param>
    /// <param name="quantity">How many units.</param>
    /// <param name="paymentMethod"><c>prepaid</c> or <c>cod</c>.</param>
    public async Task<DeliveredOrder> DeliveredOrderAsync(
        ReturnableCatalogue catalogue,
        int quantity = 1,
        string paymentMethod = "prepaid")
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var (shopper, mobile) = await SignedInShopperAsync();

        var addressId = await AddressAsync(shopper);

        await Rest.ReadAsync(
            await shopper.PostAsJsonAsync(
                "/api/v1/store/cart/items",
                new { listingId = catalogue.ListingId, quantity },
                cancellationToken),
            cancellationToken);

        var checkout = await Rest.ReadAsync(
            await shopper.PostAsync(new Uri("/api/v1/store/checkout", UriKind.Relative), content: null, cancellationToken),
            cancellationToken);

        var checkoutId = checkout.GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/address",
                new { shippingAddressId = addressId, billingAddressId = (Guid?)null, gstin = (string?)null },
                cancellationToken),
            cancellationToken);

        var options = await Rest.ReadAsync(
            await shopper.GetAsync(
                new Uri($"/api/v1/store/checkout/{checkoutId}/shipping-options", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var perVendor = options.EnumerateArray()
            .Select(vendorOptions => new
            {
                vendorId = vendorOptions.GetProperty("vendorId").GetGuid(),
                optionCode = vendorOptions.GetProperty("options")[0].GetProperty("code").GetString(),
            })
            .ToArray();

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/shipping",
                new { perVendor },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await shopper.PutAsJsonAsync(
                $"/api/v1/store/checkout/{checkoutId}/payment-method",
                new { method = paymentMethod },
                cancellationToken),
            cancellationToken);

        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var placeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/store/checkout/{checkoutId}/place-order");

        placeRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        var placed = await Rest.ReadAsync(await shopper.SendAsync(placeRequest, cancellationToken), cancellationToken);

        var orderId = placed.GetProperty("orderId").GetGuid();

        if (string.Equals(paymentMethod, "prepaid", StringComparison.OrdinalIgnoreCase))
        {
            var payment = placed.GetProperty("payment");
            var providerOrderId = payment.GetProperty("providerOrderId").GetString()!;
            var amount = payment.GetProperty("amount").GetDecimal();

            await CapturePaymentAsync(providerOrderId, amount);
        }

        var order = await Rest.ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/orders/{orderId}", UriKind.Relative), cancellationToken),
            cancellationToken);

        var subOrder = order.GetProperty("subOrders")[0];
        var subOrderId = subOrder.GetProperty("id").GetGuid();
        var subOrderNumber = subOrder.GetProperty("subOrderNumber").GetString()!;
        var orderLineId = subOrder.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var shipmentId = await DeliverAsync(subOrderId);

        return new DeliveredOrder(
            orderId,
            order.GetProperty("orderNumber").GetString()!,
            subOrderId,
            subOrderNumber,
            orderLineId,
            quantity,
            shipmentId,
            mobile);
    }

    /// <summary>Books, packs, dispatches and delivers a parcel for one seller's part of an order.</summary>
    /// <param name="subOrderId">The seller's part.</param>
    public async Task<Guid> DeliverAsync(Guid subOrderId)
    {
        // The sub-order must reach Packed before a dispatch can move it to Shipped — a shipment may
        // be booked as soon as it is Confirmed, but the machine still wants the intermediate states
        // named.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status = "Processing", reason = (string?)null },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/transition",
                new { status = "Packed", reason = (string?)null },
                cancellationToken),
            cancellationToken);

        var shipment = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/shipments",
                new
                {
                    lines = Array.Empty<object>(),
                    weight = 500,
                    dimensions = (object?)null,
                    courier = (string?)null,
                    pickupLocationId = (Guid?)null,
                    manualAwb = (string?)null,
                    manualCourier = (string?)null,
                },
                cancellationToken),
            cancellationToken);

        var shipmentId = shipment.GetProperty("id").GetGuid();

        // CreateShipmentCommand packs, weighs and books in one call — there is nothing left to book.
        await Rest.ReadAsync(
            await admin.PostAsync(
                new Uri($"/api/v1/admin/shipments/{shipmentId}/dispatch", UriKind.Relative),
                content: null,
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/tracking",
                new { status = "OutForDelivery", remark = (string?)null, occurredAt = (DateTimeOffset?)null },
                cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipments/{shipmentId}/tracking",
                new { status = "Delivered", remark = (string?)null, occurredAt = (DateTimeOffset?)null },
                cancellationToken),
            cancellationToken);

        return shipmentId;
    }

    /// <summary>Records that the shopper paid, and lets the platform confirm it the way a webhook does.</summary>
    /// <param name="providerOrderId">The collection the checkout widget opened.</param>
    /// <param name="amount">What was paid.</param>
    public async Task CapturePaymentAsync(string providerOrderId, decimal amount)
    {
        var payment = factory.Gateway.PayOrder(providerOrderId, amount);

        var body = JsonSerializer.Serialize(new
        {
            id = $"evt_{Guid.NewGuid():N}",
            @event = "payment.captured",
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            order_id = providerOrderId,
            payment_id = payment.ProviderPaymentId,
        });

        var signature = FakePaymentProvider.Sign(body);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/razorpay")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("X-Razorpay-Signature", signature);

        using var anonymous = factory.CreateClient();
        var response = await anonymous.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await DrainGatewayEventsAsync();
    }

    /// <summary>Runs the payments gateway-event worker's drain once, over the real host.</summary>
    public async Task DrainGatewayEventsAsync()
    {
        using var scope = factory.Services.CreateScope();

        var worker = scope.ServiceProvider
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<Modules.Payments.Infrastructure.Jobs.GatewayEventWorker>()
            .Single();

        // Several passes rather than a fixed one or two: a full batch would ask for another, and a
        // pass over an empty queue claims nothing and costs one round trip — cheap insurance against
        // this host's own webhook write not yet being visible to the very next statement under load.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await worker.DrainOnceAsync(cancellationToken);
        }
    }

    /// <summary>Runs the real outbox dispatcher once, for the cross-module handlers it feeds.</summary>
    public Task DrainOutboxAsync() => OutboxDrain.RunAsync(factory, database, cancellationToken);

    /// <summary>Reads the caller's own returns-settings section, as a mutable JSON document.</summary>
    public async Task<JsonElement> ReturnsSettingsAsync()
    {
        var settings = await Rest.ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/admin/settings", UriKind.Relative), cancellationToken),
            cancellationToken);

        return settings.GetProperty("sections")
            .EnumerateArray()
            .First(section => section.GetProperty("key").GetString() == "returns")
            .GetProperty("value");
    }

    /// <summary>
    /// Replaces the store's returns settings, merging <paramref name="patch"/> over the current
    /// values — a section is edited whole, so every field the schema requires still has to travel.
    /// </summary>
    /// <param name="patch">The fields to change.</param>
    public async Task<HttpResponseMessage> UpdateReturnsSettingsAsync(IReadOnlyDictionary<string, object?> patch)
    {
        ArgumentNullException.ThrowIfNull(patch);

        var current = await ReturnsSettingsAsync();

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in current.EnumerateObject())
        {
            merged[property.Name] = Materialise(property.Value);
        }

        foreach (var (key, value) in patch)
        {
            merged[key] = value;
        }

        return await admin.PutAsJsonAsync("/api/v1/admin/settings/returns", merged, cancellationToken);
    }

    /// <summary>Puts the returns settings back to the platform's own shipped defaults.</summary>
    public Task<HttpResponseMessage> ResetReturnsSettingsAsync()
        => UpdateReturnsSettingsAsync(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["replacementsEnabled"] = false,
            ["autoApproveBelow"] = 0m,
            ["requireEvidence"] = false,
            ["maxEvidenceFiles"] = 5,
            ["defaultShippingPayer"] = "Platform",
            ["returnShippingFee"] = 0m,
            ["refundShippingOnFullReturn"] = true,
            ["defaultRefundMode"] = "Original",
            ["allowWalletRefunds"] = true,
            ["autoRefundOnQcPass"] = true,
            ["defaultPassedDisposition"] = "Restock",
            ["defaultFailedDisposition"] = "Quarantine",
            ["pickupSlaDays"] = 3,
        });

    /// <summary>Signs in as a shopper who has never been here before.</summary>
    public async Task<(HttpClient Client, string Mobile)> SignedInShopperAsync()
    {
        var client = factory.CreateClient();
        // "+91" and not the bare ten digits: the platform normalises a mobile number for storage
        // and for the OTP destination it dispatches to, and Otp.Latest looks it up by that
        // normalised form.
        var mobile = $"+919{Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture)}";

        var start = await client.PostAsJsonAsync(
            "/api/v1/store/auth/otp/request",
            new { mobile },
            cancellationToken);

        start.EnsureSuccessStatusCode();

        var code = factory.Otp.Latest(mobile, Modules.Identity.Domain.OtpPurpose.Login);

        await TestSignIn.SignInWithOtpAsync(client, mobile, code, cancellationToken);

        return (client, mobile);
    }

    /// <summary>Saves a delivery address for a signed-in shopper.</summary>
    private async Task<Guid> AddressAsync(HttpClient shopper)
    {
        var stateId = await _vendors.StateIdAsync();

        var created = await Rest.ReadAsync(
            await shopper.PostAsJsonAsync(
                "/api/v1/store/me/addresses",
                new
                {
                    label = "Home",
                    recipientName = "Test Shopper",
                    mobile = "9876500002",
                    line1 = "12 MG Road",
                    line2 = (string?)null,
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId,
                    pincode = "500034",
                    gstin = (string?)null,
                    type = "Home",
                    isDefaultShipping = true,
                    isDefaultBilling = true,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Opens a warehouse for a seller, in the covered area.</summary>
    private async Task<Guid> WarehouseAsync(Guid vendorId)
    {
        var stateId = await _vendors.StateIdAsync();

        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/warehouses",
                new
                {
                    vendorId,
                    code = $"WH-{Guid.NewGuid():N}"[..12],
                    name = "Test Warehouse",
                    pincode = "500034",
                    address = new
                    {
                        line1 = "Plot 1",
                        line2 = (string?)null,
                        landmark = (string?)null,
                        city = "Hyderabad",
                        stateId,
                        contactName = "Warehouse Manager",
                        contactPhone = "9876500003",
                    },
                    priority = 0,
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Puts stock behind a listing, opening the row explicitly if activation has not already.</summary>
    private async Task StockUpAsync(Guid listingId, Guid warehouseId, int quantity)
    {
        var existing = await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/stock?listingId={listingId}", UriKind.Relative),
                cancellationToken),
            cancellationToken);

        var items = existing.GetProperty("items");

        var stockItemId = items.GetArrayLength() > 0
            ? items[0].GetProperty("id").GetGuid()
            : (await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    "/api/v1/admin/stock",
                    new { listingId, warehouseId },
                    cancellationToken),
                cancellationToken)).GetProperty("id").GetGuid();

        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/stock/adjustments",
                new { stockItemId, change = quantity, reason = "Adjustment", note = "Test stock-up." },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Overrides a product's own return disclosure.</summary>
    private async Task OverrideReturnPolicyAsync(Guid productId, bool isReturnable, int? returnWindowDays)
    {
        var current = await Rest.ReadAsync(
            await admin.GetAsync(new Uri($"/api/v1/admin/products/{productId}", UriKind.Relative), cancellationToken),
            cancellationToken);

        await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                $"/api/v1/admin/products/{productId}",
                new
                {
                    name = current.GetProperty("name").GetString(),
                    slug = current.GetProperty("slug").GetString(),
                    categoryId = current.GetProperty("categoryId").GetGuid(),
                    brandId = current.TryGetProperty("brandId", out var brand) && brand.ValueKind == JsonValueKind.String
                        ? brand.GetGuid()
                        : (Guid?)null,
                    shortDescription = current.GetProperty("shortDescription").GetString(),
                    description = current.GetProperty("description").GetString(),
                    hsnCode = current.GetProperty("hsnCode").GetString(),
                    gstRate = current.GetProperty("gstRate").GetDecimal(),
                    countryOfOrigin = current.GetProperty("countryOfOrigin").GetString(),
                    manufacturer = new
                    {
                        name = "Klara Textiles Pvt Ltd",
                        address = "Plot 9, Panipat, Haryana 132103",
                        contact = "care@klarahome.test",
                    },
                    packer = (object?)null,
                    importer = (object?)null,
                    isReturnable,
                    returnWindowDays,
                    warranty = (string?)null,
                    specifications = new[] { new { label = "Weave", value = "Panama", group = (string?)null } },
                    seo = (object?)null,
                    attributes = Array.Empty<object>(),
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Turns a JSON element back into a plain value a re-serialisation will not mangle.</summary>
    private static object? Materialise(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.Clone(),
        };
}
