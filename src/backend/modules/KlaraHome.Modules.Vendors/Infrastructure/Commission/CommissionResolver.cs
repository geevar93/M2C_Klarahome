using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Infrastructure.Commission;

/// <summary>
/// Answers <see cref="ICommissionResolver"/>: loads the seller's plan with its rules and asks the
/// plan itself what applies.
/// </summary>
/// <remarks>
/// <para>
/// The arithmetic is deliberately not here. <see cref="CommissionPlan.Resolve"/> owns it, on the
/// aggregate, so the rule that decides money is a pure function over the plan that a unit test can
/// drive without a database — and so that this class stays what it is, which is a loader.
/// </para>
/// <para>
/// Rules are loaded rather than filtered in SQL. A plan has a handful of them, the specificity
/// comparison is not something SQL should be asked to express, and the alternative is a query whose
/// ORDER BY has to stay in step with a C# comparer — which is the shape of bug that shows up as a
/// rate that was right in testing.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
internal sealed class CommissionResolver(VendorsDbContext context) : ICommissionResolver
{
    /// <inheritdoc />
    public async ValueTask<CommissionQuote?> ResolveAsync(
        Guid vendorId,
        Guid? categoryId,
        decimal unitPrice,
        CancellationToken cancellationToken = default)
    {
        var planId = await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendor.Id == vendorId)
            .Select(vendor => vendor.CommissionPlanId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (planId is null)
        {
            // No seller, or a seller nobody has put on a plan. Null rather than a zero-rate quote:
            // a settlement that charged nothing because of a missing assignment must be a visible
            // failure, not a generous one.
            return null;
        }

        var plan = await LoadAsync(planId.Value, cancellationToken).ConfigureAwait(false);

        if (plan is null)
        {
            return null;
        }

        var (rate, fee, matched) = plan.Resolve(categoryId, unitPrice);

        return new CommissionQuote(plan.Id, plan.Name, rate, fee, matched);
    }

    /// <summary>Loads a plan with its rules, untracked.</summary>
    /// <param name="planId">The plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<CommissionPlan?> LoadAsync(Guid planId, CancellationToken cancellationToken)
        => await context.CommissionPlans
            .AsNoTracking()
            .Include(plan => plan.Rules)
            .FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken)
            .ConfigureAwait(false);
}
