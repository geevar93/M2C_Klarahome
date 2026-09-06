namespace KlaraHome.Modules.Reviews.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another, so the two lists are kept in step by a test that
/// asserts every permission an endpoint asks for appears in the catalogue.
/// </para>
/// <para>
/// Three, and the split is between three jobs. Reading is support's — the person who takes the call
/// about a review needs to find it and needs nothing else. Moderating is the queue, and it is the
/// only permission here that can take something down. Replying is a seller's, and it is deliberately
/// separate from moderating: a seller may answer a one-star review of their own sale, and must never
/// be able to make it disappear.
/// </para>
/// <para>
/// There is no permission for a shopper writing a review, asking a question or saving something. All
/// three are the ordinary rights of a signed-in customer, and a permission on them would mean every
/// new account needed a grant before it could use the store.
/// </para>
/// </remarks>
internal static class ReviewPermissions
{
    /// <summary>
    /// Read reviews, questions, answers and abuse reports, whatever their state.
    /// </summary>
    /// <remarks>
    /// Support's permission. It includes the pending and the refused, which is the point: the
    /// question a support agent is answering is almost always "where has my review gone", and it
    /// cannot be answered from the storefront's view of the world.
    /// </remarks>
    public const string ReviewRead = "reviews.review.read";

    /// <summary>
    /// Approve, refuse and reinstate reviews, questions and answers, and resolve abuse reports.
    /// </summary>
    /// <remarks>
    /// The queue, and the only permission in this module that can remove something a shopper wrote.
    /// It is deliberately not a seller's — a seller who could refuse reviews of their own goods would
    /// be curating their own rating, which is the single most valuable thing a marketplace review
    /// system has to protect.
    /// </remarks>
    public const string ReviewModerate = "reviews.review.moderate";

    /// <summary>
    /// Write a public reply to a review of one's own sale.
    /// </summary>
    /// <remarks>
    /// Held by sellers and by the store's own staff. It is confined to the caller's own vendor in the
    /// handler rather than by a query filter, because the same endpoint serves a seller replying to
    /// their own review and a staff member replying on behalf of the store — and the difference
    /// between them is whether the caller's token carries a vendor id.
    /// </remarks>
    public const string ReviewReply = "reviews.review.reply";
}
