using KlaraHome.Infrastructure.Observability;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.UnitTests.Observability;

public sealed class PiiMaskingTests
{
    [Theory]
    [InlineData("9876543210", PiiMask.LastFour, "***3210")]
    [InlineData("abc", PiiMask.LastFour, "***")]
    [InlineData("priya@example.com", PiiMask.Email, "p***@example.com")]
    [InlineData("not-an-email", PiiMask.Email, "***")]
    [InlineData("anything", PiiMask.Full, "***")]
    [InlineData("", PiiMask.Full, "***")]
    public void Applies_the_requested_mask(string value, PiiMask mask, string expected)
        => Assert.Equal(expected, PiiMasker.Apply(value, mask));

    [Theory]
    [InlineData("password")]
    [InlineData("otp")]
    [InlineData("mobile")]
    [InlineData("email")]
    [InlineData("refreshToken")]
    public void Treats_well_known_property_names_as_personal_data_even_without_an_attribute(string propertyName)
        => Assert.Contains(propertyName, PiiMasker.AlwaysMaskedNames);

    [Fact]
    public void Masks_annotated_and_well_known_properties_but_leaves_the_rest_readable()
    {
        var policy = new PiiMaskingDestructuringPolicy();

        var destructured = policy.TryDestructure(
            new CustomerSnapshot("0192-abc", "Priya", "9876543210", "priya@example.com", "hunter2"),
            new PassthroughFactory(),
            out var value);

        Assert.True(destructured);
        var properties = ((StructureValue)value!).Properties.ToDictionary(
            property => property.Name,
            property => property.Value.ToString().Trim('"'),
            StringComparer.Ordinal);

        Assert.Equal("0192-abc", properties["CustomerId"]);
        Assert.Equal("Priya", properties["DisplayName"]);

        // Mobile carries an explicit LastFour mask; EmailAddress and Password are caught by name
        // alone, which is the safety net for a DTO nobody remembered to annotate.
        Assert.Equal("***3210", properties["Mobile"]);
        Assert.Equal("***", properties["EmailAddress"]);
        Assert.Equal("***", properties["Password"]);
    }

    [Fact]
    public void Leaves_types_from_outside_this_codebase_to_the_default_destructurer()
    {
        var policy = new PiiMaskingDestructuringPolicy();

        Assert.False(policy.TryDestructure(new Uri("https://example.com"), new PassthroughFactory(), out _));
    }

    private sealed record CustomerSnapshot(
        string CustomerId,
        string DisplayName,
        [property: Pii(Mask = PiiMask.LastFour)] string Mobile,
        string EmailAddress,
        string Password);

    /// <summary>Turns any value into a scalar, which is all these assertions need.</summary>
    private sealed class PassthroughFactory : ILogEventPropertyValueFactory
    {
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects = false)
            => new ScalarValue(value);
    }
}
