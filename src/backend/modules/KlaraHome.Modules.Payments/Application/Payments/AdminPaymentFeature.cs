using FluentValidation;
using KlaraHome.Contracts.Orders;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.Modules.Payments.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Application.Payments;

/// <summary>Lists collections for the support and finance screens.</summary>
/// <param name="Status">Filter by where the collection stands.</param>
/// <param name="Method">Filter by the rail it was taken on.</param>
/// <param name="Provider">Filter by gateway, or <c>internal_cod</c>.</param>
/// <param name="OrderId">Filter to one order.</param>
/// <param name="Query">Search the order number or a gateway identifier.</param>
/// <param name="From">Only collections opened on or after this instant.</param>
/// <param name="To">Only collections opened strictly before this instant.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListPaymentsQuery(
    string? Status,
    string? Method,
    string? Provider,
    Guid? OrderId,
    string? Query,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size) : IQuery<PagedResult<PaymentSummaryResponse>>;

/// <summary>Reads one collection in full, with its attempts and refunds.</summary>
/// <param name="PaymentId">The collection.</param>
internal sealed record GetPaymentQuery(Guid PaymentId) : IQuery<PaymentResponse>;

/// <summary>
/// Re-reads a collection from the gateway and applies what it says.
/// </summary>
/// <remarks>
/// The repair for a lost webhook, and the <em>only</em> way an operator moves a payment. There is
/// deliberately no endpoint that sets a payment's status by hand: a platform where a human can
/// declare an order paid is a platform where an order can be paid without money.
/// </remarks>
/// <param name="PaymentId">The collection.</param>
internal sealed record SyncPaymentCommand(Guid PaymentId) : ICommand<PaymentResponse>;

/// <summary>Takes money the gateway is holding.</summary>
/// <param name="PaymentId">The collection.</param>
/// <param name="Amount">How much, or null for everything outstanding.</param>
internal sealed record CapturePaymentCommand(Guid PaymentId, decimal? Amount) : ICommand<PaymentResponse>;

/// <summary>Validates a manual capture.</summary>
internal sealed class CapturePaymentValidator : AbstractValidator<CapturePaymentCommand>
{
    public CapturePaymentValidator()
        => RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .When(command => command.Amount is not null);
}

/// <summary>Lists collections, newest first.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListPaymentsQueryHandler(PaymentsDbContext context, IOptions<PaymentsOptions> options)
    : IQueryHandler<ListPaymentsQuery, PagedResult<PaymentSummaryResponse>>
{
    public async Task<Result<PagedResult<PaymentSummaryResponse>>> HandleAsync(
        ListPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);
        var rows = context.Payments.AsNoTracking().AsQueryable();

        if (Enum.TryParse<PaymentStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(payment => payment.Status == status);
        }

        if (Enum.TryParse<PaymentMethod>(query.Method, ignoreCase: true, out var method))
        {
            rows = rows.Where(payment => payment.Method == method);
        }

        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            var provider = query.Provider.Trim();
            rows = rows.Where(payment => payment.Provider == provider);
        }

        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(payment => payment.OrderId == orderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            // One box for three identifiers, because a support caller quotes whichever one they
            // happen to be looking at — an order number, a gateway order id, or a payment id.
            var term = query.Query.Trim();

            rows = rows.Where(payment =>
                payment.OrderNumber.Contains(term)
                || (payment.ProviderOrderId != null && payment.ProviderOrderId.Contains(term))
                || (payment.ProviderPaymentId != null && payment.ProviderPaymentId.Contains(term)));
        }

        if (query.From is { } from)
        {
            rows = rows.Where(payment => payment.OpenedAt >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(payment => payment.OpenedAt < to);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(payment => payment.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(payment => payment.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(PaymentProjection.ToSummary).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<PaymentSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one collection in full.</summary>
/// <param name="context">The Payments data context.</param>
/// <param name="orders">Supplies the order, to work out whether a retry is still possible.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetPaymentQueryHandler(
    PaymentsDbContext context,
    IOrderPaymentSync orders,
    IOptions<PaymentsOptions> options,
    IClock clock) : IQueryHandler<GetPaymentQuery, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(
        GetPaymentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payment = await context.Payments
            .AsNoTracking()
            .Include(candidate => candidate.Attempts)
            .Include(candidate => candidate.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<PaymentResponse>(PaymentsErrors.NotFound("payment"));
        }

        var order = await orders.GetAsync(payment.OrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        var canRetry = order.IsSuccess
                       && PaymentRetry.IsOpen(payment, order.Value, options.Value, clock.UtcNow);

        return Result.Success(PaymentProjection.ToDetail(payment, canRetry));
    }
}

/// <summary>
/// Re-reads a collection from the gateway and applies whatever it says.
/// </summary>
/// <remarks>
/// It goes through the same <see cref="PaymentWorkflow"/> a webhook does, so an operator repairing a
/// stuck order confirms it in exactly the same way — with the same stock commit, the same invoice and
/// the same events — and leaves an attempt row whose source says <c>Admin</c>.
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter to ask.</param>
/// <param name="workflow">Applies what it says.</param>
/// <param name="orders">Supplies the order, for the retry flag on the response.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SyncPaymentCommandHandler(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow,
    IOrderPaymentSync orders,
    IOptions<PaymentsOptions> options,
    IClock clock) : ICommandHandler<SyncPaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(
        SyncPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await LoadAsync(context, command.PaymentId, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<PaymentResponse>(PaymentsErrors.NotFound("payment"));
        }

        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure)
        {
            return Result.Failure<PaymentResponse>(resolved.Error);
        }

        // By payment id when there is one, and by gateway order id when there is not — which is the
        // case that matters, because a collection with no payment id is exactly the one whose
        // webhook went missing.
        var fetched = string.IsNullOrWhiteSpace(payment.ProviderPaymentId)
            ? await FirstForOrderAsync(resolved.Value, payment, cancellationToken).ConfigureAwait(false)
            : await resolved.Value
                .FetchPaymentAsync(payment.ProviderPaymentId, cancellationToken)
                .ConfigureAwait(false);

        if (fetched.IsFailure)
        {
            return Result.Failure<PaymentResponse>(fetched.Error);
        }

        if (fetched.Value is not null)
        {
            var applied = await workflow
                .ApplyAsync(payment, fetched.Value, PaymentAttemptSource.Admin, cancellationToken)
                .ConfigureAwait(false);

            if (applied.IsFailure)
            {
                // The mismatch event has already been raised by the workflow. Saving here keeps the
                // attempt row that records what the gateway said, which is the evidence.
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Failure<PaymentResponse>(applied.Error);
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await RespondAsync(payment, orders, options.Value, clock, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Result<ProviderPayment?>> FirstForOrderAsync(
        IPaymentProvider provider,
        Payment payment,
        CancellationToken cancellationToken)
    {
        var listed = await provider
            .FetchPaymentsForOrderAsync(payment.ProviderOrderId ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return listed.IsFailure
            ? Result.Failure<ProviderPayment?>(listed.Error)
            : Result.Success(listed.Value.FirstOrDefault(candidate => candidate.IsCaptured)
                             ?? listed.Value.FirstOrDefault());
    }

    internal static async Task<Payment?> LoadAsync(
        PaymentsDbContext context,
        Guid paymentId,
        CancellationToken cancellationToken)
        => await context.Payments
            .Include(payment => payment.Attempts)
            .Include(payment => payment.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken)
            .ConfigureAwait(false);

    internal static async Task<Result<PaymentResponse>> RespondAsync(
        Payment payment,
        IOrderPaymentSync orders,
        PaymentsOptions options,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(payment.OrderId, customerId: null, cancellationToken)
            .ConfigureAwait(false);

        var canRetry = order.IsSuccess && PaymentRetry.IsOpen(payment, order.Value, options, clock.UtcNow);

        return Result.Success(PaymentProjection.ToDetail(payment, canRetry));
    }
}

/// <summary>
/// Takes money the gateway is holding.
/// </summary>
/// <remarks>
/// Needed only where automatic capture is off, or where an authorisation was left behind by a
/// gateway that could not capture at the time. It applies the gateway's answer through the workflow
/// rather than assuming the capture worked, which is the same rule every other path here follows.
/// </remarks>
/// <param name="context">The Payments data context.</param>
/// <param name="providers">Finds the adapter to ask.</param>
/// <param name="workflow">Applies what it says.</param>
/// <param name="orders">Supplies the order, for the retry flag on the response.</param>
/// <param name="options">Supplies the retry window.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CapturePaymentCommandHandler(
    PaymentsDbContext context,
    PaymentProviderRegistry providers,
    PaymentWorkflow workflow,
    IOrderPaymentSync orders,
    IOptions<PaymentsOptions> options,
    IClock clock) : ICommandHandler<CapturePaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(
        CapturePaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await SyncPaymentCommandHandler
            .LoadAsync(context, command.PaymentId, cancellationToken)
            .ConfigureAwait(false);

        if (payment is null)
        {
            return Result.Failure<PaymentResponse>(PaymentsErrors.NotFound("payment"));
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            return Result.Failure<PaymentResponse>(PaymentsErrors.NotPayable);
        }

        var resolved = providers.Require(payment.Provider);

        if (resolved.IsFailure)
        {
            return Result.Failure<PaymentResponse>(resolved.Error);
        }

        var captured = await resolved.Value
            .CapturePaymentAsync(
                payment.ProviderPaymentId,
                command.Amount ?? payment.AmountOutstanding,
                payment.CurrencyCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (captured.IsFailure)
        {
            return Result.Failure<PaymentResponse>(captured.Error);
        }

        var applied = await workflow
            .ApplyAsync(payment, captured.Value, PaymentAttemptSource.Admin, cancellationToken)
            .ConfigureAwait(false);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return applied.IsFailure
            ? Result.Failure<PaymentResponse>(applied.Error)
            : await SyncPaymentCommandHandler
                .RespondAsync(payment, orders, options.Value, clock, cancellationToken)
                .ConfigureAwait(false);
    }
}
