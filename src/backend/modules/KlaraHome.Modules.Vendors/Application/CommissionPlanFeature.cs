using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Vendors.Application.Validation;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Commission;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>One override within a plan, as the API states and accepts it.</summary>
/// <param name="CategoryId">The category it applies to, or null for any.</param>
/// <param name="MinPrice">Lowest unit price, inclusive.</param>
/// <param name="MaxPrice">Unit price it stops applying at, exclusive.</param>
/// <param name="Rate">The commission rate as a percentage.</param>
/// <param name="FixedFee">A flat fee per unit, on top of the rate.</param>
internal sealed record CommissionRulePayload(
    Guid? CategoryId,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal Rate,
    decimal FixedFee);

/// <summary>A commission plan, as the API states it.</summary>
/// <param name="Id">The plan.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PlanType">Its shape.</param>
/// <param name="DefaultRate">The rate charged when no rule matches.</param>
/// <param name="DefaultFixedFee">The fee charged when no rule matches.</param>
/// <param name="IsActive">Whether it may be assigned.</param>
/// <param name="IsDefault">Whether a new seller is put on it.</param>
/// <param name="Rules">Its overrides.</param>
/// <param name="VendorCount">How many sellers are on it.</param>
internal sealed record CommissionPlanResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    CommissionPlanType PlanType,
    decimal DefaultRate,
    decimal DefaultFixedFee,
    bool IsActive,
    bool IsDefault,
    IReadOnlyList<CommissionRulePayload> Rules,
    int VendorCount);

/// <summary>Lists the commission plans.</summary>
/// <param name="IncludeInactive">Whether to include plans that may no longer be assigned.</param>
internal sealed record ListCommissionPlansQuery(bool IncludeInactive)
    : IQuery<IReadOnlyList<CommissionPlanResponse>>;

/// <summary>Reads one plan with its rules.</summary>
/// <param name="PlanId">The plan.</param>
internal sealed record GetCommissionPlanQuery(Guid PlanId) : IQuery<CommissionPlanResponse>;

/// <summary>Creates a plan.</summary>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PlanType">Its shape.</param>
/// <param name="DefaultRate">The rate charged when no rule matches.</param>
/// <param name="DefaultFixedFee">The fee charged when no rule matches.</param>
/// <param name="Rules">Its overrides.</param>
internal sealed record CreateCommissionPlanCommand(
    string Code,
    string Name,
    string? Description,
    CommissionPlanType PlanType,
    decimal DefaultRate,
    decimal DefaultFixedFee,
    IReadOnlyList<CommissionRulePayload> Rules) : ICommand<CommissionPlanResponse>;

/// <summary>Changes a plan and replaces its rules.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PlanType">Its shape.</param>
/// <param name="DefaultRate">The rate charged when no rule matches.</param>
/// <param name="DefaultFixedFee">The fee charged when no rule matches.</param>
/// <param name="IsActive">Whether it may be assigned.</param>
/// <param name="IsDefault">Whether a new seller is put on it.</param>
/// <param name="Rules">Its overrides.</param>
internal sealed record UpdateCommissionPlanCommand(
    Guid PlanId,
    string Name,
    string? Description,
    CommissionPlanType PlanType,
    decimal DefaultRate,
    decimal DefaultFixedFee,
    bool IsActive,
    bool IsDefault,
    IReadOnlyList<CommissionRulePayload> Rules) : ICommand<CommissionPlanResponse>;

/// <summary>
/// Asks what a plan would charge for a given category and price, without a sale existing.
/// </summary>
/// <remarks>
/// The screen that edits a price-banded ladder is unreadable without one: an operator needs to be
/// able to type ₹4,999 and see which rule catches it. It is also how the resolution rule is
/// demonstrated to somebody who does not want to read the code.
/// </remarks>
/// <param name="VendorId">The seller whose plan to resolve.</param>
/// <param name="CategoryId">The category, or null.</param>
/// <param name="UnitPrice">The selling price of one unit.</param>
internal sealed record PreviewCommissionQuery(Guid VendorId, Guid? CategoryId, decimal UnitPrice)
    : IQuery<CommissionQuote>;

/// <summary>The rules a plan payload must satisfy, shared by create and update.</summary>
internal sealed class CommissionRuleValidator : AbstractValidator<CommissionRulePayload>
{
    public CommissionRuleValidator()
    {
        RuleFor(rule => rule.Rate).InclusiveBetween(0m, CommissionPlan.MaxRatePercent);
        RuleFor(rule => rule.FixedFee).GreaterThanOrEqualTo(0m);
        RuleFor(rule => rule.MinPrice!.Value).GreaterThanOrEqualTo(0m).When(rule => rule.MinPrice is not null);
        RuleFor(rule => rule.MaxPrice!.Value).GreaterThan(0m).When(rule => rule.MaxPrice is not null);

        RuleFor(rule => rule)
            .Must(rule => rule.MinPrice is null || rule.MaxPrice is null || rule.MinPrice < rule.MaxPrice)
            .WithMessage("A price band's floor must be below its ceiling, or it matches nothing at all.");
    }
}

/// <summary>Rules for a new plan.</summary>
internal sealed class CreateCommissionPlanValidator : AbstractValidator<CreateCommissionPlanCommand>
{
    public CreateCommissionPlanValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches(VendorFormats.Slug())
            .WithMessage("A plan code may contain only lowercase letters, digits and hyphens.");

        RuleFor(command => command.Name).NotEmpty().MaximumLength(120);
        RuleFor(command => command.Description!).MaximumLength(500).When(command => command.Description is not null);
        RuleFor(command => command.DefaultRate).InclusiveBetween(0m, CommissionPlan.MaxRatePercent);
        RuleFor(command => command.DefaultFixedFee).GreaterThanOrEqualTo(0m);
        RuleForEach(command => command.Rules).SetValidator(new CommissionRuleValidator());
    }
}

/// <summary>Rules for a change to a plan.</summary>
internal sealed class UpdateCommissionPlanValidator : AbstractValidator<UpdateCommissionPlanCommand>
{
    public UpdateCommissionPlanValidator()
    {
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(120);
        RuleFor(command => command.Description!).MaximumLength(500).When(command => command.Description is not null);
        RuleFor(command => command.DefaultRate).InclusiveBetween(0m, CommissionPlan.MaxRatePercent);
        RuleFor(command => command.DefaultFixedFee).GreaterThanOrEqualTo(0m);
        RuleForEach(command => command.Rules).SetValidator(new CommissionRuleValidator());

        RuleFor(command => command)
            .Must(command => command.IsActive || !command.IsDefault)
            .WithMessage("An inactive plan cannot be the default — a new seller would be put on a plan "
                         + "nobody is allowed to assign.");
    }
}

/// <summary>Lists the plans, with how many sellers are on each.</summary>
/// <param name="context">The Vendors data context.</param>
internal sealed class ListCommissionPlansQueryHandler(VendorsDbContext context)
    : IQueryHandler<ListCommissionPlansQuery, IReadOnlyList<CommissionPlanResponse>>
{
    public async Task<Result<IReadOnlyList<CommissionPlanResponse>>> HandleAsync(
        ListCommissionPlansQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var plans = context.CommissionPlans.AsNoTracking().Include(plan => plan.Rules).AsQueryable();

        if (!query.IncludeInactive)
        {
            plans = plans.Where(plan => plan.IsActive);
        }

        var ordered = await plans
            .OrderByDescending(plan => plan.IsDefault)
            .ThenBy(plan => plan.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // One grouped count for every plan rather than a count per plan. The list is short, but the
        // per-plan form is the query that becomes a problem the day somebody creates thirty plans.
        var counts = await context.Vendors
            .AsNoTracking()
            .Where(vendor => vendor.CommissionPlanId != null)
            .GroupBy(vendor => vendor.CommissionPlanId!.Value)
            .Select(group => new { PlanId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.PlanId, entry => entry.Count, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<CommissionPlanResponse>>(
            ordered.ConvertAll(plan => CommissionProjection.ToResponse(
                plan,
                counts.GetValueOrDefault(plan.Id))));
    }
}

/// <summary>Reads one plan.</summary>
/// <param name="context">The Vendors data context.</param>
internal sealed class GetCommissionPlanQueryHandler(VendorsDbContext context)
    : IQueryHandler<GetCommissionPlanQuery, CommissionPlanResponse>
{
    public async Task<Result<CommissionPlanResponse>> HandleAsync(
        GetCommissionPlanQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var plan = await context.CommissionPlans
            .AsNoTracking()
            .Include(candidate => candidate.Rules)
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PlanId, cancellationToken)
            .ConfigureAwait(false);

        if (plan is null)
        {
            return VendorErrors.PlanNotFound;
        }

        var count = await context.Vendors
            .AsNoTracking()
            .CountAsync(vendor => vendor.CommissionPlanId == plan.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(CommissionProjection.ToResponse(plan, count));
    }
}

/// <summary>Creates a plan.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Refuses a vendor caller.</param>
/// <param name="audit">Records it. A plan decides money.</param>
internal sealed class CreateCommissionPlanCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<CreateCommissionPlanCommand, CommissionPlanResponse>
{
    /// <summary>The audited action for a new plan.</summary>
    public const string AuditAction = "vendors.commission-plan.created";

    /// <summary>The entity type recorded against a plan action.</summary>
    public const string AuditEntityType = "CommissionPlan";

    public async Task<Result<CommissionPlanResponse>> HandleAsync(
        CreateCommissionPlanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var code = command.Code.Trim().ToLowerInvariant();

        var taken = await context.CommissionPlans
            .AnyAsync(plan => plan.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Error.Conflict("COMMISSION_PLAN_EXISTS", "A plan already uses that code.");
        }

        var plan = CommissionPlan.Create(code, command.Name.Trim(), command.PlanType, command.DefaultRate);

        plan.Update(
            command.Name.Trim(),
            command.Description,
            command.PlanType,
            command.DefaultRate,
            Money.Rupees(command.DefaultFixedFee));

        plan.ReplaceRules(CommissionProjection.ToRules(plan.Id, command.Rules));

        context.CommissionPlans.Add(plan);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = plan.Id.ToString(),
                After = CommissionProjection.ToAudit(plan),
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(CommissionProjection.ToResponse(plan, vendorCount: 0));
    }
}

/// <summary>Changes a plan and replaces its rules.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Refuses a vendor caller.</param>
/// <param name="audit">Records the before and after. This is a rate change.</param>
internal sealed class UpdateCommissionPlanCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<UpdateCommissionPlanCommand, CommissionPlanResponse>
{
    /// <summary>The audited action for a change to a plan.</summary>
    public const string AuditAction = "vendors.commission-plan.updated";

    public async Task<Result<CommissionPlanResponse>> HandleAsync(
        UpdateCommissionPlanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var plan = await context.CommissionPlans
            .Include(candidate => candidate.Rules)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PlanId, cancellationToken)
            .ConfigureAwait(false);

        if (plan is null)
        {
            return VendorErrors.PlanNotFound;
        }

        var before = CommissionProjection.ToAudit(plan);

        // Retiring the plan sellers are already on would leave them charged by a plan nobody can
        // assign, which is legal but is never what the operator meant.
        if (!command.IsActive && plan.IsActive)
        {
            var inUse = await context.Vendors
                .AnyAsync(vendor => vendor.CommissionPlanId == plan.Id, cancellationToken)
                .ConfigureAwait(false);

            if (inUse)
            {
                return Error.Conflict(
                    "COMMISSION_PLAN_IN_USE",
                    "Sellers are still on this plan. Move them to another one before retiring it.");
            }
        }

        plan.Update(
            command.Name.Trim(),
            command.Description,
            command.PlanType,
            command.DefaultRate,
            Money.Rupees(command.DefaultFixedFee));

        plan.SetActive(command.IsActive);

        if (command.IsDefault && !plan.IsDefault)
        {
            // At most one default, and the database says so too. Clearing the others here is what
            // stops that constraint from being the way an operator finds out.
            var others = await context.CommissionPlans
                .Where(candidate => candidate.IsDefault && candidate.Id != plan.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var other in others)
            {
                other.SetDefault(false);
            }
        }

        plan.SetDefault(command.IsDefault);

        context.CommissionPlanRules.RemoveRange(plan.Rules);
        plan.ReplaceRules(CommissionProjection.ToRules(plan.Id, command.Rules));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateCommissionPlanCommandHandler.AuditEntityType,
                EntityId = plan.Id.ToString(),
                Before = before,
                After = CommissionProjection.ToAudit(plan),
            },
            cancellationToken).ConfigureAwait(false);

        var count = await context.Vendors
            .CountAsync(vendor => vendor.CommissionPlanId == plan.Id, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(CommissionProjection.ToResponse(plan, count));
    }
}

/// <summary>Answers what a seller's plan would charge, without a sale existing.</summary>
/// <param name="resolver">The one implementation of the resolution rule.</param>
internal sealed class PreviewCommissionQueryHandler(ICommissionResolver resolver)
    : IQueryHandler<PreviewCommissionQuery, CommissionQuote>
{
    public async Task<Result<CommissionQuote>> HandleAsync(
        PreviewCommissionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var quote = await resolver
            .ResolveAsync(query.VendorId, query.CategoryId, query.UnitPrice, cancellationToken)
            .ConfigureAwait(false);

        return quote is null
            ? VendorErrors.NotReady("That seller has no commission plan assigned.")
            : Result.Success(quote);
    }
}

/// <summary>Maps plans and their rules between the API and the domain.</summary>
internal static class CommissionProjection
{
    /// <summary>Builds the response.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="vendorCount">How many sellers are on it.</param>
    public static CommissionPlanResponse ToResponse(CommissionPlan plan, int vendorCount)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new CommissionPlanResponse(
            plan.Id,
            plan.Code,
            plan.Name,
            plan.Description,
            plan.PlanType,
            plan.DefaultRate,
            plan.DefaultFixedFee.Amount,
            plan.IsActive,
            plan.IsDefault,
            [.. plan.Rules.Select(rule => new CommissionRulePayload(
                rule.CategoryId,
                rule.MinPrice,
                rule.MaxPrice,
                rule.Rate,
                rule.FixedFee.Amount))],
            vendorCount);
    }

    /// <summary>Builds the rule entities for a plan.</summary>
    /// <param name="planId">The plan.</param>
    /// <param name="payloads">The rules the caller sent.</param>
    public static List<CommissionPlanRule> ToRules(Guid planId, IReadOnlyList<CommissionRulePayload> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        return [.. payloads.Select(payload => CommissionPlanRule.Create(
            planId,
            payload.CategoryId,
            payload.MinPrice,
            payload.MaxPrice,
            payload.Rate,
            Money.Rupees(payload.FixedFee)))];
    }

    /// <summary>
    /// The shape written to the audit trail — the whole plan, rules included.
    /// </summary>
    /// <remarks>
    /// A rate change with no record of the rules is unauditable: "the plan was edited" does not
    /// answer what a seller was charged last March, which is the question a commission dispute asks.
    /// </remarks>
    /// <param name="plan">The plan.</param>
    public static object ToAudit(CommissionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new
        {
            plan.Code,
            plan.Name,
            planType = plan.PlanType.ToString(),
            plan.DefaultRate,
            defaultFixedFee = plan.DefaultFixedFee.Amount,
            plan.IsActive,
            plan.IsDefault,
            rules = plan.Rules
                .Select(rule => new
                {
                    rule.CategoryId,
                    rule.MinPrice,
                    rule.MaxPrice,
                    rule.Rate,
                    fixedFee = rule.FixedFee.Amount,
                })
                .ToList(),
        };
    }
}
