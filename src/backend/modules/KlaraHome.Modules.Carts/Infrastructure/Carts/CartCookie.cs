using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Carts.Infrastructure.Carts;

/// <summary>
/// The anonymous basket's cookie, and the digest the row is found by.
/// </summary>
/// <remarks>
/// <para>
/// The token is a bearer capability: anyone holding it can read and edit the basket it names. It is
/// therefore treated the way a refresh token is (docs/07-security-compliance.md §1) — 256 bits from
/// a CSPRNG, carried in an <c>HttpOnly; Secure; SameSite=Lax</c> cookie, and stored only as a
/// SHA-256 digest, so a dump of <c>carts.carts</c> hands an attacker nobody's basket.
/// </para>
/// <para>
/// <c>SameSite=Lax</c> rather than <c>Strict</c>, for the same reason the auth cookie is: the
/// storefront returns from a payment redirect as a cross-site navigation, and <c>Strict</c> would
/// drop the cookie and empty the shopper's basket on the way back from paying.
/// </para>
/// <para>
/// It is not <c>HttpOnly</c>-optional and it is not signed. A signature would prove the server
/// issued it, which is not a question worth answering: the digest lookup already fails for anything
/// the server did not issue, and a forged token simply matches no row.
/// </para>
/// </remarks>
/// <param name="accessor">The ambient request, for reading and writing the cookie.</param>
/// <param name="options">Cookie name, lifetime and the Secure flag.</param>
internal sealed class CartCookie(IHttpContextAccessor accessor, IOptions<CartsOptions> options)
{
    /// <summary>The token the browser presented, or null when it presented none.</summary>
    public string? Read()
    {
        var context = accessor.HttpContext;

        if (context is null)
        {
            return null;
        }

        return context.Request.Cookies.TryGetValue(options.Value.CartCookieName, out var value)
               && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>Issues a fresh token and writes the cookie. Returns the token so it can be hashed.</summary>
    public string Issue()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var settings = options.Value;

        accessor.HttpContext?.Response.Cookies.Append(settings.CartCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = settings.CartCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromDays(settings.CartLifetimeDays),

            // Essential: a shopping basket is not tracking, and a consent banner must not be able
            // to switch it off (docs/07-security-compliance.md §6).
            IsEssential = true,
        });

        return token;
    }

    /// <summary>
    /// Removes the cookie, once the basket it named has been claimed by a signed-in shopper.
    /// </summary>
    /// <remarks>
    /// The attributes must match the ones it was written with, or the browser keeps the original
    /// and the merged basket stays reachable by whoever else uses that machine.
    /// </remarks>
    public void Clear()
    {
        var settings = options.Value;

        accessor.HttpContext?.Response.Cookies.Delete(settings.CartCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = settings.CartCookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
        });
    }

    /// <summary>The digest a basket row is found by. Never the token itself.</summary>
    /// <param name="token">The token the browser presented.</param>
    public static string HashOf(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
