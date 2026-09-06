using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Endpoints;

namespace KlaraHome.Modules.Reviews.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they do to somebody else's words".
/// </summary>
/// <remarks>
/// <para>
/// This module has three audiences rather than the storefront's one, and the questions each of them
/// raises are different. A shopper may write and may edit what they wrote. A seller may reply to a
/// review of their own sale and may answer a question about their own product, and may do nothing
/// else. Staff may moderate.
/// </para>
/// <para>
/// <see cref="VendorId"/> is read from the caller's token and never from a request. A seller
/// identifying their own vendor in a body would be a seller able to reply as a competitor, and the
/// reply is the most visible piece of text a seller can put under a bad review.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class ReviewScope(ICallerContext caller)
{
    /// <summary>The user to attribute a write, a moderation decision or a reply to.</summary>
    public Guid? ActorId => caller.UserId;

    /// <summary>The shopper, when the caller is one. Reviews and wishlists are keyed on it.</summary>
    public Guid? CustomerId => caller.UserId;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? VendorId => caller.VendorId;

    /// <summary>Whether the caller may take somebody's words down.</summary>
    public bool CanModerate => caller.HasPermission(ReviewPermissions.ReviewModerate);

    /// <summary>Whether the caller may read the queue, including what is pending and what was refused.</summary>
    public bool CanRead => caller.HasPermission(ReviewPermissions.ReviewRead) || CanModerate;

    /// <summary>Whether the caller may write a public reply.</summary>
    public bool CanReply => caller.HasPermission(ReviewPermissions.ReviewReply);

    /// <summary>Whether the caller is signed in at all.</summary>
    public bool IsAuthenticated => caller.IsAuthenticated && caller.UserId is not null;

    /// <summary>
    /// How an answer this caller writes should be labelled.
    /// </summary>
    /// <remarks>
    /// Decided here from the caller's own claims and never taken from the request. "Answered by the
    /// seller" is the most trusted line on a product page, and a request that could assert it would
    /// let any shopper put words in a seller's mouth.
    /// </remarks>
    public AnswerAuthor AuthorType => VendorId is not null
        ? AnswerAuthor.Vendor
        : CanModerate || CanReply
            ? AnswerAuthor.Store
            : AnswerAuthor.Customer;

    /// <summary>Whether this caller owns a review of the named seller's sale.</summary>
    /// <remarks>
    /// True for platform staff, who hold no vendor and answer for the store. That asymmetry is
    /// deliberate: staff replying on a seller's behalf is a support action a marketplace needs, and a
    /// seller replying on another seller's behalf is not.
    /// </remarks>
    /// <param name="vendorId">The seller whose sale the review is of.</param>
    public bool OwnsVendor(Guid vendorId) => VendorId is null || VendorId == vendorId;
}
