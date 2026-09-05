using KlaraHome.Infrastructure.Persistence.Seeding;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlaraHome.UnitTests.Persistence;

/// <summary>
/// Seeders run on every deploy, in a fixed order, and a failing one must name itself. A seeder
/// that fails anonymously in a deploy log is a seeder nobody fixes.
/// </summary>
public sealed class SeedRunnerTests
{
    private static DataSeedRunner Runner(params IDataSeeder[] seeders)
        => new(seeders, NullLogger<DataSeedRunner>.Instance);

    [Fact]
    public async Task Seeders_run_in_order_then_alphabetically()
    {
        var log = new List<string>();

        var count = await Runner(
                new RecordingSeeder("zebra", 10, log),
                new RecordingSeeder("beta", 50, log),
                new RecordingSeeder("alpha", 50, log))
            .RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, count);
        Assert.Equal(["zebra", "alpha", "beta"], log);
    }

    [Fact]
    public async Task Running_twice_is_the_contract_seeders_are_held_to()
    {
        var log = new List<string>();
        var runner = Runner(new RecordingSeeder("settings", 100, log));

        await runner.RunAsync(TestContext.Current.CancellationToken);
        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["settings", "settings"], log);
    }

    [Fact]
    public async Task A_failing_seeder_is_named_in_the_error()
    {
        var runner = Runner(new ThrowingSeeder());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(TestContext.Current.CancellationToken));

        Assert.Contains("broken-seeder", failure.Message, StringComparison.Ordinal);
        Assert.IsType<NotSupportedException>(failure.InnerException);
    }

    [Fact]
    public async Task A_failure_stops_the_run_rather_than_seeding_past_it()
    {
        var log = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner(new ThrowingSeeder(), new RecordingSeeder("after", 200, log))
                .RunAsync(TestContext.Current.CancellationToken));

        // Later seeders may depend on earlier data; carrying on would produce a half-seeded
        // database that looks like a success.
        Assert.Empty(log);
    }

    private sealed class RecordingSeeder(string name, int order, List<string> log) : IDataSeeder
    {
        public string Name => name;

        public int Order => order;

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSeeder : IDataSeeder
    {
        public string Name => "broken-seeder";

        public int Order => 1;

        public Task SeedAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("no");
    }
}
