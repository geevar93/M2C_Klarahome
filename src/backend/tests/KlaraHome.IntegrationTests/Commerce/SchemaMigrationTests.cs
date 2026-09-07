using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.IntegrationTests.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Every module's migrations apply to an empty database and re-run as a no-op.
/// </summary>
/// <remarks>
/// <para>
/// One test for all eighteen modules rather than one per module. The same row appears in
/// <c>docs/TEST_DEBT.md</c> for every step that added a schema — "the migration applies against a
/// live database and re-runs clean" — and the honest way to close them is a single assertion over
/// the whole set, because the failure they all fear is a <em>collision</em> between two modules and
/// a per-module test could not see one.
/// </para>
/// <para>
/// Step 28A established this by hand, on a laptop, once. That is worth exactly as much as the
/// afternoon it was done: <c>MigrationPipelineTests</c> composes a single module and asserts
/// <c>ContextsInspected == 1</c>, so nothing in CI has ever migrated the full set. This is what
/// makes it a standing guarantee.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database — already carrying every module's schema.</param>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class SchemaMigrationTests(KlaraHomeSchemaFixture fixture)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Every registered module owns a schema, and it exists.</summary>
    [Fact]
    public async Task Every_module_schema_exists_in_the_migrated_database()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var registry = fixture.Services.GetRequiredService<ModuleRegistry>();
        var database = new Sql(fixture.ConnectionString);

        Assert.NotEmpty(registry.Modules);

        var present = (await database.RowsAsync(
                "SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg\\_%' AND nspname <> 'information_schema'",
                Cancellation))
            .Select(row => (string)row["nspname"]!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = registry.Modules
            .Select(module => module.Schema)
            .Where(schema => !present.Contains(schema))
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"These modules are composed but their schemas were never created: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Running the migrator again over an already-migrated database applies nothing.
    /// </summary>
    /// <remarks>
    /// The property a deploy depends on. A migration that is not idempotent is one that works on the
    /// first environment and fails on the second, and the failure arrives during a release rather
    /// than during a build.
    /// </remarks>
    [Fact]
    public async Task Re_running_every_modules_migrations_applies_nothing()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var result = await fixture.Services.GetRequiredService<MigrationRunner>().RunAsync(Cancellation);

        Assert.Equal(fixture.Services.GetRequiredService<ModuleRegistry>().Modules.Count, result.ContextsInspected);
        Assert.Equal(0, result.MigrationsApplied);
    }

    /// <summary>
    /// Re-running every seeder changes nothing, so a redeploy is safe.
    /// </summary>
    /// <remarks>
    /// Seeders run on every deploy. One that inserted rather than upserted would double the
    /// reference data each time, and nothing would notice until a dropdown had two of everything.
    /// </remarks>
    [Fact]
    public async Task Re_running_every_seeder_changes_nothing()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var database = new Sql(fixture.ConnectionString);

        var before = await database.CountAsync(
            "SELECT COUNT(*) FROM platform.states",
            Cancellation);

        await fixture.ReseedAsync(Cancellation);

        var after = await database.CountAsync(
            "SELECT COUNT(*) FROM platform.states",
            Cancellation);

        Assert.Equal(before, after);
    }

    /// <summary>
    /// Each module keeps its migration history inside its own schema, not in a shared table.
    /// </summary>
    /// <remarks>
    /// What makes the modules independently deployable: two modules sharing one history table would
    /// have to be migrated together forever, which is the coupling the whole schema-per-module
    /// arrangement exists to avoid.
    /// </remarks>
    [Fact]
    public async Task Each_module_keeps_its_migration_history_in_its_own_schema()
    {
        Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

        var registry = fixture.Services.GetRequiredService<ModuleRegistry>();
        var database = new Sql(fixture.ConnectionString);

        var histories = (await database.RowsAsync(
                "SELECT table_schema FROM information_schema.tables WHERE table_name = '__ef_migrations_history'",
                Cancellation))
            .Select(row => (string)row["table_schema"]!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(histories);

        var schemas = registry.Modules.Select(module => module.Schema).ToHashSet(StringComparer.Ordinal);

        // No history table lives anywhere but a module schema — a stray one in `public` would be
        // exactly the shared table this arrangement rules out.
        Assert.All(histories, schema => Assert.Contains(schema, schemas));
    }
}
