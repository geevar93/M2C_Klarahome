using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

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
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class DemoVendorSeeder(
    VendorsDbContext context,
    IHostEnvironment environment,
    IClock clock) : IDataSeeder
{
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
    }
}
