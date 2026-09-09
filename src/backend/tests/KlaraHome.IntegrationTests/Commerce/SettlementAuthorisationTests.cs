using System.Net;
using System.Net.Http.Json;
using KlaraHome.Contracts.Vendors;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Endpoints;
using KlaraHome.Modules.Settlements.Infrastructure.Accounting;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Who may see a seller's money, who may move it, and the seams either side of the module.
/// </summary>
/// <remarks>
/// TEST_DEBT.md rows 250, 251, 255, 256 and 258.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class SettlementAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A seller sees only their own cycles, ledger and payout items through the vendor query filter,
    /// and another seller's batch id answers <c>404</c> rather than <c>403</c>.
    /// </summary>
    [Fact]
    public async Task A_seller_sees_only_their_own_settlement_data_and_a_strangers_batch_is_404()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellerA = await Sellers(admin).ActiveAsync();
        var sellerB = await Sellers(admin).ActiveAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

            context.LedgerEntries.Add(LedgerEntry.Post(
                sellerA.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 500m, "INR",
                LedgerReferenceTypes.Manual, null, $"a:{Guid.NewGuid()}", DateTimeOffset.UtcNow.AddDays(-1)));

            context.LedgerEntries.Add(LedgerEntry.Post(
                sellerB.Id, LedgerEntryTypes.Adjustment, LedgerDirection.Credit, 700m, "INR",
                LedgerReferenceTypes.Manual, null, $"b:{Guid.NewGuid()}", DateTimeOffset.UtcNow.AddDays(-1)));

            await context.SaveChangesAsync(Cancellation);
        }

        var (clientA, _) = await SignedInVendorOwnerAsync(admin, sellerA.Id);

        var ledger = await ReadAsync(await clientA.GetAsync(
            new Uri("/api/v1/admin/settlements/ledger", UriKind.Relative), Cancellation));

        Assert.All(
            ledger.GetProperty("items").EnumerateArray(),
            entry => Assert.Equal(sellerA.Id, entry.GetProperty("vendorId").GetGuid()));

        // Naming somebody else's vendor id in a statement request is ignored, not refused: the
        // caller's own token is what decides the account, exactly as the module's own contract says.
        var statement = await ReadAsync(await clientA.GetAsync(
            new Uri($"/api/v1/admin/vendors/{sellerB.Id}/ledger", UriKind.Relative), Cancellation));

        Assert.Equal(sellerA.Id, statement.GetProperty("vendorId").GetGuid());

        // A payout batch that never touched seller A is invisible to them — 404, not 403, because the
        // difference would tell a seller that another seller was ever paid.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();

            var period = new SettlementPeriod(DateTimeOffset.UtcNow.AddDays(-14), DateTimeOffset.UtcNow.AddDays(-1));
            var cycles = scope.ServiceProvider.GetRequiredService<SettlementCycleService>();

            context.LedgerEntries.Add(LedgerEntry
                .Post(sellerB.Id, LedgerEntryTypes.Sale, LedgerDirection.Credit, 5000m, "INR",
                    LedgerReferenceTypes.Manual, null, $"sale:{Guid.NewGuid()}", period.Start.AddDays(1))
                .Taxed(4500m));
            await context.SaveChangesAsync(Cancellation);

            await cycles.CloseAsync(sellerB.Id, period, closedBy: null, Cancellation);
            await context.SaveChangesAsync(Cancellation);
        }

        var closedB = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/settlements/cycles?vendorId={sellerB.Id}&status=Closed", UriKind.Relative),
            Cancellation));
        var cycleBId = closedB.GetProperty("items")[0].GetProperty("id").GetGuid();

        var batchForB = await ReadAsync(await admin.PostAsJsonAsync(
            "/api/v1/admin/payout-batches", new { cycleIds = new[] { cycleBId } }, Cancellation));

        var refused = await clientA.GetAsync(
            new Uri($"/api/v1/admin/payout-batches/{batchForB.GetProperty("id").GetGuid()}", UriKind.Relative),
            Cancellation);

        await RefusedAsync(refused, HttpStatusCode.NotFound, "SETTLEMENT_NOT_FOUND");

        // And a made-up cycle id answers exactly the same way — the two cases are indistinguishable
        // from outside, which is the point.
        var invented = await clientA.GetAsync(
            new Uri($"/api/v1/admin/settlements/cycles/{Guid.NewGuid()}", UriKind.Relative), Cancellation);
        await RefusedAsync(invented, HttpStatusCode.NotFound, "SETTLEMENT_NOT_FOUND");
    }

    /// <summary>
    /// A seller cannot reach the write surface as platform finance: adjustments, closes, batch
    /// creation, approval and processing all refuse a vendor-scoped caller with
    /// <c>SETTLEMENT_VENDOR_FORBIDDEN</c>.
    /// </summary>
    [Fact]
    public async Task A_vendor_scoped_caller_is_refused_on_every_settlement_write_surface()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var seller = await Sellers(admin).ActiveAsync();
        var (vendorClient, _) = await SignedInVendorOwnerAsync(admin, seller.Id);

        // A seller does not hold settlements.settlement.manage or settlements.payout.* at all, so
        // every one of these is refused before the handler's own vendor check ever runs — proving the
        // permission model and the handler agree rather than one silently covering for the other.
        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                "/api/v1/admin/settlements/adjustments",
                new { vendorId = seller.Id, direction = "credit", amount = 10m, reason = "test" },
                Cancellation),
            HttpStatusCode.Forbidden,
            null);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                "/api/v1/admin/settlements/cycles/close",
                new { vendorId = seller.Id, force = true },
                Cancellation),
            HttpStatusCode.Forbidden,
            null);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                "/api/v1/admin/payout-batches", new { cycleIds = Array.Empty<Guid>() }, Cancellation),
            HttpStatusCode.Forbidden,
            null);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                $"/api/v1/admin/payout-batches/{Guid.NewGuid()}/approve", new { }, Cancellation),
            HttpStatusCode.Forbidden,
            null);

        await RefusedAsync(
            await vendorClient.PostAsJsonAsync(
                $"/api/v1/admin/payout-batches/{Guid.NewGuid()}/process", new { }, Cancellation),
            HttpStatusCode.Forbidden,
            null);

        // No HTTP caller can present as a vendor and hold platform permissions at once, so the
        // handler's own "is this a vendor account" check — the second layer, beside the permission
        // that already refused above — is proved directly, with the module's own stable code rather
        // than a generic 403.
        using var scope = Factory.Services.CreateScope();
        var caller = new FakeCallerContext(seller.Id);
        var settlementsScope = new KlaraHome.Modules.Settlements.Infrastructure.SettlementsScope(caller);

        var context = scope.ServiceProvider.GetRequiredService<SettlementsDbContext>();
        var poster = scope.ServiceProvider.GetRequiredService<
            KlaraHome.Modules.Settlements.Infrastructure.Accounting.SettlementPoster>();
        var clock = scope.ServiceProvider.GetRequiredService<KlaraHome.SharedKernel.Time.IClock>();

        var handler = new KlaraHome.Modules.Settlements.Application.Ledger.PostAdjustmentCommandHandler(
            context, poster, settlementsScope, clock);

        var result = await handler.HandleAsync(
            new KlaraHome.Modules.Settlements.Application.Ledger.PostAdjustmentCommand(
                seller.Id, "credit", 10m, "test"),
            Cancellation);

        Assert.True(result.IsFailure);
        Assert.Equal("SETTLEMENT_VENDOR_FORBIDDEN", result.Error.Code);
    }

    /// <summary>A minimal <see cref="ICallerContext"/> for driving a handler directly as a seller.</summary>
    private sealed class FakeCallerContext(Guid vendorId) : KlaraHome.Infrastructure.Authorization.ICallerContext
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public Guid? SessionId => Guid.NewGuid();
        public Guid? VendorId => vendorId;
        public string? UserType => "vendor";
        public string? Segment => null;
        public Guid? ImpersonatorId => null;

        // Every settlement permission, deliberately — the point of this test is that the handler
        // refuses a vendor account even when the permission layer, hypothetically, did not.
        public bool HasPermission(string permission) => true;
    }

    /// <summary>
    /// Every permission the Settlements endpoints declare appears in <c>PermissionCatalog</c>, so a
    /// role can actually grant it.
    /// </summary>
    [Fact]
    public void Every_settlements_permission_is_declared_in_the_catalogue()
    {
        var declared = new[]
        {
            SettlementsPermissions.SettlementRead,
            SettlementsPermissions.SettlementManage,
            SettlementsPermissions.PayoutManage,
            SettlementsPermissions.PayoutApprove,
        };

        var catalogue = PermissionCatalog.All.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);

        Assert.All(declared, code => Assert.Contains(code, catalogue));
    }

    /// <summary>
    /// The settlement settings validator refuses an unknown frequency, a nil rate on an enabled
    /// deduction, and a gateway fee configured but switched off — through the real settings endpoint,
    /// not against the validator directly.
    /// </summary>
    [Fact]
    public async Task Settlement_settings_are_validated_through_the_real_settings_endpoint()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var valid = new Dictionary<string, object?>
        {
            ["frequency"] = "weekly",
            ["weekStartDay"] = 1,
            ["holdDays"] = 7,
            ["minimumPayoutAmount"] = 100m,
            ["payoutApprovalThreshold"] = 0m,
            ["platformFeePercent"] = 0m,
            ["platformFeeFixed"] = 0m,
            ["platformServiceGstRate"] = 18m,
            ["paymentGatewayFeePercent"] = 0m,
            ["chargeGatewayFeeToVendor"] = false,
            ["chargeShippingToVendor"] = true,
            ["tcsEnabled"] = true,
            ["tcsRatePercent"] = 0.5m,
            ["tdsEnabled"] = true,
            ["tdsRatePercent"] = 0.1m,
            ["tdsRateWithoutPanPercent"] = 5m,
            ["tdsAnnualThreshold"] = 0m,
            ["autoBatchOnClose"] = false,
        };

        // An unknown frequency.
        await Refuse(admin, valid, "frequency", "monthly-ish");

        // TCS switched on with a zero rate.
        await Refuse(admin, valid, "tcsRatePercent", 0m, "tcsEnabled", true);

        // The gateway fee charged on with nothing to charge.
        await Refuse(admin, valid, "chargeGatewayFeeToVendor", true, "paymentGatewayFeePercent", 0m);

        // The gateway fee left configured while the charge is switched off.
        await Refuse(admin, valid, "chargeGatewayFeeToVendor", false, "paymentGatewayFeePercent", 2m);

        // The same document, unmodified, is accepted.
        var accepted = await admin.PutAsJsonAsync("/api/v1/admin/settings/settlements", valid, Cancellation);
        await ReadAsync(accepted);

        static async Task Refuse(
            HttpClient admin,
            Dictionary<string, object?> baseline,
            string key,
            object? value,
            string? secondKey = null,
            object? secondValue = null)
        {
            var body = new Dictionary<string, object?>(baseline) { [key] = value };

            if (secondKey is not null)
            {
                body[secondKey] = secondValue;
            }

            var response = await admin.PutAsJsonAsync("/api/v1/admin/settings/settlements", body, Cancellation);
            await RefusedAsync(response, HttpStatusCode.UnprocessableEntity, null);
        }
    }

    /// <summary>
    /// <c>IVendorPayouts</c> reports a seller unpayable for each reason in turn, and no plaintext
    /// account number ever crosses the seam — only the last four digits and the IFSC.
    /// </summary>
    [Fact]
    public async Task IVendorPayouts_names_each_unpayable_reason_and_never_a_full_account_number()
    {
        SkipWithoutDocker();

        // Reaching "active with no verified bank account at all" needs the readiness gate turned
        // off: under the store's default settings a seller cannot activate without one, which is
        // itself part of what makes the ledger trustworthy. This lets the test reach the state
        // IVendorPayouts is asked about without contradicting that gate.
        Factory.Overrides["Vendors:RequireVerifiedBankAccount"] = "false";

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var noAccountVendor = await sellers.ApplyAsync();
        var noAccountId = noAccountVendor.GetProperty("id").GetGuid();
        var planId = await sellers.CommissionPlanAsync();
        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/vendors/{noAccountId}/commission-plan", new { planId }, Cancellation));
        await sellers.KycDocumentAsync(noAccountId, "Pan");
        await sellers.KycDocumentAsync(noAccountId, "IdentityProof");
        await sellers.PickupLocationAsync(noAccountId);
        await ReadAsync(await sellers.TransitionAsync(noAccountId, "submit"));
        await ReadAsync(await sellers.TransitionAsync(noAccountId, "approve"));
        var activatedNoAccount = await ReadAsync(await sellers.TransitionAsync(noAccountId, "activate"));
        Assert.Equal("Active", activatedNoAccount.GetProperty("status").GetString());

        var suspended = await sellers.ActiveAsync();
        await ReadAsync(await sellers.TransitionAsync(suspended.Id, "suspend", "reason"));

        using var scope = Factory.Services.CreateScope();
        var payouts = scope.ServiceProvider.GetRequiredService<IVendorPayouts>();

        var profiles = await payouts.FindManyAsync([noAccountId, suspended.Id], Cancellation);

        Assert.False(profiles[noAccountId].IsPayable);
        Assert.False(profiles[noAccountId].HasVerifiedBankAccount);
        Assert.Contains(
            "no verified bank account",
            profiles[noAccountId].NotPayableReason,
            StringComparison.OrdinalIgnoreCase);

        Assert.False(profiles[suspended.Id].IsPayable);
        Assert.Contains("not active", profiles[suspended.Id].NotPayableReason, StringComparison.OrdinalIgnoreCase);

        // A payable seller's own verified account — check that the profile carries only the last
        // four digits of it, never the full number the module holds encrypted.
        var payableVendor = await sellers.ApplyAsync();
        var payableId = payableVendor.GetProperty("id").GetGuid();
        var payablePlanId = await sellers.CommissionPlanAsync();
        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/vendors/{payableId}/commission-plan", new { planId = payablePlanId }, Cancellation));
        await sellers.KycDocumentAsync(payableId, "Pan");
        await sellers.KycDocumentAsync(payableId, "IdentityProof");

        var fullAccountNumber = $"9{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";
        var account = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{payableId}/bank-accounts",
            new
            {
                accountName = "Payable Seller",
                accountNumber = fullAccountNumber,
                ifsc = "HDFC0001234",
                bankName = "HDFC Bank",
                branchName = "Test Branch",
                makePrimary = true,
            },
            Cancellation));
        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{payableId}/bank-accounts/{account.GetProperty("id").GetGuid()}/verify",
            new { verified = true, note = (string?)null },
            Cancellation));

        await sellers.PickupLocationAsync(payableId);
        await ReadAsync(await sellers.TransitionAsync(payableId, "submit"));
        await ReadAsync(await sellers.TransitionAsync(payableId, "approve"));
        await ReadAsync(await sellers.TransitionAsync(payableId, "activate"));

        var profile = await payouts.FindAsync(payableId, Cancellation);

        Assert.NotNull(profile);
        Assert.True(profile!.IsPayable);
        Assert.NotNull(profile.BankAccountLast4);
        Assert.Equal(4, profile.BankAccountLast4!.Length);
        Assert.Equal(fullAccountNumber[^4..], profile.BankAccountLast4);

        // The record's own shape is part of the proof: VendorPayoutProfile has no field the full
        // number could travel in, so there is nowhere for it to leak from even by accident.
        Assert.DoesNotContain("AccountNumber", typeof(VendorPayoutProfile).GetProperties().Select(p => p.Name));
    }
}
