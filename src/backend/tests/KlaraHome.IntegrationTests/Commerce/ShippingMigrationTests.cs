using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.IntegrationTests.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The Shipping module's <c>ShiprocketServiceabilityDetails</c> migration applies against a live
/// database over an existing <c>serviceability_cache</c> table and re-runs clean (ADR-018).
/// </summary>
/// <remarks>
/// <see cref="SchemaMigrationTests"/> already proves this for every module's migrations as a set —
/// the collection's shared database carries every one of them, this migration included, and re-runs
/// them as a no-op. This test names the one migration Step 16A added and asserts the shape it was
/// meant to leave: two nullable columns added to a table that pre-dated it, so a deployment
/// upgrading from Step 16 keeps its existing serviceability rows.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class ShippingMigrationTests(KlaraHomeSchemaFixture fixture)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>
    /// <c>shipping.serviceability_cache</c> carries nullable <c>city</c> and <c>state</c> columns,
    /// and running every module's migrator again over this database — already migrated — applies
    /// nothing.
    /// </summary>
    [Fact]
    public async Task ShiprocketServiceabilityDetails_added_nullable_city_and_state_and_reruns_clean()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var database = new Sql(fixture.ConnectionString);

        var columns = (await database.RowsAsync(
                "SELECT column_name, is_nullable FROM information_schema.columns "
                + "WHERE table_schema = 'shipping' AND table_name = 'serviceability_cache' "
                + "AND column_name IN ('city', 'state')",
                Cancellation))
            .ToDictionary(row => (string)row["column_name"]!, row => (string)row["is_nullable"]!);

        Assert.Equal(2, columns.Count);
        Assert.Equal("YES", columns["city"]);
        Assert.Equal("YES", columns["state"]);

        var result = await fixture.Services.GetRequiredService<MigrationRunner>().RunAsync(Cancellation);

        Assert.Equal(0, result.MigrationsApplied);
    }
}
