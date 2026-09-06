using KlaraHome.Modules.Orders.Domain;

namespace KlaraHome.UnitTests.Orders;

/// <summary>
/// The Indian financial year, and the period an order number is scoped to.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1: this is a date calculation with two
/// boundaries that are easy to get wrong and expensive to discover late. An off-by-one in April
/// restarts a statutory invoice series three months early, and computing the year in UTC files a
/// small-hours order into the previous year's GST return.
/// </remarks>
public sealed class FinancialYearTests
{
    [Theory]
    [InlineData("2026-04-01T00:00:00+05:30", "2026-27")]
    [InlineData("2026-09-06T12:00:00+05:30", "2026-27")]
    [InlineData("2027-03-31T23:59:59+05:30", "2026-27")]
    [InlineData("2027-04-01T00:00:00+05:30", "2027-28")]
    [InlineData("2026-01-15T12:00:00+05:30", "2025-26")]
    public void The_year_runs_april_to_march(string instant, string expected)
        => Assert.Equal(expected, FinancialYear.Of(DateTimeOffset.Parse(instant, null)));

    /// <summary>
    /// 21:30 UTC on 31 March is 03:00 IST on 1 April. The order belongs to the new financial year,
    /// and computing it in UTC would file it in the old one's return.
    /// </summary>
    [Fact]
    public void The_year_is_decided_in_india_standard_time()
    {
        var justAfterMidnightInIndia = new DateTimeOffset(2027, 3, 31, 21, 30, 0, TimeSpan.Zero);

        Assert.Equal("2027-28", FinancialYear.Of(justAfterMidnightInIndia));
    }

    [Theory]
    [InlineData("2026-09-06T12:00:00+05:30", "2609")]
    [InlineData("2026-01-02T12:00:00+05:30", "2601")]
    [InlineData("2027-12-31T12:00:00+05:30", "2712")]
    public void The_period_is_the_two_digit_year_and_month(string instant, string expected)
        => Assert.Equal(expected, FinancialYear.PeriodOf(DateTimeOffset.Parse(instant, null)));

    /// <summary>The same IST boundary, for the period a number carries.</summary>
    [Fact]
    public void The_period_is_decided_in_india_standard_time()
    {
        var justAfterMidnightInIndia = new DateTimeOffset(2026, 8, 31, 20, 0, 0, TimeSpan.Zero);

        Assert.Equal("2609", FinancialYear.PeriodOf(justAfterMidnightInIndia));
    }

    [Fact]
    public void A_counter_starts_at_one_and_hands_out_consecutive_numbers()
    {
        var sequence = NumberSequence.Start(NumberSequenceKinds.Invoice, "vendor", "2026-27");

        Assert.Equal(1, sequence.Take());
        Assert.Equal(2, sequence.Take());
        Assert.Equal(3, sequence.Take());
        Assert.Equal(4, sequence.NextValue);
    }
}
