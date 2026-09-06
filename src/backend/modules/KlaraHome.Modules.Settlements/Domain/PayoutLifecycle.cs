namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>
/// Where a payout batch stands (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// The numbering leaves gaps for the reason every other lifecycle in this platform does: a state
/// inserted between two others must not renumber what follows it. The values are written to the
/// database as words; the ordering is what a worklist sorts by.
/// </remarks>
internal enum PayoutBatchStatus
{
    /// <summary>Built and not yet signed off. Nothing has been asked of the gateway.</summary>
    Draft = 0,

    /// <summary>A second pair of eyes has approved it. It may now be sent.</summary>
    Approved = 10,

    /// <summary>The gateway has been asked, and some transfers are still in flight.</summary>
    Processing = 20,

    /// <summary>Every item reached its seller.</summary>
    Completed = 30,

    /// <summary>Some items reached their seller and some did not.</summary>
    PartiallyFailed = 40,

    /// <summary>No item reached its seller.</summary>
    Failed = 50,

    /// <summary>Abandoned before anything was sent. Terminal, and the cycles are payable again.</summary>
    Cancelled = 60,
}

/// <summary>Where one seller's transfer within a batch stands.</summary>
/// <remarks>
/// A separate, smaller machine from the batch's. The batch's state is <em>derived</em> from these:
/// a batch is not "partially failed" because somebody said so, it is partially failed because its
/// items are.
/// </remarks>
internal enum PayoutItemStatus
{
    /// <summary>Waiting for the batch to be sent.</summary>
    Pending = 0,

    /// <summary>Handed to the gateway; the money has not landed yet.</summary>
    Processing = 10,

    /// <summary>The gateway confirmed the transfer.</summary>
    Completed = 20,

    /// <summary>The gateway refused it, or the bank returned it.</summary>
    Failed = 30,

    /// <summary>Not attempted: the seller had nowhere for the money to go.</summary>
    Skipped = 40,
}

/// <summary>Who is asking for a batch to move. The permission half of the transition table.</summary>
/// <remarks>
/// Three actors and no shopper, which is the difference between this machine and the returns one. A
/// payout is a conversation between the platform, the gateway and a seller's bank; nobody outside
/// finance ever moves one, and a seller sees the result rather than the button.
/// </remarks>
[Flags]
internal enum PayoutActor
{
    /// <summary>Nobody. Never granted.</summary>
    None = 0,

    /// <summary>The person who builds a batch. Holds <c>settlements.payout.manage</c>.</summary>
    Maker = 1,

    /// <summary>The person who signs one off. Holds <c>settlements.payout.approve</c>.</summary>
    Checker = 2,

    /// <summary>The platform itself, on the gateway's word or a job's. No HTTP caller may claim it.</summary>
    System = 4,

    /// <summary>Anybody in finance.</summary>
    Staff = Maker | Checker,

    /// <summary>Anybody at all.</summary>
    Anyone = Maker | Checker | System,
}

/// <summary>
/// The one place a payout batch's transition table lives.
/// </summary>
/// <remarks>
/// <para>
/// One table saying both which edges exist and who may take them, exactly as the return and
/// sub-order machines do. Nothing in this module moves a batch by assigning a status; an endpoint, a
/// reconciliation sweep and the gateway's own answer all come through here.
/// </para>
/// <para>
/// The three outcome states — completed, partially failed, failed — are reachable only by
/// <see cref="PayoutActor.System"/>. It is not an oversight: whether a transfer arrived is the
/// gateway's fact and not an operator's opinion, and an operator who could declare a batch complete
/// could declare a seller paid who was not.
/// </para>
/// <para>
/// There is no edge out of <see cref="PayoutBatchStatus.PartiallyFailed"/> or
/// <see cref="PayoutBatchStatus.Failed"/>. A failed transfer is retried by putting the cycle into a
/// <em>new</em> batch once the reason is fixed, which leaves the failed batch standing as the record
/// that it was tried and why it did not work.
/// </para>
/// </remarks>
internal static class PayoutLifecycle
{
    /// <summary>The states from which nothing further happens.</summary>
    public static readonly IReadOnlyList<PayoutBatchStatus> Terminal =
    [
        PayoutBatchStatus.Completed,
        PayoutBatchStatus.PartiallyFailed,
        PayoutBatchStatus.Failed,
        PayoutBatchStatus.Cancelled,
    ];

    /// <summary>The transition table: every edge, and who may take it.</summary>
    private static readonly Dictionary<(PayoutBatchStatus From, PayoutBatchStatus To), PayoutActor> Edges = new()
    {
        // The maker–checker edge. Only a checker takes it, and the aggregate additionally refuses an
        // approver who is the person that built the batch — the same two-part control the Payments
        // module puts on a refund.
        [(PayoutBatchStatus.Draft, PayoutBatchStatus.Approved)] = PayoutActor.Checker,

        // Abandoning a batch before anything left. Either half of finance may, because nothing is
        // lost: the cycles become payable again and a new batch can be built.
        [(PayoutBatchStatus.Draft, PayoutBatchStatus.Cancelled)] = PayoutActor.Staff,
        [(PayoutBatchStatus.Approved, PayoutBatchStatus.Cancelled)] = PayoutActor.Staff,

        // Sending it. A maker may press it once a checker has signed, and the scheduler may press it
        // for an unattended run.
        [(PayoutBatchStatus.Approved, PayoutBatchStatus.Processing)] = PayoutActor.Staff | PayoutActor.System,

        // How it ended. The gateway's answer, never an operator's.
        [(PayoutBatchStatus.Processing, PayoutBatchStatus.Completed)] = PayoutActor.System,
        [(PayoutBatchStatus.Processing, PayoutBatchStatus.PartiallyFailed)] = PayoutActor.System,
        [(PayoutBatchStatus.Processing, PayoutBatchStatus.Failed)] = PayoutActor.System,
    };

    /// <summary>Whether nothing further can happen to a batch in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(PayoutBatchStatus status) => Terminal.Contains(status);

    /// <summary>Whether the machine has this edge at all, whoever is asking.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    public static bool Exists(PayoutBatchStatus from, PayoutBatchStatus to)
        => Edges.ContainsKey((from, to));

    /// <summary>Whether this actor may take this edge.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    /// <param name="actor">Who is asking.</param>
    public static bool IsAllowed(PayoutBatchStatus from, PayoutBatchStatus to, PayoutActor actor)
        => Edges.TryGetValue((from, to), out var permitted) && (permitted & actor) != 0;

    /// <summary>Every state this one can move to for this actor, for an admin screen's buttons.</summary>
    /// <remarks>
    /// The reason the table is data rather than a switch: the buttons an operator sees come off the
    /// machine, so a button that exists for an edge that does not is impossible rather than merely
    /// unlikely.
    /// </remarks>
    /// <param name="from">Where it is.</param>
    /// <param name="actor">Who is asking.</param>
    public static IReadOnlyList<PayoutBatchStatus> NextFor(PayoutBatchStatus from, PayoutActor actor)
        =>
        [
            .. Edges
                .Where(edge => edge.Key.From == from && (edge.Value & actor) != 0)
                .Select(edge => edge.Key.To)
                .OrderBy(status => status),
        ];

    /// <summary>
    /// What a batch's state becomes once every one of its items has stopped moving.
    /// </summary>
    /// <remarks>
    /// Derived rather than decided, which is what keeps "the batch failed" and "every item failed"
    /// the same statement. A batch of nothing but skipped items counts as failed: no money left, and
    /// calling that a completion would mark cycles paid that were not.
    /// </remarks>
    /// <param name="completed">How many items the gateway confirmed.</param>
    /// <param name="failed">How many it refused, including the ones never attempted.</param>
    public static PayoutBatchStatus Outcome(int completed, int failed)
        => (completed, failed) switch
        {
            ( > 0, 0) => PayoutBatchStatus.Completed,
            ( > 0, > 0) => PayoutBatchStatus.PartiallyFailed,
            _ => PayoutBatchStatus.Failed,
        };
}
