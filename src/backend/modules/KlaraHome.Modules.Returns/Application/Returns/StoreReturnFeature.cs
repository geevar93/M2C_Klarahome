using FluentValidation;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.Modules.Returns.Infrastructure.Numbering;
using KlaraHome.Modules.Returns.Infrastructure.Persistence;
using KlaraHome.Modules.Returns.Infrastructure.Policy;
using KlaraHome.Modules.Returns.Infrastructure.Processing;
using KlaraHome.Modules.Returns.Infrastructure.Refunds;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Returns.Application.Returns;

/// <summary>One line a shopper wants to send back.</summary>
/// <param name="OrderLineId">The line.</param>
/// <param name="Quantity">How many units.</param>
internal sealed record ReturnLineRequest(Guid OrderLineId, int Quantity);

/// <summary>What a shopper may send back from one seller's part.</summary>
/// <param name="SubOrderId">The seller's part.</param>
internal sealed record GetReturnEligibilityQuery(Guid SubOrderId) : IQuery<ReturnEligibilityResponse>;

/// <summary>Raises a return against one seller's part of an order.</summary>
/// <param name="SubOrderId">The seller's part.</param>
/// <param name="Type">Money back, or a replacement.</param>
/// <param name="ReasonCode">Why, from the configured list.</param>
/// <param name="ReasonNote">Why, in the shopper's own words.</param>
/// <param name="Lines">The units.</param>
/// <param name="EvidenceFileIds">The photographs.</param>
/// <param name="RefundMode">Where the money should go, when the shopper has a preference.</param>
internal sealed record RaiseReturnCommand(
    Guid SubOrderId,
    string? Type,
    string ReasonCode,
    string? ReasonNote,
    IReadOnlyList<ReturnLineRequest> Lines,
    IReadOnlyList<Guid>? EvidenceFileIds,
    string? RefundMode) : ICommand<ReturnResponse>;

/// <summary>The caller's own returns, newest first.</summary>
/// <param name="Status">Filter by where they stand.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListMyReturnsQuery(string? Status, string? Cursor, int? Size)
    : IQuery<PagedResult<ReturnSummaryResponse>>;

/// <summary>One of the caller's own returns, in full.</summary>
/// <param name="ReturnId">The RMA.</param>
internal sealed record GetMyReturnQuery(Guid ReturnId) : IQuery<ReturnResponse>;

/// <summary>Withdraws a return the shopper no longer wants.</summary>
/// <param name="ReturnId">The RMA.</param>
/// <param name="Reason">Why, in their own words.</param>
internal sealed record CancelMyReturnCommand(Guid ReturnId, string? Reason) : ICommand<ReturnResponse>;

/// <summary>The reasons this store offers, as a shopper sees them.</summary>
internal sealed record ListReturnReasonOptionsQuery : IQuery<IReadOnlyList<ReturnReasonOption>>;

/// <summary>Validates a return request.</summary>
internal sealed class RaiseReturnValidator : AbstractValidator<RaiseReturnCommand>
{
    public RaiseReturnValidator()
    {
        RuleFor(command => command.SubOrderId).NotEmpty();
        RuleFor(command => command.ReasonCode).NotEmpty().MaximumLength(64);
        RuleFor(command => command.ReasonNote).MaximumLength(2000);
        RuleFor(command => command.Lines).NotEmpty();

        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(request => request.OrderLineId).NotEmpty();
            line.RuleFor(request => request.Quantity).GreaterThan(0).LessThanOrEqualTo(999);
        });

        // A shopper naming the same line twice means a quantity, not two rows, and saying so here is
        // cheaper than reconciling it in the handler.
        RuleFor(command => command.Lines)
            .Must(lines => lines.Select(line => line.OrderLineId).Distinct().Count() == lines.Count)
            .WithMessage("Each item can only appear once. Change the quantity instead.");
    }
}

/// <summary>Validates a withdrawal.</summary>
internal sealed class CancelMyReturnValidator : AbstractValidator<CancelMyReturnCommand>
{
    public CancelMyReturnValidator()
        => RuleFor(command => command.Reason).MaximumLength(500);
}

/// <summary>
/// Answers "what can I send back, and until when".
/// </summary>
/// <remarks>
/// The screen a shopper sees before they decide anything, and it is deliberately generous with
/// detail: every line is listed, including the ones that cannot be returned, each with the reason it
/// cannot. A screen that silently omitted them would leave the shopper hunting for an item they can
/// see in their order.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="orders">Reads what may still be sent back.</param>
/// <param name="policy">Resolves the window that applies.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetReturnEligibilityQueryHandler(
    ReturnsDbContext context,
    IOrderReturns orders,
    ReturnPolicyService policy,
    ReturnsScope scope) : IQueryHandler<GetReturnEligibilityQuery, ReturnEligibilityResponse>
{
    public async Task<Result<ReturnEligibilityResponse>> HandleAsync(
        GetReturnEligibilityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var view = await orders
            .GetAsync(query.SubOrderId, scope.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ReturnEligibilityResponse>(ReturnsErrors.NotFound("order"));
        }

        var order = view.Value;
        var resolved = await policy.ResolveAsync(order.VendorId, cancellationToken).ConfigureAwait(false);

        var reasons = await context.Reasons
            .AsNoTracking()
            .Where(reason => reason.IsActive)
            .OrderBy(reason => reason.SortOrder)
            .ThenBy(reason => reason.Label)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var delivered = string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase);

        var lines = new List<ReturnableLineResponse>(order.Lines.Count);
        var closesAt = (DateTimeOffset?)null;
        var source = "store";
        var anyEligible = false;

        foreach (var line in order.Lines)
        {
            var eligibility = policy.Eligibility(line, order.DeliveredAt, resolved);
            var returnable = eligibility.IsReturnable && delivered && line.QuantityReturnable > 0;

            anyEligible |= returnable;
            closesAt ??= eligibility.ClosesAt;
            source = eligibility.Source;

            var breakdown = ReturnRefundCalculator.ForUnits(line, line.QuantityReturnable);

            lines.Add(new ReturnableLineResponse(
                line.OrderLineId,
                line.Sku,
                line.Name,
                line.ImageFileId,
                line.QuantityReturnable,
                line.UnitPrice,
                breakdown.Payable,
                returnable,
                Explain(line, eligibility, delivered, order.Status)));
        }

        return Result.Success(new ReturnEligibilityResponse(
            order.SubOrderId,
            order.SubOrderNumber,
            order.OrderNumber,
            anyEligible,
            closesAt ?? order.ReturnWindowEndsAt,
            source,
            anyEligible ? null : "Nothing on this order can be returned.",
            order.CurrencyCode,
            lines,
            [.. reasons.Select(ReturnProjection.ToOption)]));
    }

    /// <summary>Why a line cannot be sent back, in words a shopper can act on.</summary>
    private static string? Explain(
        ReturnableLine line,
        ReturnEligibility eligibility,
        bool delivered,
        string status)
    {
        if (!delivered)
        {
            return $"This order is {status.ToLowerInvariant()} and has not been delivered yet.";
        }

        if (line.QuantityReturnable <= 0)
        {
            return "There are no units of this item left to return.";
        }

        if (!line.IsReturnable)
        {
            return "This item cannot be returned.";
        }

        return eligibility.IsReturnable
            ? null
            : eligibility.ClosesAt is { } closed
                ? $"The return window closed on {closed:d MMMM yyyy}."
                : "The return window for this item has closed.";
    }
}

/// <summary>
/// Raises a return.
/// </summary>
/// <remarks>
/// <para>
/// The one write a shopper makes in this module, and everything it refuses it refuses <em>before</em>
/// allocating a number: the order must be theirs and delivered, the window must be open, the reason
/// must exist, the evidence must be there if the reason wants it, and every unit asked for must
/// still be returnable. A number allocated and then abandoned would leave a hole in the RMA series,
/// which is untidy, and — more to the point — a shopper who was told their return exists and then
/// told it does not is a support call.
/// </para>
/// <para>
/// The estimate is computed here and frozen on the lines. The shopper is quoted a figure on the
/// screen where they confirm, and a figure recomputed later from a price list that has since moved
/// would be a different promise from the one they accepted.
/// </para>
/// <para>
/// A return that the policy auto-approves is approved in the same transaction — including its
/// freight decision — so a shopper whose reason the business trusts gets an answer immediately
/// rather than after somebody opens a queue.
/// </para>
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="orders">Reads what may still be sent back.</param>
/// <param name="policy">Resolves the window, the freight and the auto-approval.</param>
/// <param name="numbering">Allocates the RMA number.</param>
/// <param name="media">Checks that attached photographs exist.</param>
/// <param name="workflow">Moves the return once it exists.</param>
/// <param name="events">Announces it.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class RaiseReturnCommandHandler(
    ReturnsDbContext context,
    IOrderReturns orders,
    ReturnPolicyService policy,
    ReturnNumbering numbering,
    IMediaLibrary media,
    ReturnWorkflow workflow,
    ReturnsEventPublisher events,
    ReturnsScope scope,
    IClock clock) : ICommandHandler<RaiseReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        RaiseReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (scope.CustomerId is not { } customerId)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NotFound("order"));
        }

        var view = await orders
            .GetAsync(command.SubOrderId, customerId, cancellationToken)
            .ConfigureAwait(false);

        if (view.IsFailure)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NotFound("order"));
        }

        var order = view.Value;

        if (!string.Equals(order.Status, "Delivered", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(order.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NotDelivered(order.Status));
        }

        var open = await context.Returns
            .AsNoTracking()
            .Where(request => request.SubOrderId == command.SubOrderId
                              && request.Status != ReturnStatus.Rejected
                              && request.Status != ReturnStatus.Cancelled
                              && request.Status != ReturnStatus.Closed)
            .Select(request => request.ReturnNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (open is not null)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.AlreadyOpen(open));
        }

        // Normalised once, here, rather than lowered inside the predicate: a function applied to the
        // column would defeat the unique index the lookup rides on, and reason codes are stored
        // lower-case by the aggregate in the first place.
        var code = command.ReasonCode.Trim().ToLowerInvariant();

        var reason = await context.Reasons
            .FirstOrDefaultAsync(
                candidate => candidate.Code == code && candidate.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (reason is null)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.UnknownReason);
        }

        var resolved = await policy.ResolveAsync(order.VendorId, cancellationToken).ConfigureAwait(false);

        var type = string.Equals(command.Type, nameof(ReturnType.Replacement), StringComparison.OrdinalIgnoreCase)
            ? ReturnType.Replacement
            : ReturnType.Return;

        if (type == ReturnType.Replacement
            && (!resolved.Store.ReplacementsEnabled
                || !reason.AllowsReplacement
                || resolved.Vendor is { AcceptsExchanges: false }))
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.ReplacementUnavailable);
        }

        var evidence = await CheckEvidenceAsync(command, reason, resolved, cancellationToken)
            .ConfigureAwait(false);

        if (evidence.IsFailure)
        {
            return Result.Failure<ReturnResponse>(evidence.Error);
        }

        var byLine = order.Lines.ToDictionary(line => line.OrderLineId);
        var breakdowns = new List<RefundBreakdown>(command.Lines.Count);
        var prepared = new List<(ReturnableLine Line, int Quantity, RefundBreakdown Value)>(command.Lines.Count);

        foreach (var wanted in command.Lines)
        {
            if (!byLine.TryGetValue(wanted.OrderLineId, out var line))
            {
                return Result.Failure<ReturnResponse>(ReturnsErrors.NotFound("item"));
            }

            var eligibility = policy.Eligibility(line, order.DeliveredAt, resolved);

            if (!line.IsReturnable)
            {
                return Result.Failure<ReturnResponse>(ReturnsErrors.NotReturnable(line.Name));
            }

            if (!eligibility.IsReturnable)
            {
                return Result.Failure<ReturnResponse>(ReturnsErrors.WindowClosed(eligibility.ClosesAt));
            }

            if (wanted.Quantity > line.QuantityReturnable)
            {
                return Result.Failure<ReturnResponse>(
                    ReturnsErrors.TooManyUnits(line.Name, line.QuantityReturnable));
            }

            var value = ReturnRefundCalculator.ForUnits(line, wanted.Quantity);

            breakdowns.Add(value);
            prepared.Add((line, wanted.Quantity, value));
        }

        if (prepared.Count == 0)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NothingToReturn);
        }

        var isFull = order.Lines
            .Where(line => line.QuantityReturnable > 0)
            .All(line => prepared.Any(entry =>
                entry.Line.OrderLineId == line.OrderLineId && entry.Quantity >= line.QuantityReturnable));

        var fee = ReturnPolicyService.ReturnShippingFee(reason, resolved);

        var total = ReturnRefundCalculator.Combine(
            breakdowns,
            order,
            isFull,
            resolved.Store.RefundShippingOnFullReturn,
            fee);

        var number = await numbering
            .NextReturnNumberAsync(clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        var request = ReturnRequest.Raise(
            number,
            order.OrderId,
            order.OrderNumber,
            order.SubOrderId,
            order.SubOrderNumber,
            order.VendorId,
            customerId,
            type,
            reason.Code,
            order.CurrencyCode,
            clock.UtcNow);

        request.Explain(command.ReasonNote, command.EvidenceFileIds);
        request.Quote(total.Payable);
        request.ChargePickup(fee);

        if (total.ShippingRefund > 0m)
        {
            request.RefundShipping(total.ShippingRefund);
        }

        foreach (var (line, quantity, value) in prepared)
        {
            var returnLine = ReturnLine.For(request.Id, line.OrderLineId, line.ListingId, line.Sku, quantity);

            returnLine.Capture(Snapshot(line));
            returnLine.Value(value.TaxableValue, value.Cgst, value.Sgst, value.Igst, value.Cess, value.Payable);

            request.Add(returnLine);
        }

        context.Returns.Add(request);

        events.Requested(request);

        // The order timeline says a return was asked for, whether or not it is approved in a moment.
        // A shopper looking at their order should see the request they made, not only its outcome.
        await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Requested,
                ReturnActor.Customer,
                customerId,
                note: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (ReturnPolicyService.IsAutoApproved(reason, total.Payable, resolved))
        {
            request.Agree(total.Payable, reason.IsPickupRequired);

            await workflow
                .TransitionAsync(
                    request,
                    ReturnStatus.Approved,
                    ReturnActor.System,
                    actorId: null,
                    "Your return was approved automatically.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, ReturnActor.Customer));
    }

    /// <summary>
    /// Checks the photographs: that there are enough of them, not too many, and that they exist.
    /// </summary>
    /// <remarks>
    /// A file id is a guessable-looking value and this is a route a caller can put one on. An id the
    /// media library does not know is refused rather than stored — an RMA carrying a reference to
    /// somebody else's private file would be a disclosure waiting for a screen to render it.
    /// </remarks>
    private async Task<Result> CheckEvidenceAsync(
        RaiseReturnCommand command,
        ReturnReason reason,
        ReturnPolicyService.ResolvedPolicy policy,
        CancellationToken cancellationToken)
    {
        var files = command.EvidenceFileIds ?? [];
        var required = reason.RequiresEvidence || policy.Store.RequireEvidence;

        if (required && files.Count == 0)
        {
            return Result.Failure(ReturnsErrors.EvidenceRequired);
        }

        var ceiling = Math.Max(0, policy.Store.MaxEvidenceFiles);

        if (files.Count > ceiling)
        {
            return Result.Failure(ReturnsErrors.TooMuchEvidence(ceiling));
        }

        if (files.Count == 0)
        {
            return Result.Success();
        }

        var known = await media.GetManyAsync([.. files.Distinct()], cancellationToken).ConfigureAwait(false);

        return files.Distinct().All(known.ContainsKey)
            ? Result.Success()
            : Result.Failure(ReturnsErrors.EvidenceUnknown);
    }

    /// <summary>Freezes what was bought, per unit, so a partial return can apportion it.</summary>
    private static ReturnLineSnapshot Snapshot(ReturnableLine line)
    {
        var units = Math.Max(1, line.Quantity);

        return new ReturnLineSnapshot
        {
            Name = line.Name,
            ImageFileId = line.ImageFileId,
            HsnCode = line.HsnCode,
            VariantId = line.VariantId,
            UnitPrice = line.UnitPrice,
            GstRate = line.GstRate,
            UnitTaxableValue = Round(line.TaxableValue / units),
            UnitCgst = Round(line.Cgst / units),
            UnitSgst = Round(line.Sgst / units),
            UnitIgst = Round(line.Igst / units),
            UnitCess = Round(line.Cess / units),
        };
    }

    private static decimal Round(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Lists the caller's own returns, newest first.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="options">Supplies the page ceiling.</param>
internal sealed class ListMyReturnsQueryHandler(
    ReturnsDbContext context,
    ReturnsScope scope,
    IOptions<ReturnsOptions> options)
    : IQueryHandler<ListMyReturnsQuery, PagedResult<ReturnSummaryResponse>>
{
    public async Task<Result<PagedResult<ReturnSummaryResponse>>> HandleAsync(
        ListMyReturnsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.Value.MaxPageSize);

        // Scoped by the token, never by a parameter. There is no customer id on this query for the
        // same reason there is none on the order list: an endpoint that took one would be an
        // endpoint somebody could pass another shopper's id to.
        var rows = context.Returns
            .AsNoTracking()
            .Where(request => request.CustomerId == scope.CustomerId);

        if (Enum.TryParse<ReturnStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(request => request.Status == status);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(request => request.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(request => request.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).Select(ReturnProjection.ToSummary).ToArray();
        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ReturnSummaryResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one of the caller's own returns.</summary>
/// <param name="context">The Returns data context.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class GetMyReturnQueryHandler(ReturnsDbContext context, ReturnsScope scope)
    : IQueryHandler<GetMyReturnQuery, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        GetMyReturnQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var request = await context.Returns
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == query.ReturnId && candidate.CustomerId == scope.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        // An RMA belonging to somebody else does not resolve, and the answer is the same one an
        // invented id gets.
        return request is null
            ? Result.Failure<ReturnResponse>(ReturnsErrors.NotFound("return"))
            : Result.Success(ReturnProjection.ToResponse(request, ReturnActor.Customer));
    }
}

/// <summary>
/// Withdraws a return the shopper no longer wants.
/// </summary>
/// <remarks>
/// Allowed only while the goods are still with them. Once a courier has the parcel there is nothing
/// to withdraw — stopping it would leave the goods in a van belonging to neither party — and the
/// refusal says so rather than reporting an invalid transition.
/// </remarks>
/// <param name="context">The Returns data context.</param>
/// <param name="workflow">Moves it.</param>
/// <param name="pickups">Calls off a courier that was booked.</param>
/// <param name="scope">Who is asking.</param>
internal sealed class CancelMyReturnCommandHandler(
    ReturnsDbContext context,
    ReturnWorkflow workflow,
    ReversePickupCoordinator pickups,
    ReturnsScope scope) : ICommandHandler<CancelMyReturnCommand, ReturnResponse>
{
    public async Task<Result<ReturnResponse>> HandleAsync(
        CancelMyReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = await context.Returns
            .FirstOrDefaultAsync(
                candidate => candidate.Id == command.ReturnId && candidate.CustomerId == scope.CustomerId,
                cancellationToken)
            .ConfigureAwait(false);

        if (request is null)
        {
            return Result.Failure<ReturnResponse>(ReturnsErrors.NotFound("return"));
        }

        if (!request.IsBeforeCollection)
        {
            return Result.Failure<ReturnResponse>(
                request.IsTerminal
                    ? ReturnsErrors.AlreadyClosed(request.Status)
                    : ReturnsErrors.TooLateToCancel);
        }

        await pickups
            .StandDownAsync(request, command.Reason ?? "The customer withdrew the return.", cancellationToken)
            .ConfigureAwait(false);

        var cancelled = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Cancelled,
                ReturnActor.Customer,
                scope.ActorId,
                command.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        if (cancelled.IsFailure)
        {
            return Result.Failure<ReturnResponse>(cancelled.Error);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReturnProjection.ToResponse(request, ReturnActor.Customer));
    }
}

/// <summary>Lists the reasons this store offers, as a shopper sees them.</summary>
/// <param name="context">The Returns data context.</param>
internal sealed class ListReturnReasonOptionsQueryHandler(ReturnsDbContext context)
    : IQueryHandler<ListReturnReasonOptionsQuery, IReadOnlyList<ReturnReasonOption>>
{
    public async Task<Result<IReadOnlyList<ReturnReasonOption>>> HandleAsync(
        ListReturnReasonOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var reasons = await context.Reasons
            .AsNoTracking()
            .Where(reason => reason.IsActive)
            .OrderBy(reason => reason.SortOrder)
            .ThenBy(reason => reason.Label)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ReturnReasonOption>>(
            [.. reasons.Select(ReturnProjection.ToOption)]);
    }
}
