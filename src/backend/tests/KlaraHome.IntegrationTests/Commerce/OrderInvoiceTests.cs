using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The seller's tax invoice: raised once per sub-order when the parcel is closed, numbered from a
/// gapless per-seller series, carrying the statutory content of rule 46, and split across the tax
/// heads the supply type calls for.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class OrderInvoiceTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// An intra-state supply is invoiced under CGST and SGST, with the statutory identifiers on it
    /// and a rendered document behind it.
    /// </summary>
    /// <remarks>
    /// The tax figures are asserted against the order's own frozen lines rather than recomputed.
    /// That is the invariant worth holding: the quote engine is the only thing on this platform that
    /// computes GST, and an invoice that resolved its own rates would be a second opinion about what
    /// the customer owes — a discrepancy that would surface at a refund rather than at a review.
    /// </remarks>
    [Fact]
    public async Task An_intra_state_invoice_splits_the_tax_across_cgst_and_sgst_and_names_its_supplier()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        // Registered in the state the parcel is going to, which is what makes the supply intra-state.
        var home = await scenario.HomeStateAsync();
        await scenario.RegisterInStateAsync(seller.Vendor.Id, home);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId, quantity: 2);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var order = await scenario.ReadOrderAsync(placed.OrderId);
        var subOrder = Assert.Single(order.GetProperty("subOrders").EnumerateArray());
        var invoice = subOrder.GetProperty("invoice");

        Assert.NotEqual(JsonValueKind.Null, invoice.ValueKind);

        // The number is the seller's series, their financial year, and a padded counter — the shape
        // rule 46 asks for and the shape a seller quotes on a credit note.
        var series = $"{seller.Vendor.Code}/{invoice.GetProperty("financialYear").GetString()}";

        Assert.Equal(series, invoice.GetProperty("series").GetString());
        Assert.StartsWith(series + "/", invoice.GetProperty("invoiceNumber").GetString(), StringComparison.Ordinal);
        Assert.Equal("Issued", invoice.GetProperty("status").GetString());

        // Per head, summed off the frozen lines and not recomputed.
        var lines = subOrder.GetProperty("lines").EnumerateArray().ToList();

        Assert.Equal(Sum(lines, "cgst"), invoice.GetProperty("cgst").GetDecimal());
        Assert.Equal(Sum(lines, "sgst"), invoice.GetProperty("sgst").GetDecimal());
        Assert.Equal(Sum(lines, "taxableValue"), invoice.GetProperty("taxableValue").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("igst").GetDecimal());

        // An intra-state supply splits the rate in equal halves, which is what makes the two-column
        // layout right and the three-column one wrong.
        Assert.Equal(invoice.GetProperty("cgst").GetDecimal(), invoice.GetProperty("sgst").GetDecimal());

        // Every line the invoice covers carries the HSN the rate came from, which the document
        // prints per item.
        Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.GetProperty("hsnCode").GetString())));

        var invoiceId = invoice.GetProperty("id").GetGuid();

        // What is stored: the supply type and the place of supply, which decide the columns and are
        // reported on rather than merely printed.
        var row = Assert.Single(await Database.RowsAsync(
            "SELECT is_intra_state, place_of_supply_state_code, vendor_id, financial_year "
            + "FROM orders.invoices WHERE id = $1",
            Cancellation,
            invoiceId));

        Assert.Equal(true, row["is_intra_state"]);
        Assert.Equal(home.Code, row["place_of_supply_state_code"]);
        Assert.Equal(seller.Vendor.Id, row["vendor_id"]);

        // The supplier's own GSTIN is frozen on the sub-order, because the invoice is their supply
        // and a reprint next year must not read a registration they have since changed.
        var gstin = await Database.ScalarAsync<string>(
            "SELECT vendor_gstin FROM orders.sub_orders WHERE id = $1",
            Cancellation,
            subOrderId);

        Assert.NotNull(gstin);
        Assert.StartsWith(home.Code, gstin, StringComparison.Ordinal);

        // And a real PDF was produced and registered against it.
        await AssertRenderedAsync(invoice);
    }

    /// <summary>
    /// An inter-state supply is invoiced under IGST alone, with nothing under the state heads.
    /// </summary>
    /// <remarks>
    /// The seller is registered somewhere other than the destination, which is the only thing that
    /// makes a supply inter-state: the split is decided against the <em>supplier's</em> registration
    /// rather than the store's, because each seller invoices under their own.
    /// </remarks>
    [Fact]
    public async Task An_inter_state_supply_is_invoiced_under_igst_and_carries_nothing_under_cgst_or_sgst()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var elsewhere = await scenario.ElsewhereAsync();
        await scenario.RegisterInStateAsync(seller.Vendor.Id, elsewhere);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var order = await scenario.ReadOrderAsync(placed.OrderId);
        var subOrder = Assert.Single(order.GetProperty("subOrders").EnumerateArray());
        var invoice = subOrder.GetProperty("invoice");

        Assert.NotEqual(JsonValueKind.Null, invoice.ValueKind);

        var lines = subOrder.GetProperty("lines").EnumerateArray().ToList();

        Assert.True(invoice.GetProperty("igst").GetDecimal() > 0m, "An inter-state supply carries IGST.");
        Assert.Equal(Sum(lines, "igst"), invoice.GetProperty("igst").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("cgst").GetDecimal());
        Assert.Equal(0m, invoice.GetProperty("sgst").GetDecimal());

        Assert.Equal(
            false,
            Assert.Single(await Database.RowsAsync(
                "SELECT is_intra_state FROM orders.invoices WHERE id = $1",
                Cancellation,
                invoice.GetProperty("id").GetGuid()))["is_intra_state"]);

        await AssertRenderedAsync(invoice);
    }

    /// <summary>
    /// An invoice raised while the document store is unreachable still gets its number, with no
    /// document against it, and an operator can have the document produced afterwards.
    /// </summary>
    /// <remarks>
    /// Both halves are the point. The invoice is the numbered record and the PDF is a rendering of
    /// it, so a bucket that is briefly away must not stop a dispatched parcel from having a validly
    /// numbered invoice; and an invoice that is left with no document for ever is a statutory record
    /// nobody can produce, so raising it again has to repair it rather than refuse.
    /// </remarks>
    [Fact]
    public async Task An_invoice_raised_without_a_document_store_keeps_its_number_and_is_rendered_later()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        Factory.Storage.IsAvailable = false;

        string? number;

        try
        {
            // The parcel is closed while the bucket is away. The invoice is raised anyway.
            await scenario.DriveAsync(subOrderId, "Processing", "Packed");

            var raised = Assert.Single(
                (await scenario.ReadOrderAsync(placed.OrderId)).GetProperty("subOrders").EnumerateArray())
                .GetProperty("invoice");

            Assert.NotEqual(JsonValueKind.Null, raised.ValueKind);

            number = raised.GetProperty("invoiceNumber").GetString();

            Assert.False(string.IsNullOrWhiteSpace(number));
            Assert.Equal(JsonValueKind.Null, raised.GetProperty("fileId").ValueKind);

            // There is nothing to download, and the refusal says which of the two is missing.
            await RefusedAsync(
                await shopper.Client.GetAsync(
                    new Uri($"/api/v1/store/invoices/{raised.GetProperty("id").GetGuid()}/download", UriKind.Relative),
                    Cancellation),
                HttpStatusCode.NotFound,
                "ORDER_INVOICE_FILE_MISSING");
        }
        finally
        {
            Factory.Storage.IsAvailable = true;
        }

        // The operator asks for the invoice again once storage is back. It is not a second invoice:
        // the number is the one already allocated, and the series gains neither a hole nor a
        // duplicate.
        var repaired = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/invoice",
            new { },
            Cancellation));

        Assert.Equal(number, repaired.GetProperty("invoiceNumber").GetString());
        Assert.NotEqual(JsonValueKind.Null, repaired.GetProperty("fileId").ValueKind);

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM orders.invoices WHERE sub_order_id = $1",
                Cancellation,
                subOrderId));

        await AssertRenderedAsync(repaired);

        // Asking a third time is the conflict it always was: the document is there now, and a second
        // number would be the defect this path exists to avoid.
        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{subOrderId}/invoice",
                new { },
                Cancellation),
            HttpStatusCode.Conflict,
            "ORDER_ALREADY_INVOICED");
    }

    /// <summary>
    /// An invoice is refused before confirmation, raised once at dispatch, and covers only the units
    /// that survive a cancellation.
    /// </summary>
    [Fact]
    public async Task An_invoice_covers_the_live_units_and_cannot_be_raised_before_confirmation()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy, sellingPrice: 1000m);

        // A prepaid order that nobody has paid for is still awaiting payment, and a tax invoice for
        // goods nobody has agreed to pay for is not a document anybody should be able to raise.
        var (unpaidClient, _) = await SignedInShopperAsync();
        var unpaid = await scenario.ShopperAsync(unpaidClient);

        await scenario.AddToCartAsync(unpaid, seller.ListingId);

        var pending = await scenario.PlaceAsync(unpaid, "prepaid");
        var pendingSubOrderId = await scenario.SubOrderIdAsync(pending.OrderId, seller.Vendor.Id);

        await RefusedAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/sub-orders/{pendingSubOrderId}/invoice",
                new { },
                Cancellation),
            HttpStatusCode.Conflict,
            "ORDER_NOT_INVOICEABLE");

        // A confirmed order with a partly cancelled line: the invoice is raised for what is left.
        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId, quantity: 4);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        var order = await scenario.ReadOrderAsync(placed.OrderId);
        var line = Assert.Single(
            Assert.Single(order.GetProperty("subOrders").EnumerateArray()).GetProperty("lines").EnumerateArray());

        await scenario.CancelAsync(
            subOrderId,
            "One unit damaged in the warehouse.",
            new[] { new { orderLineId = line.GetProperty("id").GetGuid(), quantity = 1 } });

        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var invoiced = Assert.Single(
            (await scenario.ReadOrderAsync(placed.OrderId)).GetProperty("subOrders").EnumerateArray());

        var invoice = invoiced.GetProperty("invoice");
        var live = Assert.Single(invoiced.GetProperty("lines").EnumerateArray());

        // The line is still on the order at four units, and the invoice is for the three that are
        // actually being supplied.
        Assert.Equal(4, live.GetProperty("quantity").GetInt32());
        Assert.Equal(1, live.GetProperty("quantityCancelled").GetInt32());

        var expected = live.GetProperty("lineTotal").GetDecimal()
                       - invoiced.GetProperty("cancelledTotal").GetDecimal()
                       + invoiced.GetProperty("shippingTotal").GetDecimal();

        Assert.Equal(expected, invoice.GetProperty("total").GetDecimal());
        Assert.True(invoice.GetProperty("total").GetDecimal() < invoiced.GetProperty("total").GetDecimal());
    }

    /// <summary>The sum of one money field across a sub-order's lines.</summary>
    /// <param name="lines">The projected lines.</param>
    /// <param name="field">The field to add up.</param>
    private static decimal Sum(IEnumerable<JsonElement> lines, string field)
        => lines.Sum(line => line.GetProperty(field).GetDecimal());

    /// <summary>Asserts a rendered PDF exists behind an invoice and is registered as a document.</summary>
    /// <param name="invoice">The projected invoice.</param>
    private async Task AssertRenderedAsync(JsonElement invoice)
    {
        var fileId = invoice.GetProperty("fileId");

        Assert.NotEqual(JsonValueKind.Null, fileId.ValueKind);

        var file = Assert.Single(await Database.RowsAsync(
            "SELECT storage_key, content_type, byte_size, visibility FROM media.files WHERE id = $1",
            Cancellation,
            fileId.GetGuid()));

        Assert.Equal("application/pdf", file["content_type"]);
        Assert.Equal("Private", file["visibility"]?.ToString());
        Assert.True(Convert.ToInt64(file["byte_size"], System.Globalization.CultureInfo.InvariantCulture) > 0);

        // And the bytes are really in the store under that key, rather than a registry row pointing
        // at nothing.
        Assert.True(
            Factory.Storage.Contains(
                (string)file["storage_key"]!,
                Infrastructure.Storage.StorageVisibility.Private),
            "The invoice's registry row names a storage key that holds nothing.");
    }
}
