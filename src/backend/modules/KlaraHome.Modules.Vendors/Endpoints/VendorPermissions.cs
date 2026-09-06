namespace KlaraHome.Modules.Vendors.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the
/// admin UI lists and what a role grants. The duplication is deliberate and is what the module
/// boundary costs: a module may not reference another module, so the two lists are kept in step by
/// a test that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement the Media module uses.
/// </remarks>
internal static class VendorPermissions
{
    /// <summary>List sellers, read one, and read their documents, accounts and locations.</summary>
    public const string VendorRead = "vendors.vendor.read";

    /// <summary>
    /// Create a seller and change their details, profile, settings, staff, bank accounts and
    /// pickup locations. Held by platform staff and by a vendor owner, who is confined to their own.
    /// </summary>
    public const string VendorManage = "vendors.vendor.manage";

    /// <summary>
    /// Move a seller through the onboarding life cycle. Platform staff only — a seller holding this
    /// could approve themselves, which is why it is not part of <see cref="VendorManage"/>.
    /// </summary>
    public const string VendorApprove = "vendors.vendor.approve";

    /// <summary>Accept or refuse a KYC document, and record a bank-account check.</summary>
    public const string KycVerify = "vendors.kyc.verify";

    /// <summary>Create and edit commission plans, and put a seller on one.</summary>
    public const string CommissionManage = "vendors.commission.manage";
}
