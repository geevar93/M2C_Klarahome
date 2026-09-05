using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Endpoints;

/// <summary>The body of an OTP request.</summary>
/// <param name="Mobile">The mobile number to send the code to.</param>
internal sealed record OtpRequestBody(string Mobile);

/// <summary>The body of an OTP verification.</summary>
/// <param name="Mobile">The number the code was sent to.</param>
/// <param name="Code">The code.</param>
internal sealed record OtpVerifyBody(string Mobile, string Code);

/// <summary>The body of a password sign-in.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The password.</param>
internal sealed record LoginBody(string Email, string Password);

/// <summary>The body of a shopper registration.</summary>
/// <param name="Email">The email address.</param>
/// <param name="Password">The chosen password.</param>
/// <param name="Mobile">An optional mobile number.</param>
/// <param name="MarketingConsent">Whether marketing was opted into. Unbundled from the sign-up.</param>
internal sealed record RegisterBody(string Email, string Password, string? Mobile, bool MarketingConsent);

/// <summary>The body of a password-reset request.</summary>
/// <param name="Email">The email address.</param>
internal sealed record ForgotPasswordBody(string Email);

/// <summary>The body of a password reset.</summary>
/// <param name="Email">The address the link was sent to.</param>
/// <param name="Token">The token from the link.</param>
/// <param name="NewPassword">The new password.</param>
internal sealed record ResetPasswordBody(string Email, string Token, string NewPassword);

/// <summary>The body of a 2FA enrolment request.</summary>
/// <param name="ChallengeToken">The pending-sign-in token.</param>
internal sealed record TwoFactorEnrolBody(string ChallengeToken);

/// <summary>The body of a 2FA verification.</summary>
/// <param name="ChallengeToken">The pending-sign-in token.</param>
/// <param name="Code">The six digits from the authenticator app.</param>
internal sealed record TwoFactorVerifyBody(string ChallengeToken, string Code);

/// <summary>
/// The authentication surface, mapped identically beneath <c>/store</c> and <c>/admin</c>.
/// </summary>
/// <remarks>
/// <para>
/// One set of endpoints, mapped twice. The storefront and the admin app are different origins with
/// different CORS rules and different clients, so they need their own routes
/// (docs/04-api-specification.md §2) — but the flows behind them are the same flows, and writing
/// them twice would mean fixing the next authentication bug twice.
/// </para>
/// <para>
/// Every endpoint here is anonymous by necessity: they are how a caller stops being anonymous.
/// Each says so explicitly, because the fallback policy closes anything that does not.
/// </para>
/// </remarks>
internal static class AuthEndpoints
{
    /// <summary>Maps the authentication endpoints beneath a surface group.</summary>
    /// <param name="surface">The <c>/store</c> or <c>/admin</c> group.</param>
    /// <param name="namePrefix">
    /// Prefixes every <c>operationId</c>, because the two surfaces map the same routes and an
    /// endpoint name has to be unique — and because the generated client reads better as
    /// <c>storeAuthLogin</c> and <c>adminAuthLogin</c> than as one shared method
    /// (docs/04-api-specification.md §7).
    /// </param>
    /// <param name="includeOtp">
    /// Whether to map the mobile-OTP endpoints. The storefront has them because a shopper's primary
    /// credential is their mobile number; the admin surface does not, because staff and vendor
    /// users sign in with an email address and a mandatory second factor
    /// (docs/07-security-compliance.md §1).
    /// </param>
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder surface,
        string namePrefix,
        bool includeOtp)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var group = surface
            .MapGroup("/auth")
            .WithTags("Identity")
            .AllowAnonymous();

        if (includeOtp)
        {
            MapOtp(group, namePrefix);
            MapRegistration(group, namePrefix);
        }

        MapPassword(group, namePrefix);
        MapSession(group, namePrefix);
        MapTwoFactor(group, namePrefix);

        return surface;
    }

    private static void MapOtp(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapPost("/otp/request", async (
                OtpRequestBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RequestOtpCommand(body.Mobile), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "AuthOtpRequest")
            .WithSummary("Sends a one-time code to a mobile number. The response is the same whether or "
                         + "not the number is registered.")
            .RequireRateLimiting(RateLimitPolicies.Otp)
            .Produces<OtpRequestedResponse>();

        group.MapPost("/otp/verify", async (
                OtpVerifyBody body,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new VerifyOtpCommand(body.Mobile, body.Code, AuthCookies.DeviceOf(context));
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return AuthCookies.ToSignIn(result, context, options);
            })
            .WithName(namePrefix + "AuthOtpVerify")
            .WithSummary("Signs in with a one-time code, registering the customer if the number is new.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<SignInResponse>();
    }

    private static void MapRegistration(IEndpointRouteBuilder group, string namePrefix)
        => group.MapPost("/register", async (
                RegisterBody body,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new RegisterCustomerCommand(
                    body.Email,
                    body.Password,
                    body.Mobile,
                    body.MarketingConsent,
                    AuthCookies.DeviceOf(context));

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return AuthCookies.ToSignIn(result, context, options);
            })
            .WithName(namePrefix + "AuthRegister")
            .WithSummary("Registers a shopper with an email address and a password.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<SignInResponse>();

    private static void MapPassword(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapPost("/login", async (
                LoginBody body,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new PasswordLoginCommand(body.Email, body.Password, AuthCookies.DeviceOf(context));
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return AuthCookies.ToSignIn(result, context, options);
            })
            .WithName(namePrefix + "AuthLogin")
            .WithSummary("Signs in with an email address and a password. May answer with a two-factor challenge.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<SignInResponse>();

        group.MapPost("/password/forgot", async (
                ForgotPasswordBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new ForgotPasswordCommand(body.Email), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "AuthPasswordForgot")
            .WithSummary("Sends a password-reset link. Always answers 204, whether or not the address is known.")
            .RequireRateLimiting(RateLimitPolicies.Otp)
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/password/reset", async (
                ResetPasswordBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new ResetPasswordCommand(body.Email, body.Token, body.NewPassword);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "AuthPasswordReset")
            .WithSummary("Sets a new password from a reset link, and signs every device out.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapSession(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapPost("/refresh", async (
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new RefreshSessionCommand(AuthCookies.Read(context, options));
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                if (result.IsFailure)
                {
                    // A refresh that failed leaves a cookie the browser would keep presenting.
                    // Clearing it is what turns "signed out" into a state the client can see.
                    AuthCookies.Clear(context, options);
                }

                return AuthCookies.ToSignIn(result, context, options);
            })
            .WithName(namePrefix + "AuthRefresh")
            .WithSummary("Exchanges the refresh cookie for a new access token, rotating the refresh token.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<SignInResponse>();

        group.MapPost("/logout", async (
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new SignOutCommand(AuthCookies.Read(context, options));
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                AuthCookies.Clear(context, options);
                return result.ToNoContent(context);
            })
            .WithName(namePrefix + "AuthLogout")
            .WithSummary("Ends this session and clears the refresh cookie.")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapTwoFactor(IEndpointRouteBuilder group, string namePrefix)
    {
        group.MapPost("/2fa/enrol", async (
                TwoFactorEnrolBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new BeginTwoFactorEnrolmentCommand(body.ChallengeToken), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName(namePrefix + "AuthTwoFactorEnrol")
            .WithSummary("Returns a new authenticator secret for a sign-in that stopped at enrolment.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<TwoFactorSetupResponse>();

        group.MapPost("/2fa/verify", async (
                TwoFactorVerifyBody body,
                IDispatcher dispatcher,
                IOptions<AuthOptions> options,
                HttpContext context) =>
            {
                var command = new CompleteTwoFactorCommand(
                    body.ChallengeToken,
                    body.Code,
                    AuthCookies.DeviceOf(context));

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return AuthCookies.ToSignIn(result, context, options);
            })
            .WithName(namePrefix + "AuthTwoFactorVerify")
            .WithSummary("Answers the second factor and completes the sign-in, enrolling a staged secret if this "
                         + "is the first code it has produced.")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .Produces<SignInResponse>();
    }
}

/// <summary>The body of a verification request.</summary>
/// <param name="Channel">Which identifier to verify.</param>
internal sealed record VerificationRequestBody(OtpChannel Channel);

/// <summary>The body of a verification confirmation.</summary>
/// <param name="Channel">Which identifier is being proved.</param>
/// <param name="Code">The code or link token.</param>
internal sealed record VerificationConfirmBody(OtpChannel Channel, string Code);

/// <summary>The body of a 2FA enable.</summary>
/// <param name="Code">The six digits from the authenticator app.</param>
internal sealed record EnableTwoFactorBody(string Code);

/// <summary>The body of a 2FA disable.</summary>
/// <param name="Password">The account password.</param>
/// <param name="Code">A current code from the authenticator app.</param>
internal sealed record DisableTwoFactorBody(string Password, string Code);
