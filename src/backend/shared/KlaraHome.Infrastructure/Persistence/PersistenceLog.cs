using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Persistence;

/// <summary>
/// Source-generated log messages for the data-access layer. Event ids are allocated in the 1400
/// block, following the 1300 block the migrator host already uses.
/// </summary>
internal static partial class PersistenceLog
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "Applying {PendingCount} migration(s) for module {Module} (schema {Schema})")]
    public static partial void ApplyingMigrations(ILogger logger, string module, string schema, int pendingCount);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "Module {Module} is up to date (schema {Schema})")]
    public static partial void ModuleUpToDate(ILogger logger, string module, string schema);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information,
        Message = "Applied {AppliedCount} migration(s) for module {Module}")]
    public static partial void MigrationsApplied(ILogger logger, string module, int appliedCount);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Information, Message = "Seeder {Seeder} applied")]
    public static partial void SeederApplied(ILogger logger, string seeder);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning,
        Message = "Seeders skipped: Database:RunSeeders is false")]
    public static partial void SeedersSkipped(ILogger logger);

    [LoggerMessage(EventId = 1405, Level = LogLevel.Warning,
        Message = "Tenant:Id is not configured; using the id derived from Tenant:Code '{Code}': {TenantId}. "
                  + "Set Tenant:Id explicitly before this deployment holds data — changing the code would "
                  + "otherwise strand every row written under the derived id.")]
    public static partial void DerivedTenantId(ILogger logger, string code, Guid tenantId);

    [LoggerMessage(EventId = 1406, Level = LogLevel.Information,
        Message = "Dispatched outbox message {MessageId} of type {MessageType} to {HandlerCount} handler(s)")]
    public static partial void OutboxDispatched(ILogger logger, Guid messageId, string messageType, int handlerCount);

    [LoggerMessage(EventId = 1407, Level = LogLevel.Error,
        Message = "Outbox message {MessageId} of type {MessageType} failed on attempt {Attempt}")]
    public static partial void OutboxFailed(ILogger logger, Exception exception, Guid messageId, string messageType, int attempt);

    [LoggerMessage(EventId = 1408, Level = LogLevel.Error,
        Message = "Outbox message {MessageId} exhausted its {MaxAttempts} attempts and will not be retried. "
                  + "It stays in platform.outbox_messages with processed_at NULL for inspection.")]
    public static partial void OutboxDeadLettered(ILogger logger, Guid messageId, int maxAttempts);

    [LoggerMessage(EventId = 1409, Level = LogLevel.Warning,
        Message = "No handler is registered for outbox message type {MessageType}; message {MessageId} is "
                  + "marked processed so it cannot block the queue")]
    public static partial void OutboxNoHandler(ILogger logger, string messageType, Guid messageId);

    [LoggerMessage(EventId = 1410, Level = LogLevel.Error, Message = "The outbox dispatcher poll failed")]
    public static partial void OutboxPollFailed(ILogger logger, Exception exception);
}
