using System.Globalization;
using System.Text.Json;
using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Platform.Application.Auditing;

/// <summary>One audit entry, as the admin surface reads it.</summary>
/// <param name="Id">Identity of the entry.</param>
/// <param name="OccurredAt">When it happened, UTC.</param>
/// <param name="Action">The dotted verb phrase.</param>
/// <param name="EntityType">The kind of thing acted on.</param>
/// <param name="EntityId">Identifier of the thing acted on.</param>
/// <param name="ActorType">What class of actor did it.</param>
/// <param name="ActorId">The acting subject, if any.</param>
/// <param name="Before">State before the change, or null for a creation.</param>
/// <param name="After">State after the change, or null for a deletion.</param>
/// <param name="Ip">Client IP.</param>
/// <param name="UserAgent">Client user agent.</param>
/// <param name="CorrelationId">Correlation id of the causing request.</param>
internal sealed record AuditLogResponse(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    string? EntityId,
    AuditActorType ActorType,
    Guid? ActorId,
    JsonElement? Before,
    JsonElement? After,
    string? Ip,
    string? UserAgent,
    string? CorrelationId);

/// <summary>
/// Searches the audit trail, newest first.
/// </summary>
/// <param name="EntityType">Restrict to one kind of thing.</param>
/// <param name="EntityId">Restrict to one thing. Only meaningful with <paramref name="EntityType"/>.</param>
/// <param name="ActorId">Restrict to one actor.</param>
/// <param name="Action">Restrict to one action.</param>
/// <param name="From">Earliest instant to include.</param>
/// <param name="To">Latest instant to include.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">How many entries to return.</param>
internal sealed record SearchAuditLogsQuery(
    string? EntityType,
    string? EntityId,
    Guid? ActorId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<AuditLogResponse>>;

/// <summary>Rules for an audit search.</summary>
internal sealed class SearchAuditLogsQueryValidator : AbstractValidator<SearchAuditLogsQuery>
{
    public SearchAuditLogsQueryValidator()
    {
        RuleFor(query => query.Size)
            .InclusiveBetween(1, Cursor.MaxPageSize)
            .When(query => query.Size is not null)
            .WithMessage($"Page size must be between 1 and {Cursor.MaxPageSize}.");

        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From!.Value)
            .When(query => query.From is not null && query.To is not null)
            .WithMessage("The end of the range must not be before its start.");

        RuleFor(query => query.EntityId)
            .Must((query, _) => !string.IsNullOrWhiteSpace(query.EntityType))
            .When(query => !string.IsNullOrWhiteSpace(query.EntityId))
            .WithMessage("An entity id only identifies something together with its entity type.");
    }
}

/// <summary>
/// Reads the audit trail with keyset pagination.
/// </summary>
/// <remarks>
/// <para>
/// Keyset rather than offset: the table only ever grows at the head, so an offset page would shift
/// under the reader between one page and the next. The key is <c>(occurred_at, id)</c> descending,
/// which is the table's own partition and index order — the id breaks a tie at identical instants,
/// so no entry can be skipped or repeated at a page boundary.
/// </para>
/// <para>
/// No total is returned. Counting a partitioned, append-only table that is expected to hold years
/// of history would cost more than the page itself, and §1.1 of the API specification exists to
/// let a collection say so.
/// </para>
/// </remarks>
/// <param name="context">The Platform module's context.</param>
internal sealed class SearchAuditLogsQueryHandler(PlatformDbContext context)
    : IQueryHandler<SearchAuditLogsQuery, PagedResult<AuditLogResponse>>
{
    public async Task<Result<PagedResult<AuditLogResponse>>> HandleAsync(
        SearchAuditLogsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var entries = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(entry => entry.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            entries = entries.Where(entry => entry.EntityId == query.EntityId);
        }

        if (query.ActorId is not null)
        {
            entries = entries.Where(entry => entry.ActorId == query.ActorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (query.From is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt >= query.From);
        }

        if (query.To is not null)
        {
            entries = entries.Where(entry => entry.OccurredAt <= query.To);
        }

        if (query.Cursor is not null)
        {
            if (!AuditCursor.TryDecode(query.Cursor, out var occurredAt, out var id))
            {
                return Error.Malformed("CURSOR_INVALID", "The page cursor could not be read.");
            }

            entries = entries.Where(entry =>
                entry.OccurredAt < occurredAt
                || (entry.OccurredAt == occurredAt && entry.Id.CompareTo(id) < 0));
        }

        // One more than the page, so "is there a next page" is answered without a second query.
        // The before/after documents stay strings until the rows are materialised: they are jsonb
        // in the database and JSON objects in the response, and the conversion between the two
        // cannot be expressed in a query EF has to translate.
        var rows = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(size + 1)
            .Select(entry => new AuditLogRow(
                entry.Id,
                entry.OccurredAt,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorType,
                entry.ActorId,
                entry.Before,
                entry.After,
                entry.Ip,
                entry.UserAgent,
                entry.CorrelationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > size;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var next = hasMore && rows.Count > 0
            ? AuditCursor.Encode(rows[^1].OccurredAt, rows[^1].Id)
            : null;

        IReadOnlyList<AuditLogResponse> page = [.. rows.Select(row => row.ToResponse())];
        return new PagedResult<AuditLogResponse>(page, new PageInfo(size, next));
    }
}

/// <summary>One row as it comes out of the database, before the jsonb columns become objects.</summary>
/// <param name="Id">Identity of the entry.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="Action">The dotted verb phrase.</param>
/// <param name="EntityType">The kind of thing acted on.</param>
/// <param name="EntityId">Identifier of the thing acted on.</param>
/// <param name="ActorType">What class of actor did it.</param>
/// <param name="ActorId">The acting subject, if any.</param>
/// <param name="Before">State before, as stored.</param>
/// <param name="After">State after, as stored.</param>
/// <param name="Ip">Client IP.</param>
/// <param name="UserAgent">Client user agent.</param>
/// <param name="CorrelationId">Correlation id of the causing request.</param>
internal sealed record AuditLogRow(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    string EntityType,
    string? EntityId,
    AuditActorType ActorType,
    Guid? ActorId,
    string? Before,
    string? After,
    string? Ip,
    string? UserAgent,
    string? CorrelationId)
{
    /// <summary>Turns the row into the response, parsing the two jsonb documents.</summary>
    public AuditLogResponse ToResponse()
        => new(
            Id,
            OccurredAt,
            Action,
            EntityType,
            EntityId,
            ActorType,
            ActorId,
            Parse(Before),
            Parse(After),
            Ip,
            UserAgent,
            CorrelationId);

    private static JsonElement? Parse(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>Encodes the <c>(occurred_at, id)</c> key an audit page continues from.</summary>
internal static class AuditCursor
{
    /// <summary>Round-trip format: no loss of sub-second precision, and no locale in it.</summary>
    private const string InstantFormat = "O";

    /// <summary>Encodes the last row of a page.</summary>
    /// <param name="occurredAt">Its timestamp.</param>
    /// <param name="id">Its id.</param>
    public static string Encode(DateTimeOffset occurredAt, Guid id)
        => Cursor.Encode($"{occurredAt.ToString(InstantFormat, CultureInfo.InvariantCulture)}|{id}");

    /// <summary>Decodes a cursor, or returns false for anything malformed.</summary>
    /// <param name="token">The token supplied by the caller.</param>
    /// <param name="occurredAt">The decoded timestamp.</param>
    /// <param name="id">The decoded id.</param>
    public static bool TryDecode(string? token, out DateTimeOffset occurredAt, out Guid id)
    {
        occurredAt = default;
        id = default;

        if (!Cursor.TryDecode(token, out var key))
        {
            return false;
        }

        var separator = key.LastIndexOf('|');
        if (separator <= 0)
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
                   key[..separator],
                   InstantFormat,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out occurredAt)
               && Guid.TryParse(key[(separator + 1)..], out id);
    }
}
