using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Pricing.Application.Tax;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>Query-string filters for the tax-rate listing.</summary>
/// <param name="HsnCode">Restrict to one code, to see its whole history.</param>
/// <param name="ActiveOnly">Hide superseded rows.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record TaxRateFilter(string? HsnCode, bool? ActiveOnly, string? Cursor, int? Size);

/// <summary>The body of a new tax rate.</summary>
/// <param name="HsnCode">The HSN code.</param>
/// <param name="Description">What the code covers.</param>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last day it applies, or null while it is current.</param>
internal sealed record CreateTaxRateBody(
    string HsnCode,
    string? Description,
    decimal Rate,
    decimal CessRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>The body of a change to a tax rate.</summary>
/// <param name="Description">What the code covers.</param>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last day it applies.</param>
/// <param name="IsActive">Whether it is considered.</param>
internal sealed record UpdateTaxRateBody(
    string? Description,
    decimal Rate,
    decimal CessRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

/// <summary>
/// The GST rate surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Platform-wide and not vendor-scoped: a GST rate is the law's, not a seller's, and a marketplace
/// where two sellers charged different tax on the same HSN would be one with a filing problem.
/// </remarks>
internal static class AdminTaxRateEndpoints
{
    /// <summary>Maps the GST rate surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminTaxRateEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var rates = admin.MapGroup("/tax-rates").WithTags("Pricing");

        rates.MapGet("/", async ([AsParameters] TaxRateFilter filter, IDispatcher dispatcher, HttpContext context) =>
            {
                var query = new ListTaxRatesQuery(filter.HsnCode, filter.ActiveOnly, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTaxRatesList")
            .WithSummary("Lists GST rates. Filter by HSN code to read one code's whole history.")
            .RequirePermission(PricingPermissions.TaxRateRead)
            .Produces<PagedResult<TaxRateResponse>>();

        rates.MapGet("/resolve", async (
                string hsnCode,
                DateOnly? asOf,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ResolveTaxRateQuery(hsnCode, asOf), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTaxRateResolve")
            .WithSummary("The rate in force for a code on a date. The same resolver the quote engine uses.")
            .RequirePermission(PricingPermissions.TaxRateRead)
            .Produces<TaxRateResolutionResponse>();

        rates.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetTaxRateQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTaxRateGet")
            .WithSummary("Reads one GST rate.")
            .RequirePermission(PricingPermissions.TaxRateRead)
            .Produces<TaxRateResponse>();

        rates.MapPost("/", async (CreateTaxRateBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateTaxRateCommand(
                    body.HsnCode,
                    body.Description,
                    body.Rate,
                    body.CessRate,
                    body.EffectiveFrom,
                    body.EffectiveTo);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    rate => Results.Created($"{context.Request.Path}/{rate.Id}", rate),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminTaxRateCreate")
            .WithSummary("Records a GST rate for an HSN code from a date. A rate change is a new row.")
            .RequirePermission(PricingPermissions.TaxRateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<TaxRateResponse>(StatusCodes.Status201Created);

        rates.MapPut("/{id:guid}", async (
                Guid id,
                UpdateTaxRateBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateTaxRateCommand(
                    id,
                    body.Description,
                    body.Rate,
                    body.CessRate,
                    body.EffectiveFrom,
                    body.EffectiveTo,
                    body.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminTaxRateUpdate")
            .WithSummary("Amends a GST rate. Audited with its before and after, because a filing may turn on it.")
            .RequirePermission(PricingPermissions.TaxRateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<TaxRateResponse>();

        rates.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteTaxRateCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminTaxRateDelete")
            .WithSummary("Removes a GST rate that was entered in error. Close the window instead where it was used.")
            .RequirePermission(PricingPermissions.TaxRateManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        return admin;
    }
}
