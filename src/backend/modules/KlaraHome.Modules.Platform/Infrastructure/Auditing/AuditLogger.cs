using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Correlation;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Persistence.Configurations;
using KlaraHome.SharedKernel.Time;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.Modules.Platform.Infrastructure.Auditing;

/// <summary>
/// Writes audit entries to <c>platform.audit_logs</c>.
/// </summary>
/// <remarks>
/// <para>
/// The write happens in its own scope, with its own <c>DbContext</c>. The caller's context may be
/// carrying unsaved work — an audit call that quietly committed it would turn "record what
/// happened" into "commit whatever else was pending", which is a bug nobody would look for here.
/// </para>
/// <para>
/// Actor, IP, user agent and correlation id are read from the ambient context rather than taken
/// from the caller. Those four fields are what make an entry worth keeping, and a caller that has
/// to remember to pass them is a caller that will eventually pass none.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Creates the isolated scope the entry is written in.</param>
/// <param name="userContext">The acting subject, when there is one.</param>
/// <param name="correlationContext">The correlation id of the causing request.</param>
/// <param name="clock">The sanctioned clock; UTC everywhere.</param>
/// <param name="httpContextAccessor">
/// Supplies IP and user agent. Null in the worker and the migrator, which have no request.
/// </param>
internal sealed class AuditLogger(
    IServiceScopeFactory scopeFactory,
    IUserContext userContext,
    ICorrelationContext correlationContext,
    IClock clock,
    IHttpContextAccessor? httpContextAccessor = null) : IAuditLogger
{
    /// <inheritdoc />
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var request = httpContextAccessor?.HttpContext?.Request;

        var record = AuditLogEntry.Record(
            clock.UtcNow,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.ActorType,
            entry.ActorId ?? userContext.UserId,
            Serialize(entry.Before),
            Serialize(entry.After),
            httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Truncate(request?.Headers.UserAgent.ToString(), 512),
            correlationContext.CorrelationId);

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        context.AuditLogs.Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? Serialize(object? value)
        => value is null ? null : JsonSerializer.Serialize(value, PlatformJson.Options);

    /// <summary>
    /// A user agent is attacker-controlled and unbounded. It is cut to the column width here rather
    /// than left to the database, which would reject the whole insert and lose the entry.
    /// </summary>
    private static string? Truncate(string? value, int length)
        => string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}
