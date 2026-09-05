using Microsoft.Extensions.Logging;

namespace KlaraHome.Migrator;

/// <summary>Source-generated log messages for the migration runner.</summary>
internal static partial class MigratorLog
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information,
        Message = "Migration run complete: {ContextCount} module context(s) inspected, "
                  + "{MigrationCount} migration(s) applied, {SeederCount} seeder(s) run")]
    public static partial void RunCompleted(
        ILogger logger,
        int contextCount,
        int migrationCount,
        int seederCount);
}
