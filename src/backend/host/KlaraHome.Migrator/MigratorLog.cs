using Microsoft.Extensions.Logging;

namespace KlaraHome.Migrator;

/// <summary>Source-generated log messages for the migration runner.</summary>
internal static partial class MigratorLog
{
    [LoggerMessage(EventId = 1300, Level = LogLevel.Information,
        Message = "Migrator: no migrations are defined yet. Nothing to apply.")]
    public static partial void NothingToApply(ILogger logger);
}
