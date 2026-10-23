using KlaraHome.Infrastructure.Health;
using StackExchange.Redis;

namespace KlaraHome.UnitTests.Observability;

/// <summary>
/// The Redis client settings that keep a connection inside a shared server's ACL for this
/// application. The server-side behaviour is proved against a real Redis in the integration suite.
/// </summary>
public sealed class RedisConnectionTests
{
    private const string Connection = "redis:6379,user=klarahome,password=not-a-real-secret";

    [Fact]
    public void Credentials_and_endpoint_from_the_connection_string_are_kept()
    {
        var options = RedisConnection.CreateOptions(Connection, "klarahome:");

        Assert.Equal("klarahome", options.User);
        Assert.Equal("not-a-real-secret", options.Password);
        Assert.Single(options.EndPoints);
    }

    // The command map has no public query API; a disabled command is rendered as "$NAME=" in the
    // connection string the options print, which is also the form an operator would write it in.
    [Theory]
    [InlineData("CONFIG")]
    [InlineData("CLUSTER")]
    [InlineData("SENTINEL")]
    public void Administrative_commands_are_never_issued(string command)
        => Assert.Contains($"${command}=", RedisConnection.CreateOptions(Connection, "klarahome:").ToString(false), StringComparison.Ordinal);

    [Theory]
    [InlineData("PING")]
    [InlineData("INFO")]
    [InlineData("CLIENT")]
    public void Commands_a_restricted_user_may_run_stay_available(string command)
        => Assert.DoesNotContain($"${command}=", RedisConnection.CreateOptions(Connection, "klarahome:").ToString(false), StringComparison.Ordinal);

    [Fact]
    public void The_tie_breaker_key_and_configuration_channel_outside_the_prefix_are_switched_off()
    {
        var options = RedisConnection.CreateOptions(Connection, "klarahome:");

        Assert.Equal(string.Empty, options.TieBreaker);
        Assert.Equal(string.Empty, options.ConfigurationChannel);
    }

    [Fact]
    public void Channels_carry_the_configured_prefix()
    {
        var options = RedisConnection.CreateOptions(Connection, "other:");

        Assert.Equal("other:", (string?)options.ChannelPrefix);
    }

    [Fact]
    public void An_unreachable_server_does_not_stop_the_host_starting()
    {
        var options = RedisConnection.CreateOptions(Connection, "klarahome:");

        Assert.False(options.AbortOnConnectFail);
        Assert.True(options.ConnectTimeout <= 3000);
    }

    [Fact]
    public void The_default_prefix_is_the_applications_own()
        => Assert.Equal("klarahome:", RedisOptions.DefaultKeyPrefix);
}
