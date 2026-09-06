using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Pricing.Domain;

/// <summary>
/// The GST payable on one HSN code, for a period (docs/03-database-design.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// A row per period rather than a rate column that gets edited. When the Council moves a rate, the
/// old invoices must still reproduce: an order placed in March has to re-render at March's rate
/// however many times it is reprinted, and an edited row makes that impossible. So a change is a
/// new row with a new <see cref="EffectiveFrom"/>, and resolution is always "as of" a date.
/// </para>
/// <para>
/// An HSN with no row at all is not an error. The product carries its own <c>gst_rate</c>
/// (docs/03-database-design.md §4.4), and that is the fallback — this table exists to centralise
/// the rates a store sells against, not to be a precondition for selling.
/// </para>
/// </remarks>
internal sealed class TaxRate : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private TaxRate(Guid id, string hsnCode, decimal rate, decimal cessRate, DateOnly effectiveFrom)
        : base(id)
    {
        HsnCode = Guard.NotNullOrWhiteSpace(hsnCode);
        Rate = rate;
        CessRate = cessRate;
        EffectiveFrom = effectiveFrom;
        IsActive = true;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private TaxRate() => HsnCode = string.Empty;

    /// <summary>The Harmonised System of Nomenclature code. Four to eight digits in India.</summary>
    public string HsnCode { get; private set; }

    /// <summary>What the code covers, so an operator can find it.</summary>
    public string? Description { get; private set; }

    /// <summary>
    /// The GST percentage, for example <c>18.0000</c>. Split into CGST and SGST for an intra-state
    /// supply, or charged whole as IGST for an inter-state one.
    /// </summary>
    public decimal Rate { get; private set; }

    /// <summary>Compensation cess, as a percentage of the same taxable value. Usually zero.</summary>
    public decimal CessRate { get; private set; }

    /// <summary>The first day this rate applies.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The last day it applies, or null while it is the current rate.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Whether it is considered. A superseded row is closed, not deactivated.</summary>
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

    /// <summary>The highest percentage this platform will store. GST tops out well below it.</summary>
    public const decimal MaxRate = 100m;

    /// <summary>Records a rate.</summary>
    /// <param name="hsnCode">The HSN code.</param>
    /// <param name="rate">The GST percentage.</param>
    /// <param name="cessRate">The compensation cess percentage.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    public static TaxRate Create(string hsnCode, decimal rate, decimal cessRate, DateOnly effectiveFrom)
        => new(UuidV7.New(), Normalize(hsnCode), rate, cessRate, effectiveFrom);

    /// <summary>Restates the rate and its window.</summary>
    /// <param name="description">What the code covers.</param>
    /// <param name="rate">The GST percentage.</param>
    /// <param name="cessRate">The compensation cess percentage.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    /// <param name="effectiveTo">The last day it applies, or null.</param>
    /// <param name="isActive">Whether it is considered.</param>
    public void Update(
        string? description,
        decimal rate,
        decimal cessRate,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        bool isActive)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Rate = Math.Clamp(rate, 0m, MaxRate);
        CessRate = Math.Clamp(cessRate, 0m, MaxRate);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = isActive;
    }

    /// <summary>Whether this rate is in force on <paramref name="date"/>.</summary>
    /// <param name="date">The date of supply.</param>
    public bool AppliesOn(DateOnly date)
        => IsActive && EffectiveFrom <= date && (EffectiveTo is null || EffectiveTo >= date);

    /// <summary>Strips the separators an operator pastes in from a government PDF.</summary>
    /// <param name="hsnCode">The code as typed.</param>
    public static string Normalize(string hsnCode)
    {
        ArgumentNullException.ThrowIfNull(hsnCode);

        return hsnCode.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
