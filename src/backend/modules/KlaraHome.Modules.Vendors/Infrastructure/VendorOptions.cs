using System.ComponentModel.DataAnnotations;
using KlaraHome.Modules.Vendors.Domain;

namespace KlaraHome.Modules.Vendors.Infrastructure;

/// <summary>
/// What this marketplace demands of a seller before it lets them trade.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, deliberately. These are the operator's compliance
/// posture — whether an unverified bank account is acceptable, whether a GSTIN is mandatory — and a
/// shopkeeper must not be able to lower them from an admin screen while the platform carries the
/// regulatory risk. The things a shopkeeper genuinely decides, such as the return window, are store
/// settings and live in the Platform module.
/// </remarks>
internal sealed class VendorOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Vendors";

    /// <summary>
    /// The prefix of a generated vendor code, before the sequence — <c>VND-000017</c>.
    /// </summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,7}$")]
    public string CodePrefix { get; set; } = "VND";

    /// <summary>
    /// Whether a seller must hold a GST registration before they can be approved.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is the legally correct default rather than a lenient one: a seller
    /// below the turnover threshold is not required to register, and refusing them would exclude
    /// exactly the small sellers a marketplace exists to carry. An operator who only wants
    /// registered sellers turns it on.
    /// </remarks>
    public bool RequireGstin { get; set; }

    /// <summary>
    /// Whether every KYC document the seller's legal form requires must be <c>Verified</c> before
    /// they may be approved.
    /// </summary>
    /// <remarks>
    /// On by default. Approving a seller whose PAN nobody has looked at is how a marketplace ends
    /// up filing a TDS return against a number that does not exist.
    /// </remarks>
    public bool RequireVerifiedKyc { get; set; } = true;

    /// <summary>
    /// Whether a seller must have a primary bank account, verified, before they may be activated.
    /// </summary>
    /// <remarks>
    /// On by default. A seller with no payable account can take orders they will never be paid for,
    /// and the discovery happens at the first settlement run rather than at onboarding.
    /// </remarks>
    public bool RequireVerifiedBankAccount { get; set; } = true;

    /// <summary>
    /// Whether a seller must have at least one pickup location before they may be activated.
    /// </summary>
    /// <remarks>
    /// On by default: a confirmed order with nowhere for a courier to collect from is a cancellation
    /// waiting to be explained to a customer.
    /// </remarks>
    public bool RequirePickupLocation { get; set; } = true;

    /// <summary>The dispatch SLA a new seller starts on, in hours.</summary>
    [Range(1, Vendor.MaxDispatchSlaHours)]
    public int DefaultDispatchSlaHours { get; set; } = Vendor.DefaultDispatchSlaHours;

    /// <summary>The return window, in days, that a new seller's policy starts with.</summary>
    [Range(0, 90)]
    public int DefaultReturnWindowDays { get; set; } = 7;
}
