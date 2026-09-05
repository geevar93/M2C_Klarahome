using System.Globalization;

namespace KlaraHome.SharedKernel.Primitives;

/// <summary>
/// An amount of money. Stored as <c>decimal</c> with four decimal places to match the database
/// column type (<c>numeric(18,4)</c>, docs/01-architecture.md §6) — never a float, never a
/// double. Rounding rules for display and tax live in the Pricing module, not here.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Scale used for storage and for every intermediate calculation.</summary>
    public const int Scale = 4;

    /// <summary>The platform's home currency (docs/03-database-design.md).</summary>
    public const string Inr = "INR";

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));
        }

        Amount = decimal.Round(amount, Scale, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Rupees(decimal amount) => new(amount, Inr);

    public static Money ZeroIn(string currency) => new(0m, currency);

    public static Money operator +(Money left, Money right)
        => new(left.Amount + SameCurrency(left, right).Amount, left.Currency);

    public static Money operator -(Money left, Money right)
        => new(left.Amount - SameCurrency(left, right).Amount, left.Currency);

    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor, money.Currency);

    public static Money operator -(Money money) => new(-money.Amount, money.Currency);

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public Money Add(Money other) => this + other;

    public Money Subtract(Money other) => this - other;

    public Money Multiply(decimal factor) => this * factor;

    public Money Negate() => -this;

    public int CompareTo(Money other) => Amount.CompareTo(SameCurrency(this, other).Amount);

    public override string ToString() => $"{Amount.ToString("0.0000", CultureInfo.InvariantCulture)} {Currency}";

    private static Money SameCurrency(Money left, Money right)
        => left.Currency == right.Currency
            ? right
            : throw new InvalidOperationException(
                $"Cannot combine {left.Currency} with {right.Currency}. Convert explicitly first.");
}
