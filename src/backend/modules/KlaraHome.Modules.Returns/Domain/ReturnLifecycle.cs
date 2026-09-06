namespace KlaraHome.Modules.Returns.Domain;

/// <summary>What the shopper is asking for.</summary>
internal enum ReturnType
{
    /// <summary>Money back. The default, and the only one a store has to support.</summary>
    Return = 0,

    /// <summary>The same thing again. A second dispatch, and off unless a store turns it on.</summary>
    Replacement = 1,
}

/// <summary>
/// Where an RMA stands (docs/02-domain-model.md §5.3).
/// </summary>
/// <remarks>
/// <para>
/// The numbering leaves gaps for the reason the shipment lifecycle's does: this is a workflow that
/// grows a step every time a business learns something, and a state inserted between two others must
/// not renumber everything after it. The values are written to the database as words; the ordering
/// is what a worklist sorts by.
/// </para>
/// <para>
/// <see cref="Received"/> and <see cref="QcPassed"/> are deliberately two states rather than one.
/// The parcel arriving and somebody opening it are different days, done by different people, and a
/// platform that collapsed them could not answer the one question a returns operation is judged on:
/// how much is sitting in the receiving bay uninspected.
/// </para>
/// </remarks>
internal enum ReturnStatus
{
    /// <summary>The shopper has asked. Nothing has been agreed and nothing has moved.</summary>
    Requested = 0,

    /// <summary>Agreed. The goods are expected back, or the shopper has been told to keep them.</summary>
    Approved = 10,

    /// <summary>Refused. Terminal, and the shopper has been told why.</summary>
    Rejected = 20,

    /// <summary>A courier has been booked to collect.</summary>
    PickupScheduled = 30,

    /// <summary>The courier has the goods.</summary>
    Picked = 40,

    /// <summary>Moving back through the courier network.</summary>
    InTransit = 50,

    /// <summary>Booked in at the warehouse. Nobody has opened the box yet.</summary>
    Received = 60,

    /// <summary>Inspected, and the goods are what they should have been.</summary>
    QcPassed = 70,

    /// <summary>Inspected, and they are not. No refund follows from here without a decision.</summary>
    QcFailed = 75,

    /// <summary>The money has gone back.</summary>
    Refunded = 80,

    /// <summary>The replacement has been dispatched.</summary>
    Replaced = 85,

    /// <summary>Finished. Nothing further happens.</summary>
    Closed = 90,

    /// <summary>The shopper changed their mind before anything moved. Terminal.</summary>
    Cancelled = 100,
}

/// <summary>What quality control decided about a line.</summary>
/// <remarks>
/// The same three words <c>KlaraHome.Contracts.Inventory.RestockDisposition</c> uses, kept as this
/// module's own enum because a module may not take another's vocabulary as its domain — the mapping
/// happens once, at the seam.
/// </remarks>
internal enum ReturnDisposition
{
    /// <summary>Not yet inspected.</summary>
    Pending = 0,

    /// <summary>Saleable. Goes back on supply.</summary>
    Restock = 1,

    /// <summary>Not saleable and not worth keeping. Written off supply.</summary>
    Scrap = 2,

    /// <summary>Held pending a decision. Moves no stock at all.</summary>
    Quarantine = 3,
}

/// <summary>Where a refund went.</summary>
internal enum ReturnRefundMode
{
    /// <summary>Back to the card, account or wallet it was paid from.</summary>
    Original = 0,

    /// <summary>Into the shopper's store credit.</summary>
    Wallet = 1,
}

/// <summary>Who asked for a state to change. The permission half of the transition table.</summary>
[Flags]
internal enum ReturnActor
{
    /// <summary>Nobody. Never granted.</summary>
    None = 0,

    /// <summary>The shopper whose return it is.</summary>
    Customer = 1,

    /// <summary>The seller the goods came from.</summary>
    Vendor = 2,

    /// <summary>Platform staff: support, the receiving bay, the inspector.</summary>
    Platform = 4,

    /// <summary>The platform itself, on a courier's word or a job's. No HTTP caller can claim it.</summary>
    System = 8,

    /// <summary>Anybody who is not the shopper.</summary>
    Staff = Vendor | Platform | System,

    /// <summary>Anybody at all.</summary>
    Anyone = Customer | Vendor | Platform | System,
}

/// <summary>
/// The one place an RMA's transition table lives.
/// </summary>
/// <remarks>
/// <para>
/// One table saying both which edges exist and who may take them, exactly as the sub-order machine
/// does. Nothing in this module moves a return by assigning a status; every route — a shopper's
/// cancellation, an operator's approval, a courier's scan — comes through here, which is what stops
/// the three drifting about what "received" means.
/// </para>
/// <para>
/// It is stricter than the shipment machine in one respect and looser in another. Stricter, because
/// there is no skipping: a return that reached <see cref="ReturnStatus.QcPassed"/> without ever
/// being <see cref="ReturnStatus.Received"/> is a refund against goods nobody has seen, and refusing
/// that is the whole point of having a machine. Looser, because a return whose goods are not
/// required back goes from <see cref="ReturnStatus.Approved"/> straight to
/// <see cref="ReturnStatus.QcPassed"/> — there is nothing to inspect, and the store has already
/// decided it believes the shopper.
/// </para>
/// </remarks>
internal static class ReturnLifecycle
{
    /// <summary>The states from which nothing further happens.</summary>
    public static readonly IReadOnlyList<ReturnStatus> Terminal =
    [
        ReturnStatus.Rejected,
        ReturnStatus.Closed,
        ReturnStatus.Cancelled,
    ];

    /// <summary>The states in which the goods are still with the shopper.</summary>
    /// <remarks>
    /// What a cancellation is allowed from, and the reason it is: once a courier has the parcel,
    /// stopping the return would leave the goods in a van belonging to neither party.
    /// </remarks>
    public static readonly IReadOnlyList<ReturnStatus> BeforeCollection =
    [
        ReturnStatus.Requested,
        ReturnStatus.Approved,
        ReturnStatus.PickupScheduled,
    ];

    /// <summary>The transition table: every edge, and who may take it.</summary>
    /// <remarks>
    /// A dictionary rather than a switch, so the machine can be read as data — the admin screen
    /// showing an operator which buttons to draw asks this, and a button that exists for an edge
    /// that does not is a support call.
    /// </remarks>
    private static readonly Dictionary<(ReturnStatus From, ReturnStatus To), ReturnActor> Edges = new()
    {
        // A request is agreed to or refused by whoever is reviewing it, or withdrawn by the shopper.
        [(ReturnStatus.Requested, ReturnStatus.Approved)] = ReturnActor.Staff,
        [(ReturnStatus.Requested, ReturnStatus.Rejected)] = ReturnActor.Vendor | ReturnActor.Platform,
        [(ReturnStatus.Requested, ReturnStatus.Cancelled)] = ReturnActor.Customer | ReturnActor.Platform,

        // Approved, and now the goods have to travel — unless they do not, which is the edge
        // straight to QC below.
        [(ReturnStatus.Approved, ReturnStatus.PickupScheduled)] = ReturnActor.Staff,
        [(ReturnStatus.Approved, ReturnStatus.Picked)] = ReturnActor.Staff,
        [(ReturnStatus.Approved, ReturnStatus.Received)] = ReturnActor.Platform | ReturnActor.System,
        [(ReturnStatus.Approved, ReturnStatus.Cancelled)] = ReturnActor.Customer | ReturnActor.Platform,

        // The store waived the pickup: there is nothing to inspect, and the shopper keeps the goods.
        // Only the platform may take it, because it is a decision to pay for goods it will not see.
        [(ReturnStatus.Approved, ReturnStatus.QcPassed)] = ReturnActor.Platform,

        [(ReturnStatus.PickupScheduled, ReturnStatus.Picked)] = ReturnActor.Staff,
        [(ReturnStatus.PickupScheduled, ReturnStatus.Cancelled)] = ReturnActor.Customer | ReturnActor.Platform,

        // A courier missed the collection, or the shopper was not there. Back to waiting.
        [(ReturnStatus.PickupScheduled, ReturnStatus.Approved)] = ReturnActor.Platform | ReturnActor.System,

        [(ReturnStatus.Picked, ReturnStatus.InTransit)] = ReturnActor.System | ReturnActor.Platform,
        [(ReturnStatus.Picked, ReturnStatus.Received)] = ReturnActor.Platform | ReturnActor.System,
        [(ReturnStatus.InTransit, ReturnStatus.Received)] = ReturnActor.Platform | ReturnActor.System,

        // The inspection. Both outcomes are the platform's — a seller may not grade goods they are
        // about to be charged for, and a shopper may obviously not grade their own.
        [(ReturnStatus.Received, ReturnStatus.QcPassed)] = ReturnActor.Platform,
        [(ReturnStatus.Received, ReturnStatus.QcFailed)] = ReturnActor.Platform,

        // A failed inspection reconsidered. It happens: the second look finds the packaging was the
        // problem, or support decides a goodwill refund is cheaper than the argument.
        [(ReturnStatus.QcFailed, ReturnStatus.QcPassed)] = ReturnActor.Platform,
        [(ReturnStatus.QcFailed, ReturnStatus.Closed)] = ReturnActor.Platform,

        // What passing leads to. Refunded and Replaced are separate because they are different
        // promises to the shopper, and because only one of them costs money now.
        [(ReturnStatus.QcPassed, ReturnStatus.Refunded)] = ReturnActor.Platform | ReturnActor.System,
        [(ReturnStatus.QcPassed, ReturnStatus.Replaced)] = ReturnActor.Staff,
        [(ReturnStatus.QcPassed, ReturnStatus.Closed)] = ReturnActor.Platform,

        [(ReturnStatus.Refunded, ReturnStatus.Closed)] = ReturnActor.Platform | ReturnActor.System,
        [(ReturnStatus.Replaced, ReturnStatus.Closed)] = ReturnActor.Platform | ReturnActor.System,
    };

    /// <summary>Whether nothing further can happen to a return in this state.</summary>
    /// <param name="status">The status.</param>
    public static bool IsTerminal(ReturnStatus status) => Terminal.Contains(status);

    /// <summary>Whether the goods are still with the shopper.</summary>
    /// <param name="status">The status.</param>
    public static bool IsBeforeCollection(ReturnStatus status) => BeforeCollection.Contains(status);

    /// <summary>Whether the machine has this edge at all, whoever is asking.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    public static bool Exists(ReturnStatus from, ReturnStatus to) => Edges.ContainsKey((from, to));

    /// <summary>Whether this actor may take this edge.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it is being moved to.</param>
    /// <param name="actor">Who is asking.</param>
    public static bool IsAllowed(ReturnStatus from, ReturnStatus to, ReturnActor actor)
        => Edges.TryGetValue((from, to), out var permitted) && (permitted & actor) != 0;

    /// <summary>Every state this one can move to for this actor, for an admin screen's buttons.</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="actor">Who is asking.</param>
    public static IReadOnlyList<ReturnStatus> NextFor(ReturnStatus from, ReturnActor actor)
        =>
        [
            .. Edges
                .Where(edge => edge.Key.From == from && (edge.Value & actor) != 0)
                .Select(edge => edge.Key.To)
                .OrderBy(status => status),
        ];

    /// <summary>
    /// The ordering module's word for what a return's state means for the sale, or null when it
    /// means nothing.
    /// </summary>
    /// <remarks>
    /// Three states cross the boundary and no more. A sub-order does not have a QC result and has no
    /// use for one; what it needs to know is that a return has been asked for, that goods are
    /// moving, and that they arrived. The names are the ordering module's, spelled as strings
    /// because no module may reference another.
    /// </remarks>
    /// <param name="status">The return's state.</param>
    public static string? OrderStatusFor(ReturnStatus status)
        => status switch
        {
            ReturnStatus.Requested => "ReturnRequested",
            ReturnStatus.Approved => "ReturnInProgress",
            ReturnStatus.PickupScheduled => "ReturnInProgress",
            ReturnStatus.Picked => "ReturnInProgress",
            ReturnStatus.InTransit => "ReturnInProgress",
            ReturnStatus.Received => "Returned",
            _ => null,
        };

    /// <summary>A sentence a shopper can read, for the order timeline entry.</summary>
    /// <param name="status">The return's state.</param>
    public static string Narrate(ReturnStatus status)
        => status switch
        {
            ReturnStatus.Requested => "You asked to return an item.",
            ReturnStatus.Approved => "Your return was approved.",
            ReturnStatus.Rejected => "Your return could not be approved.",
            ReturnStatus.PickupScheduled => "A courier has been arranged to collect your return.",
            ReturnStatus.Picked => "The courier has collected your return.",
            ReturnStatus.InTransit => "Your return is on its way back to us.",
            ReturnStatus.Received => "We have received your return.",
            ReturnStatus.QcPassed => "Your return passed our checks.",
            ReturnStatus.QcFailed => "Your return did not pass our checks.",
            ReturnStatus.Refunded => "Your refund is on its way.",
            ReturnStatus.Replaced => "Your replacement has been dispatched.",
            ReturnStatus.Closed => "Your return is complete.",
            ReturnStatus.Cancelled => "Your return was cancelled.",
            _ => "Your return was updated.",
        };
}
