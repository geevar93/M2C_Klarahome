namespace KlaraHome.Modules.Returns.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another, so the two lists are kept in step by a test that
/// asserts every permission an endpoint asks for appears in the catalogue — the arrangement every
/// module since Media has used.
/// </para>
/// <para>
/// The storefront surface takes none of these. A shopper acts on their own returns through their own
/// token, and every route resolves an RMA by <em>(return, customer)</em> — a permission would be the
/// wrong tool, because there is no role that should let one shopper see another's.
/// </para>
/// <para>
/// Four permissions, and the split is the split between four jobs that are usually four people. Read
/// is support answering "where is my refund". Manage is the queue: approve, refuse, book a
/// collection. QC is the receiving bay, and it is separate because it is the one decision a seller
/// may never take about their own goods. Refund is finance, and it is separate because it is the one
/// that moves money.
/// </para>
/// </remarks>
internal static class ReturnsPermissions
{
    /// <summary>List returns and read one, with its evidence and its history.</summary>
    /// <remarks>
    /// A read of a shopper's photographs and their words about a product, so it is a grant rather
    /// than something every operator has.
    /// </remarks>
    public const string ReturnRead = "returns.return.read";

    /// <summary>Approve a return, refuse it, book its collection and close it.</summary>
    /// <remarks>
    /// The daily work of the returns queue, and the permission a seller holds for their own goods.
    /// It is one permission rather than four because they are one job: nobody approves a return they
    /// may not then arrange a courier for.
    /// </remarks>
    public const string ReturnManage = "returns.return.manage";

    /// <summary>
    /// Book a parcel in and grade what is in it.
    /// </summary>
    /// <remarks>
    /// Deliberately separate, and deliberately not a seller's. Grading decides whether a shopper is
    /// refunded and whether a seller is charged, and a seller who could grade their own returns
    /// would be deciding their own liability.
    /// </remarks>
    public const string ReturnQc = "returns.qc.manage";

    /// <summary>
    /// Pay a refund out of a return, and raise the credit note that goes with it.
    /// </summary>
    /// <remarks>
    /// The money, and finance's rather than the queue's. It sits on top of the Payments module's own
    /// maker–checker threshold rather than replacing it — holding this permission raises a refund,
    /// it does not approve one above the threshold.
    /// </remarks>
    public const string ReturnRefund = "returns.refund.manage";

    /// <summary>Edit the reason codes and the policy attached to each.</summary>
    /// <remarks>
    /// A commercial decision — who pays the freight, what is inspected, what is auto-approved — and
    /// deliberately not the queue's. It is staff-only: a seller may not decide that their own
    /// category of fault is free to return.
    /// </remarks>
    public const string ReasonManage = "returns.reason.manage";
}
