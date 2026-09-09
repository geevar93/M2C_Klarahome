using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using KlaraHome.Modules.Shipping.Endpoints;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Every permission the Shipping endpoints declare appears in <c>PermissionCatalog</c> — the two
/// lists Identity and Shipping each keep by hand, in step only because a test says so.
/// </summary>
public sealed class ShippingPermissionTests
{
    /// <summary>The five permissions <see cref="ShippingPermissions"/> declares, read by reflection
    /// so the test fails the moment a sixth is added and forgotten here.</summary>
    [Fact]
    public void Every_shipping_permission_appears_in_the_identity_catalogue()
    {
        var declared = typeof(ShippingPermissions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(declared);

        var catalogued = typeof(PermissionCatalog)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(permission => !catalogued.Contains(permission)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"These Shipping permissions are declared on endpoints but missing from PermissionCatalog: "
            + $"{string.Join(", ", missing)}.");
    }
}
