using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>The services a store sells delivery as.</summary>
/// <remarks>
/// Two, and deliberately not a free-text field. The shopper is choosing between "cheaper" and
/// "sooner", and a rate card with nine named services is a checkout nobody can read. A courier's own
/// service names live on the shipment, where they belong.
/// </remarks>
internal enum ShippingMethod
{
    /// <summary>The default service. What a rate card must always have.</summary>
    Standard = 0,

    /// <summary>Faster and dearer, where the store offers it.</summary>
    Express = 1,
}

/// <summary>
/// One rule of a rate card (docs/03-database-design.md §4.10).
/// </summary>
/// <remarks>
/// <para>
/// This decides what the <b>customer</b> pays. What the <b>platform</b> pays is whatever the
/// aggregator invoices, and both are recorded on the shipment so the margin on delivery is a query
/// rather than a guess (docs/08-integrations.md §2).
/// </para>
/// <para>
/// A rule is selected by zone, method, weight band and order-value band, all of which must match. The
/// bands are how one zone carries a whole tariff: under half a kilo, half to two, two to five, and
/// so on. Where two rules still match, the one with the narrower weight band wins — a specific rule
/// beats a general one, which is the only tie-break that makes a tariff editable without fear.
/// </para>
/// <para>
/// <see cref="VendorId"/> is the override. A seller who has negotiated their own delivery pricing
/// gets rows of their own, and they beat the platform's for that seller only. It is a nullable
/// column rather than a separate table because the selection logic is identical and duplicating it
/// is how the two drift.
/// </para>
/// </remarks>
internal sealed class ShippingRate : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    /// <summary>What a base rate buys when the band starts at nothing.</summary>
    /// <remarks>
    /// A band whose floor is zero is a card saying "up to this weight, this price". Without this the
    /// first gram would buy a whole extra kilogram, and the cheapest band on every rate card would
    /// silently cost twice what it says.
    /// </remarks>
    private const int FirstKilogramGrams = 1_000;

    private ShippingRate(Guid id, Guid zoneId, ShippingMethod method)
        : base(id)
    {
        ZoneId = Guard.NotEmpty(zoneId);
        Method = method;
        CurrencyCode = Money.Inr;
        IsActive = true;
        MaxWeightGrams = int.MaxValue;
        EtaMinDays = 3;
        EtaMaxDays = 7;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ShippingRate() => CurrencyCode = Money.Inr;

    /// <summary>The zone this rule prices.</summary>
    public Guid ZoneId { get; private set; }

    /// <inheritdoc />
    /// <remarks>Null for the platform's own rate card; set for a seller's override.</remarks>
    public Guid? VendorId { get; private set; }

    /// <summary>Which service it prices.</summary>
    public ShippingMethod Method { get; private set; }

    /// <summary>The lightest parcel this rule applies to, in grams, inclusive.</summary>
    public int MinWeightGrams { get; private set; }

    /// <summary>The heaviest parcel it applies to, in grams, inclusive.</summary>
    public int MaxWeightGrams { get; private set; }

    /// <summary>The smallest basket it applies to, inclusive. Zero for no floor.</summary>
    public decimal MinOrderValue { get; private set; }

    /// <summary>The largest basket it applies to, inclusive. Null for no ceiling.</summary>
    public decimal? MaxOrderValue { get; private set; }

    /// <summary>What the parcel costs at the band's minimum weight, inclusive of tax.</summary>
    public decimal BaseRate { get; private set; }

    /// <summary>What each further kilogram above the band's minimum adds, inclusive of tax.</summary>
    public decimal PerKgRate { get; private set; }

    /// <summary>The basket value at or above which delivery is free. Null for never.</summary>
    public decimal? FreeAbove { get; private set; }

    /// <summary>What cash on delivery adds, inclusive of tax. It survives the free-shipping threshold.</summary>
    public decimal CodFee { get; private set; }

    /// <summary>Whether cash on delivery may be chosen on this service at all.</summary>
    public bool IsCodAllowed { get; private set; }

    /// <summary>ISO 4217 code every amount here is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The earliest the courier is promised to deliver, in days from dispatch.</summary>
    public int EtaMinDays { get; private set; }

    /// <summary>The latest, in days from dispatch. What the checkout promises.</summary>
    public int EtaMaxDays { get; private set; }

    /// <summary>Whether the rule is used when a rate is looked up.</summary>
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

    /// <summary>How wide the weight band is. The tie-break: narrower wins.</summary>
    public long WeightBandWidth => (long)MaxWeightGrams - MinWeightGrams;

    /// <summary>Opens a rate rule.</summary>
    /// <param name="zoneId">The zone it prices.</param>
    /// <param name="method">Which service.</param>
    /// <param name="vendorId">The seller it overrides for, or null for the platform's card.</param>
    public static ShippingRate Create(Guid zoneId, ShippingMethod method, Guid? vendorId)
        => new(UuidV7.New(), zoneId, method) { VendorId = vendorId };

    /// <summary>Sets the band this rule applies within.</summary>
    /// <param name="minWeightGrams">The lightest parcel, inclusive.</param>
    /// <param name="maxWeightGrams">The heaviest, inclusive.</param>
    /// <param name="minOrderValue">The smallest basket, inclusive.</param>
    /// <param name="maxOrderValue">The largest, inclusive, or null for none.</param>
    public void Band(
        int minWeightGrams,
        int maxWeightGrams,
        decimal minOrderValue,
        decimal? maxOrderValue)
    {
        MinWeightGrams = Math.Max(0, minWeightGrams);
        MaxWeightGrams = Math.Max(MinWeightGrams, maxWeightGrams);
        MinOrderValue = Guard.NotNegative(minOrderValue);
        MaxOrderValue = maxOrderValue is { } ceiling ? Math.Max(MinOrderValue, ceiling) : null;
    }

    /// <summary>Sets what it charges.</summary>
    /// <param name="baseRate">The band's floor price, inclusive of tax.</param>
    /// <param name="perKgRate">What each further kilogram adds, inclusive of tax.</param>
    /// <param name="freeAbove">The basket value at or above which delivery is free.</param>
    /// <param name="codFee">What cash on delivery adds.</param>
    /// <param name="isCodAllowed">Whether cash on delivery may be chosen at all.</param>
    public void Price(
        decimal baseRate,
        decimal perKgRate,
        decimal? freeAbove,
        decimal codFee,
        bool isCodAllowed)
    {
        BaseRate = Guard.NotNegative(baseRate);
        PerKgRate = Guard.NotNegative(perKgRate);
        FreeAbove = freeAbove is { } threshold ? Guard.NotNegative(threshold) : null;
        CodFee = Guard.NotNegative(codFee);
        IsCodAllowed = isCodAllowed;
    }

    /// <summary>Sets what it promises.</summary>
    /// <param name="minDays">The earliest, in days from dispatch.</param>
    /// <param name="maxDays">The latest, in days from dispatch.</param>
    public void Promise(int minDays, int maxDays)
    {
        EtaMinDays = Math.Max(0, minDays);
        EtaMaxDays = Math.Max(EtaMinDays, maxDays);
    }

    /// <summary>Switches the rule on or off.</summary>
    /// <param name="isActive">Whether it is used.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Whether this rule covers a parcel of a given weight out of a basket of a given value.</summary>
    /// <param name="chargeableWeightGrams">The weight the courier prices on.</param>
    /// <param name="orderValue">What the seller's part of the basket comes to.</param>
    public bool Applies(int chargeableWeightGrams, decimal orderValue)
        => chargeableWeightGrams >= MinWeightGrams
           && chargeableWeightGrams <= MaxWeightGrams
           && orderValue >= MinOrderValue
           && (MaxOrderValue is not { } ceiling || orderValue <= ceiling);

    /// <summary>
    /// What this rule charges for one parcel, inclusive of tax.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base rate buys the band's floor weight, and where the floor is zero it buys the first
    /// kilogram — which is what a rate card means when it says "up to 500 g: ₹49". Above that,
    /// every part-kilogram is charged whole, as every Indian courier does and as a customer who has
    /// ever posted a parcel expects. Rounding down instead would make the platform pay the
    /// difference on every heavy parcel.
    /// </para>
    /// <para>
    /// The free-shipping threshold zeroes the freight and <b>not</b> the cash-on-delivery fee. The
    /// fee is what the courier charges to handle money, and a store that gives away delivery has not
    /// thereby agreed to hand over that too.
    /// </para>
    /// </remarks>
    /// <param name="chargeableWeightGrams">The weight the courier prices on.</param>
    /// <param name="orderValue">What the seller's part of the basket comes to.</param>
    /// <param name="isCod">Whether cash will be collected at the door.</param>
    public decimal AmountFor(int chargeableWeightGrams, decimal orderValue, bool isCod)
    {
        var freight = 0m;

        if (FreeAbove is not { } threshold || orderValue < threshold)
        {
            var covered = MinWeightGrams > 0 ? MinWeightGrams : FirstKilogramGrams;
            var over = Math.Max(0, chargeableWeightGrams - covered);
            var extraKilos = (int)Math.Ceiling(over / 1000m);

            freight = BaseRate + (PerKgRate * extraKilos);
        }

        return Math.Round(freight + (isCod ? CodFee : 0m), 2, MidpointRounding.AwayFromZero);
    }
}
