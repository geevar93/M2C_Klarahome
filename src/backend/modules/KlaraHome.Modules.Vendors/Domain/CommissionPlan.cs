using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Vendors.Domain;

/// <summary>How a plan charges (docs/03-database-design.md §4.3).</summary>
/// <remarks>
/// The type is a description of the plan's shape, not a second calculation path: every plan
/// resolves to a rate and a fixed fee, and the type says which of the two an operator should expect
/// to be looking at. It is what lets the admin UI show a percentage plan without a fee column.
/// </remarks>
internal enum CommissionPlanType
{
    /// <summary>A flat amount per unit sold, whatever it sold for.</summary>
    Flat = 0,

    /// <summary>A percentage of the selling price.</summary>
    Percentage = 1,

    /// <summary>A percentage that changes with the selling price, expressed as price-banded rules.</summary>
    Tiered = 2,
}

/// <summary>
/// What the platform charges its sellers (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// <para>
/// A plan is a default rate plus a list of rules that override it. Resolution is
/// <see cref="Resolve"/>: the most specific category first, then the price band, then the plan's
/// own default — and it is defined here, on the aggregate, so that the answer does not depend on
/// which caller asked or on a query somebody wrote by hand.
/// </para>
/// <para>
/// Plans are shared: many sellers point at one plan, and changing it changes what all of them are
/// charged from that moment on. It does not change what they were already charged — Settlements
/// stores the numbers it was quoted, which is what makes a historical statement reproducible.
/// </para>
/// </remarks>
internal sealed class CommissionPlan : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private readonly List<CommissionPlanRule> _rules = [];

    private CommissionPlan(Guid id, string code, string name, CommissionPlanType planType, decimal defaultRate)
        : base(id)
    {
        Code = Guard.NotNullOrWhiteSpace(code);
        Name = Guard.NotNullOrWhiteSpace(name);
        PlanType = planType;
        DefaultRate = Guard.NotNegative(defaultRate);
        DefaultFixedFee = Money.Rupees(0m);
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CommissionPlan()
    {
        Code = string.Empty;
        Name = string.Empty;
        DefaultFixedFee = Money.Rupees(0m);
    }

    /// <summary>The stable lowercase code. Unique per tenant.</summary>
    public string Code { get; private set; }

    /// <summary>The name an operator sees, and that a settlement statement quotes.</summary>
    public string Name { get; private set; }

    /// <summary>What the plan is for. Free text.</summary>
    public string? Description { get; private set; }

    /// <summary>The shape of the plan.</summary>
    public CommissionPlanType PlanType { get; private set; }

    /// <summary>The rate charged when no rule matches, as a percentage. <c>12.5000</c> is 12.5%.</summary>
    public decimal DefaultRate { get; private set; }

    /// <summary>The per-unit fee charged when no rule matches. Usually zero.</summary>
    public Money DefaultFixedFee { get; private set; }

    /// <summary>Whether the plan may be assigned. An inactive plan keeps working for sellers already on it.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Whether a new seller is put on this plan when nobody chooses one.</summary>
    public bool IsDefault { get; private set; }

    /// <summary>The overrides, most specific first once <see cref="Resolve"/> has ordered them.</summary>
    public IReadOnlyList<CommissionPlanRule> Rules => _rules;

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

    /// <summary>The highest commission rate the platform will let an operator configure.</summary>
    /// <remarks>
    /// A guard against a typed decimal point, not a commercial policy. A seller charged 950%
    /// because somebody meant 9.5% would owe the platform money on every sale, and the mistake
    /// would be found in a settlement run rather than in a form.
    /// </remarks>
    public const decimal MaxRatePercent = 100m;

    /// <summary>Creates a plan.</summary>
    /// <param name="code">The stable lowercase code.</param>
    /// <param name="name">The name an operator sees.</param>
    /// <param name="planType">The shape of the plan.</param>
    /// <param name="defaultRate">The rate charged when no rule matches.</param>
    public static CommissionPlan Create(
        string code,
        string name,
        CommissionPlanType planType,
        decimal defaultRate)
        => new(UuidV7.New(), code, name, planType, defaultRate);

    /// <summary>Renames the plan and changes what it charges by default.</summary>
    /// <param name="name">The name an operator sees.</param>
    /// <param name="description">What the plan is for.</param>
    /// <param name="planType">The shape of the plan.</param>
    /// <param name="defaultRate">The rate charged when no rule matches.</param>
    /// <param name="defaultFixedFee">The per-unit fee charged when no rule matches.</param>
    public void Update(
        string name,
        string? description,
        CommissionPlanType planType,
        decimal defaultRate,
        Money defaultFixedFee)
    {
        Name = Guard.NotNullOrWhiteSpace(name);
        Description = description;
        PlanType = planType;
        DefaultRate = Guard.NotNegative(defaultRate);
        DefaultFixedFee = defaultFixedFee;
    }

    /// <summary>Whether the plan may be assigned to a seller.</summary>
    /// <param name="isActive">Whether it is available.</param>
    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Whether a seller with no plan chosen is put on this one.</summary>
    /// <param name="isDefault">Whether it is the default.</param>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Replaces every rule on the plan.</summary>
    /// <remarks>
    /// Wholesale rather than one at a time: the rules are read as a set and a partial edit of a
    /// price-banded ladder is how a gap appears between two bands.
    /// </remarks>
    /// <param name="rules">The new rules.</param>
    public void ReplaceRules(IEnumerable<CommissionPlanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        _rules.Clear();
        _rules.AddRange(rules);
    }

    /// <summary>
    /// The rate and fee that apply to one sale (docs/03-database-design.md §4.3: "most specific
    /// category, then price band, then default").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Specificity is decided in one place, by <see cref="CommissionPlanRule.Specificity"/>: a rule
    /// naming both a category and a price band beats one naming only the category, which beats one
    /// naming only a band, which beats the plan default. Ties — two rules that are equally specific
    /// and both match — are broken by the narrower price band, and then by the older rule, so the
    /// answer is deterministic even for a badly configured plan. It is worth being deliberate about
    /// that: the alternative is a rate that depends on the order the database happened to return.
    /// </para>
    /// <para>
    /// Category matching is exact, on the id passed in. Resolving a rule set against a category
    /// <em>tree</em> — where a rule on Furniture should catch a sale of Sofas — needs the ancestry,
    /// which lives in Catalog; the caller passes whichever ancestor it wants matched.
    /// </para>
    /// </remarks>
    /// <param name="categoryId">The category of the item sold, or null when it is not known.</param>
    /// <param name="unitPrice">The selling price of one unit.</param>
    public (decimal RatePercent, decimal FixedFee, Guid? MatchedCategoryId) Resolve(
        Guid? categoryId,
        decimal unitPrice)
    {
        CommissionPlanRule? best = null;

        foreach (var rule in _rules)
        {
            if (!rule.Matches(categoryId, unitPrice))
            {
                continue;
            }

            if (best is null || rule.Beats(best))
            {
                best = rule;
            }
        }

        return best is null
            ? (DefaultRate, DefaultFixedFee.Amount, null)
            : (best.Rate, best.FixedFee.Amount, best.CategoryId);
    }
}

/// <summary>
/// One override within a plan: what to charge for a category, a price band, or both
/// (docs/03-database-design.md §4.3).
/// </summary>
/// <remarks>
/// The price band is half-open — <c>MinPrice</c> inclusive, <c>MaxPrice</c> exclusive — so a ladder
/// written as 0–1000, 1000–5000, 5000–null has no gap at 1000 and no overlap either. A closed upper
/// bound is the classic way to charge two different rates for the same rupee.
/// </remarks>
internal sealed class CommissionPlanRule : Entity<Guid>, ITenantScoped
{
    private CommissionPlanRule(Guid id, Guid planId, decimal rate)
        : base(id)
    {
        PlanId = planId;
        Rate = Guard.NotNegative(rate);
        FixedFee = Money.Rupees(0m);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CommissionPlanRule() => FixedFee = Money.Rupees(0m);

    /// <summary>The plan this rule belongs to.</summary>
    public Guid PlanId { get; private set; }

    /// <summary>The <c>catalog.categories</c> row this applies to, or null for any category.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>The lowest unit price this applies to, inclusive. Null for no lower bound.</summary>
    public decimal? MinPrice { get; private set; }

    /// <summary>The unit price this stops applying at, exclusive. Null for no upper bound.</summary>
    public decimal? MaxPrice { get; private set; }

    /// <summary>The commission rate as a percentage.</summary>
    public decimal Rate { get; private set; }

    /// <summary>A flat fee per unit, charged on top of the rate.</summary>
    public Money FixedFee { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>Creates a rule.</summary>
    /// <param name="planId">The plan it belongs to.</param>
    /// <param name="categoryId">The category it applies to, or null for any.</param>
    /// <param name="minPrice">Lowest unit price, inclusive.</param>
    /// <param name="maxPrice">Unit price it stops applying at, exclusive.</param>
    /// <param name="rate">The commission rate as a percentage.</param>
    /// <param name="fixedFee">A flat fee per unit.</param>
    public static CommissionPlanRule Create(
        Guid planId,
        Guid? categoryId,
        decimal? minPrice,
        decimal? maxPrice,
        decimal rate,
        Money fixedFee)
        => new(UuidV7.New(), planId, rate)
        {
            CategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            FixedFee = fixedFee,
        };

    /// <summary>Whether this rule covers a given sale.</summary>
    /// <param name="categoryId">The category of the item sold.</param>
    /// <param name="unitPrice">The selling price of one unit.</param>
    public bool Matches(Guid? categoryId, decimal unitPrice)
    {
        if (CategoryId is not null && CategoryId != categoryId)
        {
            return false;
        }

        if (MinPrice is not null && unitPrice < MinPrice)
        {
            return false;
        }

        return MaxPrice is null || unitPrice < MaxPrice;
    }

    /// <summary>
    /// How specific this rule is: 2 names a category and a band, 1 names one of them, 0 names
    /// neither.
    /// </summary>
    public int Specificity => (CategoryId is null ? 0 : 1) + (MinPrice is null && MaxPrice is null ? 0 : 1);

    /// <summary>
    /// Whether this rule should win over another that also matches.
    /// </summary>
    /// <remarks>
    /// A category is more specific than a price band at the same count, which is why the category
    /// is compared before the band width: "electronics, any price" is a more deliberate statement
    /// than "any category, over ₹10,000".
    /// </remarks>
    /// <param name="other">The rule currently winning.</param>
    public bool Beats(CommissionPlanRule other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Specificity != other.Specificity)
        {
            return Specificity > other.Specificity;
        }

        if ((CategoryId is not null) != (other.CategoryId is not null))
        {
            return CategoryId is not null;
        }

        var width = BandWidth;
        var otherWidth = other.BandWidth;

        return width != otherWidth ? width < otherWidth : Id.CompareTo(other.Id) < 0;
    }

    /// <summary>
    /// How wide the price band is, for tie-breaking. An unbounded side counts as very wide rather
    /// than as infinite, so the comparison never has to handle a null.
    /// </summary>
    private decimal BandWidth => (MaxPrice ?? decimal.MaxValue) - (MinPrice ?? 0m);
}
