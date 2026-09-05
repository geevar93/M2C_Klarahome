namespace KlaraHome.Infrastructure.Authorization;

/// <summary>
/// The claim types the access token carries (docs/07-security-compliance.md §1). Named here, in
/// the shared layer, rather than in the Identity module: the module that issues them and the
/// several that read them must agree, and a string repeated in two assemblies is a string that
/// eventually differs in one.
/// </summary>
public static class KlaraHomeClaims
{
    /// <summary>The authenticated subject — the user's id.</summary>
    public const string UserId = "sub";

    /// <summary>The tenant the token was issued for. Checked against the ambient tenant.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Which class of actor this is: <c>customer</c>, <c>vendor</c> or <c>staff</c>.</summary>
    public const string UserType = "user_type";

    /// <summary>The seller a vendor user acts for. Absent for customers and platform staff.</summary>
    public const string VendorId = "vendor_id";

    /// <summary>One entry per granular permission the user holds. Repeated, not comma-joined.</summary>
    public const string Permission = "permissions";

    /// <summary>The session the token belongs to, so a revoked session invalidates its tokens.</summary>
    public const string SessionId = "session_id";

    /// <summary>A named cohort, read by the feature-flag evaluator's segment rollout.</summary>
    public const string Segment = "segment";
}
