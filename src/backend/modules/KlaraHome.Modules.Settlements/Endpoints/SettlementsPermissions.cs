namespace KlaraHome.Modules.Settlements.Endpoints;

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
/// There is no storefront surface at all, which is the difference between this module and every
/// other one in Phase D. A shopper has no interest in what a seller was paid and no business
/// knowing it.
/// </para>
/// <para>
/// Four permissions, and the split is the split between four jobs. Reading a statement is what a
/// seller does and what support does when a seller asks. Adjusting a ledger is a correction to
/// somebody's money and is finance's alone. Building a batch and approving one are deliberately two
/// permissions held by two people — that separation <em>is</em> the maker–checker control, and
/// granting both to one person is a decision an operator has to make explicitly rather than one the
/// permission model makes for them.
/// </para>
/// </remarks>
internal static class SettlementsPermissions
{
    /// <summary>
    /// Read settlement cycles, ledger statements, payout batches and the statutory extracts.
    /// </summary>
    /// <remarks>
    /// A seller holds this for their own account, and the vendor query filter is what confines them:
    /// a seller reading their own statement and finance reading everybody's run the same query, and
    /// the only difference is whether the caller carries a vendor id.
    /// </remarks>
    public const string SettlementRead = "settlements.settlement.read";

    /// <summary>
    /// Close a settlement cycle by hand, and post an adjustment to a seller's ledger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a seller's, and deliberately one permission rather than two. Closing a period
    /// early and writing a correction into it are the same job — the one somebody does when a
    /// statement is wrong — and separating them would only mean a person who can fix half of a
    /// problem.
    /// </para>
    /// <para>
    /// An adjustment is an append, never an edit: the permission grants writing a reversing entry,
    /// and nothing anywhere grants changing one that has been written.
    /// </para>
    /// </remarks>
    public const string SettlementManage = "settlements.settlement.manage";

    /// <summary>
    /// Build a payout batch from closed cycles, and abandon one nothing has left.
    /// </summary>
    /// <remarks>
    /// The maker's half of the control. It is a large amount of power and no money: a batch that is
    /// built and never approved sends nothing, which is exactly what makes it safe to give to the
    /// person who does the daily work.
    /// </remarks>
    public const string PayoutManage = "settlements.payout.manage";

    /// <summary>
    /// Approve a payout batch and send it.
    /// </summary>
    /// <remarks>
    /// The checker's half, and the one permission in this module that moves money. It is named in
    /// docs/02-domain-model.md §8 as the example of a fine-grained permission, which it is: the
    /// aggregate additionally refuses an approval by the person who built the batch, so holding this
    /// is necessary and not sufficient.
    /// </remarks>
    public const string PayoutApprove = "settlements.payout.approve";
}
