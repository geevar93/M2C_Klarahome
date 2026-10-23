using DotNet.Testcontainers.Builders;
using KlaraHome.Infrastructure.Persistence.Migrations;
using KlaraHome.IntegrationTests.Database;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// Every module's migrations, and the seeders, run by the migrator as it runs on the shared
/// PostgreSQL server: a login that owns its own database and is not a superuser.
/// </summary>
/// <remarks>
/// The other migration tests connect as the container's bootstrap user, which is a superuser, so
/// they cannot notice a migration that needs more than a database owner has. This one provisions
/// the role the way the shared server's provisioning does (<c>NOSUPERUSER NOCREATEDB NOCREATEROLE
/// NOBYPASSRLS</c>, database owned by the role, <c>CONNECT</c> revoked from <c>PUBLIC</c>) and runs
/// everything in the Production environment, which is what the deployed migrator runs in.
/// The extensions are the part most likely to need more: they install only because PostgreSQL
/// 13+ marks <c>btree_gin</c>, <c>pg_trgm</c>, <c>pgcrypto</c> and <c>unaccent</c> as trusted.
/// </remarks>
public sealed class NonSuperuserMigrationTests : IAsyncLifetime
{
    // Throwaway credentials for a container that lives for one test.
    private const string OwnerRole = "klarahome_user";
    private const string OwnerSecret = "owner-test-secret";
    private const string Database = "klarahome";

    private PostgreSqlContainer? _container;
    private string? _skipReason;
    private string? _ownerConnectionString;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder(PostgresFixture.Image)
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword("admin-test-secret")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "postgres"))
                .Build();

            await _container.StartAsync();

            await using var admin = new NpgsqlConnection(_container.GetConnectionString());
            await admin.OpenAsync();

            foreach (var statement in new[]
            {
                $"CREATE ROLE {OwnerRole} LOGIN PASSWORD '{OwnerSecret}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS",
                $"CREATE DATABASE {Database} OWNER {OwnerRole}",
                $"REVOKE CONNECT ON DATABASE {Database} FROM PUBLIC",
                $"GRANT CONNECT ON DATABASE {Database} TO {OwnerRole}",
            })
            {
                await using var command = new NpgsqlCommand(statement, admin);
                await command.ExecuteNonQueryAsync();
            }

            _ownerConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Username = OwnerRole,
                Password = OwnerSecret,
                Database = Database,
            }.ConnectionString;
        }
        catch (Exception exception)
        {
            _container = null;
            _skipReason = $"Docker is not available for the database tests: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Every_migration_and_seeder_applies_for_a_database_owner_who_is_not_a_superuser()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        await using (var probe = new NpgsqlConnection(_ownerConnectionString))
        {
            await probe.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT rolsuper FROM pg_roles WHERE rolname = current_user",
                probe);
            Assert.False((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        }

        await using var provider = KlaraHomeSchemaFixture.BuildServices(
            _ownerConnectionString!,
            Microsoft.Extensions.Hosting.Environments.Production);

        var runner = provider.GetRequiredService<MigrationRunner>();

        var first = await runner.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(first.MigrationsApplied > 0);
        Assert.True(first.SeedersRun > 0);

        // A redeploy of the same version changes nothing.
        var second = await runner.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, second.MigrationsApplied);

        await using var check = new NpgsqlConnection(_ownerConnectionString);
        await check.OpenAsync(TestContext.Current.CancellationToken);
        await using var extensions = new NpgsqlCommand(
            "SELECT extname FROM pg_extension WHERE extowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)",
            check);

        var owned = new List<string>();
        await using (var reader = await extensions.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                owned.Add(reader.GetString(0));
            }
        }

        foreach (var required in new[] { "btree_gin", "pg_trgm", "pgcrypto", "unaccent" })
        {
            Assert.Contains(required, owned);
        }
    }
}
