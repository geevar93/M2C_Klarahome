using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Infrastructure.Configuration;

/// <summary>
/// How every module <c>DbContext</c> connects and behaves. Bound and validated at startup, so a
/// misconfigured container refuses to boot rather than failing on the first query
/// (docs/06-infrastructure-devops.md §4).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Seconds a single command may run before Npgsql aborts it.</summary>
    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many times a transient failure is retried. Npgsql's execution strategy covers
    /// connection drops and a restarting database, which is exactly what a single-VPS deployment
    /// sees during a nightly restart.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>Upper bound on the delay between retries.</summary>
    [Range(1, 120)]
    public int MaxRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Whether the API applies pending migrations at startup. <b>Never true in production</b>:
    /// migrations are a one-shot job run by the <c>migrator</c> container before the new API
    /// starts (docs/03-database-design.md §7). Startup validation enforces that.
    /// </summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>
    /// Whether EF Core logs parameter values and returns detailed errors. Leaks data into logs,
    /// so it is refused outside Development.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>
    /// Whether the migrator runs the registered seeders after migrating. Seeders are idempotent
    /// and re-runnable, so this is safe to leave on; it exists so a restore drill can migrate a
    /// schema without touching data.
    /// </summary>
    public bool RunSeeders { get; set; } = true;
}
