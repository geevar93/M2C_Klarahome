using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>One product waiting in, or having passed through, the moderation queue.</summary>
/// <param name="Id">The submission.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductName">Its title, so the queue is readable without a second call.</param>
/// <param name="VendorId">The seller who owns it.</param>
/// <param name="Status">What became of it.</param>
/// <param name="SubmittedBy">Who submitted it.</param>
/// <param name="SubmittedAt">When.</param>
/// <param name="ReviewerId">Who decided.</param>
/// <param name="ReviewedAt">When they decided.</param>
/// <param name="Notes">Their note.</param>
internal sealed record ModerationResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    Guid? VendorId,
    ModerationStatus Status,
    Guid? SubmittedBy,
    DateTimeOffset SubmittedAt,
    Guid? ReviewerId,
    DateTimeOffset? ReviewedAt,
    string? Notes);

/// <summary>Reads the moderation queue.</summary>
/// <param name="Status">Restrict to one outcome. Defaults to what is still pending.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListModerationQueueQuery(string? Status, string? Cursor, int? Size)
    : IQuery<PagedResult<ModerationResponse>>;

/// <summary>Sends a product for review, or publishes it outright if the caller is an approver.</summary>
/// <param name="ProductId">The product.</param>
internal sealed record SubmitProductCommand(Guid ProductId) : ICommand<ProductResponse>;

/// <summary>Approves a submitted product and publishes it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Notes">The reviewer's note.</param>
internal sealed record ApproveProductCommand(Guid ProductId, string? Notes) : ICommand<ProductResponse>;

/// <summary>Rejects a submitted product and sends it back to draft.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Notes">Why. Required, and shown to the seller.</param>
internal sealed record RejectProductCommand(Guid ProductId, string Notes) : ICommand<ProductResponse>;

/// <summary>Moves a published product in or out of the storefront, or retires it.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Status">Where to take it.</param>
internal sealed record ChangeProductStatusCommand(Guid ProductId, ProductStatus Status)
    : ICommand<ProductResponse>;

/// <summary>Reads the moderation queue.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListModerationQueueQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListModerationQueueQuery, PagedResult<ModerationResponse>>
{
    public async Task<Result<PagedResult<ModerationResponse>>> HandleAsync(
        ListModerationQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var status = Enum.TryParse<ModerationStatus>(query.Status, ignoreCase: true, out var parsed)
            ? parsed
            : ModerationStatus.Pending;

        // A join rather than two reads: the queue is unreadable without the product's title, and
        // fetching it separately for twenty-five rows is twenty-five lookups nobody needed.
        var submissions =
            from moderation in context.Moderations.AsNoTracking()
            join product in context.Products.AsNoTracking() on moderation.ProductId equals product.Id
            where moderation.Status == status
            select new { moderation, product.Name };

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            submissions = submissions.Where(row => row.moderation.Id.CompareTo(after) > 0);
        }

        // Oldest first: this is a work queue, and a seller whose product has been waiting three
        // days must not be overtaken by one submitted this morning.
        var page = await submissions
            .OrderBy(row => row.moderation.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<ModerationResponse>(
            [
                .. page.Select(row => new ModerationResponse(
                    row.moderation.Id,
                    row.moderation.ProductId,
                    row.Name,
                    row.moderation.VendorId,
                    row.moderation.Status,
                    row.moderation.SubmittedBy,
                    row.moderation.SubmittedAt,
                    row.moderation.ReviewerId,
                    row.moderation.ReviewedAt,
                    row.moderation.Notes)),
            ],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].moderation.Id.ToString()) : null)));
    }
}

/// <summary>Submits a product for review.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller submitting somebody else's product.</param>
/// <param name="lifecycle">Performs the publication checks and the transition.</param>
/// <param name="caller">Records who submitted it.</param>
/// <param name="options">Says whether moderation is required at all.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SubmitProductCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    ProductLifecycle lifecycle,
    ICallerContext caller,
    IOptions<CatalogOptions> options,
    IClock clock) : ICommandHandler<SubmitProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        SubmitProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        if (!scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        var ready = await lifecycle.CheckPublishableAsync(product, cancellationToken).ConfigureAwait(false);

        if (ready.IsFailure)
        {
            return ready.Error;
        }

        // Platform staff are the approvers. Sending their own draft to their own queue would be
        // theatre, so their submission publishes it — and the moderation row still records that a
        // decision was taken, by whom, so the trail is complete either way.
        var straightThrough = !options.Value.RequireModeration || !scope.IsVendorCaller;
        var now = clock.UtcNow;

        var open = await context.Moderations
            .FirstOrDefaultAsync(
                candidate => candidate.ProductId == product.Id && candidate.Status == ModerationStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

        if (open is null)
        {
            open = ProductModeration.Submit(product.Id, product.VendorId, caller.UserId, now);
            context.Moderations.Add(open);
        }

        var target = straightThrough ? ProductStatus.Active : ProductStatus.PendingApproval;
        var from = product.Status;

        if (!product.TransitionTo(target, now))
        {
            return CatalogErrors.InvalidTransition(from, target);
        }

        if (straightThrough)
        {
            open.Decide(ModerationStatus.Approved, caller.UserId, "Published by platform staff.", now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await lifecycle.StateAsync(product.Id, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Approves a submitted product.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="lifecycle">Performs the publication checks and the transition.</param>
/// <param name="caller">Records who decided.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the decision.</param>
internal sealed class ApproveProductCommandHandler(
    CatalogDbContext context,
    ProductLifecycle lifecycle,
    ICallerContext caller,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<ApproveProductCommand, ProductResponse>
{
    /// <summary>The audited action for an approval.</summary>
    public const string AuditAction = "catalog.product.approved";

    public async Task<Result<ProductResponse>> HandleAsync(
        ApproveProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await lifecycle
            .DecideAsync(
                command.ProductId,
                ModerationStatus.Approved,
                ProductStatus.Active,
                command.Notes,
                AuditAction,
                caller,
                clock,
                audit,
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Rejects a submitted product.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="lifecycle">Performs the transition.</param>
/// <param name="caller">Records who decided.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the decision.</param>
internal sealed class RejectProductCommandHandler(
    CatalogDbContext context,
    ProductLifecycle lifecycle,
    ICallerContext caller,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<RejectProductCommand, ProductResponse>
{
    /// <summary>The audited action for a rejection.</summary>
    public const string AuditAction = "catalog.product.rejected";

    public async Task<Result<ProductResponse>> HandleAsync(
        RejectProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Notes))
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["notes"] = ["A rejection needs a reason. The seller is shown it."],
                });
        }

        return await lifecycle
            .DecideAsync(
                command.ProductId,
                ModerationStatus.Rejected,
                ProductStatus.Draft,
                command.Notes,
                AuditAction,
                caller,
                clock,
                audit,
                context,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Publishes, unpublishes or archives a product.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="scope">Refuses a seller acting on somebody else's product.</param>
/// <param name="lifecycle">Performs the publication checks and the transition.</param>
/// <param name="publisher">Announces the offers a withdrawal takes down.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ChangeProductStatusCommandHandler(
    CatalogDbContext context,
    CatalogScope scope,
    ProductLifecycle lifecycle,
    Infrastructure.Events.CatalogEventPublisher publisher,
    IClock clock) : ICommandHandler<ChangeProductStatusCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(
        ChangeProductStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await context.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        if (!scope.CanWrite(product.VendorId))
        {
            return CatalogErrors.OutOfScope;
        }

        if (command.Status == ProductStatus.Active)
        {
            var ready = await lifecycle.CheckPublishableAsync(product, cancellationToken).ConfigureAwait(false);

            if (ready.IsFailure)
            {
                return ready.Error;
            }
        }

        var from = product.Status;

        if (!product.TransitionTo(command.Status, clock.UtcNow))
        {
            return CatalogErrors.InvalidTransition(from, command.Status);
        }

        if (command.Status is ProductStatus.Inactive or ProductStatus.Archived)
        {
            await lifecycle
                .WithdrawListingsAsync(product.Id, "The product was withdrawn.", publisher, cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await lifecycle.StateAsync(product.Id, cancellationToken).ConfigureAwait(false);
    }
}
