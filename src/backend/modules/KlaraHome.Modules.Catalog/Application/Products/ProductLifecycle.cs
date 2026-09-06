using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure;
using KlaraHome.Modules.Catalog.Infrastructure.Events;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Catalog.Application.Products;

/// <summary>
/// The rules that govern a product's life cycle, in one place.
/// </summary>
/// <remarks>
/// Five handlers move a product between states — submit, approve, reject, activate, archive — and
/// every one of them has to answer the same two questions: is this product allowed to be published,
/// and what happens to its offers when it stops being. Two implementations of either would drift,
/// and the drift would be a published product that fails a compliance rule the other path enforced.
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="reader">Assembles the response every one of those handlers returns.</param>
/// <param name="options">Says whether compliance and moderation are enforced.</param>
internal sealed class ProductLifecycle(
    CatalogDbContext context,
    ProductReader reader,
    IOptions<CatalogOptions> options)
{
    /// <summary>
    /// Whether a product may be published: it needs at least one sellable variant, and every
    /// mandatory disclosure the law attaches to it and to that variant.
    /// </summary>
    /// <param name="product">The product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> CheckPublishableAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        var variants = await context.Variants
            .AsNoTracking()
            .Where(variant => variant.ProductId == product.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // "A Product cannot be Active with zero Active Variants" (docs/02-domain-model.md §4.1).
        // A draft variant counts: publishing the product is what makes them sellable, and requiring
        // the merchandiser to activate each one first is a step with no decision in it.
        if (variants.Count == 0)
        {
            return Result.Failure(
                CatalogErrors.NotPublishable("A product needs at least one variant before it can be published."));
        }

        if (!options.Value.EnforceComplianceOnPublish)
        {
            return Result.Success();
        }

        var gaps = new List<string>(product.ComplianceGaps());

        foreach (var variant in variants.Where(variant => variant.Status != VariantStatus.Archived))
        {
            gaps.AddRange(variant.ComplianceGaps());
        }

        return gaps.Count == 0 ? Result.Success() : Result.Failure(CatalogErrors.ComplianceIncomplete(gaps));
    }

    /// <summary>
    /// Takes every live offer for a product out of the storefront, announcing each one.
    /// </summary>
    /// <remarks>
    /// Does not save: the caller is inside a unit of work whose outbox rows must commit in the same
    /// transaction as the state change that caused them (ADR-003).
    /// </remarks>
    /// <param name="productId">The product.</param>
    /// <param name="reason">Why, in words shown to the seller.</param>
    /// <param name="publisher">Writes the integration events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WithdrawListingsAsync(
        Guid productId,
        string reason,
        CatalogEventPublisher publisher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        var listings = await context.Listings
            .Where(listing => listing.ProductId == productId && listing.Status == ListingStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        foreach (var listing in listings)
        {
            if (listing.TransitionTo(ListingStatus.Inactive, now, reason))
            {
                publisher.Deactivated(listing, reason);
            }
        }
    }

    /// <summary>States a product after a change.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<ProductResponse>> StateAsync(Guid productId, CancellationToken cancellationToken)
    {
        var response = await reader.ReadAsync(productId, cancellationToken).ConfigureAwait(false);

        return response is null ? CatalogErrors.NotFound("product") : Result.Success(response);
    }

    /// <summary>
    /// Records a moderation decision and moves the product to where that decision takes it.
    /// </summary>
    /// <remarks>
    /// Approve and reject differ only in which status they write and whether the note is required,
    /// so they share this. The publication checks run again on an approval: the seller may have
    /// edited the product between submitting it and somebody reading it, and the queue is not a
    /// snapshot.
    /// </remarks>
    /// <param name="productId">The product.</param>
    /// <param name="decision">Approved or rejected.</param>
    /// <param name="target">Where the product goes.</param>
    /// <param name="notes">The reviewer's note.</param>
    /// <param name="auditAction">The audited action name.</param>
    /// <param name="caller">Who decided.</param>
    /// <param name="clock">The sanctioned clock.</param>
    /// <param name="audit">Records the decision.</param>
    /// <param name="writeContext">The tracked context to save through.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<ProductResponse>> DecideAsync(
        Guid productId,
        ModerationStatus decision,
        ProductStatus target,
        string? notes,
        string auditAction,
        ICallerContext caller,
        IClock clock,
        IAuditLogger audit,
        CatalogDbContext writeContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(writeContext);

        var product = await writeContext.Products
            .FirstOrDefaultAsync(candidate => candidate.Id == productId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return CatalogErrors.NotFound("product");
        }

        var submission = await writeContext.Moderations
            .FirstOrDefaultAsync(
                candidate => candidate.ProductId == productId && candidate.Status == ModerationStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

        if (submission is null)
        {
            return CatalogErrors.NotFound("open moderation submission");
        }

        if (decision == ModerationStatus.Approved)
        {
            var ready = await CheckPublishableAsync(product, cancellationToken).ConfigureAwait(false);

            if (ready.IsFailure)
            {
                return ready.Error;
            }
        }

        var now = clock.UtcNow;
        var from = product.Status;

        if (!submission.Decide(decision, caller.UserId, notes, now))
        {
            return CatalogErrors.InvalidTransition(submission.Status, decision);
        }

        if (!product.TransitionTo(target, now))
        {
            return CatalogErrors.InvalidTransition(from, target);
        }

        await writeContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = auditAction,
                EntityType = CreateProductCommandHandler.AuditEntityType,
                EntityId = product.Id.ToString(),
                Before = new { Status = from.ToString() },
                After = new { Status = product.Status.ToString(), Notes = notes },
            },
            cancellationToken).ConfigureAwait(false);

        return await StateAsync(product.Id, cancellationToken).ConfigureAwait(false);
    }
}
