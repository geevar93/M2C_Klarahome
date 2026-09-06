using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Pricing.Application.Wallets;
using KlaraHome.Modules.Pricing.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Pricing.Endpoints;

/// <summary>The body of a manual wallet adjustment.</summary>
/// <param name="Amount">Signed. Positive credits, negative debits.</param>
/// <param name="Reason">Why, from the store-credit reason list.</param>
/// <param name="Note">Free text shown to the customer on their statement.</param>
/// <param name="ExpiresAt">When the credit lapses, for a credit that does.</param>
internal sealed record AdjustWalletBody(decimal Amount, string Reason, string? Note, DateTimeOffset? ExpiresAt);

/// <summary>
/// The store-credit surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// The whole group is gated on <c>pricing.store-credit</c>, so a deployment that has not enabled
/// loyalty answers 404 rather than exposing a balance nobody can spend. The adjustment endpoint is
/// separately permissioned because it is the one route on this platform that creates money.
/// </remarks>
internal static class AdminWalletEndpoints
{
    /// <summary>Maps the store-credit surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminWalletEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var wallets = admin
            .MapGroup("/wallets")
            .WithTags("Pricing")
            .RequireFeature(PricingFeatureFlags.StoreCredit);

        wallets.MapGet("/", async (
                Guid? customerId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListWalletsQuery(customerId, cursor, size), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWalletsList")
            .WithSummary("Lists store-credit wallets.")
            .RequirePermission(PricingPermissions.WalletRead)
            .Produces<PagedResult<WalletResponse>>();

        wallets.MapGet("/{customerId:guid}", async (
                Guid customerId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetWalletQuery(customerId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWalletGet")
            .WithSummary("Reads one customer's store-credit balance.")
            .RequirePermission(PricingPermissions.WalletRead)
            .Produces<WalletResponse>();

        wallets.MapGet("/{customerId:guid}/transactions", async (
                Guid customerId,
                string? cursor,
                int? size,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListWalletTransactionsQuery(customerId, cursor, size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWalletTransactions")
            .WithSummary("One customer's store-credit statement, newest first.")
            .RequirePermission(PricingPermissions.WalletRead)
            .Produces<PagedResult<WalletTransactionResponse>>();

        wallets.MapPost("/{customerId:guid}/adjust", async (
                Guid customerId,
                AdjustWalletBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AdjustWalletCommand(
                    customerId,
                    body.Amount,
                    body.Reason,
                    body.Note,
                    body.ExpiresAt);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminWalletAdjust")
            .WithSummary("Credits or debits a customer's store credit by hand. Audited with the actor.")
            .RequirePermission(PricingPermissions.WalletAdjust)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<WalletResponse>();

        return admin;
    }
}
