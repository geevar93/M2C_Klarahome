using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Pricing.Domain;

/// <summary>How a promotion computes what it takes off.</summary>
internal enum PromotionType
{
    /// <summary>A percentage of whatever it is scoped to.</summary>
    Percentage = 0,

    /// <summary>A flat amount.</summary>
    Fixed = 1,

    /// <summary>Shipping is not charged.</summary>
    FreeShipping = 2,

    /// <summary>Buy some, get some at a discount. The quantities are in the conditions.</summary>
    Bogo = 3,

    /// <summary>A named set of offers sold together for one price.</summary>
    Bundle = 4,

    /// <summary>A percentage or amount that steps up as the basket grows. The steps are in the conditions.</summary>
    Tiered = 5,
}

/// <summary>What a promotion reduces.</summary>
/// <remarks>
/// It is a column rather than an inference from <see cref="PromotionType"/> because the same type
/// means both things: "10% off sofas" is a line discount and "10% off your order" is an order
/// discount, and the two allocate, cap and tax differently.
/// </remarks>
internal enum PromotionApplication
{
    /// <summary>Each matching line, individually.</summary>
    Line = 0,

    /// <summary>The basket as a whole, allocated back across the matching lines pro rata.</summary>
    Order = 1,

    /// <summary>The shipping charge.</summary>
    Shipping = 2,
}

/// <summary>Whether a promotion tolerates company.</summary>
internal enum StackingMode
{
    /// <summary>Once it applies, nothing after it does.</summary>
    Exclusive = 0,

    /// <summary>It applies alongside anything else that also stacks.</summary>
    Stackable = 1,
}

/// <summary>
/// What a promotion applies to (docs/03-database-design.md §4.6). Stored as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// Every list is an "any of", and an empty list means "no restriction on this dimension". The two
/// combine as an intersection across dimensions: a promotion scoped to a brand and a category
/// applies to that brand's products in that category, not to either.
/// </remarks>
internal sealed class PromotionScope
{
    /// <summary>Categories the promotion covers, including everything beneath them.</summary>
    public List<Guid> CategoryIds { get; set; } = [];

    /// <summary>Brands it covers.</summary>
    public List<Guid> BrandIds { get; set; } = [];

    /// <summary>Sellers it covers.</summary>
    public List<Guid> VendorIds { get; set; } = [];

    /// <summary>Individual offers it covers.</summary>
    public List<Guid> ListingIds { get; set; } = [];

    /// <summary>
    /// Offers it explicitly does not cover, whatever else matches. Exclusions win over inclusions,
    /// which is what makes "everything in Furniture except the clearance rail" expressible.
    /// </summary>
    public List<Guid> ExcludedListingIds { get; set; } = [];

    /// <summary>Customer segments it is limited to. Empty means every shopper.</summary>
    public List<string> Segments { get; set; } = [];
}

/// <summary>One step of a tiered promotion: spend this much, get that much off.</summary>
/// <remarks>
/// Steps are evaluated highest-threshold-first, so a basket that clears several gets the best one
/// rather than the sum of all of them.
/// </remarks>
internal sealed class PromotionTier
{
    /// <summary>The basket value at or above which this step applies.</summary>
    public decimal MinAmount { get; set; }

    /// <summary>What this step is worth — a percentage or an amount, matching the promotion's type.</summary>
    public decimal Value { get; set; }
}

/// <summary>
/// The conditions a basket has to meet, beyond the scope (docs/03-database-design.md §4.6). Stored
/// as <c>jsonb</c>.
/// </summary>
/// <remarks>
/// Open-shaped on purpose. These are the levers a merchandiser pulls, they differ per promotion
/// type, and giving each one a column would mean a migration every time marketing had an idea.
/// </remarks>
internal sealed class PromotionConditions
{
    /// <summary>The fewest matching units a basket must hold. One means no condition.</summary>
    public int MinQuantity { get; set; } = 1;

    /// <summary>Whether only a shopper's first order qualifies.</summary>
    public bool FirstOrderOnly { get; set; }

    /// <summary>
    /// Payment methods it is limited to — <c>prepaid</c>, <c>cod</c>. Empty means any, which is
    /// how a "pay online and save" campaign is expressed without a second promotion type.
    /// </summary>
    public List<string> PaymentMethods { get; set; } = [];

    /// <summary>The most matching units one order may be discounted on, or null for no cap.</summary>
    public int? MaxQuantityPerOrder { get; set; }

    /// <summary>Buy-X-get-Y: how many units must be bought to earn a free set.</summary>
    public int BuyQuantity { get; set; } = 1;

    /// <summary>Buy-X-get-Y: how many units are earned per set bought.</summary>
    public int GetQuantity { get; set; } = 1;

    /// <summary>
    /// Buy-X-get-Y: how much comes off each earned unit, as a percentage. A hundred is the classic
    /// "buy one get one free"; anything less is "get one half price".
    /// </summary>
    public decimal GetDiscountPercent { get; set; } = 100m;

    /// <summary>The offers a bundle is made of. Every one must be in the basket for it to apply.</summary>
    public List<Guid> BundleListingIds { get; set; } = [];

    /// <summary>What the bundle sells for, inclusive of GST. The discount is the difference.</summary>
    public decimal BundlePrice { get; set; }

    /// <summary>The steps of a tiered promotion.</summary>
    public List<PromotionTier> Tiers { get; set; } = [];

    /// <summary>
    /// Whether a tier's value is a percentage rather than a flat amount. True by default, because
    /// "spend more, save a bigger share" is what a ladder nearly always means — and because the
    /// alternative reading of the same number is a hundredfold error.
    /// </summary>
    public bool TiersArePercentage { get; set; } = true;
}

/// <summary>
/// A discount rule: a coupon code, or an automatic cart rule
/// (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// One table for both, because they differ only in whether a shopper has to type something. A
/// separate "cart rules" table would duplicate the scope, the conditions, the window and the
/// limits, and the two copies would drift the first time somebody added a lever to one of them.
/// </para>
/// <para>
/// A flash sale is a promotion too. It is a row with a short window and a low priority number, and
/// there is deliberately no scheduling machinery beyond that: a campaign that opens because the
/// clock moved needs no job to open it.
/// </para>
/// </remarks>
internal sealed class Promotion : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private Promotion(Guid id, string? code, string name, PromotionType type, PromotionApplication appliesTo)
        : base(id)
    {
        Code = code;
        Name = Guard.NotNullOrWhiteSpace(name);
        Type = type;
        AppliesTo = appliesTo;
        Scope = new PromotionScope();
        Conditions = new PromotionConditions();
        Stacking = StackingMode.Exclusive;
        Priority = DefaultPriority;
        StartsAt = DateTimeOffset.MinValue;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Promotion()
    {
        Name = string.Empty;
        Scope = new PromotionScope();
        Conditions = new PromotionConditions();
    }

    /// <summary>The code a shopper types, or null for an automatic rule. Unique within the tenant.</summary>
    public string? Code { get; private set; }

    /// <summary>What it is called, on the cart's "you saved" line and in reports.</summary>
    public string Name { get; private set; }

    /// <summary>The customer-facing terms, shown next to the code.</summary>
    public string? Description { get; private set; }

    /// <summary>How it computes what it takes off.</summary>
    public PromotionType Type { get; private set; }

    /// <summary>What it reduces.</summary>
    public PromotionApplication AppliesTo { get; private set; }

    /// <summary>The percentage or amount, read according to <see cref="Type"/>.</summary>
    public decimal Value { get; private set; }

    /// <summary>What it applies to.</summary>
    public PromotionScope Scope { get; private set; }

    /// <summary>What a basket must satisfy beyond the scope.</summary>
    public PromotionConditions Conditions { get; private set; }

    /// <summary>Whether it tolerates company.</summary>
    public StackingMode Stacking { get; private set; }

    /// <summary>Evaluation order. Lower goes first, and an exclusive one that applies ends the walk.</summary>
    public int Priority { get; private set; }

    /// <summary>When it opens.</summary>
    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>When it closes, or null for open-ended.</summary>
    public DateTimeOffset? EndsAt { get; private set; }

    /// <summary>The most times it may ever be redeemed, or null for unlimited.</summary>
    public int? UsageLimitTotal { get; private set; }

    /// <summary>The most times one shopper may redeem it, or null for unlimited.</summary>
    public int? UsageLimitPerCustomer { get; private set; }

    /// <summary>
    /// How many times it has been redeemed. A derived cache of the redemption rows, incremented by
    /// a conditional update so two shoppers racing for the last use produce one redemption.
    /// </summary>
    public int UsageCount { get; private set; }

    /// <summary>The smallest basket it applies to, before discount. Zero means no minimum.</summary>
    public decimal MinOrderValue { get; private set; }

    /// <summary>The most it will ever take off, or null for uncapped. Caps a runaway percentage.</summary>
    public decimal? MaxDiscount { get; private set; }

    /// <summary>Whether it is considered at all.</summary>
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

    /// <summary>The middle of the range, so a promotion can be ordered either side of an existing one.</summary>
    public const int DefaultPriority = 100;

    /// <summary>The highest priority number this platform stores.</summary>
    public const int MaxPriority = 1000;

    /// <summary>Drafts a promotion. It is inactive until somebody activates it.</summary>
    /// <param name="code">The code a shopper types, or null for an automatic rule.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="type">How it computes what it takes off.</param>
    /// <param name="appliesTo">What it reduces.</param>
    public static Promotion Create(string? code, string name, PromotionType type, PromotionApplication appliesTo)
        => new(UuidV7.New(), NormalizeCode(code), name, type, appliesTo);

    /// <summary>Restates everything an operator may edit.</summary>
    /// <param name="name">What it is called.</param>
    /// <param name="description">The customer-facing terms.</param>
    /// <param name="type">How it computes what it takes off.</param>
    /// <param name="appliesTo">What it reduces.</param>
    /// <param name="value">The percentage or amount.</param>
    /// <param name="scope">What it applies to.</param>
    /// <param name="conditions">What a basket must satisfy.</param>
    /// <param name="stacking">Whether it tolerates company.</param>
    /// <param name="priority">Evaluation order.</param>
    /// <param name="startsAt">When it opens.</param>
    /// <param name="endsAt">When it closes.</param>
    /// <param name="usageLimitTotal">The most times it may ever be redeemed.</param>
    /// <param name="usageLimitPerCustomer">The most times one shopper may redeem it.</param>
    /// <param name="minOrderValue">The smallest basket it applies to.</param>
    /// <param name="maxDiscount">The most it will ever take off.</param>
    public void Update(
        string name,
        string? description,
        PromotionType type,
        PromotionApplication appliesTo,
        decimal value,
        PromotionScope scope,
        PromotionConditions conditions,
        StackingMode stacking,
        int priority,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        int? usageLimitTotal,
        int? usageLimitPerCustomer,
        decimal minOrderValue,
        decimal? maxDiscount)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Type = type;
        AppliesTo = appliesTo;
        Value = Math.Max(0m, value);
        Scope = Guard.NotNull(scope);
        Conditions = Guard.NotNull(conditions);
        Stacking = stacking;
        Priority = Math.Clamp(priority, 0, MaxPriority);
        StartsAt = startsAt;
        EndsAt = endsAt;
        UsageLimitTotal = usageLimitTotal is > 0 ? usageLimitTotal : null;
        UsageLimitPerCustomer = usageLimitPerCustomer is > 0 ? usageLimitPerCustomer : null;
        MinOrderValue = Math.Max(0m, minOrderValue);
        MaxDiscount = maxDiscount is > 0m ? maxDiscount : null;
    }

    /// <summary>Turns the promotion on or off, regardless of its window.</summary>
    /// <param name="isActive">Whether it is considered.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Whether the promotion is live at <paramref name="asOf"/>.</summary>
    /// <param name="asOf">The instant to test.</param>
    public bool IsLiveAt(DateTimeOffset asOf)
        => IsActive && StartsAt <= asOf && (EndsAt is null || EndsAt > asOf);

    /// <summary>Whether the global usage limit still has room.</summary>
    public bool HasUsesLeft => UsageLimitTotal is not { } limit || UsageCount < limit;

    /// <summary>Lowercases and trims a code so two spellings cannot be two promotions.</summary>
    /// <param name="code">The code as typed.</param>
    public static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}

/// <summary>How a redemption ended.</summary>
internal enum RedemptionStatus
{
    /// <summary>The order used it and stands.</summary>
    Redeemed = 0,

    /// <summary>The order was cancelled, and the use was given back.</summary>
    Reversed = 1,
}

/// <summary>
/// One use of a promotion by one order (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// Unique per <c>(promotion, order)</c>, which is what makes redemption idempotent under
/// at-least-once event delivery. A reversal marks the row rather than deleting it: a per-customer
/// limit that forgot a cancelled order would let one shopper cycle a single-use coupon for ever.
/// </remarks>
internal sealed class PromotionRedemption : Entity<Guid>, ITenantScoped, IAuditable
{
    private PromotionRedemption(
        Guid id,
        Guid promotionId,
        string? code,
        Guid? customerId,
        Guid orderId,
        decimal discountAmount,
        DateTimeOffset redeemedAt)
        : base(id)
    {
        PromotionId = promotionId;
        Code = code;
        CustomerId = customerId;
        OrderId = orderId;
        DiscountAmount = discountAmount;
        RedeemedAt = redeemedAt;
        Status = RedemptionStatus.Redeemed;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private PromotionRedemption()
    {
    }

    /// <summary>The promotion used.</summary>
    public Guid PromotionId { get; private set; }

    /// <summary>The code as it stood when it was used, so a later rename does not rewrite history.</summary>
    public string? Code { get; private set; }

    /// <summary>The shopper, when the order had one. Per-customer limits are counted from this.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>The order. A plain id: no foreign key crosses a schema.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>What it took off that order.</summary>
    public decimal DiscountAmount { get; private set; }

    /// <summary>When it was used.</summary>
    public DateTimeOffset RedeemedAt { get; private set; }

    /// <summary>When the use was given back, or null while it stands.</summary>
    public DateTimeOffset? ReversedAt { get; private set; }

    /// <summary>Whether it stands.</summary>
    public RedemptionStatus Status { get; private set; }

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

    /// <summary>Records a use.</summary>
    /// <param name="promotionId">The promotion.</param>
    /// <param name="code">Its code at the time.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="discountAmount">What it took off.</param>
    /// <param name="redeemedAt">When.</param>
    public static PromotionRedemption Create(
        Guid promotionId,
        string? code,
        Guid? customerId,
        Guid orderId,
        decimal discountAmount,
        DateTimeOffset redeemedAt)
        => new(UuidV7.New(), promotionId, code, customerId, orderId, discountAmount, redeemedAt);

    /// <summary>Gives the use back. Returns false when it had already been given back.</summary>
    /// <param name="reversedAt">When.</param>
    public bool Reverse(DateTimeOffset reversedAt)
    {
        if (Status == RedemptionStatus.Reversed)
        {
            return false;
        }

        Status = RedemptionStatus.Reversed;
        ReversedAt = reversedAt;

        return true;
    }
}
