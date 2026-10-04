using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Infrastructure.Security;
using KlaraHome.Modules.Vendors.Application;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Vendors.Infrastructure.Seeding;

/// <summary>
/// The one seller the demonstration catalogue is offered by.
/// </summary>
/// <remarks>
/// <para>
/// A demonstration product needs a seller, and not for form's sake: the storefront's buy box drops
/// any offer whose vendor the directory does not know or reports as unable to trade
/// (<c>StorefrontCatalogService.OffersAsync</c>). A catalogue seeded without an active vendor is a
/// catalogue of products that all render at "no offers available", which looks like a bug in the
/// product page rather than a gap in the data.
/// </para>
/// <para>
/// It is walked through the real life cycle — applied, under review, approved, active — rather than
/// constructed at <see cref="VendorStatus.Active"/>. That is four extra lines and it is worth them:
/// a row that arrived in a state the state machine could not have produced is a row that will
/// eventually disagree with an invariant somebody wrote later.
/// </para>
/// <para>
/// **It is also made genuinely ready to trade**, not merely marked active. The readiness panel on
/// the seller page (<c>VendorReadinessService</c>) is computed from the seller's PAN, commission
/// plan, verified documents, a primary verified bank account and an active pickup location, so a
/// seller activated without them reads "Not ready to trade yet" beside a green Active badge - a
/// contradiction a reviewer cannot tell from a defect. Every one of those is recorded here, each
/// only when it is missing, so the step is safe on a database seeded by an earlier version.
/// </para>
/// <para>
/// What is invented, so nobody mistakes it for something checked: the PAN, the account number and
/// the document scans are fictitious, and each "scan" is a media-file id that points at no file (the
/// media link is not a foreign key). The documents carry <c>Verified</c> because that is the state
/// the readiness rule reads, not because a person looked at anything.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="reference">Names the state the demonstration pickup point is in.</param>
/// <param name="protector">Encrypts the fictitious account number the way a real one is.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Says what readiness was filled in, and what could not be.</param>
internal sealed partial class DemoVendorSeeder(
    VendorsDbContext context,
    IReferenceData reference,
    IFieldProtector protector,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DemoVendorSeeder> logger) : IDataSeeder
{
    /// <summary>A fictitious company PAN (the fourth character is the entity class, <c>C</c>).</summary>
    private const string DemoPan = "AAACK1234F";

    /// <summary>The GST state code of Rajasthan, where the demonstration seller is based.</summary>
    private const string DemoStateCode = "08";

    /// <summary>The seller's code, and the natural key this seeder is idempotent on.</summary>
    public const string DemoVendorCode = "DEMO-ATELIER";

    /// <inheritdoc />
    public string Name => "Vendors.DemoVendor";

    /// <summary>
    /// After every bootstrap seeder, and before the demonstration catalogue that needs this seller
    /// to exist.
    /// </summary>
    public int Order => 900;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The registration helper already refused outside a non-production host; this is the second
        // fence, and it is here because registration and execution can drift — a container started
        // with a stale ASPNETCORE_ENVIRONMENT is exactly the case that would otherwise get through.
        if (environment.IsProduction())
        {
            return;
        }

        var existing = await context.Vendors
            .FirstOrDefaultAsync(vendor => vendor.Code == DemoVendorCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await EnsureReadyToTradeAsync(existing, cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = clock.UtcNow;

        var vendor = Vendor.Apply(
            DemoVendorCode,
            "Klara Atelier Home Furnishings Private Limited",
            "Klara Atelier",
            "klara-atelier",
            VendorBusinessType.PrivateLimited);

        vendor.UpdateProfile(
            "Klara Atelier",
            "Hand-finished textiles, stoneware and lighting, made in small batches across "
            + "Jaipur, Channapatna and the Nilgiris.",
            logoFileId: null,
            bannerFileId: null,
            supportEmail: "care@klara-atelier.example",
            supportPhone: "1800 000 0000");

        vendor.UpdateOperations(
            dispatchSlaHours: 24,
            new ReturnPolicy
            {
                AcceptsReturns = true,
                WindowDays = 7,
                AcceptsExchanges = true,
                CustomerPaysReturnShipping = false,
            },
            servesAllIndia: true);

        // A rating, because the buy box ranks on it and a null one is the less interesting of the
        // two cases to look at while reviewing the design.
        vendor.RecordRating(4.6m);

        vendor.TransitionTo(VendorStatus.UnderReview, now);
        vendor.TransitionTo(VendorStatus.Approved, now);
        vendor.TransitionTo(VendorStatus.Active, now);

        context.Vendors.Add(vendor);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await EnsureReadyToTradeAsync(vendor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records whatever the readiness rule asks for that the seller does not yet have.
    /// </summary>
    /// <remarks>
    /// Each part is checked and filled on its own, so a half-seeded database is completed rather than
    /// duplicated, and a seller somebody has since edited by hand keeps what they put there: a PAN is
    /// only written when blank, a plan only when none is assigned, a document only when no document of
    /// that type exists, an account or a pickup point only when there is none.
    /// </remarks>
    private async Task EnsureReadyToTradeAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var stateId = await reference.StateIdAsync(DemoStateCode, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(vendor.Pan))
        {
            vendor.DescribeBusiness(
                vendor.LegalName,
                vendor.BusinessType,
                DemoPan,
                vendor.Gstin,
                stateId is { } registeredIn
                    ? new RegisteredAddress
                    {
                        Line1 = "14, Bani Park",
                        Line2 = "Near Collectorate Circle",
                        City = "Jaipur",
                        StateId = registeredIn,
                        Pincode = "302016",
                    }
                    : vendor.RegisteredAddress);
        }

        if (vendor.CommissionPlanId is null)
        {
            var plan = await context.CommissionPlans
                .AsNoTracking()
                .Where(candidate => candidate.Code == CommissionPlanSeeder.DefaultPlanCode)
                .Select(candidate => (Guid?)candidate.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            vendor.AssignCommissionPlan(plan);
        }

        var documents = await context.KycDocuments
            .Where(document => document.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var type in VendorReadinessService.RequiredDocuments(vendor))
        {
            var present = documents.Find(document => document.DocumentType == type);

            if (present is { Status: KycVerificationStatus.Verified })
            {
                continue;
            }

            if (present is null)
            {
                present = VendorKycDocument.Submit(
                    vendor.Id,
                    type,
                    UuidV7.New(),
                    type == KycDocumentType.Pan ? DemoPan : null);

                context.KycDocuments.Add(present);
            }

            present.Verify(verifiedBy: null, now);
        }

        var hasAccount = await context.BankAccounts
            .AnyAsync(account => account.VendorId == vendor.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!hasAccount)
        {
            if (protector.IsConfigured)
            {
                const string accountNumber = "000123456789";

                var account = VendorBankAccount.Add(
                    vendor.Id,
                    vendor.LegalName,
                    protector.Protect(accountNumber),
                    VendorBankAccount.Last4(accountNumber),
                    "HDFC0000123");

                account.Describe("HDFC Bank", "Jaipur - Bani Park");
                account.SetPrimary(true);
                account.RecordVerification(BankVerificationStatus.Verified, now);

                context.BankAccounts.Add(account);
            }
            else
            {
                NoEncryptionKey(logger);
            }
        }

        var hasPickup = await context.PickupLocations
            .AnyAsync(location => location.VendorId == vendor.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!hasPickup)
        {
            if (stateId is { } pickupState)
            {
                var pickup = VendorPickupLocation.Add(
                    vendor.Id,
                    "Jaipur workshop",
                    "Klara Atelier dispatch",
                    "+919999900000",
                    "14, Bani Park",
                    "Jaipur",
                    pickupState,
                    "302016");

                pickup.SetDefault(true);
                pickup.SetActive(true);

                context.PickupLocations.Add(pickup);
            }
            else
            {
                NoStateReferenceData(logger);
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            ReadinessRecorded(logger, vendor.Code);
        }
    }

    [LoggerMessage(
        EventId = 9108,
        Level = LogLevel.Information,
        Message = "Recorded the PAN, plan, verified documents, bank account and pickup point the demonstration seller {Code} needs to be ready to trade.")]
    private static partial void ReadinessRecorded(ILogger logger, string code);

    [LoggerMessage(
        EventId = 9109,
        Level = LogLevel.Warning,
        Message = "No column-encryption key is configured, so the demonstration seller has no bank account and will read as not ready to trade.")]
    private static partial void NoEncryptionKey(ILogger logger);

    [LoggerMessage(
        EventId = 9110,
        Level = LogLevel.Warning,
        Message = "The state reference data is not seeded, so the demonstration seller has no pickup location and will read as not ready to trade.")]
    private static partial void NoStateReferenceData(ILogger logger);
}
