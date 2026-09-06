using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>A contiguous run of PIN codes, as a zone lists them.</summary>
/// <remarks>
/// A range rather than a prefix, because Indian delivery geography is banded by number and a zone is
/// almost always expressible as two or three runs. A prefix list for "the south" would be forty
/// entries; the ranges are three.
/// </remarks>
/// <param name="From">The first PIN code in the run, inclusive.</param>
/// <param name="To">The last PIN code in the run, inclusive.</param>
internal sealed record PincodeRange(string From, string To)
{
    /// <summary>Whether a PIN code falls inside this run.</summary>
    /// <param name="pincode">The six-digit code.</param>
    public bool Contains(string? pincode)
        => pincode is { Length: 6 }
           && string.CompareOrdinal(pincode, From) >= 0
           && string.CompareOrdinal(pincode, To) <= 0;
}

/// <summary>
/// A delivery region, and the first thing a rate card is looked up by
/// (docs/03-database-design.md §4.10).
/// </summary>
/// <remarks>
/// <para>
/// A zone is described two ways at once — by state and by PIN-code range — and either is enough to
/// match. That is not redundancy: "the north-east" is a list of states, "metro" is a list of PIN
/// ranges that cuts across states, and a platform that could only say one of them would have to
/// enumerate the other by hand.
/// </para>
/// <para>
/// <see cref="Priority"/> is what makes overlapping zones usable rather than ambiguous. Metro and
/// Karnataka both contain Bengaluru; the one with the lower priority number wins, and a store that
/// wants metro pricing to beat state pricing says so once instead of carving holes in its own map.
/// </para>
/// <para>
/// A zone with no states and no ranges matches everywhere. That is the rest-of-India fallback every
/// rate card needs, and it is why the seeded default has neither.
/// </para>
/// </remarks>
internal sealed class ShippingZone : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private ShippingZone(Guid id, string code, string name)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        States = [];
        PincodeRanges = [];
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ShippingZone()
    {
        Code = string.Empty;
        Name = string.Empty;
        States = [];
        PincodeRanges = [];
    }

    /// <summary>The stable code a rate row and an operator both refer to it by.</summary>
    public string Code { get; private set; }

    /// <summary>What it is called: Metro, South, North-East, Rest of India.</summary>
    public string Name { get; private set; }

    /// <summary>The <c>platform.states</c> rows this zone covers. Empty means "not decided by state".</summary>
    public IReadOnlyList<Guid> States { get; private set; }

    /// <summary>The PIN-code runs it covers. Empty means "not decided by PIN code".</summary>
    public IReadOnlyList<PincodeRange> PincodeRanges { get; private set; }

    /// <summary>Lower wins where two zones both match. The tie-break that makes overlap usable.</summary>
    public int Priority { get; private set; }

    /// <summary>Whether the zone is still used when a rate is looked up.</summary>
    public bool IsActive { get; private set; }

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

    /// <summary>Whether this zone covers nowhere in particular, and therefore everywhere.</summary>
    public bool IsCatchAll => States.Count == 0 && PincodeRanges.Count == 0;

    /// <summary>Opens a zone.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="priority">Lower wins where two zones both match.</param>
    public static ShippingZone Create(string code, string name, int priority)
        => new(UuidV7.New(), code, name) { Priority = priority };

    /// <summary>Renames it and re-orders it against its siblings.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="priority">Lower wins where two zones both match.</param>
    public void Update(string name, int priority)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Priority = priority;
    }

    /// <summary>Replaces the geography wholesale.</summary>
    /// <remarks>
    /// Wholesale rather than add-and-remove, because a zone map is edited as a whole in the admin
    /// screen and a partial update is how two operators end up with half of each other's map.
    /// </remarks>
    /// <param name="states">The states it covers.</param>
    /// <param name="ranges">The PIN-code runs it covers.</param>
    public void Redefine(IReadOnlyList<Guid> states, IReadOnlyList<PincodeRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(ranges);

        States = [.. states.Distinct()];
        PincodeRanges = [.. ranges];
    }

    /// <summary>Switches the zone on or off without deleting its rates.</summary>
    /// <param name="isActive">Whether it is used.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// Whether a destination falls in this zone.
    /// </summary>
    /// <remarks>
    /// Either description matching is enough. A zone that names both states and ranges is saying
    /// "these states, and also these PIN codes wherever they are", which is how a metro zone that
    /// happens to include one state entirely is expressed.
    /// </remarks>
    /// <param name="stateId">The destination's state.</param>
    /// <param name="pincode">The destination's six-digit PIN code.</param>
    public bool Covers(Guid? stateId, string? pincode)
    {
        if (IsCatchAll)
        {
            return true;
        }

        if (stateId is { } state && States.Contains(state))
        {
            return true;
        }

        return PincodeRanges.Any(range => range.Contains(pincode));
    }
}
