using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// What an aggregator last said about one PIN code (docs/03-database-design.md §4.10).
/// </summary>
/// <remarks>
/// <para>
/// A cache with a table behind it rather than a memory cache, and that is the point of the design.
/// A product page and a checkout both have to answer "can you deliver here" in milliseconds, and
/// neither may call an aggregator to do it — an API on the hot path is an API that takes the
/// storefront down with it (docs/08-integrations.md §2). A row that is a day old is a better answer
/// than a spinner.
/// </para>
/// <para>
/// Prepaid and cash on delivery are separate flags because they genuinely differ: plenty of Indian
/// PIN codes accept a prepaid parcel and refuse to handle cash, and a platform that had one flag
/// would either lose those orders or promise a collection nobody will make.
/// </para>
/// <para>
/// <see cref="RefreshedAt"/> is what the nightly job works from and what an operator reads when the
/// answer looks wrong. There is no expiry column: freshness is a policy the reader applies, and
/// baking it into the row would mean rewriting the table to change the policy.
/// </para>
/// </remarks>
internal sealed class ServiceabilityEntry : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private ServiceabilityEntry(Guid id, string pincode, string courier)
        : base(id)
    {
        Pincode = Guard.NotNullOrWhiteSpace(pincode);
        Courier = Guard.NotNullOrWhiteSpace(courier);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ServiceabilityEntry()
    {
        Pincode = string.Empty;
        Courier = string.Empty;
    }

    /// <summary>The six-digit destination.</summary>
    public string Pincode { get; private set; }

    /// <summary>
    /// The courier the answer is about, or the aggregator's own name when it answers for all of them.
    /// </summary>
    public string Courier { get; private set; }

    /// <summary>Whether a prepaid parcel can be delivered here.</summary>
    public bool PrepaidOk { get; private set; }

    /// <summary>Whether cash can be collected here. Frequently false where prepaid is true.</summary>
    public bool CodOk { get; private set; }

    /// <summary>Whether a reverse pickup can be collected here, for a return.</summary>
    public bool PickupOk { get; private set; }

    /// <summary>How long the courier says it takes, in days. Null when it does not say.</summary>
    public int? EtaDays { get; private set; }

    /// <summary>The heaviest parcel the courier will take here, in grams. Null for no stated limit.</summary>
    public int? MaxWeightGrams { get; private set; }

    /// <summary>When the answer was last obtained.</summary>
    public DateTimeOffset RefreshedAt { get; private set; }

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

    /// <summary>Records an answer for a PIN code that has never been asked about.</summary>
    /// <param name="pincode">The six-digit destination.</param>
    /// <param name="courier">The courier, or the aggregator answering for all of them.</param>
    /// <param name="refreshedAt">When the answer was obtained.</param>
    public static ServiceabilityEntry Create(string pincode, string courier, DateTimeOffset refreshedAt)
        => new(UuidV7.New(), pincode, courier) { RefreshedAt = refreshedAt };

    /// <summary>Replaces the answer with a fresher one.</summary>
    /// <param name="prepaidOk">Whether a prepaid parcel can be delivered.</param>
    /// <param name="codOk">Whether cash can be collected.</param>
    /// <param name="pickupOk">Whether a reverse pickup can be collected.</param>
    /// <param name="etaDays">How long the courier says it takes.</param>
    /// <param name="maxWeightGrams">The heaviest parcel the courier will take.</param>
    /// <param name="refreshedAt">When the answer was obtained.</param>
    public void Record(
        bool prepaidOk,
        bool codOk,
        bool pickupOk,
        int? etaDays,
        int? maxWeightGrams,
        DateTimeOffset refreshedAt)
    {
        PrepaidOk = prepaidOk;
        CodOk = codOk;
        PickupOk = pickupOk;
        EtaDays = etaDays;
        MaxWeightGrams = maxWeightGrams;
        RefreshedAt = refreshedAt;
    }

    /// <summary>Whether the answer is recent enough for the reader's purposes.</summary>
    /// <param name="now">The current instant.</param>
    /// <param name="maxAge">How old an answer the reader will accept.</param>
    public bool IsFresh(DateTimeOffset now, TimeSpan maxAge) => now - RefreshedAt <= maxAge;
}
