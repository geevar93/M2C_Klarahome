using DotNet.Testcontainers.Builders;
using KlaraHome.Infrastructure.Persistence.Interceptors;
using KlaraHome.Testing.Persistence;
using Testcontainers.PostgreSql;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// A throwaway PostgreSQL 18 for the data-layer tests.
/// </summary>
/// <remarks>
/// <para>
/// The conventions being tested — <c>xmin</c> concurrency, partial indexes, <c>jsonb</c>,
/// <c>timestamptz</c>, snake_case identifiers, <c>numeric(18,4)</c> rounding — are all PostgreSQL
/// behaviour. An in-memory provider would agree with every assertion here and with none of
/// production, so these tests use the real engine or they do not run at all.
/// </para>
/// <para>
/// The image is pinned to the one the dev compose stack runs, so a test never passes against a
/// server version nobody deploys.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Pinned to the image in infra/compose/docker-compose.dev.yml.</summary>
    public const string Image = "postgres:18.6-alpine";

    private PostgreSqlContainer? _container;

    /// <summary>Why the container could not start, or null if it did.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>The connection string for the running container.</summary>
    public string ConnectionString => _container?.GetConnectionString()
                                      ?? throw new InvalidOperationException(SkipReason ?? "Not started.");

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder(Image)
                .WithDatabase("klarahome_tests")
                .WithUsername("klarahome")
                .WithPassword("klarahome_tests")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "klarahome"))
                .Build();

            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            // No Docker daemon is a reason to skip, not to fail: a developer editing a validator
            // should not be blocked by a container runtime they are not using. CI has Docker, and
            // CI is where the skip would be a problem — Step 5 fails the build on a skipped test.
            _container = null;
            SkipReason = $"Docker is not available for the database tests: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// A context wired exactly as the application wires one — same provider options, same naming
    /// convention, same auditing interceptor — differing only in which tenant and clock it sees.
    /// </summary>
    /// <param name="tenantId">The ambient tenant for this context.</param>
    /// <param name="userId">The acting user, or null for an unattributed operation.</param>
    /// <param name="clock">The clock the interceptor stamps from.</param>
    public ConventionsDbContext CreateContext(Guid tenantId, Guid? userId = null, FixedClock? clock = null)
    {
        var interceptor = new AuditingInterceptor(
            new FixedTenantContext(tenantId),
            new FixedUserContext(userId),
            clock ?? new FixedClock(DateTimeOffset.UtcNow));

        return new ConventionsDbContext(
            ConventionsContextFactory.Options(ConnectionString, interceptor),
            new FixedTenantContext(tenantId));
    }
}

/// <summary>
/// Shares one container across every database test class. Starting a PostgreSQL container per
/// class would multiply a five-second cost by the number of classes for no isolation gain — the
/// tests already isolate themselves by tenant and by row.
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class PostgresDatabase : ICollectionFixture<PostgresFixture>
{
    /// <summary>The xUnit collection name the database test classes join.</summary>
    public const string CollectionName = "postgres";
}
