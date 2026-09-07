using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Authorization;

/// <summary>
/// The requirement behind a <c>perm:</c> policy: the principal must carry this exact permission.
/// </summary>
/// <param name="Permission">The granular permission, for example <c>platform.settings.manage</c>.</param>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Turns <see cref="RequiredPermissionMetadata"/> into an authorisation policy name, and back.
/// </summary>
/// <remarks>
/// Policies are named rather than registered, so adding an endpoint never means remembering to add
/// a matching <c>AddPolicy</c> call somewhere else. <see cref="PermissionPolicyProvider"/>
/// manufactures the policy on first use from the name alone.
/// </remarks>
public static class PermissionPolicy
{
    /// <summary>Marks a policy name as one this provider owns.</summary>
    public const string Prefix = "perm:";

    /// <summary>The policy name for a permission.</summary>
    /// <param name="permission">The granular permission.</param>
    public static string NameFor(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return Prefix + permission;
    }

    /// <summary>The permission a policy name encodes, or null if the name is not one of ours.</summary>
    /// <param name="policyName">The policy name.</param>
    public static string? PermissionOf(string policyName)
        => policyName is not null && policyName.StartsWith(Prefix, StringComparison.Ordinal)
            ? policyName[Prefix.Length..]
            : null;
}

/// <summary>
/// Manufactures a policy for every <c>perm:</c> name, and supplies the deny-by-default fallback.
/// </summary>
/// <remarks>
/// <para>
/// The fallback policy is what makes "every endpoint is deny-by-default"
/// (docs/07-security-compliance.md §2) true rather than aspirational: an endpoint with no
/// authorisation metadata gets this policy, so forgetting to protect one closes it rather than
/// opening it. Public endpoints say <c>AllowAnonymous()</c> out loud.
/// </para>
/// <para>
/// Any name this provider does not recognise is handed to the framework's own provider, so a
/// module can still register a conventional named policy if it ever needs one.
/// </para>
/// </remarks>
/// <param name="options">The framework's policy options, for names this provider does not own.</param>
/// <param name="schemes">Whether any authentication scheme exists at all.</param>
public sealed class PermissionPolicyProvider(
    IOptions<AuthorizationOptions> options,
    IAuthenticationSchemeProvider schemes) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    /// <inheritdoc />
    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var permission = PermissionPolicy.PermissionOf(policyName);

        if (permission is null)
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    /// <inheritdoc />
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    /// <summary>Deny by default: an endpoint that declares nothing requires an authenticated caller.</summary>
    /// <remarks>
    /// A host with no authentication scheme registered gets no fallback rather than a policy
    /// nothing could satisfy. That is the difference between "not wired for auth", which
    /// <see cref="UnsecuredEndpointGuard"/> already refuses outside Development, and "every
    /// request is 401 and nothing says why".
    /// </remarks>
    public async Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        var registered = await schemes.GetAllSchemesAsync().ConfigureAwait(false);

        return registered.Any()
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
            : null;
    }
}

/// <summary>Succeeds when the principal carries the required permission as a claim.</summary>
/// <remarks>
/// Permissions are read from the token rather than the database on every request. The access token
/// is short-lived precisely so that this is safe: a permission taken away is gone within its
/// lifetime, and immediately for anything that revokes the session
/// (docs/07-security-compliance.md §1).
/// </remarks>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        foreach (var claim in context.User.FindAll(KlaraHomeClaims.Permission))
        {
            if (string.Equals(claim.Value, requirement.Permission, StringComparison.Ordinal))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// The caller of the current request, as their access token describes them, so no handler parses
/// claims by hand.
/// </summary>
public interface ICallerContext
{
    /// <summary>Whether the request carries a valid access token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The authenticated subject, or null.</summary>
    Guid? UserId { get; }

    /// <summary>The session the access token belongs to, or null.</summary>
    Guid? SessionId { get; }

    /// <summary>
    /// The seller this caller acts for, or null for a customer, platform staff or background work.
    /// Read by the vendor query filter, so it decides which rows exist for this request at all.
    /// </summary>
    Guid? VendorId { get; }

    /// <summary>The class of actor: <c>customer</c>, <c>vendor</c>, <c>staff</c>, or null.</summary>
    string? UserType { get; }

    /// <summary>A named cohort this caller belongs to, for feature-flag segment rollout.</summary>
    string? Segment { get; }

    /// <summary>
    /// The support user acting as <see cref="UserId"/>, or null for an ordinary session
    /// (docs/07-security-compliance.md §2). Non-null means every action this request takes was
    /// really taken by somebody else, which is what the audit trail has to say.
    /// </summary>
    Guid? ImpersonatorId { get; }

    /// <summary>Whether the caller holds a permission, for checks that are not endpoint-shaped.</summary>
    /// <param name="permission">The granular permission.</param>
    bool HasPermission(string permission);
}

/// <summary>Reads the caller from the request principal.</summary>
/// <param name="accessor">The ambient request.</param>
internal sealed class ClaimsCallerContext(IHttpContextAccessor accessor) : ICallerContext
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId => ClaimGuid(KlaraHomeClaims.UserId);

    public Guid? SessionId => ClaimGuid(KlaraHomeClaims.SessionId);

    public Guid? VendorId => ClaimGuid(KlaraHomeClaims.VendorId);

    public string? UserType => Claim(KlaraHomeClaims.UserType);

    public Guid? ImpersonatorId => ClaimGuid(KlaraHomeClaims.ImpersonatorId);

    public string? Segment => Claim(KlaraHomeClaims.Segment);

    public bool HasPermission(string permission)
        => IsAuthenticated
           && accessor.HttpContext!.User
               .FindAll(KlaraHomeClaims.Permission)
               .Any(claim => string.Equals(claim.Value, permission, StringComparison.Ordinal));

    private string? Claim(string type)
        => IsAuthenticated ? accessor.HttpContext!.User.FindFirst(type)?.Value : null;

    private Guid? ClaimGuid(string type)
        => Guid.TryParse(Claim(type), CultureInfo.InvariantCulture, out var id) ? id : null;
}

/// <summary>The non-HTTP caller: background work acts as the system, with no vendor scope.</summary>
internal sealed class SystemCallerContext : ICallerContext
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public Guid? SessionId => null;

    public Guid? VendorId => null;

    public string? UserType => null;

    public string? Segment => null;

    public Guid? ImpersonatorId => null;

    public bool HasPermission(string permission) => false;
}
