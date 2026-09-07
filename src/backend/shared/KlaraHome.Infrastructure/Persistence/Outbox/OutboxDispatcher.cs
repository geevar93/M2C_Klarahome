using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KlaraHome.Contracts.IntegrationEvents;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Persistence.Outbox;

/// <summary>Handles one integration event type. Registered by the consuming module.</summary>
/// <typeparam name="TEvent">The event handled.</typeparam>
/// <remarks>
/// A handler must be idempotent regardless of the inbox: the inbox stops a redelivery that
/// already <em>completed</em>, not one that failed halfway.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "CA1711 guards against types that are not .NET events being named like one. This "
                    + "is a handler for an integration event in the messaging sense, which is what every "
                    + "reader of this codebase and of ADR-003 will expect it to be called; renaming it to "
                    + "satisfy a rule about delegates would make it harder to recognise, not easier.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <summary>Reacts to the event.</summary>
    /// <param name="integrationEvent">The published fact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>How the outbox dispatcher polls and retries.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Whether the dispatcher runs in this host. On in the worker, off in the API: dispatching
    /// from every API replica would multiply the work and race for the same rows.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Seconds between polls when the queue is empty.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Messages claimed per poll.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Attempts before a message is left alone. Retries are spaced by the poll interval rather
    /// than by a stored backoff, so this is a budget in polls, not in milliseconds.
    /// </summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 8;
}

/// <summary>
/// Drains the transactional outbox: reads pending messages in occurrence order, invokes their
/// handlers, and marks them processed.
/// </summary>
/// <remarks>
/// <para>
/// Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so running more than one worker is safe
/// by construction rather than by convention — two dispatchers never see the same row.
/// </para>
/// <para>
/// Delivery is at-least-once, which is the strongest guarantee available without distributed
/// transactions: the handler can succeed and the process die before the row is marked. The inbox
/// turns that into effectively-once for handlers that record their completion.
/// </para>
/// </remarks>
public sealed partial class OutboxDispatcher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ModuleDbContextDescriptor _descriptor;
    private readonly IOptionsMonitor<OutboxOptions> _options;
    private readonly IntegrationEventTypeMap _typeMap;
    private readonly IClock _clock;
    private readonly ILogger<OutboxDispatcher> _logger;

    /// <param name="services">Resolves a scoped context per batch.</param>
    /// <param name="descriptors">The registered module contexts; the outbox owner is used to read the queue.</param>
    /// <param name="options">Poll and retry settings, re-read each cycle.</param>
    /// <param name="typeMap">Resolves a stored type name back to a contract type.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="logger">Reports dispatch outcomes.</param>
    public OutboxDispatcher(
        IServiceProvider services,
        IEnumerable<ModuleDbContextDescriptor> descriptors,
        IOptionsMonitor<OutboxOptions> options,
        IntegrationEventTypeMap typeMap,
        IClock clock,
        ILogger<OutboxDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        _services = services;
        _options = options;
        _typeMap = typeMap;
        _clock = clock;
        _logger = logger;

        // The queue is one table. Reading it through the context that owns its DDL keeps the
        // dispatcher independent of how many modules happen to be registered.
        _descriptor = descriptors.DistinctBy(descriptor => descriptor.ContextType).First();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;

            try
            {
                var dispatched = await DrainAsync(options, stoppingToken).ConfigureAwait(false);

                // A full batch means there is probably more waiting; go straight round again
                // rather than idling while the backlog grows.
                if (dispatched >= options.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A poll failure is infrastructure-level — the database is gone, say. Log it and
                // keep the loop alive; killing the worker would help nobody.
                PersistenceLog.OutboxPollFailed(_logger, exception);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<int> DrainAsync(OutboxOptions options, CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var context = (KlaraHomeDbContext)scope.ServiceProvider.GetRequiredService(_descriptor.ContextType);

        var dispatched = 0;

        // The transaction must span the claim and the release: the rows stay locked from
        // FOR UPDATE SKIP LOCKED until processed_at is committed, so no other worker can pick up a
        // message this one is midway through.
        await context.ExecuteInTransactionAsync(
            async (transactional, token) =>
                dispatched = await ClaimAndDispatchAsync(scope.ServiceProvider, transactional, options, token)
                    .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return dispatched;
    }

    private async Task<int> ClaimAndDispatchAsync(
        IServiceProvider scope,
        KlaraHomeDbContext context,
        OutboxOptions options,
        CancellationToken cancellationToken)
    {
        var pending = await context
            .Claim<OutboxMessage>(
                "platform.outbox_messages",
                $"processed_at IS NULL AND attempts < {options.MaxAttempts}",
                "occurred_at",
                options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in pending)
        {
            await DispatchAsync(scope, message, options, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return pending.Count;
    }

    private async Task DispatchAsync(
        IServiceProvider scope,
        OutboxMessage message,
        OutboxOptions options,
        CancellationToken cancellationToken)
    {
        message.Attempts++;

        try
        {
            if (!_typeMap.TryResolve(message.Type, out var eventType))
            {
                // A message nobody handles is not a failure — it is an event published before its
                // consumer existed. Marking it processed keeps the queue moving; the log line is
                // the record that it was dropped.
                PersistenceLog.OutboxNoHandler(_logger, message.Type, message.Id);
                message.ProcessedAt = _clock.UtcNow;
                return;
            }

            var payload = (IIntegrationEvent)JsonSerializer.Deserialize(
                message.Payload,
                eventType,
                OutboxSerialization.Options)!;

            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
            var handlers = scope.GetServices(handlerType).Where(handler => handler is not null).ToList();

            foreach (var handler in handlers)
            {
                await InvokeAsync(handler!, handlerType, payload, cancellationToken).ConfigureAwait(false);
            }

            message.ProcessedAt = _clock.UtcNow;
            message.Error = null;
            PersistenceLog.OutboxDispatched(_logger, message.Id, message.Type, handlers.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The attempt count and the error are still saved: the message stays pending and is
            // retried on the next poll until its budget runs out.
            message.Error = exception.ToString();
            PersistenceLog.OutboxFailed(_logger, exception, message.Id, message.Type, message.Attempts);

            if (message.Attempts >= options.MaxAttempts)
            {
                PersistenceLog.OutboxDeadLettered(_logger, message.Id, options.MaxAttempts);
            }
        }
    }

    private static Task InvokeAsync(
        object handler,
        Type handlerType,
        IIntegrationEvent payload,
        CancellationToken cancellationToken)
    {
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.HandleAsync))!;
        return (Task)method.Invoke(handler, [payload, cancellationToken])!;
    }
}

/// <summary>
/// Resolves the type name stored on an outbox row back to the contract type. Built once from the
/// contract assemblies, because a queued message must still be readable after the code that wrote
/// it has been redeployed.
/// </summary>
public sealed class IntegrationEventTypeMap
{
    private readonly Dictionary<string, Type> _types;

    /// <param name="assemblies">Assemblies scanned for <see cref="IIntegrationEvent"/> implementations.</param>
    public IntegrationEventTypeMap(IEnumerable<System.Reflection.Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        _types = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && typeof(IIntegrationEvent).IsAssignableFrom(type))
            .DistinctBy(OutboxSerialization.NameOf)
            .ToDictionary(OutboxSerialization.NameOf, type => type, StringComparer.Ordinal);
    }

    /// <summary>How many event types are known. Used by tests and diagnostics.</summary>
    public int Count => _types.Count;

    /// <summary>Looks up an event type by its stored name.</summary>
    /// <param name="name">The stored <c>type</c> column value.</param>
    /// <param name="type">The resolved contract type.</param>
    public bool TryResolve(string name, out Type type) => _types.TryGetValue(name, out type!);
}
