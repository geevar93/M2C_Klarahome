using System.Globalization;
using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Identity.Application.Authentication;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.External;

/// <summary>
/// What a started sign-in has to remember until the provider redirects the browser back.
/// </summary>
/// <param name="Provider">Which provider was started, so a callback cannot be replayed at another.</param>
/// <param name="State">The opaque value the provider echoes back.</param>
/// <param name="CodeVerifier">The PKCE verifier, which never leaves this server.</param>
/// <param name="ReturnUrl">Where the storefront is sent afterwards, already checked against the allow-list.</param>
/// <param name="ExpiresAt">When an unfinished sign-in stops being accepted.</param>
internal sealed record ExternalLoginState(
    ExternalProvider Provider,
    string State,
    string CodeVerifier,
    string ReturnUrl,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Carries the in-flight sign-in in an encrypted cookie rather than a table.
/// </summary>
/// <remarks>
/// <para>
/// A row would need a primary key, an index and a retention job for the sign-ins nobody finishes.
/// A cookie needs none of those and expires by itself. It is encrypted with the same AES-GCM key
/// that protects TOTP secrets, so a caller can neither read the PKCE verifier nor forge a state.
/// </para>
/// <para>
/// <c>SameSite=Lax</c> is what makes this work at all: the provider returns the browser by a
/// top-level GET navigation, which Lax permits and Strict would not — the cookie would be dropped
/// and every sign-in would fail with nothing to point at.
/// </para>
/// </remarks>
/// <param name="protector">Encrypts and decrypts the envelope.</param>
/// <param name="options">Cookie name, lifetime and the Secure flag.</param>
/// <param name="clock">The clock.</param>
internal sealed class ExternalLoginStateCookie(
    SecretProtector protector,
    IOptions<AuthOptions> options,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Mints the state for a sign-in and returns both the state and its cookie value.</summary>
    /// <param name="provider">The provider being started.</param>
    /// <param name="returnUrl">The already-validated return URL.</param>
    public (ExternalLoginState State, string Cookie) Issue(ExternalProvider provider, string returnUrl)
    {
        var external = options.Value.External;

        var state = new ExternalLoginState(
            provider,
            Pkce.NewState(),
            Pkce.NewVerifier(),
            returnUrl,
            clock.UtcNow.AddMinutes(external.StateLifetimeMinutes));

        return (state, protector.Protect(JsonSerializer.Serialize(state, Json)));
    }

    /// <summary>
    /// Reads a cookie back, or returns null. Every failure is the same null: a tampered envelope,
    /// an expired sign-in and a callback for a different provider are all "this is not a sign-in
    /// this server started".
    /// </summary>
    /// <param name="cookie">The cookie value.</param>
    /// <param name="provider">The provider whose callback is being handled.</param>
    /// <param name="state">The state the provider echoed back.</param>
    public ExternalLoginState? Read(string? cookie, ExternalProvider provider, string? state)
    {
        var plaintext = protector.Unprotect(cookie);

        if (plaintext is null || string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        ExternalLoginState? stored;

        try
        {
            stored = JsonSerializer.Deserialize<ExternalLoginState>(plaintext, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (stored is null || stored.Provider != provider || stored.ExpiresAt <= clock.UtcNow)
        {
            return null;
        }

        // Constant-time, because this is the anti-forgery check and the comparison is against a
        // value the caller supplied.
        var expected = System.Text.Encoding.ASCII.GetBytes(stored.State);
        var offered = System.Text.Encoding.ASCII.GetBytes(state);

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, offered)
            ? stored
            : null;
    }
}

/// <summary>
/// Turns a provider's assertion into a session, applying the linking rules from
/// <c>07-security-compliance.md</c> §1.
/// </summary>
/// <remarks>
/// The rules are narrow because the obvious implementation — "find a user with this email" — is
/// the standard account-takeover vector. Only a subject the provider has used before, or an email
/// it states it has verified, may reach an existing account.
/// </remarks>
/// <param name="context">The Identity data context.</param>
/// <param name="signIn">Completes the sign-in, so external logins obey every rule the others do.</param>
/// <param name="access">Finds the customer role for a newly created account.</param>
/// <param name="audit">Records a link and a first sign-in.</param>
/// <param name="clock">The clock.</param>
internal sealed class ExternalLoginService(
    IdentityDbContext context,
    SignInCoordinator signIn,
    AccessResolver access,
    IAuditLogger audit,
    IClock clock)
{
    /// <summary>The audited action for a provider identity attached to an account.</summary>
    public const string LinkedAction = "identity.external-login.linked";

    /// <summary>The audited action for a provider identity removed from an account.</summary>
    public const string UnlinkedAction = "identity.external-login.unlinked";

    /// <summary>
    /// Signs the person in, linking or creating an account as the rules allow.
    /// </summary>
    /// <param name="provider">Which provider asserted the identity.</param>
    /// <param name="identity">What it asserted.</param>
    /// <param name="device">What the caller looks like.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<SignInResult>> SignInAsync(
        ExternalProvider provider,
        ExternalIdentity identity,
        DeviceInfo device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var now = clock.UtcNow;

        // 1. A subject we have seen before is this person, whatever their email says today.
        var existingLink = await context.ExternalLogins
            .FirstOrDefaultAsync(
                link => link.Provider == provider && link.Subject == identity.Subject,
                cancellationToken)
            .ConfigureAwait(false);

        if (existingLink is not null)
        {
            var linked = await context.Users
                .FirstOrDefaultAsync(user => user.Id == existingLink.UserId, cancellationToken)
                .ConfigureAwait(false);

            if (linked is null || linked.Status != UserStatus.Active)
            {
                return AuthErrors.InvalidCredentials();
            }

            existingLink.RecordSignIn(identity.Email, identity.EmailVerified, identity.DisplayName, now);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return await signIn
                .CompleteAsync(linked, device, secondFactorSatisfied: false, cancellationToken)
                .ConfigureAwait(false);
        }

        // 2. A verified email may join an existing account. An unverified one may not: it is the
        //    difference between "Google says this is their address" and "somebody typed it in".
        User? user = null;

        if (identity.EmailVerified && !string.IsNullOrWhiteSpace(identity.Email))
        {
            user = await context.Users
                .FirstOrDefaultAsync(candidate => candidate.Email == identity.Email, cancellationToken)
                .ConfigureAwait(false);

            if (user is not null && user.UserType != UserType.Customer)
            {
                // Staff and vendor users do not sign in this way (ADR-014 decision 3), and silently
                // linking their account would create exactly the path that decision refuses.
                return Error.Forbidden(
                    "IDENTITY_EXTERNAL_NOT_PERMITTED",
                    "This account signs in with a password and a second factor, not with an identity provider.");
            }

            if (user is not null && user.Status != UserStatus.Active)
            {
                return AuthErrors.InvalidCredentials();
            }
        }

        var created = false;

        // 3. Otherwise a new customer, whose email is already verified — by a stronger assertion
        //    than our own verification link would have made.
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(identity.Email))
            {
                // Facebook accounts may carry no email at all, and ck_users_has_identifier requires
                // a mobile number or one. Recorded in ADR-014; it costs nothing while Facebook is
                // disabled, and this is the message whoever enables it will see first.
                return Error.Validation(
                    "IDENTITY_EXTERNAL_NO_EMAIL",
                    $"{provider} did not supply an email address, and an account cannot be created without one.");
            }

            // The address may belong to somebody already — reached here when the provider did not
            // verify it, which is exactly the case rule 2 refuses to link on. Creating a second
            // account would violate the unique index; refusing says what happened.
            var taken = await context.Users
                .AnyAsync(candidate => candidate.Email == identity.Email, cancellationToken)
                .ConfigureAwait(false);

            if (taken)
            {
                return Error.Conflict(
                    "IDENTITY_ACCOUNT_EXISTS",
                    $"An account already exists for that email address. Sign in with it, or ask {provider} to "
                    + "verify the address first.");
            }

            user = User.RegisterCustomer(mobile: null, identity.Email);
            user.MarkEmailVerified(now);
            context.Users.Add(user);

            context.CustomerProfiles.Add(NewProfileFor(user.Id, identity.DisplayName));

            var customerRole = await access
                .FindRoleAsync(Seeding.SystemRoles.Customer, cancellationToken)
                .ConfigureAwait(false);

            if (customerRole is not null)
            {
                user.GrantRole(customerRole.Id);
            }

            created = true;
        }
        else if (user.EmailVerifiedAt is null)
        {
            user.MarkEmailVerified(now);
        }

        context.ExternalLogins.Add(ExternalLogin.Link(
            user.Id,
            provider,
            identity.Subject,
            identity.Email,
            identity.EmailVerified,
            identity.DisplayName,
            now));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = LinkedAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = user.Id.ToString(),
                ActorId = user.Id,
                ActorType = AuditActorType.Customer,
                After = new
                {
                    provider = provider.ToString(),
                    accountCreated = created,
                    linkedByVerifiedEmail = !created,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return await signIn
            .CompleteAsync(user, device, secondFactorSatisfied: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A profile for an account the provider created, taking the name it supplied.
    /// </summary>
    /// <remarks>
    /// Split on the first space. Crude, and wrong for a good number of Indian names — which is why
    /// both halves are editable on the account page and neither is required anywhere.
    /// </remarks>
    private static CustomerProfile NewProfileFor(Guid userId, string? displayName)
    {
        var profile = CustomerProfile.For(userId, ReferralCodes.New());

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var space = displayName.Trim().IndexOf(' ', StringComparison.Ordinal);

            profile.Update(
                space < 0 ? displayName.Trim() : displayName.Trim()[..space],
                space < 0 ? null : displayName.Trim()[(space + 1)..],
                dateOfBirth: null,
                gender: null,
                gstin: null);
        }

        return profile;
    }

    /// <summary>
    /// Removes a link, unless it is the only way the account can be signed in to.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="linkId">The link to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> UnlinkAsync(Guid userId, Guid linkId, CancellationToken cancellationToken)
    {
        var link = await context.ExternalLogins
            .FirstOrDefaultAsync(candidate => candidate.Id == linkId && candidate.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (link is null)
        {
            return Result.Failure(Error.NotFound(
                "IDENTITY_EXTERNAL_LOGIN_NOT_FOUND",
                "That sign-in method is not linked to this account."));
        }

        var user = await context.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UnknownUser());
        }

        var otherLinks = await context.ExternalLogins
            .CountAsync(candidate => candidate.UserId == userId && candidate.Id != linkId, cancellationToken)
            .ConfigureAwait(false);

        if (!user.HasLocalCredential && otherLinks == 0)
        {
            // Removing it would leave an account nobody can reach, including its owner.
            return Result.Failure(Error.Validation(
                "IDENTITY_LAST_CREDENTIAL",
                "This is the only way to sign in to this account. Set a password or add a mobile number first."));
        }

        context.ExternalLogins.Remove(link);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = UnlinkedAction,
                EntityType = SignInCoordinator.UserEntityType,
                EntityId = userId.ToString(),
                ActorId = userId,
                ActorType = AuditActorType.Customer,
                Before = new { provider = link.Provider.ToString() },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// Whether a caller-supplied return URL may be redirected to.
    /// </summary>
    /// <remarks>
    /// An unchecked <c>returnUrl</c> is an open redirect, and an open redirect on the endpoint that
    /// has just issued a session is a phishing primitive. A relative path is always fine; anything
    /// absolute has to match a configured origin exactly.
    /// </remarks>
    /// <param name="returnUrl">What the caller asked for, or null.</param>
    /// <param name="options">The allow-list and the default.</param>
    /// <param name="resolved">The URL to redirect to.</param>
    public static bool TryResolveReturnUrl(string? returnUrl, ExternalAuthOptions options, out string resolved)
    {
        ArgumentNullException.ThrowIfNull(options);

        resolved = options.DefaultReturnUrl;

        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return true;
        }

        if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            // A single leading slash is this host. Two is a protocol-relative URL pointing
            // somewhere else entirely, which is the trick this check exists for.
            resolved = returnUrl;
            return true;
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute))
        {
            return false;
        }

        foreach (var allowed in options.AllowedReturnUrls)
        {
            if (Uri.TryCreate(allowed, UriKind.Absolute, out var permitted)
                && Uri.Compare(
                    absolute,
                    permitted,
                    UriComponents.SchemeAndServer,
                    UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                resolved = returnUrl;
                return true;
            }
        }

        return false;
    }

    /// <summary>The redirect URI registered with the provider, for one provider.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="options">Supplies the callback origin.</param>
    public static string CallbackUriFor(ExternalProvider provider, ExternalAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.CallbackBaseUrl.TrimEnd('/')}/api/v1/store/auth/external/{ProviderNames.Of(provider)}/callback");
    }
}

/// <summary>The wire spelling of <see cref="ExternalProvider"/>, used in routes and responses.</summary>
internal static class ProviderNames
{
    /// <summary>Google.</summary>
    public const string Google = "google";

    /// <summary>Facebook.</summary>
    public const string Facebook = "facebook";

    /// <summary>The wire spelling of a provider.</summary>
    /// <param name="provider">The provider.</param>
    public static string Of(ExternalProvider provider) => provider switch
    {
        ExternalProvider.Google => Google,
        ExternalProvider.Facebook => Facebook,
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    /// <summary>Reads a provider from a route segment, or returns false for anything else.</summary>
    /// <param name="name">The route segment.</param>
    /// <param name="provider">The provider.</param>
    public static bool TryParse(string? name, out ExternalProvider provider)
    {
        provider = default;

        if (string.Equals(name, Google, StringComparison.OrdinalIgnoreCase))
        {
            provider = ExternalProvider.Google;
            return true;
        }

        if (string.Equals(name, Facebook, StringComparison.OrdinalIgnoreCase))
        {
            provider = ExternalProvider.Facebook;
            return true;
        }

        return false;
    }
}
