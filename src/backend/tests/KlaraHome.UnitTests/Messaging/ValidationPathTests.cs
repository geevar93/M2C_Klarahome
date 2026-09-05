using KlaraHome.Infrastructure.Messaging.Behaviors;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.UnitTests.Messaging;

public sealed class ValidationPathTests
{
    [Theory]
    [InlineData("ShippingAddressId", "shippingAddressId")]
    [InlineData("Lines[2].Quantity", "lines[2].quantity")]
    [InlineData("Address.Line1", "address.line1")]
    [InlineData("Items[0].Options[1].Value", "items[0].options[1].value")]
    [InlineData("", "")]
    public void Converts_a_dotnet_property_path_to_the_json_path_the_client_sees(string input, string expected)
        => Assert.Equal(expected, ValidationBehavior<object, Result>.ToCamelCasePath(input));
}
