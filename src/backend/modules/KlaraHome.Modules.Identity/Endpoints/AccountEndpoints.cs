using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Identity.Application.Account;
using KlaraHome.Modules.Identity.Application.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Identity.Endpoints;

/// <summary>The body of a profile update.</summary>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="DateOfBirth">Date of birth.</param>
/// <param name="Gender">Self-declared gender.</param>
/// <param name="Gstin">Default GSTIN for B2B invoices.</param>
/// <param name="MarketingConsent">Whether marketing is opted into.</param>
internal sealed record UpdateMeBody(
    string? FirstName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Gstin,
    bool MarketingConsent);

/// <summary>
/// The caller's own account: who they are, their addresses, their devices and their second factor
/// (docs/04-api-specification.md §3.1).
/// </summary>
/// <remarks>
/// Every endpoint here requires authentication and nothing more. There is no permission to hold:
/// the resource is the caller, and the scope is enforced by reading the subject from the token
/// rather than from a path parameter — which is what makes an IDOR here impossible to write.
/// </remarks>
internal static class AccountEndpoints
{
    /// <summary>Maps the account surface beneath a surface group.</summary>
    /// <param name="surface">The <c>/store</c> or <c>/admin</c> group.</param>
    /// <param name="namePrefix">Prefixes the operation ids; the two surfaces map the same routes.</param>
    /// <param name="includeAddresses">
    /// Whether to map the address book. Addresses belong to a shopper, so the admin surface — where
    /// the caller is staff or a vendor user — does not carry them.
    /// </param>
    public static IEndpointRouteBuilder MapAccountEndpoints(
        this IEndpointRouteBuilder surface,
        string namePrefix,
        bool includeAddresses)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var group = surface
            .MapGroup("/me")
            .WithTags("Identity")
            .RequireAuthorization();

        MapProfile(group, namePrefix);
        MapSessions(group, namePrefix);
        MapTwoFactor(group, namePrefix);

        if (includeAddresses)
        {
            MapAddresses(group, namePrefix);
        }

        return surface;
    }

    private static void MapProfile(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetMeQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeGet")
            .WithSummary("The caller's account: identity, roles, permissions and shopper profile.")
            .Produces<MeResponse>();

        group.MapPatch("/", async (UpdateMeBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new UpdateMeCommand(
                    body.FirstName,
                    body.LastName,
                    body.DateOfBirth,
                    body.Gender,
                    body.Gstin,
                    body.MarketingConsent);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName(namePrefix + "MePatch")
            .WithSummary("Updates the caller's own details and marketing consent.")
            .Produces<MeResponse>();

        group.MapPost("/verify/request", async (
                VerificationRequestBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RequestVerificationCommand(body.Channel), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeVerifyRequest")
            .WithSummary("Sends a code or link proving the caller owns their email address or mobile number.")
            .RequireRateLimiting(RateLimitPolicies.Otp)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/verify/confirm", async (
                VerificationConfirmBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new ConfirmVerificationCommand(body.Channel, body.Code);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeVerifyConfirm")
            .WithSummary("Marks an email address or mobile number verified.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapSessions(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapGet("/sessions", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSessionsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeSessionsGet")
            .WithSummary("The caller's signed-in devices, with the current one flagged.")
            .Produces<IReadOnlyList<SessionResponse>>();

        group.MapDelete("/sessions/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RevokeSessionCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeSessionRevoke")
            .WithSummary("Signs one device out.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapDelete("/sessions", async (
                bool? exceptCurrent,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new RevokeAllSessionsCommand(exceptCurrent ?? true);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeSessionsRevokeAll")
            .WithSummary("Signs every device out. Keeps the current session unless exceptCurrent is false.")
            .Produces<RevokedSessionsResponse>();
    }

    private static void MapTwoFactor(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapPost("/2fa/setup", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new StartTwoFactorSetupCommand(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeTwoFactorSetup")
            .WithSummary("Returns a new authenticator secret, staged but not yet in force.")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<TwoFactorSetupResponse>();

        group.MapPost("/2fa/enable", async (
                EnableTwoFactorBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new EnableTwoFactorCommand(body.Code), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeTwoFactorEnable")
            .WithSummary("Turns on the staged second factor, once it has produced a valid code.")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/2fa/disable", async (
                DisableTwoFactorBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new DisableTwoFactorCommand(body.Password, body.Code);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeTwoFactorDisable")
            .WithSummary("Removes the second factor. Refused for roles that require one.")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapAddresses(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapGet("/addresses", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetAddressesQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeAddressesGet")
            .WithSummary("The caller's saved addresses, the default shipping one first.")
            .Produces<IReadOnlyList<AddressResponse>>();

        group.MapPost("/addresses", async (
                AddressInput body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new CreateAddressCommand(body), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeAddressCreate")
            .WithSummary("Saves an address. The first one saved becomes both defaults.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<AddressResponse>();

        group.MapPut("/addresses/{id:guid}", async (
                Guid id,
                AddressInput body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new UpdateAddressCommand(id, body), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeAddressUpdate")
            .WithSummary("Replaces one address. Addresses are edited whole, never patched.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<AddressResponse>();

        group.MapDelete("/addresses/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new DeleteAddressCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeAddressDelete")
            .WithSummary("Removes an address. Orders already placed to it are unaffected.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces(StatusCodes.Status204NoContent);
    }
}
