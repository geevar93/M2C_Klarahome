using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Vendors.Application;
using KlaraHome.Modules.Vendors.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Vendors.Endpoints;

/// <summary>The body of a new commission plan.</summary>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PlanType">Its shape.</param>
/// <param name="DefaultRate">The rate charged when no rule matches.</param>
/// <param name="DefaultFixedFee">The fee charged when no rule matches.</param>
/// <param name="Rules">Its overrides.</param>
internal sealed record CreateCommissionPlanBody(
    string Code,
    string Name,
    string? Description,
    CommissionPlanType PlanType,
    decimal DefaultRate,
    decimal DefaultFixedFee,
    IReadOnlyList<CommissionRulePayload> Rules);

/// <summary>The body of a change to a commission plan.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it is for.</param>
/// <param name="PlanType">Its shape.</param>
/// <param name="DefaultRate">The rate charged when no rule matches.</param>
/// <param name="DefaultFixedFee">The fee charged when no rule matches.</param>
/// <param name="IsActive">Whether it may be assigned.</param>
/// <param name="IsDefault">Whether a new seller is put on it.</param>
/// <param name="Rules">Its overrides, which replace the existing set.</param>
internal sealed record UpdateCommissionPlanBody(
    string Name,
    string? Description,
    CommissionPlanType PlanType,
    decimal DefaultRate,
    decimal DefaultFixedFee,
    bool IsActive,
    bool IsDefault,
    IReadOnlyList<CommissionRulePayload> Rules);

/// <summary>Query string for the commission preview.</summary>
/// <param name="VendorId">The seller whose plan to resolve.</param>
/// <param name="CategoryId">The category, or null.</param>
/// <param name="UnitPrice">The selling price of one unit.</param>
internal sealed record CommissionPreviewFilter(Guid VendorId, Guid? CategoryId, decimal UnitPrice);

/// <summary>
/// Commission plans (docs/04-api-specification.md §4, Vendors &amp; settlements).
/// </summary>
/// <remarks>
/// Platform staff only, in full. A seller sees what they are charged through their own record and
/// through their settlement statements; they do not get to browse the plans they are not on, and
/// they certainly do not get to edit one.
/// </remarks>
internal static class AdminCommissionPlanEndpoints
{
    /// <summary>Maps the commission-plan surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminCommissionPlanEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var plans = admin.MapGroup("/commission-plans").WithTags("Vendors");

        plans.MapGet("/", async (bool? includeInactive, IDispatcher dispatcher, HttpContext context) =>
            {
                var query = new ListCommissionPlansQuery(includeInactive ?? false);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCommissionPlansList")
            .WithSummary("Lists the commission plans, the default first, with how many sellers are on each.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .Produces<IReadOnlyList<CommissionPlanResponse>>();

        plans.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetCommissionPlanQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCommissionPlanGet")
            .WithSummary("Reads one plan with its rules.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .Produces<CommissionPlanResponse>();

        plans.MapPost("/", async (
                CreateCommissionPlanBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new CreateCommissionPlanCommand(
                    body.Code,
                    body.Name,
                    body.Description,
                    body.PlanType,
                    body.DefaultRate,
                    body.DefaultFixedFee,
                    body.Rules);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    plan => Results.Created($"{context.Request.Path}/{plan.Id}", plan),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminCommissionPlanCreate")
            .WithSummary("Creates a commission plan.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CommissionPlanResponse>(StatusCodes.Status201Created);

        plans.MapPut("/{id:guid}", async (
                Guid id,
                UpdateCommissionPlanBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateCommissionPlanCommand(
                    id,
                    body.Name,
                    body.Description,
                    body.PlanType,
                    body.DefaultRate,
                    body.DefaultFixedFee,
                    body.IsActive,
                    body.IsDefault,
                    body.Rules);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminCommissionPlanUpdate")
            .WithSummary("Changes a plan and replaces its rules. Rates already quoted to a settlement "
                         + "are unaffected.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<CommissionPlanResponse>();

        plans.MapGet("/preview", async (
                [AsParameters] CommissionPreviewFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new PreviewCommissionQuery(filter.VendorId, filter.CategoryId, filter.UnitPrice);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminCommissionPreview")
            .WithSummary("Answers what a seller would be charged for a category and a price, without a sale. "
                         + "The one way to see which rule a price-banded ladder actually catches.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .Produces<CommissionQuote>();

        return admin;
    }
}
