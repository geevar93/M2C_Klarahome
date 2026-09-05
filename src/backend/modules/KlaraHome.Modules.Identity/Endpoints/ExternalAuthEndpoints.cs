using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Features;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Endpoints;

/// <summary>The body of a password change.</summary>
/// <param name="ChallengeToken">From a <c>password-change-required</c> challenge; omit when signed in.</param>
/// <param name="CurrentPassword">The password being replaced.</param>
/// <param name="NewPassword">The replacement.</param>
internal sealed record ChangePasswordBody(string? ChallengeToken, string CurrentPassword, string NewPassword);

/// <summary>The body of an administrator-issued temporary password.</summary>
/// <param name="TemporaryPassword">What the administrator will convey out of band.</param>
internal sealed record SetTemporaryPasswordBody(string TemporaryPassword);

/// <summary>
/// Signing in with an identity provider (docs/08-integrations.md §3.5, ADR-014).
/// </summary>
/// <remarks>
/// <para>
/// Two browser redirects with a server-to-server token exchange between them, so the client secret
/// never leaves this process. Both are <c>GET</c> because both are navigations the browser makes
/// on its own, and both are anonymous because they are how a caller stops being anonymous.
/// </para>
/// <para>
/// Mapped on the storefront only. Staff and vendor users sign in with a password and a second
/// factor (ADR-014 decision 3), so there is deliberately no admin equivalent of these routes.
/// </para>
/// </remarks>
internal static class ExternalAuthEndpoints
{
    /// <summary>Maps the external sign-in surface beneath the storefront group.</summary>
    /// <param name="store">The <c>/store</c> group.</param>
    public static IEndpointRouteBuilder MapExternalAuthEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/auth/external")
            .WithTags("Identity")
            .AllowAnonymous()
            .RequireFeature(IdentityFeatures.ExternalLogin);

        group.MapGet("/providers", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetExternalProvidersQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeAuthExternalProviders")
            .WithSummary("The identity providers this store offers, for the sign-in page's buttons. "
                         + "A provider that is switched on but not configured is not listed.")
            .Produces<IReadOnlyList<ExternalProviderResponse>>();

        group.MapGet("/{provider}/start", async (
                string provider,
                string? returnUrl,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new StartExternalLoginCommand(provider, returnUrl), context.RequestAborted)
                    .ConfigureAwait(false);

                if (result.IsFailure)
                {
                    return result.Error.ToProblemResult(context);
                }

                WriteStateCookie(context, result.Value.StateCookie, options.Value);
                return Results.Redirect(result.Value.AuthorizationUri.ToString());
            })
            .WithName("storeAuthExternalStart")
            .WithSummary("Redirects the browser to the provider, carrying the anti-forgery state and the "
                         + "PKCE challenge.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces(StatusCodes.Status302Found);

        group.MapGet("/{provider}/callback", async (
                string provider,
                string? code,
                string? state,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new CompleteExternalLoginCommand(
                    provider,
                    code,
                    state,
                    ReadStateCookie(context, options.Value),
                    AuthCookies.DeviceOf(context));

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                // Spent either way. A state that has been through the callback must not be usable
                // a second time, and one that failed is of no further use to anybody.
                ClearStateCookie(context, options.Value);

                if (result.IsFailure)
                {
                    return result.Error.ToProblemResult(context);
                }

                if (result.Value.SignIn.RefreshToken is not null)
                {
                    AuthCookies.Write(context, result.Value.SignIn.RefreshToken, options.Value.Tokens);
                }

                // A redirect, not a JSON body: this is a browser navigation, and the session it
                // carries is in the cookie. The storefront calls /auth/refresh on arrival to pick
                // up an access token, which is the same thing it does on a returning visit.
                return Results.Redirect(result.Value.ReturnUrl);
            })
            .WithName("storeAuthExternalCallback")
            .WithSummary("Completes the sign-in the provider redirected back from, and returns the browser "
                         + "to the storefront with a refresh cookie set.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces(StatusCodes.Status302Found);

        return store;
    }

    /// <summary>Maps the caller's own linked providers, beneath <c>/me</c>.</summary>
    /// <param name="self">The <c>/me</c> group.</param>
    /// <param name="namePrefix">Prefixes the operation ids.</param>
    public static IEndpointRouteBuilder MapExternalLoginManagement(
        this IEndpointRouteBuilder self,
        string namePrefix)
    {
        ArgumentNullException.ThrowIfNull(self);

        self.MapGet("/external-logins", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetExternalLoginsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "MeExternalLoginsGet")
            .WithSummary("The identity providers linked to the caller's account.")
            .Produces<IReadOnlyList<ExternalLoginResponse>>();

        self.MapDelete("/external-logins/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new UnlinkExternalLoginCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "MeExternalLoginDelete")
            .WithSummary("Unlinks a provider. Refused when it is the only way into the account.")
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces(StatusCodes.Status204NoContent);

        return self;
    }

    /// <summary>
    /// The cookie carrying the sign-in between the two redirects.
    /// </summary>
    /// <remarks>
    /// <c>SameSite=Lax</c>, which is what lets the provider's top-level GET redirect bring it back;
    /// <c>Strict</c> would drop it and every sign-in would fail with nothing to point at. Scoped to
    /// the callback path so it is not sent with anything else, and short-lived because an
    /// unfinished sign-in has no reason to outlive the attempt.
    /// </remarks>
    private static void WriteStateCookie(HttpContext context, string value, AuthOptions options)
        => context.Response.Cookies.Append(options.External.StateCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Tokens.RefreshCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/store/auth/external",
            MaxAge = TimeSpan.FromMinutes(options.External.StateLifetimeMinutes),
            IsEssential = true,
        });

    private static string? ReadStateCookie(HttpContext context, AuthOptions options)
        => context.Request.Cookies.TryGetValue(options.External.StateCookieName, out var value) ? value : null;

    private static void ClearStateCookie(HttpContext context, AuthOptions options)
        => context.Response.Cookies.Delete(options.External.StateCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = options.Tokens.RefreshCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/store/auth/external",
        });
}
