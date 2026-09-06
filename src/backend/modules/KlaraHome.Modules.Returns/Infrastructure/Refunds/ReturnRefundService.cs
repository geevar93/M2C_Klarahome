using System.Globalization;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Payments;
using KlaraHome.Contracts.Pricing;
using KlaraHome.Modules.Returns.Application;
using KlaraHome.Modules.Returns.Domain;
using KlaraHome.Modules.Returns.Infrastructure.Documents;
using KlaraHome.Modules.Returns.Infrastructure.Events;
using KlaraHome.Modules.Returns.Infrastructure.Processing;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Returns.Infrastructure.Refunds;

/// <summary>What a refund attempt came to.</summary>
/// <param name="Amount">What went back, inclusive of tax.</param>
/// <param name="Mode">Where it went.</param>
/// <param name="RefundId">The Payments module's refund, when it went to the original instrument.</param>
/// <param name="IsAwaitingApproval">
/// Whether it is waiting for a second signature rather than moving. The return still closes as
/// refunded, because the decision was made — what is outstanding is somebody else's click, not this
/// module's.
/// </param>
/// <param name="CreditNoteNumber">The note raised alongside it, when one was.</param>
internal readonly record struct ReturnRefundOutcome(
    decimal Amount,
    ReturnRefundMode Mode,
    Guid? RefundId,
    bool IsAwaitingApproval,
    string? CreditNoteNumber);

/// <summary>
/// Pays a return out: the money, the credit note, and the state that follows.
/// </summary>
/// <remarks>
/// <para>
/// The orchestration, and deliberately not the arithmetic — what a return is worth was settled by
/// <see cref="ReturnRefundCalculator"/> when the shopper asked and apportioned again when quality
/// control counted. This decides <em>where</em> the money goes and makes sure the paperwork goes
/// with it.
/// </para>
/// <para>
/// Two destinations, and they are genuinely different mechanisms rather than two branches of one.
/// The original instrument goes through the Payments module's own maker–checker control, which may
/// hold the refund for a second signature; store credit is a wallet entry that is immediate and
/// final. A store with no gateway configured can still do the second, which is what makes returns
/// work on a deployment that has not been given Razorpay credentials.
/// </para>
/// <para>
/// The credit note is raised <em>before</em> the money is asked for, and that order is deliberate. A
/// note raised for a refund that then failed at the gateway is a document an operator can act on; a
/// refund paid against a supply nobody reversed is a seller who has lost the goods and still owes
/// the tax.
/// </para>
/// <para>
/// Idempotent on the return. The key handed to Payments is derived from the return and the amount,
/// so a retried request collides on their unique index rather than sending money twice.
/// </para>
/// </remarks>
/// <param name="refunds">The seam money goes back through.</param>
/// <param name="credit">The store-credit wallet.</param>
/// <param name="creditNotes">Raises the note that reverses the supply.</param>
/// <param name="workflow">Moves the return once the money is settled.</param>
/// <param name="events">Announces the close.</param>
/// <param name="logger">Reports what went back and what did not.</param>
internal sealed partial class ReturnRefundService(
    IRefundInitiation refunds,
    IStoreCredit credit,
    CreditNoteService creditNotes,
    ReturnWorkflow workflow,
    ReturnsEventPublisher events,
    ILogger<ReturnRefundService> logger)
{
    /// <summary>
    /// Pays the return out and closes it.
    /// </summary>
    /// <param name="request">The RMA, which must have passed quality control.</param>
    /// <param name="order">The seller's part it relates to.</param>
    /// <param name="mode">Where the money goes.</param>
    /// <param name="amount">
    /// What to send back, or null for everything the return is worth. An operator may send back less
    /// — a goodwill decision, or a deduction agreed with the shopper — and may never send back more.
    /// </param>
    /// <param name="actor">Who is asking.</param>
    /// <param name="actorId">The user, when there is one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result<ReturnRefundOutcome>> PayAsync(
        ReturnRequest request,
        SubOrderReturnView order,
        ReturnRefundMode mode,
        decimal? amount,
        ReturnActor actor,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(order);

        if (request.RefundAmount > 0m)
        {
            return Result.Failure<ReturnRefundOutcome>(ReturnsErrors.AlreadyRefunded);
        }

        if (request.Status != ReturnStatus.QcPassed)
        {
            return Result.Failure<ReturnRefundOutcome>(ReturnsErrors.NotRefundable);
        }

        var breakdown = ReturnRefundCalculator.ForAccepted(request, order.IsIntraState, order.ShippingTax);
        var payable = amount ?? breakdown.Payable;

        if (payable > breakdown.Payable)
        {
            return Result.Failure<ReturnRefundOutcome>(ReturnsErrors.RefundTooLarge(breakdown.Payable));
        }

        // The paperwork first. A note raised against a refund that then failed is recoverable; a
        // refund paid against a supply nobody reversed leaves the seller owing tax on goods they no
        // longer have.
        var note = await creditNotes
            .IssueAsync(request, order, breakdown, cancellationToken)
            .ConfigureAwait(false);

        if (payable <= 0m)
        {
            // Everything failed inspection, or the collection fee swallowed the value. The return
            // still closes and the supply is still reversed — there is simply nothing to send.
            var closed = await CloseAsync(
                    request,
                    ReturnOutcomes.Closed,
                    actor,
                    actorId,
                    "There was nothing left to refund after inspection.",
                    cancellationToken)
                .ConfigureAwait(false);

            return closed.IsFailure
                ? Result.Failure<ReturnRefundOutcome>(closed.Error)
                : Result.Success(new ReturnRefundOutcome(
                    0m,
                    mode,
                    null,
                    false,
                    note?.CreditNoteNumber));
        }

        var paid = mode == ReturnRefundMode.Wallet
            ? await ToWalletAsync(request, payable, cancellationToken).ConfigureAwait(false)
            : await ToOriginalAsync(request, payable, cancellationToken).ConfigureAwait(false);

        if (paid.IsFailure)
        {
            return Result.Failure<ReturnRefundOutcome>(paid.Error);
        }

        request.Settle(payable, mode, paid.Value.RefundId);

        var moved = await workflow
            .TransitionAsync(
                request,
                ReturnStatus.Refunded,
                actor,
                actorId,
                $"A refund of {payable.ToString("0.00", CultureInfo.InvariantCulture)} "
                + $"{order.CurrencyCode} is on its way to you.",
                cancellationToken)
            .ConfigureAwait(false);

        if (moved.IsFailure)
        {
            return Result.Failure<ReturnRefundOutcome>(moved.Error);
        }

        var finished = await CloseAsync(
                request,
                ReturnOutcomes.Refunded,
                actor,
                actorId,
                note: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (finished.IsFailure)
        {
            return Result.Failure<ReturnRefundOutcome>(finished.Error);
        }

        Refunded(logger, request.ReturnNumber, payable, mode);

        return Result.Success(paid.Value with { CreditNoteNumber = note?.CreditNoteNumber });
    }

    /// <summary>
    /// Sends the money back to where it came from, through the Payments module's own control.
    /// </summary>
    /// <remarks>
    /// A refund above the configured threshold is created and deliberately not sent — it waits in
    /// the approvals queue for a second pair of eyes (docs/07-security-compliance.md §4). That is
    /// reported rather than treated as failure: the shopper's return has been decided, and telling
    /// them it was refused would be untrue.
    /// </remarks>
    private async Task<Result<ReturnRefundOutcome>> ToOriginalAsync(
        ReturnRequest request,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var refundable = await refunds
            .RefundableAmountAsync(request.OrderId, cancellationToken)
            .ConfigureAwait(false);

        if (refundable <= 0m)
        {
            // Nothing was ever collected — a cash-on-delivery parcel refused at the door, or an order
            // already refunded in full. Saying so is more useful than a gateway error about an
            // amount it cannot find.
            return Result.Failure<ReturnRefundOutcome>(ReturnsErrors.NothingRefundable);
        }

        // Clamped rather than refused. A shopper whose order was partly refunded for a cancellation
        // and is now returning the rest is owed what is left, not an error message.
        var payable = Math.Min(amount, refundable);

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"return:{request.Id}:{payable:0.0000}");

        var raised = await refunds
            .RefundAsync(
                new RefundInitiationRequest(
                    request.OrderId,
                    payable,
                    $"Return {request.ReturnNumber} against order {request.OrderNumber}: "
                    + (request.ReasonNote ?? request.ReasonCode),
                    request.SubOrderId,
                    request.Id,
                    key),
                cancellationToken)
            .ConfigureAwait(false);

        if (raised.IsFailure)
        {
            RefundNotRaised(logger, request.ReturnNumber, raised.Error.Message);
            return Result.Failure<ReturnRefundOutcome>(raised.Error);
        }

        return Result.Success(new ReturnRefundOutcome(
            payable,
            ReturnRefundMode.Original,
            raised.Value.RefundId,
            raised.Value.IsAwaitingApproval,
            CreditNoteNumber: null));
    }

    /// <summary>
    /// Puts the money into the shopper's store credit.
    /// </summary>
    /// <remarks>
    /// Immediate and final, with no gateway and no second signature — which is exactly why it is the
    /// only refund a deployment with no payment credentials can make, and the only one a
    /// cash-on-delivery order can have without a bank transfer.
    /// </remarks>
    private async Task<Result<ReturnRefundOutcome>> ToWalletAsync(
        ReturnRequest request,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var balance = await credit
            .CreditAsync(
                request.CustomerId,
                amount,
                StoreCreditReasons.Refund,
                referenceType: "return",
                request.Id,
                expiresAt: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (!balance.IsActive)
        {
            return Result.Failure<ReturnRefundOutcome>(ReturnsErrors.WalletUnavailable);
        }

        return Result.Success(new ReturnRefundOutcome(
            amount,
            ReturnRefundMode.Wallet,
            RefundId: null,
            IsAwaitingApproval: false,
            CreditNoteNumber: null));
    }

    /// <summary>Closes the return and announces how it ended.</summary>
    private async Task<Result> CloseAsync(
        ReturnRequest request,
        string outcome,
        ReturnActor actor,
        Guid? actorId,
        string? note,
        CancellationToken cancellationToken)
    {
        var closed = await workflow
            .TransitionAsync(request, ReturnStatus.Closed, actor, actorId, note, cancellationToken)
            .ConfigureAwait(false);

        if (closed.IsSuccess)
        {
            events.Closed(request, outcome);
        }

        return closed;
    }

    [LoggerMessage(EventId = 1730, Level = LogLevel.Information,
        Message = "Return {ReturnNumber} was refunded {Amount} to {Mode}.")]
    private static partial void Refunded(
        ILogger logger,
        string returnNumber,
        decimal amount,
        ReturnRefundMode mode);

    [LoggerMessage(EventId = 1731, Level = LogLevel.Error,
        Message = "No refund was raised for return {ReturnNumber}: {Detail}")]
    private static partial void RefundNotRaised(ILogger logger, string returnNumber, string detail);
}
