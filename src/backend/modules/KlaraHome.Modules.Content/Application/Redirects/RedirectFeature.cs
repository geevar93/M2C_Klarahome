using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Content.Application.Redirects;

/// <summary>Lists the redirect manager's rules.</summary>
/// <param name="Search">A fragment of either path.</param>
/// <param name="ActiveOnly">Only the rules currently applied.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListRedirectsQuery(string? Search, bool? ActiveOnly, string? Cursor, int? Size)
    : IQuery<PagedResult<RedirectResponse>>;

/// <summary>Declares a rule.</summary>
/// <param name="FromPath">The path being asked for.</param>
/// <param name="ToPath">Where it goes. Null, and only null, for a 410.</param>
/// <param name="StatusCode">301, 302 or 410.</param>
/// <param name="Note">Why.</param>
internal sealed record CreateRedirectCommand(string? FromPath, string? ToPath, int StatusCode, string? Note)
    : ICommand<RedirectResponse>;

/// <summary>Rewrites a rule's destination. The path it matches is not editable.</summary>
/// <param name="Id">The rule.</param>
/// <param name="ToPath">Where it goes.</param>
/// <param name="StatusCode">301, 302 or 410.</param>
/// <param name="IsActive">Whether it is applied.</param>
/// <param name="Note">Why.</param>
internal sealed record UpdateRedirectCommand(
    Guid Id,
    string? ToPath,
    int StatusCode,
    bool IsActive,
    string? Note) : ICommand<RedirectResponse>;

/// <summary>Removes a rule.</summary>
/// <param name="Id">The rule.</param>
internal sealed record DeleteRedirectCommand(Guid Id) : ICommand;

/// <summary>
/// Asks what the storefront should do with a path nothing else claims.
/// </summary>
/// <remarks>
/// Anonymous and called on every 404, which is why it is a query with one indexed lookup and nothing
/// else in it. It also records the hit, which is the one write on this platform that is allowed to be
/// lost: the counter exists to tell a merchandiser which of four hundred rules still matter, and that
/// question does not need the last one to be exact.
/// </remarks>
/// <param name="Path">The path the visitor asked for.</param>
internal sealed record ResolveRedirectQuery(string? Path) : IQuery<RedirectResolutionResponse>;

/// <summary>Rules a new redirect has to satisfy.</summary>
internal sealed class CreateRedirectCommandValidator : AbstractValidator<CreateRedirectCommand>
{
    public CreateRedirectCommandValidator()
    {
        RuleFor(command => command.FromPath).NotEmpty().MaximumLength(Redirect.MaxPathLength);
        RuleFor(command => command.ToPath).MaximumLength(Redirect.MaxPathLength);
        RuleFor(command => command.Note).MaximumLength(Redirect.MaxNoteLength);

        RuleFor(command => command.StatusCode)
            .Must(code => Enum.IsDefined(typeof(RedirectStatus), code))
            .WithMessage("A redirect answers with 301, 302 or 410.");
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
internal sealed class UpdateRedirectCommandValidator : AbstractValidator<UpdateRedirectCommand>
{
    public UpdateRedirectCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.ToPath).MaximumLength(Redirect.MaxPathLength);
        RuleFor(command => command.Note).MaximumLength(Redirect.MaxNoteLength);

        RuleFor(command => command.StatusCode)
            .Must(code => Enum.IsDefined(typeof(RedirectStatus), code))
            .WithMessage("A redirect answers with 301, 302 or 410.");
    }
}

/// <summary>Lists the redirect manager's rules, most-used first.</summary>
/// <remarks>
/// Ordered by hit count rather than by date, because that is the order the list is useful in: the
/// rules doing the work are at the top, and the four hundred somebody added during a migration and
/// that have never fired are at the bottom where they can be pruned.
/// </remarks>
/// <param name="context">The Content data context.</param>
internal sealed class ListRedirectsQueryHandler(ContentDbContext context)
    : IQueryHandler<ListRedirectsQuery, PagedResult<RedirectResponse>>
{
    public async Task<Result<PagedResult<RedirectResponse>>> HandleAsync(
        ListRedirectsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Redirects.AsNoTracking().AsQueryable();

        if (query.Search is { Length: > 0 } search)
        {
            var pattern = $"%{ContentQueries.EscapeLike(search)}%";

            rows = rows.Where(redirect =>
                EF.Functions.ILike(redirect.FromPath, pattern, "\\")
                || (redirect.ToPath != null && EF.Functions.ILike(redirect.ToPath, pattern, "\\")));
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(redirect => redirect.IsActive);
        }

        // Paged on the path, which is unique and therefore a stable key; the hit count is not, and
        // paging on a counter that moves under the cursor would repeat and skip rows.
        if (Cursor.TryDecode(query.Cursor, out var key))
        {
            rows = rows.Where(redirect => string.Compare(redirect.FromPath, key, StringComparison.Ordinal) > 0);
        }

        var page = await rows
            .OrderBy(redirect => redirect.FromPath)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ContentProjection.ToRedirect).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].FromPath) : null;

        return Result.Success(new PagedResult<RedirectResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Declares a rule.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class CreateRedirectCommandHandler(ContentDbContext context)
    : ICommandHandler<CreateRedirectCommand, RedirectResponse>
{
    public async Task<Result<RedirectResponse>> HandleAsync(
        CreateRedirectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var status = (RedirectStatus)command.StatusCode;
        var from = ContentFormats.NormalizePath(command.FromPath);

        if (from is null)
        {
            return Result.Failure<RedirectResponse>(ContentErrors.InvalidPath);
        }

        var to = RedirectRules.ResolveTarget(status, command.ToPath);

        if (to.IsFailure)
        {
            return Result.Failure<RedirectResponse>(to.Error);
        }

        if (string.Equals(from, to.Value, StringComparison.Ordinal))
        {
            return Result.Failure<RedirectResponse>(ContentErrors.CircularRedirect);
        }

        var taken = await context.Redirects
            .AnyAsync(redirect => redirect.FromPath == from, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<RedirectResponse>(ContentErrors.DuplicateRedirect(from));
        }

        var rule = Redirect.Create(from, to.Value, status, command.Note?.Trim());

        context.Redirects.Add(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ContentProjection.ToRedirect(rule));
    }
}

/// <summary>Rewrites a rule's destination.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class UpdateRedirectCommandHandler(ContentDbContext context)
    : ICommandHandler<UpdateRedirectCommand, RedirectResponse>
{
    public async Task<Result<RedirectResponse>> HandleAsync(
        UpdateRedirectCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.Redirects
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure<RedirectResponse>(ContentErrors.NotFound("redirect"));
        }

        var status = (RedirectStatus)command.StatusCode;
        var to = RedirectRules.ResolveTarget(status, command.ToPath);

        if (to.IsFailure)
        {
            return Result.Failure<RedirectResponse>(to.Error);
        }

        if (string.Equals(rule.FromPath, to.Value, StringComparison.Ordinal))
        {
            return Result.Failure<RedirectResponse>(ContentErrors.CircularRedirect);
        }

        rule.Update(to.Value, status, command.IsActive, command.Note?.Trim());

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ContentProjection.ToRedirect(rule));
    }
}

/// <summary>Removes a rule.</summary>
/// <remarks>
/// A hard delete. A redirect is a routing rule and not a record: nothing points at it, there is no
/// history worth keeping, and an operator who wants to keep one without applying it switches it off.
/// </remarks>
/// <param name="context">The Content data context.</param>
internal sealed class DeleteRedirectCommandHandler(ContentDbContext context)
    : ICommandHandler<DeleteRedirectCommand>
{
    public async Task<Result> HandleAsync(DeleteRedirectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rule = await context.Redirects
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return Result.Failure(ContentErrors.NotFound("redirect"));
        }

        context.Redirects.Remove(rule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Answers what to do with a path nothing else claims.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="options">Whether hits are counted.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ResolveRedirectQueryHandler(
    ContentDbContext context,
    IOptionsMonitor<ContentOptions> options,
    IClock clock)
    : IQueryHandler<ResolveRedirectQuery, RedirectResolutionResponse>
{
    public async Task<Result<RedirectResolutionResponse>> HandleAsync(
        ResolveRedirectQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var path = ContentFormats.NormalizePath(query.Path);

        if (path is null)
        {
            return Result.Failure<RedirectResolutionResponse>(ContentErrors.InvalidPath);
        }

        var rule = await context.Redirects
            .FirstOrDefaultAsync(row => row.FromPath == path && row.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            // A 404 stays a 404. This endpoint answers "no rule" with the same not-found shape every
            // other read uses, and the storefront renders its own 404 page.
            return Result.Failure<RedirectResolutionResponse>(ContentErrors.NotFound("redirect"));
        }

        if (options.CurrentValue.TrackRedirectHits)
        {
            rule.RecordHit(clock.UtcNow);

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new RedirectResolutionResponse((int)rule.Status, rule.ToPath));
    }
}

/// <summary>The one rule about a redirect's destination, shared by the two handlers that write one.</summary>
internal static class RedirectRules
{
    /// <summary>
    /// Normalises a destination, and enforces that a 410 has none and everything else has one.
    /// </summary>
    /// <remarks>
    /// A 410 with a destination is a contradiction — "this is gone, go here" — and a 301 without one
    /// is a redirect to nowhere, which browsers render as an error page with no explanation. The
    /// database says the same thing in a <c>CHECK</c>; this is where an editor gets told.
    /// </remarks>
    /// <param name="status">What the storefront will answer with.</param>
    /// <param name="toPath">The destination as it was typed.</param>
    public static Result<string?> ResolveTarget(RedirectStatus status, string? toPath)
    {
        if (status == RedirectStatus.Gone)
        {
            return Result.Success<string?>(null);
        }

        // An absolute URL is left as it is: a rule sending traffic to a partner's site or to a
        // separate help centre is legitimate, and normalising it to a path would break it.
        if (!string.IsNullOrWhiteSpace(toPath)
            && Uri.TryCreate(toPath.Trim(), UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            return Result.Success<string?>(toPath.Trim());
        }

        var normalized = ContentFormats.NormalizePath(toPath);

        return normalized is null
            ? Result.Failure<string?>(ContentErrors.InvalidPath)
            : Result.Success<string?>(normalized);
    }
}
