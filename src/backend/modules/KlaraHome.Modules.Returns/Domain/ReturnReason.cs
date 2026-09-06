using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Returns.Domain;

/// <summary>
/// A reason a shopper may give, and the policy that follows from it
/// (docs/03-database-design.md §4.11).
/// </summary>
/// <remarks>
/// <para>
/// Data rather than an enum, and the policy travels with the code rather than being decided in a
/// handler. "Damaged in transit" needs a photograph and the store pays the freight; "ordered by
/// mistake" needs neither and the shopper does. Those are commercial decisions that change, and a
/// deployment that had to ship code to change one would simply never change it.
/// </para>
/// <para>
/// <see cref="RequiresQc"/> is the interesting one. A store that trusts a reason can waive the
/// inspection entirely — for a cheap item, a van and an inspector cost more than the goods — and
/// <see cref="IsPickupRequired"/> false means the shopper keeps them. Both are refunds paid on the
/// shopper's word, which is a decision a business makes deliberately and not one a developer makes
/// for it.
/// </para>
/// </remarks>
internal sealed class ReturnReason : Entity<Guid>, ITenantScoped, IAuditable
{
    private ReturnReason(Guid id, string code, string label)
        : base(id)
    {
        Code = code;
        Label = label;
        IsActive = true;
        IsPickupRequired = true;
        RequiresQc = true;
        ShippingPayer = ReturnShippingPayer.Platform;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ReturnReason()
    {
        Code = string.Empty;
        Label = string.Empty;
    }

    /// <summary>The stable code a return names. Never reused once issued.</summary>
    public string Code { get; private set; }

    /// <summary>What the shopper reads on the dropdown.</summary>
    public string Label { get; private set; }

    /// <summary>A sentence of help beneath it, when the label alone is not enough.</summary>
    public string? Description { get; private set; }

    /// <summary>Whether it is offered. An inactive reason still explains returns already raised under it.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Lower sorts first on the shopper's dropdown.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Whether a shopper must attach a photograph to use it.</summary>
    public bool RequiresEvidence { get; private set; }

    /// <summary>Whether a courier collects, or the shopper keeps the goods.</summary>
    public bool IsPickupRequired { get; private set; }

    /// <summary>Whether the goods are inspected before the money goes back.</summary>
    public bool RequiresQc { get; private set; }

    /// <summary>
    /// Whether a return under this reason is approved without a human.
    /// </summary>
    /// <remarks>
    /// Independent of the store-wide value threshold, and both apply: a reason marked automatic
    /// still waits for review above the configured amount. A store wanting the opposite has said so
    /// by setting the threshold, which is the setting that means "trust everything below this".
    /// </remarks>
    public bool IsAutoApproved { get; private set; }

    /// <summary>Who bears the cost of the reverse pickup under this reason.</summary>
    public ReturnShippingPayer ShippingPayer { get; private set; }

    /// <summary>Whether the fault is the seller's, which is what decides who is charged at settlement.</summary>
    /// <remarks>
    /// Carried on the reason rather than judged per return, because it is the one fact Settlements
    /// needs and the one nobody wants argued case by case: a damaged parcel is the seller's cost, a
    /// change of mind is the platform's, and the list of which is which is a policy.
    /// </remarks>
    public bool IsVendorFault { get; private set; }

    /// <summary>Whether a replacement may be asked for under this reason.</summary>
    public bool AllowsReplacement { get; private set; } = true;

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Opens a reason code.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="label">What the shopper reads.</param>
    public static ReturnReason Create(string code, string label)
        => new(
            UuidV7.New(),
            Guard.NotNullOrWhiteSpace(code).Trim().ToLowerInvariant(),
            Guard.NotNullOrWhiteSpace(label));

    /// <summary>Renames it and re-orders it.</summary>
    /// <param name="label">What the shopper reads.</param>
    /// <param name="description">The help beneath it.</param>
    /// <param name="sortOrder">Where it sits on the dropdown.</param>
    public void Describe(string label, string? description, int sortOrder)
    {
        Label = Guard.NotNullOrWhiteSpace(label);
        Description = description;
        SortOrder = sortOrder;
    }

    /// <summary>Sets the policy that follows from it.</summary>
    /// <param name="requiresEvidence">Whether a photograph is needed.</param>
    /// <param name="isPickupRequired">Whether a courier collects.</param>
    /// <param name="requiresQc">Whether the goods are inspected.</param>
    /// <param name="isAutoApproved">Whether it is approved without a human.</param>
    /// <param name="shippingPayer">Who pays the freight.</param>
    /// <param name="isVendorFault">Whether the seller bears the cost at settlement.</param>
    /// <param name="allowsReplacement">Whether a replacement may be asked for.</param>
    public void Govern(
        bool requiresEvidence,
        bool isPickupRequired,
        bool requiresQc,
        bool isAutoApproved,
        ReturnShippingPayer shippingPayer,
        bool isVendorFault,
        bool allowsReplacement)
    {
        RequiresEvidence = requiresEvidence;
        IsPickupRequired = isPickupRequired;
        RequiresQc = requiresQc;
        IsAutoApproved = isAutoApproved;
        ShippingPayer = shippingPayer;
        IsVendorFault = isVendorFault;
        AllowsReplacement = allowsReplacement;
    }

    /// <summary>Offers it, or withdraws it.</summary>
    /// <param name="isActive">Whether it is offered.</param>
    public void SetActive(bool isActive) => IsActive = isActive;
}

/// <summary>Who bears the cost of sending goods back.</summary>
/// <remarks>
/// This module's own enum for the words <c>ReturnShippingPayers</c> spells as strings in the
/// settings section. The settings copy is JSON an operator edits; this one is a column with a check
/// constraint behind it, and the mapping happens once where the two meet.
/// </remarks>
internal enum ReturnShippingPayer
{
    /// <summary>The store pays.</summary>
    Platform = 0,

    /// <summary>The seller pays.</summary>
    Vendor = 1,

    /// <summary>The shopper pays, and the fee comes off what goes back to them.</summary>
    Customer = 2,
}

/// <summary>The reason codes a fresh deployment starts with.</summary>
/// <remarks>
/// The list every Indian marketplace converges on, and the policy attached to each is the one a
/// consumer forum would expect: a fault the seller caused is collected free and inspected; a change
/// of mind is collected at the shopper's cost. All of it is editable on the day it is wrong.
/// </remarks>
internal static class ReturnReasonCodes
{
    /// <summary>The parcel or its contents arrived broken.</summary>
    public const string DamagedInTransit = "damaged-in-transit";

    /// <summary>The wrong thing was sent.</summary>
    public const string WrongItem = "wrong-item";

    /// <summary>Something that should have been in the parcel was not.</summary>
    public const string MissingItem = "missing-item";

    /// <summary>It does not work.</summary>
    public const string Defective = "defective";

    /// <summary>It is not what the listing described.</summary>
    public const string NotAsDescribed = "not-as-described";

    /// <summary>The wrong size, which for homeware is the commonest reason of all.</summary>
    public const string SizeIssue = "size-issue";

    /// <summary>They simply do not want it.</summary>
    public const string ChangedMind = "changed-mind";

    /// <summary>It arrived too late to be of use.</summary>
    public const string LateDelivery = "late-delivery";
}
