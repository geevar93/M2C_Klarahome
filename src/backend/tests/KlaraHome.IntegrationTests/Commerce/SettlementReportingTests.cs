using System.Net;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The documents a finance team actually files, and the report that has to reconcile with them.
/// </summary>
/// <remarks>TEST_DEBT.md rows 259 and 260.</remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class SettlementReportingTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The TCS/TDS export opens with its byte-order mark intact, quotes a seller name containing a
    /// comma, and refuses a range above <c>MaxExportRows</c>.
    /// </summary>
    [Fact]
    public async Task Exports_carry_the_byte_order_mark_quote_a_comma_and_refuse_too_large_a_range()
    {
        SkipWithoutDocker();

        // The ceiling has to be small enough to exceed without inserting ten thousand rows, but the
        // option itself is validated at start-up to be at least 100.
        Factory.Overrides["Settlements:MaxExportRows"] = "100";

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync(legalName: "Comma, Trading & Co");

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
            var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

            var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-14), DateTimeOffset.UtcNow.AddDays(-1));

            context.LedgerEntries.Add(LedgerEntry
                .Post(vendor.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 1000m, "INR",
                    LedgerReferenceTypes.Manual, null, $"sale:{Guid.NewGuid()}", period.Start.AddDays(1))
                .Taxed(900m));
            await context.SaveChangesAsync(Cancellation);

            await cycles.CloseAsync(vendor.Id, period, closedBy: null, Cancellation);
            await context.SaveChangesAsync(Cancellation);
        }

        var response = await admin.GetAsync(
            new Uri(
                $"/api/v1/admin/reports/tcs-tds/export?vendorId={vendor.Id}"
                + $"&from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"))}"
                + $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}",
                UriKind.Relative),
            Cancellation);

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(Cancellation);

        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"Comma, Trading & Co\"", text, StringComparison.Ordinal);

        // 101 ledger entries against a ceiling of 100 rows on the statement export.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

            for (var index = 0; index < 101; index++)
            {
                context.LedgerEntries.Add(LedgerEntry.Post(
                    vendor.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 1m, "INR",
                    LedgerReferenceTypes.Manual, null, $"ceiling:{Guid.NewGuid()}", DateTimeOffset.UtcNow.AddDays(-1)));
            }

            await context.SaveChangesAsync(Cancellation);
        }

        var refused = await admin.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendor.Id}/ledger/export", UriKind.Relative), Cancellation);

        await RefusedAsync(refused, HttpStatusCode.UnprocessableEntity, "SETTLEMENT_EXPORT_TOO_LARGE");
    }

    /// <summary>
    /// What the platform-revenue report calls revenue is exactly the sum of the charge entries on the
    /// sellers' ledgers, and TCS/TDS are excluded from it.
    /// </summary>
    [Fact]
    public async Task Platform_revenue_reconciles_with_the_charge_entries_and_excludes_the_statutory_deductions()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var vendor = await Sellers(admin).ActiveAsync();

        // The report is deliberately platform-wide with no vendor filter, so a shared test database
        // may already hold other tests' entries inside any window built from "now". The reconciling
        // proof is therefore a *delta*: what this report says before these entries exist, against
        // what it says after — never an absolute figure a neighbour test could perturb.
        Uri Window() => new(
            $"/api/v1/admin/reports/platform-revenue"
            + $"?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))}"
            + $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}",
            UriKind.Relative);

        var before = await ReadAsync(await admin.GetAsync(Window(), Cancellation));

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
            var occurredAt = DateTimeOffset.UtcNow.AddHours(-1);

            void Add(string type, LedgerDirection direction, decimal amount)
                => context.LedgerEntries.Add(LedgerEntry.Post(
                    vendor.Id, type, direction, amount, "INR", LedgerReferenceTypes.Manual, null,
                    $"{type}:{Guid.NewGuid()}", occurredAt));

            Add(LedgerEntryTypes.Sale, LedgerDirection.Credit, 1000m);
            Add(LedgerEntryTypes.Commission, LedgerDirection.Debit, 100m);
            Add(LedgerEntryTypes.PlatformFee, LedgerDirection.Debit, 10m);
            Add(LedgerEntryTypes.PaymentFee, LedgerDirection.Debit, 5m);
            Add(LedgerEntryTypes.ShippingFee, LedgerDirection.Debit, 50m);
            Add(LedgerEntryTypes.PlatformTax, LedgerDirection.Debit, 21m);
            Add(LedgerEntryTypes.RefundCommissionReversal, LedgerDirection.Credit, 20m);
            Add(LedgerEntryTypes.Tcs, LedgerDirection.Debit, 5m);
            Add(LedgerEntryTypes.Tds, LedgerDirection.Debit, 1m);

            await context.SaveChangesAsync(Cancellation);
        }

        var revenue = await ReadAsync(await admin.GetAsync(Window(), Cancellation));

        decimal Delta(string property)
            => revenue.GetProperty(property).GetDecimal() - before.GetProperty(property).GetDecimal();

        Assert.Equal(100m, Delta("commission"));
        Assert.Equal(10m, Delta("platformFee"));
        Assert.Equal(5m, Delta("paymentFee"));
        Assert.Equal(50m, Delta("shippingFee"));
        Assert.Equal(21m, Delta("tax"));
        Assert.Equal(20m, Delta("chargesReversed"));

        // Net revenue is exactly the charges, net of what was given back — nothing borrowed from a
        // second source, and TCS/TDS play no part in it.
        Assert.Equal(100m + 10m + 5m + 50m - 20m, Delta("netRevenue"));
        Assert.Equal(5m, Delta("tcs"));
        Assert.Equal(1m, Delta("tds"));

        // Reconciled against the ledger itself: the sum of the same charge entries, read back
        // independently through the ledger list, agrees to the paisa.
        var ledger = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/settlements/ledger?vendorId={vendor.Id}&size=100", UriKind.Relative),
            Cancellation));

        var chargeTypes = new[] { "Commission", "PlatformFee", "PaymentFee", "ShippingFee" };
        var chargesFromLedger = ledger.GetProperty("items").EnumerateArray()
            .Where(entry => chargeTypes.Contains(entry.GetProperty("entryType").GetString()))
            .Sum(entry => entry.GetProperty("amount").GetDecimal());

        var reversedFromLedger = ledger.GetProperty("items").EnumerateArray()
            .Where(entry => entry.GetProperty("entryType").GetString() == "RefundCommissionReversal")
            .Sum(entry => entry.GetProperty("amount").GetDecimal());

        Assert.Equal(chargesFromLedger - reversedFromLedger, Delta("netRevenue"));
    }
}
