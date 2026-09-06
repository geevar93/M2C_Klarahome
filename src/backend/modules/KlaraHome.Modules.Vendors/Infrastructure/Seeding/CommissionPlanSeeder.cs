using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Seeding;

/// <summary>
/// Creates the one commission plan a deployment cannot start without.
/// </summary>
/// <remarks>
/// <para>
/// A marketplace with no plan cannot onboard anybody: activation requires an assignment, and there
/// would be nothing to assign. So one plan exists from the first deploy — a flat percentage, marked
/// as the default, which a new seller is put on when nobody chooses.
/// </para>
/// <para>
/// Idempotent in the way that matters for money: the row is created if it is absent, and then left
/// alone. Reasserting the rate on every deploy would silently undo an operator's commercial
/// decision, and would do it at deploy time, when nobody is looking at the commission screen.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class CommissionPlanSeeder(VendorsDbContext context) : IDataSeeder
{
    /// <summary>The code of the plan every deployment starts with.</summary>
    public const string DefaultPlanCode = "standard";

    /// <summary>
    /// The rate the shipped plan charges, as a percentage.
    /// </summary>
    /// <remarks>
    /// A placeholder, and a deliberately unremarkable one. The real number is a commercial decision
    /// the operator makes in the admin surface before their first seller goes live; what this
    /// guarantees is only that the screen has something to show and the seeder does not have to
    /// invent a number every deploy.
    /// </remarks>
    public const decimal DefaultRatePercent = 10m;

    /// <inheritdoc />
    public string Name => "Vendors.CommissionPlans";

    /// <summary>After the Platform and Identity seeders; nothing here depends on them.</summary>
    public int Order => 50;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var exists = await context.CommissionPlans
            .AnyAsync(plan => plan.Code == DefaultPlanCode, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var plan = CommissionPlan.Create(
            DefaultPlanCode,
            "Standard commission",
            CommissionPlanType.Percentage,
            DefaultRatePercent);

        plan.Update(
            plan.Name,
            "The plan a seller is put on when no other has been chosen. Edit the rate before your "
            + "first seller goes live.",
            CommissionPlanType.Percentage,
            DefaultRatePercent,
            plan.DefaultFixedFee);

        plan.SetDefault(true);

        context.CommissionPlans.Add(plan);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
