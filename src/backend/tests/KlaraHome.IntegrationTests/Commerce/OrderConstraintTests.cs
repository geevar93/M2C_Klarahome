using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Orders.Infrastructure;
using KlaraHome.Modules.Orders.Infrastructure.Jobs;
using KlaraHome.SharedKernel.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// What the database is asked to hold true about an order: numbers that are unique and gapless, an
/// order whose money is the sum of its parts, a snapshot no later catalogue edit reaches, pages
/// that do not lose a row across a boundary, and a sweeper two workers can run.
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class OrderConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// Invoice numbers are gapless within a seller's financial year, two issued at the same instant
    /// serialise on the counter row rather than colliding, and a rolled-back allocation gives its
    /// number back.
    /// </summary>
    /// <remarks>
    /// Gapless is the hard part and the reason the counter is a row rather than a sequence:
    /// <c>nextval</c> is fast because it is not transactional, and a transaction that rolls back
    /// keeps the number it consumed. A hole in a purchase-order series is untidy; a hole in a GST
    /// invoice series is a finding at an audit.
    /// </remarks>
    [Fact]
    public async Task Invoice_numbers_are_gapless_per_seller_per_year_and_a_rollback_gives_one_back()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);
        var neighbour = await scenario.SellerAsync(taxonomy);

        // Three orders from the same seller, invoiced one after another.
        var numbers = new List<string>();

        for (var index = 0; index < 3; index++)
        {
            numbers.Add(await InvoiceAsync(scenario, seller));
        }

        var year = await Database.ScalarAsync<string>(
            "SELECT financial_year FROM orders.invoices WHERE vendor_id = $1 LIMIT 1",
            Cancellation,
            seller.Vendor.Id);

        Assert.Equal(
            [
                $"{seller.Vendor.Code}/{year}/00001",
                $"{seller.Vendor.Code}/{year}/00002",
                $"{seller.Vendor.Code}/{year}/00003",
            ],
            numbers);

        // The series is the seller's, not the platform's: the next seller starts at one.
        Assert.Equal($"{neighbour.Vendor.Code}/{year}/00001", await InvoiceAsync(scenario, neighbour));

        // Two invoices asked for at the same instant. The counter row is taken with SELECT … FOR
        // UPDATE, so one waits for the other and they come out consecutive rather than equal.
        var firstSubOrder = await PackedSubOrderAsync(scenario, seller, invoice: false);
        var secondSubOrder = await PackedSubOrderAsync(scenario, seller, invoice: false);

        var racing = await Task.WhenAll(
            IssueAsync(admin, firstSubOrder),
            IssueAsync(admin, secondSubOrder));

        Assert.Equal(2, racing.Distinct(StringComparer.Ordinal).Count());

        var issued = await SequenceNumbersAsync(seller.Vendor.Id);

        Assert.Equal(Enumerable.Range(1, issued.Count).ToList(), issued);

        // A rolled-back allocation gives the number back. This runs the module's own claim on its
        // own counter row and then abandons it, which is exactly what a failed placement does.
        var next = await Database.ScalarAsync<long>(
            "SELECT next_value FROM orders.number_sequences WHERE kind = 'invoice' AND scope_key = $1",
            Cancellation,
            seller.Vendor.Id.ToString("N", CultureInfo.InvariantCulture));

        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync(Cancellation);
            await using var abandoned = await connection.BeginTransactionAsync(Cancellation);

            await using var take = new NpgsqlCommand(
                "UPDATE orders.number_sequences SET next_value = next_value + 1 "
                + "WHERE kind = 'invoice' AND scope_key = $1",
                connection);

            take.Parameters.Add(new NpgsqlParameter
            {
                Value = seller.Vendor.Id.ToString("N", CultureInfo.InvariantCulture),
            });

            await take.ExecuteNonQueryAsync(Cancellation);
            await abandoned.RollbackAsync(Cancellation);
        }

        Assert.Equal(
            next,
            await Database.ScalarAsync<long>(
                "SELECT next_value FROM orders.number_sequences WHERE kind = 'invoice' AND scope_key = $1",
                Cancellation,
                seller.Vendor.Id.ToString("N", CultureInfo.InvariantCulture)));

        // And the next invoice really does take it, rather than skipping past a number nobody used.
        Assert.EndsWith(
            next.ToString("D5", CultureInfo.InvariantCulture),
            await InvoiceAsync(scenario, seller),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An order number series survives a failed placement: the number the rolled-back attempt
    /// consumed is the number the next order takes.
    /// </summary>
    [Fact]
    public async Task A_failed_placement_gives_its_order_number_back_to_the_series()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var first = await scenario.PlaceAsync(shopper);

        // The attempt that falls over at the gateway. It allocates a number inside the transaction
        // it then rolls back.
        var (failingClient, _) = await SignedInShopperAsync();
        var failing = await scenario.ShopperAsync(failingClient);

        await scenario.AddToCartAsync(failing, seller.ListingId);

        var session = await scenario.CheckoutAsync(failing, "prepaid");

        Factory.Gateway.IsConfigured = false;

        try
        {
            await RefusedAsync(
                await scenario.PlaceRawAsync(failing, session.GetProperty("id").GetGuid()),
                System.Net.HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Factory.Gateway.IsConfigured = true;
        }

        var (thirdClient, _) = await SignedInShopperAsync();
        var third = await scenario.ShopperAsync(thirdClient);

        await scenario.AddToCartAsync(third, seller.ListingId);

        var second = await scenario.PlaceAsync(third);

        Assert.Equal(Counter(first.OrderNumber) + 1, Counter(second.OrderNumber));
    }

    /// <summary>
    /// Order and invoice numbers are unique per tenant, and an order's money is the sum of its
    /// sellers' parts — the §4.4 invariant, asserted against the database rather than the API.
    /// </summary>
    /// <remarks>
    /// Against the database because that is where the invariant has to hold: the API adds up what it
    /// was given, and a projection that computed a total from the sub-orders would agree with itself
    /// however the rows were written.
    /// </remarks>
    [Fact]
    public async Task Numbers_are_unique_per_tenant_and_an_orders_money_is_the_sum_of_its_parts()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var first = await scenario.SellerAsync(taxonomy, sellingPrice: 999m);
        var second = await scenario.SellerAsync(taxonomy, sellingPrice: 1199m);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        // Two units each: enough to make the sums non-trivial, and under the cash-on-delivery
        // ceiling the store's own settings impose.
        await scenario.AddToCartAsync(shopper, first.ListingId, quantity: 2);
        await scenario.AddToCartAsync(shopper, second.ListingId, quantity: 2);

        var (_, code) = await scenario.CouponAsync(percent: 10m);
        await scenario.ApplyCouponAsync(shopper, code);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, first.Vendor.Id);

        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        // Every money column on the order is the sum of the same column across its sellers' parts,
        // and the grand total is those parts plus the order-level charges that belong to nobody.
        var money = Assert.Single(await Database.RowsAsync(
            """
            SELECT o.items_total, o.discount_total, o.tax_total, o.shipping_total,
                   o.cod_fee, o.rounding_adjustment, o.grand_total,
                   s.items_total AS parts_items, s.discount_total AS parts_discount,
                   s.tax_total AS parts_tax, s.shipping_total AS parts_shipping, s.total AS parts_total
            FROM orders.orders o
            JOIN (SELECT order_id,
                         SUM(items_total) AS items_total,
                         SUM(discount_total) AS discount_total,
                         SUM(tax_total + shipping_tax) AS tax_total,
                         SUM(shipping_total) AS shipping_total,
                         SUM(total) AS total
                  FROM orders.sub_orders GROUP BY order_id) s ON s.order_id = o.id
            WHERE o.id = $1
            """,
            Cancellation,
            placed.OrderId));

        Assert.Equal(Amount(money, "items_total"), Amount(money, "parts_items"));
        Assert.Equal(Amount(money, "discount_total"), Amount(money, "parts_discount"));
        Assert.Equal(Amount(money, "tax_total"), Amount(money, "parts_tax"));
        Assert.Equal(Amount(money, "shipping_total"), Amount(money, "parts_shipping"));

        Assert.Equal(
            Amount(money, "grand_total"),
            Amount(money, "parts_total") + Amount(money, "cod_fee") + Amount(money, "rounding_adjustment"));

        // A second order of the same shopper's, so there is something to collide with.
        await scenario.AddToCartAsync(shopper, second.ListingId);

        var rival = await scenario.PlaceAsync(shopper);
        var rivalSubOrderId = await scenario.SubOrderIdAsync(rival.OrderId, second.Vendor.Id);

        await scenario.DriveAsync(rivalSubOrderId, "Processing", "Packed");

        // The order number is unique per tenant: a row that tries to take one already in use is
        // refused by the index rather than by a check somewhere in the application.
        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                "UPDATE orders.orders SET order_number = "
                + "(SELECT order_number FROM orders.orders WHERE id = $1) WHERE id = $2",
                Cancellation,
                placed.OrderId,
                rival.OrderId));

        // And so is an invoice number, within a seller's financial year. Both invoices are moved
        // onto one seller first, because the series — and therefore the uniqueness — is theirs.
        Assert.Equal(
            "23505",
            await Database.RefusalAsync(
                "UPDATE orders.invoices SET vendor_id = "
                + "(SELECT vendor_id FROM orders.invoices WHERE sub_order_id = $1), invoice_number = "
                + "(SELECT invoice_number FROM orders.invoices WHERE sub_order_id = $1) "
                + "WHERE sub_order_id = $2",
                Cancellation,
                subOrderId,
                rivalSubOrderId));
    }

    /// <summary>
    /// The order lines' frozen snapshot is unaffected by a later catalogue edit: renaming a product
    /// or repricing its offer does not alter a historical order or the invoice raised against it.
    /// </summary>
    /// <remarks>
    /// This is what "the order is the record of what was agreed" means in practice. A merchandiser
    /// renames a product every week and reprices it every day; if either reached an order already
    /// placed, a shopper's history and a seller's tax invoice would both change under them.
    /// </remarks>
    [Fact]
    public async Task A_later_catalogue_edit_does_not_reach_an_order_that_was_already_placed()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy, sellingPrice: 999m);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId, quantity: 2);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        await scenario.DriveAsync(subOrderId, "Processing", "Packed");

        var before = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        var beforeLine = Assert.Single(
            Assert.Single(before.GetProperty("subOrders").EnumerateArray()).GetProperty("lines").EnumerateArray());

        var beforeInvoice = Assert.Single(before.GetProperty("subOrders").EnumerateArray())
            .GetProperty("invoice");

        // The merchandiser renames the product and the seller cuts the price.
        var product = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/products/{seller.ProductId}", UriKind.Relative),
            Cancellation));

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/products/{seller.ProductId}",
            new
            {
                name = "Renamed after the order was placed",
                slug = product.GetProperty("slug").GetString(),
                categoryId = product.GetProperty("categoryId").GetGuid(),
                brandId = product.GetProperty("brandId").GetGuid(),
                shortDescription = "Rewritten.",
                description = "Rewritten.",
                hsnCode = "940490",
                gstRate = 12m,
                countryOfOrigin = "IN",
                manufacturer = product.GetProperty("manufacturer"),
                packer = (object?)null,
                importer = (object?)null,
                isReturnable = true,
                returnWindowDays = 7,
                warranty = (string?)null,
                specifications = Array.Empty<object>(),
                seo = (object?)null,
                attributes = Array.Empty<object>(),
            },
            Cancellation));

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/listings/{seller.ListingId}",
            new
            {
                mrp = 1299m,
                sellingPrice = 499m,
                vendorSku = (string?)null,
                handlingTimeHours = 24,
                isCodAllowed = true,
                maxOrderQuantity = (int?)null,
            },
            Cancellation));

        var after = await ReadAsync(await shopper.Client.GetAsync(
            new Uri($"/api/v1/store/orders/{placed.OrderId}", UriKind.Relative),
            Cancellation));

        var afterSubOrder = Assert.Single(after.GetProperty("subOrders").EnumerateArray());
        var afterLine = Assert.Single(afterSubOrder.GetProperty("lines").EnumerateArray());

        // Every frozen figure and every frozen word is where it was.
        Assert.Equal(beforeLine.GetProperty("name").GetString(), afterLine.GetProperty("name").GetString());
        Assert.Equal(beforeLine.GetProperty("hsnCode").GetString(), afterLine.GetProperty("hsnCode").GetString());
        Assert.Equal(beforeLine.GetProperty("unitPrice").GetDecimal(), afterLine.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(beforeLine.GetProperty("gstRate").GetDecimal(), afterLine.GetProperty("gstRate").GetDecimal());
        Assert.Equal(beforeLine.GetProperty("lineTotal").GetDecimal(), afterLine.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(before.GetProperty("grandTotal").GetDecimal(), after.GetProperty("grandTotal").GetDecimal());

        Assert.DoesNotContain(
            "Renamed after the order was placed",
            afterLine.GetProperty("name").GetString(),
            StringComparison.Ordinal);

        // And the invoice, which is a statutory record of the same supply, is untouched.
        var afterInvoice = afterSubOrder.GetProperty("invoice");

        Assert.Equal(
            beforeInvoice.GetProperty("invoiceNumber").GetString(),
            afterInvoice.GetProperty("invoiceNumber").GetString());

        Assert.Equal(beforeInvoice.GetProperty("total").GetDecimal(), afterInvoice.GetProperty("total").GetDecimal());
        Assert.Equal(
            beforeInvoice.GetProperty("taxableValue").GetDecimal(),
            afterInvoice.GetProperty("taxableValue").GetDecimal());
    }

    /// <summary>
    /// The order and sub-order listings keyset-paginate across a page boundary without losing or
    /// repeating a row, and a seller sees only their own in both.
    /// </summary>
    [Fact]
    public async Task Both_listings_page_across_a_boundary_and_confine_a_seller_to_their_own()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var mine = await scenario.SellerAsync(taxonomy);
        var theirs = await scenario.SellerAsync(taxonomy);

        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        // Three orders, each split across both sellers: six sub-orders, and a page size of two puts
        // a boundary in the middle of both listings.
        var orders = new List<Guid>();

        for (var index = 0; index < 3; index++)
        {
            await scenario.AddToCartAsync(shopper, mine.ListingId);
            await scenario.AddToCartAsync(shopper, theirs.ListingId);

            orders.Add((await scenario.PlaceAsync(shopper)).OrderId);
        }

        // The shopper's own list, paged two at a time: three rows across a boundary, none lost and
        // none repeated.
        var paged = await PageAsync(shopper.Client, "/api/v1/store/orders", size: 2);

        Assert.Equal(orders.OrderDescending(), paged);

        // The same across the admin listing, narrowed to this shopper so the boundary is inside
        // their three rather than somewhere in the whole store's.
        var adminPaged = await PageAsync(
            admin,
            $"/api/v1/admin/orders?customerId={shopper.CustomerId}",
            size: 2);

        Assert.Equal(orders.OrderDescending(), adminPaged);

        // And the fulfilment worklist, which pages on sub-orders rather than orders.
        var subOrders = await PageAsync(admin, $"/api/v1/admin/sub-orders?vendorId={mine.Vendor.Id}", size: 2);

        Assert.Equal(3, subOrders.Count);
        Assert.Equal(subOrders.Count, subOrders.Distinct().Count());

        // A seller walking the same listing with no filter at all sees exactly those three, because
        // the vendor filter has already decided what exists for them.
        var (seller, _) = await SignedInVendorOwnerAsync(admin, mine.Vendor.Id);

        Assert.Equal(
            subOrders.OrderBy(id => id),
            (await PageAsync(seller, "/api/v1/admin/sub-orders", size: 2)).OrderBy(id => id));

        // The order listing is scoped the same way: the three orders they have a part in, and
        // nothing else in the store.
        Assert.Equal(orders.OrderDescending(), await PageAsync(seller, "/api/v1/admin/orders", size: 2));
    }

    /// <summary>
    /// The lifecycle sweeper completes a delivered sub-order once its return window closes, cancels
    /// an order that waited too long for a payment, and claims its work with
    /// <c>FOR UPDATE SKIP LOCKED</c> so a second worker steps over what the first is holding.
    /// </summary>
    /// <remarks>
    /// The two clocks are wound forward in the rows rather than waited out: the window is a week and
    /// the payment timeout an hour, and a test that waited for either would not be a test. What is
    /// under test is what the sweeper does when they have passed, and that is unchanged by how they
    /// came to pass.
    /// </remarks>
    [Fact]
    public async Task The_sweeper_completes_a_lapsed_window_cancels_an_unpaid_order_and_is_safe_in_two_workers()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new OrderScenario(admin, Cancellation);

        var taxonomy = await scenario.TaxonomyAsync();
        var seller = await scenario.SellerAsync(taxonomy);

        // A delivered parcel whose return window has closed.
        var (deliveredClient, _) = await SignedInShopperAsync();
        var delivering = await scenario.ShopperAsync(deliveredClient);

        await scenario.AddToCartAsync(delivering, seller.ListingId);

        var delivered = await scenario.PlaceAsync(delivering);
        var deliveredSubOrder = await scenario.SubOrderIdAsync(delivered.OrderId, seller.Vendor.Id);

        await scenario.DriveAsync(deliveredSubOrder, "Processing", "Packed", "Shipped", "OutForDelivery", "Delivered");

        // An unpaid prepaid order, older than the timeout.
        var (unpaidClient, _) = await SignedInShopperAsync();
        var waiting = await scenario.ShopperAsync(unpaidClient);

        await scenario.AddToCartAsync(waiting, seller.ListingId);

        var unpaid = await scenario.PlaceAsync(waiting, "prepaid");
        var unpaidSubOrder = await scenario.SubOrderIdAsync(unpaid.OrderId, seller.Vendor.Id);

        // Both clocks wound past, in the rows the sweeper reads.
        Assert.Equal(
            1,
            await Database.ExecuteAsync(
                "UPDATE orders.sub_orders SET return_window_ends_at = now() - interval '1 day' WHERE id = $1",
                Cancellation,
                deliveredSubOrder));

        Assert.Equal(
            1,
            await Database.ExecuteAsync(
                "UPDATE orders.sub_orders SET created_at = now() - interval '1 day' WHERE id = $1",
                Cancellation,
                unpaidSubOrder));

        // Nothing has moved yet: the sweeper is registered but switched off in this host.
        Assert.Equal("Delivered", await scenario.SubOrderStatusAsync(delivered.OrderId, seller.Vendor.Id));
        Assert.Equal("PendingPayment", await scenario.SubOrderStatusAsync(unpaid.OrderId, seller.Vendor.Id));

        await SweepAsync(deliveredSubOrder, unpaidSubOrder);

        Assert.Equal("Completed", await scenario.SubOrderStatusAsync(delivered.OrderId, seller.Vendor.Id));
        Assert.Equal("Completed", await scenario.OrderStatusAsync(delivered.OrderId));

        Assert.Equal("Cancelled", await scenario.SubOrderStatusAsync(unpaid.OrderId, seller.Vendor.Id));
        Assert.Equal("Cancelled", await scenario.OrderStatusAsync(unpaid.OrderId));

        // The unpaid order never committed its stock, so cancelling it puts the hold back on sale
        // rather than leaving it to lapse.
        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM inventory.stock_reservations "
                + "WHERE reference_type = 'cart' AND reference_id = $1 AND status = 'Released'",
                Cancellation,
                unpaid.CartId));

        // Completion is announced exactly once, however many times the sweeper runs.
        await SweepAsync();

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM platform.outbox_messages "
                + "WHERE type LIKE '%OrderCompleted%' AND payload::text LIKE $1",
                Cancellation,
                $"%{delivered.OrderId}%"));

        // And the claim is one a second worker steps over rather than blocking on or taking twice.
        await AssertClaimIsExclusiveAsync(seller);
    }

    /// <summary>Runs the real sweeper until the named sub-orders have stopped being due.</summary>
    /// <param name="waitFor">Sub-orders whose state should change, or none to run one pass.</param>
    private async Task SweepAsync(params Guid[] waitFor)
    {
        var options = new OrdersOptions
        {
            SweeperEnabled = true,
            SweepIntervalSeconds = 1,
            SweepBatchSize = 100,
            UnpaidOrderTimeoutMinutes = 60,
        };

        using var sweeper = new OrderLifecycleSweeper(
            Factory.Services,
            new FixedOrdersOptions(options),
            SystemClock.Instance,
            NullLogger<OrderLifecycleSweeper>.Instance);

        await sweeper.StartAsync(Cancellation);

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

            while (waitFor.Length > 0 && DateTimeOffset.UtcNow < deadline)
            {
                var open = await Database.CountAsync(
                    "SELECT COUNT(*) FROM orders.sub_orders "
                    + "WHERE id = ANY($1) AND status IN ('Delivered', 'PendingPayment')",
                    Cancellation,
                    waitFor);

                if (open == 0)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), Cancellation);
            }

            if (waitFor.Length > 0)
            {
                Assert.Fail("The sweeper had not moved the due sub-orders after sixty seconds.");
            }

            // A pass with nothing to wait for still needs time to happen at all.
            await Task.Delay(TimeSpan.FromSeconds(2), Cancellation);
        }
        finally
        {
            await sweeper.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Two workers claiming the sweeper's queue at the same instant take different rows.
    /// </summary>
    /// <remarks>
    /// Both transactions are held open at once deliberately: a row lock lives for the length of a
    /// transaction, and a claim taken outside one is released the moment the query returns — which
    /// is exactly the bug this shows is absent.
    /// </remarks>
    private async Task AssertClaimIsExclusiveAsync(OrderSeller seller)
    {
        var scenario = new OrderScenario(await SignedInAdministratorAsync(), Cancellation);
        var due = new List<Guid>();

        for (var index = 0; index < 2; index++)
        {
            var (client, _) = await SignedInShopperAsync();
            var shopper = await scenario.ShopperAsync(client);

            await scenario.AddToCartAsync(shopper, seller.ListingId);

            var placed = await scenario.PlaceAsync(shopper, "prepaid");

            due.Add(await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id));
        }

        await Database.ExecuteAsync(
            "UPDATE orders.sub_orders SET created_at = now() - interval '1 day' WHERE id = ANY($1)",
            Cancellation,
            due.ToArray());

        await using var worker = new NpgsqlConnection(Database.ConnectionString);
        await using var rival = new NpgsqlConnection(Database.ConnectionString);
        await using var spare = new NpgsqlConnection(Database.ConnectionString);

        await worker.OpenAsync(Cancellation);
        await rival.OpenAsync(Cancellation);
        await spare.OpenAsync(Cancellation);

        await using var held = await worker.BeginTransactionAsync(Cancellation);
        await using var racing = await rival.BeginTransactionAsync(Cancellation);
        await using var idle = await spare.BeginTransactionAsync(Cancellation);

        var first = await ClaimAsync(worker, due);
        var second = await ClaimAsync(rival, due);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Null(await ClaimAsync(spare, due));

        await held.RollbackAsync(Cancellation);
        await racing.RollbackAsync(Cancellation);
        await idle.RollbackAsync(Cancellation);
    }

    /// <summary>The sweeper's own claim, on a connection whose transaction is open.</summary>
    private static async Task<Guid?> ClaimAsync(NpgsqlConnection connection, IReadOnlyList<Guid> candidates)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM orders.sub_orders WHERE status = 'PendingPayment' AND id = ANY($1) "
            + "ORDER BY created_at LIMIT 1 FOR UPDATE SKIP LOCKED",
            connection);

        command.Parameters.Add(new NpgsqlParameter { Value = candidates.ToArray() });

        return await command.ExecuteScalarAsync(Cancellation) as Guid?;
    }

    /// <summary>Places an order for one seller and packs it, so a tax invoice is raised.</summary>
    private async Task<string> InvoiceAsync(OrderScenario scenario, OrderSeller seller)
    {
        var subOrderId = await PackedSubOrderAsync(scenario, seller);

        var invoice = await Database.ScalarAsync<string>(
            "SELECT invoice_number FROM orders.invoices WHERE sub_order_id = $1",
            Cancellation,
            subOrderId);

        Assert.NotNull(invoice);

        return invoice;
    }

    /// <summary>Places an order for one seller and moves it to <c>Packed</c>.</summary>
    /// <param name="scenario">The scenario building it.</param>
    /// <param name="seller">The seller.</param>
    /// <param name="invoice">Whether to let the automatic invoice at dispatch happen.</param>
    private async Task<Guid> PackedSubOrderAsync(OrderScenario scenario, OrderSeller seller, bool invoice = true)
    {
        var (client, _) = await SignedInShopperAsync();
        var shopper = await scenario.ShopperAsync(client);

        await scenario.AddToCartAsync(shopper, seller.ListingId);

        var placed = await scenario.PlaceAsync(shopper);
        var subOrderId = await scenario.SubOrderIdAsync(placed.OrderId, seller.Vendor.Id);

        await scenario.TransitionAsync(subOrderId, "Processing");

        if (invoice)
        {
            await scenario.TransitionAsync(subOrderId, "Packed");
            return subOrderId;
        }

        // Held at Processing so the caller can raise the invoice by hand, which is what makes two
        // simultaneous issues possible to arrange at all.
        return subOrderId;
    }

    /// <summary>Raises an invoice by hand and answers its number.</summary>
    private static async Task<string> IssueAsync(HttpClient admin, Guid subOrderId)
    {
        var issued = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/sub-orders/{subOrderId}/invoice",
            new { },
            Cancellation));

        return issued.GetProperty("invoiceNumber").GetString()!;
    }

    /// <summary>Every counter value one seller's invoice series has issued, in order.</summary>
    private async Task<IReadOnlyList<int>> SequenceNumbersAsync(Guid vendorId)
    {
        var rows = await Database.RowsAsync(
            "SELECT invoice_number FROM orders.invoices WHERE vendor_id = $1 ORDER BY invoice_number",
            Cancellation,
            vendorId);

        return [.. rows.Select(row => Counter((string)row["invoice_number"]!))];
    }

    /// <summary>The counter at the end of an order or invoice number.</summary>
    private static int Counter(string number)
        => int.Parse(number[(number.LastIndexOf('/') + 1)..].Split('-')[^1], CultureInfo.InvariantCulture);

    /// <summary>One money column of a joined row.</summary>
    private static decimal Amount(IReadOnlyDictionary<string, object?> row, string column)
        => Convert.ToDecimal(row[column], CultureInfo.InvariantCulture);

    /// <summary>
    /// Walks every page of a keyset-paginated listing and answers the ids in the order they came.
    /// </summary>
    /// <remarks>
    /// The page after the boundary is fetched with the cursor the one before it handed back, exactly
    /// as a client would — which is the only way to show that a row does not fall between two pages.
    /// </remarks>
    /// <param name="client">The caller.</param>
    /// <param name="path">The listing route, with any filters already on it.</param>
    /// <param name="size">The page size, chosen so a boundary falls inside the data.</param>
    private static async Task<List<Guid>> PageAsync(HttpClient client, string path, int size)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var ids = new List<Guid>();
        string? cursor = null;

        for (var page = 0; page < 100; page++)
        {
            var query = $"{separator}size={size}"
                        + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");

            var body = await ReadAsync(await client.GetAsync(new Uri(path + query, UriKind.Relative), Cancellation));

            ids.AddRange(body.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));

            var next = body.GetProperty("page").GetProperty("nextCursor");
            cursor = next.ValueKind == JsonValueKind.Null ? null : next.GetString();

            if (cursor is null)
            {
                return ids;
            }
        }

        Assert.Fail($"'{path}' never stopped paging.");
        return ids;
    }

    /// <summary>The sweeper reads its settings from a monitor; this one never changes.</summary>
    /// <param name="value">The settings for this run.</param>
    private sealed class FixedOrdersOptions(OrdersOptions value) : IOptionsMonitor<OrdersOptions>
    {
        public OrdersOptions CurrentValue => value;

        public OrdersOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OrdersOptions, string?> listener) => null;
    }
}
