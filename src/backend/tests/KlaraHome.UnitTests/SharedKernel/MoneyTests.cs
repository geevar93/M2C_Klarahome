using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.SharedKernel;

public sealed class MoneyTests
{
    [Fact]
    public void Rounds_to_four_decimal_places_on_construction()
    {
        var money = Money.Rupees(10.123456m);

        Assert.Equal(10.1235m, money.Amount);
        Assert.Equal("INR", money.Currency);
    }

    [Fact]
    public void Uses_banker_rounding_so_repeated_allocation_does_not_drift_upward()
    {
        Assert.Equal(1.0000m, Money.Rupees(1.00005m).Amount);
        Assert.Equal(1.0002m, Money.Rupees(1.00015m).Amount);
    }

    [Theory]
    [InlineData("in")]
    [InlineData("INRR")]
    [InlineData(" ")]
    public void Rejects_anything_that_is_not_an_iso_currency_code(string currency)
        => Assert.ThrowsAny<ArgumentException>(() => new Money(1m, currency));

    [Fact]
    public void Normalises_the_currency_code_to_upper_case()
        => Assert.Equal("INR", new Money(1m, "inr").Currency);

    [Fact]
    public void Adds_and_subtracts_within_one_currency()
    {
        Assert.Equal(149.5m, (Money.Rupees(100m) + Money.Rupees(49.5m)).Amount);
        Assert.Equal(-49.5m, (Money.Rupees(100m) - Money.Rupees(149.5m)).Amount);
    }

    [Fact]
    public void Refuses_to_combine_two_currencies_rather_than_guessing_a_rate()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Money.Rupees(100m) + new Money(100m, "USD"));

        // Both currencies are named, so the failure says what was actually mixed.
        Assert.Contains("INR", exception.Message, StringComparison.Ordinal);
        Assert.Contains("USD", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compares_by_amount()
    {
        Assert.True(Money.Rupees(10m) > Money.Rupees(9.9999m));
        Assert.True(Money.Rupees(10m) <= Money.Rupees(10m));
    }

    [Fact]
    public void Renders_with_a_fixed_scale_and_an_invariant_separator()
        => Assert.Equal("1499.0000 INR", Money.Rupees(1499m).ToString());
}
