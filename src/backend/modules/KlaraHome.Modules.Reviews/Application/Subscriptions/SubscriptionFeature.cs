using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.Modules.Reviews.Infrastructure.Projection;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Application.Subscriptions;

/// <summary>Asks to be told when something comes back or comes down.</summary>
/// <param name="VariantId">The sellable thing to watch.</param>
/// <param name="Kind">BackInStock or PriceDrop.</param>
/// <param name="TargetPrice">The price to wait for, for a price-drop alert.</param>
/// <param name="CustomerId">The shopper, when signed in.</param>
/// <param name="Email">Where to write, when they are not.</param>
internal sealed record SubscribeToStockCommand(
    Guid VariantId,
    string? Kind,
    decimal? TargetPrice,
    Guid? CustomerId,
    string? Email) : ICommand<StockSubscriptionResponse>;

/// <summary>Withdraws a standing request.</summary>
/// <param name="Id">The subscription.</param>
/// <param name="CustomerId">The caller, checked against the owner.</param>
internal sealed record CancelSubscriptionCommand(Guid Id, Guid CustomerId) : ICommand;

/// <summary>Lists what the caller is waiting for.</summary>
/// <param name="CustomerId">The shopper.</param>
/// <param name="ActiveOnly">Only the ones still being watched.</param>
internal sealed record ListMySubscriptionsQuery(Guid CustomerId, bool? ActiveOnly)
    : IQuery<IReadOnlyList<StockSubscriptionResponse>>;

/// <summary>Rules a subscription has to satisfy.</summary>
internal sealed class SubscribeToStockCommandValidator : AbstractValidator<SubscribeToStockCommand>
{
    public SubscribeToStockCommandValidator()
    {
        RuleFor(command => command.VariantId).NotEmpty();

        RuleFor(command => command.Kind)
            .Must(kind => Enum.TryParse<SubscriptionKind>(kind, ignoreCase: true, out _))
            .WithMessage("An alert is either BackInStock or PriceDrop.");

        RuleFor(command => command.Email).EmailAddress().When(command => command.Email is { Length: > 0 });
    }
}

/// <summary>
/// Records a standing request to be told about something.
/// </summary>
/// <remarks>
/// <para>
/// The variant is resolved against the catalogue first — to confirm it exists, to name its product,
/// and to capture what it costs today. That last one is what makes a price-drop alert with no named
/// target mean anything at all: without a benchmark, "cheaper" has no answer once the price has
/// moved twice.
/// </para>
/// <para>
/// A duplicate is refused rather than silently reused, and the check is on the active ones only.
/// Somebody who was told last month that something was back and wants to be told again is making a
/// new request, not repeating an old one.
/// </para>
/// <para>
/// The row needs somewhere to send the alert. A signed-in shopper carries that on their account and
/// Notifications resolves it; an anonymous one has to give an address, and that address is the only
/// piece of personal data this module collects from somebody with no account.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="catalogue">Confirms the variant, names its product and prices it.</param>
/// <param name="options">How long a subscription is watched for.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SubscribeToStockCommandHandler(
    ReviewsDbContext context,
    IProductProjectionSource catalogue,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock) : ICommandHandler<SubscribeToStockCommand, StockSubscriptionResponse>
{
    public async Task<Result<StockSubscriptionResponse>> HandleAsync(
        SubscribeToStockCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CustomerId is null && string.IsNullOrWhiteSpace(command.Email))
        {
            return Result.Failure<StockSubscriptionResponse>(ReviewErrors.NoContact);
        }

        var kind = Enum.Parse<SubscriptionKind>(command.Kind!, ignoreCase: true);

        var projections = await catalogue
            .FindByVariantsAsync([command.VariantId], cancellationToken)
            .ConfigureAwait(false);

        // The buy box if the catalogue picked one, and otherwise the first offer there is: a
        // subscription is about the variant, and a variant with offers but no winner is still
        // something a shopper can wait for.
        var offer = projections.FirstOrDefault(projection => projection.IsBuyBox)
                    ?? (projections.Count > 0 ? projections[0] : null);

        if (offer is null)
        {
            return Result.Failure<StockSubscriptionResponse>(ReviewErrors.UnknownVariant);
        }

        if (kind == SubscriptionKind.PriceDrop
            && command.TargetPrice is { } target
            && (target <= 0m || target >= offer.SellingPrice))
        {
            return Result.Failure<StockSubscriptionResponse>(ReviewErrors.InvalidTargetPrice);
        }

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim().ToLowerInvariant();

        var duplicate = await context.StockSubscriptions
            .AnyAsync(
                row => row.VariantId == command.VariantId
                       && row.Kind == kind
                       && row.Status == SubscriptionStatus.Active
                       && (command.CustomerId != null
                           ? row.CustomerId == command.CustomerId
                           : row.Email == email),
                cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            return Result.Failure<StockSubscriptionResponse>(ReviewErrors.AlreadySubscribed);
        }

        var subscription = StockSubscription.Record(
            kind,
            command.VariantId,
            offer.ProductId,

            // The listing is recorded only where the shopper named a seller's offer. For a buy-box
            // subscription it stays null, because which seller wins may change before the alert fires
            // and pinning it would send an alert about the wrong offer.
            listingId: null,
            command.CustomerId,
            email,
            kind == SubscriptionKind.PriceDrop ? command.TargetPrice : null,
            kind == SubscriptionKind.PriceDrop ? offer.SellingPrice : null,
            clock.UtcNow.AddDays(options.CurrentValue.SubscriptionExpiryDays));

        context.StockSubscriptions.Add(subscription);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToResponse(subscription));
    }
}

/// <summary>Withdraws a standing request.</summary>
/// <remarks>
/// Confined to the caller's own subscriptions. An anonymous one cannot be cancelled through this
/// endpoint at all — it is unsubscribed through the link in the message, which is Notifications'
/// surface and the only one that can prove possession of the address.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class CancelSubscriptionCommandHandler(ReviewsDbContext context)
    : ICommandHandler<CancelSubscriptionCommand>
{
    public async Task<Result> HandleAsync(CancelSubscriptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var subscription = await context.StockSubscriptions
            .FirstOrDefaultAsync(
                row => row.Id == command.Id && row.CustomerId == command.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (subscription is null)
        {
            return Result.Failure(ReviewErrors.NotFound("alert"));
        }

        subscription.Cancel();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Lists what the caller is waiting for.</summary>
/// <remarks>
/// Unpaged, because it is bounded by how many things one person can be bothered to click a bell on.
/// A cursor here would be a cursor nobody ever uses and a second round trip on a screen that fits on
/// one.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
internal sealed class ListMySubscriptionsQueryHandler(ReviewsDbContext context)
    : IQueryHandler<ListMySubscriptionsQuery, IReadOnlyList<StockSubscriptionResponse>>
{
    /// <summary>The most alerts one screen will show.</summary>
    private const int Ceiling = 200;

    public async Task<Result<IReadOnlyList<StockSubscriptionResponse>>> HandleAsync(
        ListMySubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.StockSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.CustomerId == query.CustomerId);

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(subscription => subscription.Status == SubscriptionStatus.Active);
        }

        var subscriptions = await rows
            .OrderByDescending(subscription => subscription.Id)
            .Take(Ceiling)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<StockSubscriptionResponse> responses =
            [.. subscriptions.Select(ReviewProjection.ToResponse)];

        return Result.Success(responses);
    }
}
