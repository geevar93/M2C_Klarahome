using KlaraHome.Infrastructure.Errors;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Endpoints;

/// <summary>
/// The refresh cookie, and the one way a sign-in result becomes an HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// The refresh token travels in an <c>HttpOnly; Secure; SameSite=Lax</c> cookie and never in a
/// response body (docs/07-security-compliance.md §1); the access token goes in the body and lives
/// in memory on the client, never in <c>localStorage</c>. Both halves of that rule are enforced
/// here rather than in each endpoint, because there are eight endpoints and one of them would
/// eventually get it wrong.
/// </para>
/// <para>
/// <c>SameSite=Lax</c> rather than <c>Strict</c>: the storefront returns from a Razorpay redirect
/// as a cross-site navigation, and <c>Strict</c> would drop the cookie and sign the customer out
/// on the way back from paying.
/// </para>
/// </remarks>
internal static class AuthCookies
{
    /// <summary>Writes the sign-in result: the cookie, then the body without the refresh token.</summary>
    /// <param name="result">What the handler produced.</param>
    /// <param name="context">The request.</param>
    /// <param name="options">Cookie name, lifetime and the Secure flag.</param>
    public static IResult ToSignIn(
        Result<SignInResult> result,
        HttpContext context,
        IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (result.IsFailure)
        {
            return result.Error.ToProblemResult(context);
        }

        if (result.Value.RefreshToken is not null)
        {
            Write(context, result.Value.RefreshToken, options.Value.Tokens);
        }

        return Results.Ok(result.Value.Response);
    }

    /// <summary>Reads the refresh token from the request cookie.</summary>
    /// <param name="context">The request.</param>
    /// <param name="options">Supplies the cookie name.</param>
    public static string? Read(HttpContext context, IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return context.Request.Cookies.TryGetValue(options.Value.Tokens.RefreshCookieName, out var value)
            ? value
            : null;
    }

    /// <summary>Removes the refresh cookie, on the way out.</summary>
    /// <param name="context">The request.</param>
    /// <param name="options">Supplies the cookie name and attributes.</param>
    public static void Clear(HttpContext context, IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var tokens = options.Value.Tokens;

        // The attributes must match the ones it was written with, or the browser keeps the
        // original cookie and the user stays signed in after pressing "sign out".
        context.Response.Cookies.Delete(tokens.RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = tokens.RefreshCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    /// <summary>What the caller looks like, for the session record.</summary>
    /// <param name="context">The request.</param>
    public static DeviceInfo DeviceOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new DeviceInfo(
            Describe(context.Request.Headers.UserAgent.ToString()),
            context.Connection.RemoteIpAddress?.ToString());
    }

    private static void Write(HttpContext context, string refreshToken, TokenOptions tokens)
        => context.Response.Cookies.Append(tokens.RefreshCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = tokens.RefreshCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(tokens.RefreshTokenDays),
            IsEssential = true,
        });

    /// <summary>
    /// Turns a user agent into something a person recognises in their session list.
    /// </summary>
    /// <remarks>
    /// Deliberately crude, and deliberately not a user-agent parsing library. The string is shown
    /// to the owner of the account so they can answer one question — "was that me?" — and "Chrome
    /// on Windows" answers it as well as a version number would. Storing the raw header instead
    /// would put an unbounded, attacker-controlled string on a page.
    /// </remarks>
    /// <param name="userAgent">The raw header.</param>
    internal static string? Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser =
            userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "Browser";

        var platform =
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "unknown device";

        return $"{browser} on {platform}";
    }
}
