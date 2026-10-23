using StackExchange.Redis;

namespace KlaraHome.Infrastructure.Health;

/// <summary>
/// How this deployment names itself inside a Redis server that other applications share
/// (docs: shared infrastructure, one ACL user per application).
/// </summary>
/// <remarks>
/// Nothing in the platform writes a Redis key or publishes a channel today: Redis is reached only
/// by the readiness probe, which issues <c>PING</c>. The prefix is nonetheless configured and
/// applied at the connection, so the first feature that does use Redis (the output cache and the
/// settings cache are the planned ones) cannot reach outside <c>klarahome:*</c> by forgetting to
/// prefix a name. A shared server's ACL for this application allows only that prefix.
/// </remarks>
public sealed class RedisOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Redis";

    /// <summary>The prefix every key and channel carries unless configured otherwise.</summary>
    public const string DefaultKeyPrefix = "klarahome:";

    /// <summary>
    /// Prefix for every key and channel this application uses. Must match the key and channel
    /// patterns of the Redis ACL user the connection string authenticates as.
    /// </summary>
    public string KeyPrefix { get; set; } = DefaultKeyPrefix;
}

/// <summary>Builds the Redis client configuration for a server shared with other applications.</summary>
public static class RedisConnection
{
    /// <summary>
    /// Parses a connection string and applies the settings that keep the client inside a
    /// restricted ACL user's permissions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// StackExchange.Redis does three things on connect that a user limited to <c>~klarahome:*</c>
    /// and <c>&amp;klarahome:*</c> is refused, each recorded in the server's ACL log (verified against
    /// Redis 8 with the ACL the shared server issues):
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>CONFIG GET</c>, an administrative command, to read server settings. Switched off by removing the command from the command map.</description></item>
    /// <item><description>A read of the key <c>__Booksleeve_TieBreak</c>, used to choose a master among several endpoints. A single server has nothing to choose, so the tie-breaker is cleared.</description></item>
    /// <item><description>A subscription to the channel <c>__Booksleeve_MasterChanged</c>, which announces a master change in a replicated deployment. Cleared for the same reason.</description></item>
    /// </list>
    /// <para>
    /// <c>INFO</c> is left enabled: the client uses it to learn the server version, and it is not
    /// administrative.
    /// </para>
    /// </remarks>
    /// <param name="connectionString">The StackExchange.Redis connection string, credentials included.</param>
    /// <param name="keyPrefix">The prefix applied to every channel this connection uses.</param>
    public static ConfigurationOptions CreateOptions(string connectionString, string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        var options = ConfigurationOptions.Parse(connectionString);

        // A Redis outage must degrade the readiness report, not stop the host from starting.
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = Math.Min(options.ConnectTimeout, 3000);

        options.CommandMap = CommandMap.Create(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CONFIG", "CLUSTER", "SENTINEL" },
            available: false);
        options.TieBreaker = string.Empty;
        options.ConfigurationChannel = string.Empty;

        options.ChannelPrefix = RedisChannel.Literal(keyPrefix);

        return options;
    }
}
