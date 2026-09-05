using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>Who the caller is, as every authenticated surface reports it.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserType">Which surface they belong to: customer, vendor or staff.</param>
/// <param name="Mobile">Their mobile number, or null.</param>
/// <param name="Email">Their email address, or null.</param>
/// <param name="MobileVerified">Whether the mobile number has been proved.</param>
/// <param name="EmailVerified">Whether the email address has been proved.</param>
/// <param name="TwoFactorEnabled">Whether a second factor is enrolled.</param>
/// <param name="VendorId">The seller they act for, or null.</param>
/// <param name="Roles">The roles they hold.</param>
/// <param name="Permissions">Every permission those roles grant.</param>
internal sealed record AuthenticatedUserResponse(
    Guid Id,
    string UserType,
    string? Mobile,
    string? Email,
    bool MobileVerified,
    bool EmailVerified,
    bool TwoFactorEnabled,
    Guid? VendorId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    /// <summary>Projects a user and their resolved access.</summary>
    /// <param name="user">The user.</param>
    /// <param name="access">What they may do.</param>
    public static AuthenticatedUserResponse From(User user, UserAccess access)
        => new(
            user.Id,
            UserTypeNames.Of(user.UserType),
            user.Mobile,
            user.Email,
            user.MobileVerifiedAt is not null,
            user.EmailVerifiedAt is not null,
            user.TotpEnabled,
            access.VendorId,
            access.RoleCodes,
            access.Permissions);
}

/// <summary>
/// What a sign-in attempt produced: either a session, or a challenge that has to be answered first.
/// </summary>
/// <remarks>
/// One shape for both outcomes rather than two endpoints, because the client's next step differs
/// only in which field is populated — and a second endpoint would need the caller to know in
/// advance whether the account has a second factor, which is precisely what must not be leaked
/// before authentication succeeds.
/// </remarks>
/// <param name="AccessToken">The signed JWT, when the sign-in completed.</param>
/// <param name="ExpiresAt">When the access token expires.</param>
/// <param name="User">Who signed in.</param>
/// <param name="Challenge">What still has to be answered, when the sign-in did not complete.</param>
internal sealed record SignInResponse(
    string? AccessToken,
    DateTimeOffset? ExpiresAt,
    AuthenticatedUserResponse? User,
    TwoFactorChallengeResponse? Challenge);

/// <summary>A sign-in that stopped at the second factor.</summary>
/// <param name="Type">
/// <c>two-factor</c> when a code is expected, <c>two-factor-enrolment</c> when the account holds a
/// role that requires one and has not enrolled yet.
/// </param>
/// <param name="ChallengeToken">The short-lived token that carries the pending sign-in.</param>
internal sealed record TwoFactorChallengeResponse(string Type, string ChallengeToken)
{
    /// <summary>A code from an already-enrolled authenticator is expected.</summary>
    public const string CodeRequired = "two-factor";

    /// <summary>A second factor is mandatory for this account and has not been enrolled.</summary>
    public const string EnrolmentRequired = "two-factor-enrolment";
}

/// <summary>
/// A sign-in outcome together with the refresh secret, which never appears in a response body.
/// </summary>
/// <remarks>
/// The handler produces both; the endpoint puts the refresh secret in an <c>HttpOnly</c> cookie
/// and returns only <see cref="Response"/>. Keeping them in one object is what stops a future
/// endpoint from returning the refresh token in JSON by accident — there is no path that serialises
/// this type.
/// </remarks>
/// <param name="Response">The body the caller receives.</param>
/// <param name="RefreshToken">The opaque refresh secret for the cookie, or null.</param>
internal sealed record SignInResult(SignInResponse Response, string? RefreshToken)
{
    /// <summary>A completed sign-in.</summary>
    /// <param name="tokens">The issued pair.</param>
    /// <param name="user">Who signed in.</param>
    /// <param name="access">What they may do.</param>
    public static SignInResult Signed(IssuedTokens tokens, User user, UserAccess access)
        => new(
            new SignInResponse(
                tokens.AccessToken,
                tokens.ExpiresAt,
                AuthenticatedUserResponse.From(user, access),
                Challenge: null),
            tokens.RefreshToken);

    /// <summary>A sign-in that stopped at the second factor.</summary>
    /// <param name="type">Which kind of challenge.</param>
    /// <param name="challengeToken">The pending-sign-in token.</param>
    public static SignInResult Challenged(string type, string challengeToken)
        => new(
            new SignInResponse(
                AccessToken: null,
                ExpiresAt: null,
                User: null,
                new TwoFactorChallengeResponse(type, challengeToken)),
            RefreshToken: null);
}

/// <summary>The failures every authentication path shares, so their wording cannot drift.</summary>
/// <remarks>
/// <see cref="InvalidCredentials"/> is deliberately the answer to a wrong password, an unknown
/// email address, a disabled account and a customer with no password set. Distinguishing them is
/// an account-enumeration oracle, which docs/07-security-compliance.md §3 rules out under
/// "uniform responses on login/OTP/forgot-password regardless of account existence".
/// </remarks>
internal static class AuthErrors
{
    /// <summary>The credential offered was not accepted, for any reason.</summary>
    public static Error InvalidCredentials()
        => Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "That combination was not recognised.");

    /// <summary>Too many failures; the account is refusing attempts for a while.</summary>
    /// <param name="until">When attempts are accepted again.</param>
    public static Error LockedOut(DateTimeOffset until)
        => Error.Unauthorized(
            "AUTH_ACCOUNT_LOCKED",
            $"Too many failed attempts. Try again after {until:HH:mm} UTC.");

    /// <summary>A one-time code was requested too many times for one destination.</summary>
    public static Error OtpThrottled()
        => Error.RateLimited(
            "AUTH_OTP_THROTTLED",
            "A code has been requested too many times for this number. Please wait before asking for another.");

    /// <summary>The code, link or challenge token was wrong, spent or expired.</summary>
    public static Error InvalidCode()
        => Error.Unauthorized("AUTH_INVALID_CODE", "That code is not valid, or it has expired.");

    /// <summary>The refresh cookie was missing, unknown or already spent.</summary>
    public static Error InvalidRefreshToken()
        => Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "Please sign in again.");

    /// <summary>The caller is signed in but the account is gone.</summary>
    public static Error UnknownUser()
        => Error.NotFound("IDENTITY_USER_NOT_FOUND", "That user does not exist.");
}
