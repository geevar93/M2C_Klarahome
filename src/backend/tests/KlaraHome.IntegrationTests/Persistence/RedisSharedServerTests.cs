using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using KlaraHome.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// The readiness probe against a Redis server shared with other applications, authenticated as the
/// restricted ACL user such a server issues — not as the default user the dev stack uses.
/// </summary>
/// <remarks>
/// Without these settings the probe still reports healthy, which is exactly why it needs a test:
/// the client's connect-time <c>CONFIG GET</c>, tie-breaker read and configuration-channel
/// subscription are refused by the ACL and recorded in its log, and nothing else would notice.
/// </remarks>
public sealed class RedisSharedServerTests : IAsyncLifetime
{
    /// <summary>The version the shared infrastructure runs and the dev stack pins.</summary>
    private const string Image = "redis:8.10.1-alpine";

    // Local, throwaway credentials for a container that exists for the length of this class.
    private const string AdminSecret = "test-admin-secret";
    private const string AppUser = "klarahome";
    private const string AppSecret = "test-app-secret";

    private IContainer? _container;
    private string? _skipReason;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithCommand("redis-server", "--requirepass", AdminSecret)
                .WithPortBinding(6379, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(
                    "redis-cli", "--pass", AdminSecret, "--no-auth-warning", "ping"))
                .Build();

            await _container.StartAsync();

            // The ACL the shared server's provisioning script issues to an application.
            var setUser = await _container.ExecAsync(
            [
                "redis-cli", "--pass", AdminSecret, "--no-auth-warning",
                "ACL", "SETUSER", AppUser, "reset", "on", $">{AppSecret}",
                "~klarahome:*", "&klarahome:*", "+@all", "-@admin", "-flushall", "-flushdb", "-keys", "-debug",
            ]);

            if (!setUser.Stdout.StartsWith("OK", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"ACL SETUSER failed: {setUser.Stdout} {setUser.Stderr}");
            }

            await _container.ExecAsync(
                ["redis-cli", "--pass", AdminSecret, "--no-auth-warning", "ACL", "LOG", "RESET"]);
        }
        catch (Exception exception)
        {
            _container = null;
            _skipReason = $"Docker is not available for the Redis tests: {exception.Message}";
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
    public async Task The_readiness_probe_is_healthy_and_asks_for_nothing_the_application_user_may_not_have()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        var connectionString =
            $"{_container!.Hostname}:{_container.GetMappedPublicPort(6379)},user={AppUser},password={AppSecret}";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Redis"] = connectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKlaraHomeHealthChecks(configuration);

        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(
            check => check.Name == "redis",
            TestContext.Current.CancellationToken);

        var redis = Assert.Single(report.Entries);
        Assert.Equal(HealthStatus.Healthy, redis.Value.Status);

        // Anything the ACL refused would be listed here. An empty log is the whole assertion.
        var log = await _container.ExecAsync(
            ["redis-cli", "--pass", AdminSecret, "--no-auth-warning", "ACL", "LOG"],
            TestContext.Current.CancellationToken);

        Assert.True(
            string.IsNullOrWhiteSpace(log.Stdout),
            $"The probe issued commands the application's ACL user is refused:{Environment.NewLine}{log.Stdout}");
    }

    [Fact]
    public async Task Without_the_restricted_client_settings_the_same_server_refuses_the_connect_time_commands()
    {
        Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

        // The control for the test above: it proves the ACL log is capable of reporting a refusal,
        // so the empty log there means something.
        var plain = StackExchange.Redis.ConfigurationOptions.Parse(
            $"{_container!.Hostname}:{_container.GetMappedPublicPort(6379)},user={AppUser},password={AppSecret}");
        plain.AbortOnConnectFail = false;

        await using (var multiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(plain))
        {
            await multiplexer.GetDatabase().PingAsync();
        }

        var log = await _container.ExecAsync(
            ["redis-cli", "--pass", AdminSecret, "--no-auth-warning", "ACL", "LOG"],
            TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(log.Stdout), "A default client was expected to be refused something.");

        await _container.ExecAsync(
            ["redis-cli", "--pass", AdminSecret, "--no-auth-warning", "ACL", "LOG", "RESET"],
            TestContext.Current.CancellationToken);
    }
}
