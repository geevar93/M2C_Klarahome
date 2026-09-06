namespace KlaraHome.Modules.Reviews.Application;

/// <summary>One image on a review, resolved to something a browser can load.</summary>
/// <param name="FileId">The stored file.</param>
/// <param name="Url">Where to fetch it, or null when the file is gone.</param>
/// <param name="Caption">What the reviewer called it.</param>
internal sealed record ReviewImageResponse(Guid FileId, string? Url, string? Caption);

/// <summary>
/// A review, as a shopper reads it.
/// </summary>
/// <remarks>
/// <see cref="AuthorName"/> is a display name and there is deliberately no other identifier of the
/// author on it. A review is a public document (docs/07-security-compliance.md §4), and a customer
/// id on a public payload is a join key handed to anybody who can read the page.
/// </remarks>
/// <param name="Id">The review.</param>
/// <param name="ProductId">The product it is about.</param>
/// <param name="VariantId">The variant actually bought.</param>
/// <param name="Rating">One to five.</param>
/// <param name="Title">Its headline.</param>
/// <param name="Body">What they wrote.</param>
/// <param name="AuthorName">The name to show.</param>
/// <param name="IsVerifiedPurchase">Whether it is tied to a delivered purchase. Always true here.</param>
/// <param name="Images">The pictures they attached.</param>
/// <param name="HelpfulCount">How many people said it helped.</param>
/// <param name="NotHelpfulCount">How many said it did not.</param>
/// <param name="VendorReply">The seller's public reply, or null.</param>
/// <param name="VendorRepliedAt">When they replied.</param>
/// <param name="PublishedAt">When it became visible.</param>
internal sealed record ReviewResponse(
    Guid Id,
    Guid ProductId,
    Guid VariantId,
    int Rating,
    string? Title,
    string? Body,
    string? AuthorName,
    bool IsVerifiedPurchase,
    IReadOnlyList<ReviewImageResponse> Images,
    int HelpfulCount,
    int NotHelpfulCount,
    string? VendorReply,
    DateTimeOffset? VendorRepliedAt,
    DateTimeOffset? PublishedAt);

/// <summary>
/// A review as a moderator sees it: everything the shopper's view has, plus the parts that decide
/// what happens to it.
/// </summary>
/// <param name="Id">The review.</param>
/// <param name="ProductId">The product.</param>
/// <param name="VariantId">The variant.</param>
/// <param name="VendorId">The seller who sold it.</param>
/// <param name="CustomerId">Who wrote it.</param>
/// <param name="OrderLineId">The delivered line that proves the purchase.</param>
/// <param name="Rating">One to five.</param>
/// <param name="Title">Its headline.</param>
/// <param name="Body">What they wrote.</param>
/// <param name="AuthorName">The name shown.</param>
/// <param name="Status">Pending, Approved or Rejected.</param>
/// <param name="ModeratedBy">Who decided.</param>
/// <param name="ModeratedAt">When.</param>
/// <param name="ModerationNote">Why.</param>
/// <param name="Images">The pictures attached.</param>
/// <param name="HelpfulCount">How many said it helped.</param>
/// <param name="NotHelpfulCount">How many said it did not.</param>
/// <param name="ReportCount">How many open complaints there are about it.</param>
/// <param name="VendorReply">The seller's reply.</param>
/// <param name="PublishedAt">When it became visible.</param>
/// <param name="CreatedAt">When it was written.</param>
internal sealed record ModeratedReviewResponse(
    Guid Id,
    Guid ProductId,
    Guid VariantId,
    Guid VendorId,
    Guid CustomerId,
    Guid OrderLineId,
    int Rating,
    string? Title,
    string? Body,
    string? AuthorName,
    string Status,
    Guid? ModeratedBy,
    DateTimeOffset? ModeratedAt,
    string? ModerationNote,
    IReadOnlyList<ReviewImageResponse> Images,
    int HelpfulCount,
    int NotHelpfulCount,
    int ReportCount,
    string? VendorReply,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// What a product's reviews add up to.
/// </summary>
/// <remarks>
/// The histogram is returned alongside the average because it is what a shopper actually reads. Two
/// products at 4.2 are not the same product when one has forty fours and the other has twenty fives
/// and twenty threes, and a page that showed only the mean would hide that.
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="Average">The mean, to one decimal place, or null when there are no reviews.</param>
/// <param name="Count">How many approved reviews there are.</param>
/// <param name="OneStar">How many gave one star.</param>
/// <param name="TwoStar">How many gave two.</param>
/// <param name="ThreeStar">How many gave three.</param>
/// <param name="FourStar">How many gave four.</param>
/// <param name="FiveStar">How many gave five.</param>
internal sealed record RatingSummaryResponse(
    Guid ProductId,
    decimal? Average,
    int Count,
    int OneStar,
    int TwoStar,
    int ThreeStar,
    int FourStar,
    int FiveStar);

/// <summary>
/// Whether the shopper reading a product page may review it, and against which purchase.
/// </summary>
/// <remarks>
/// The endpoint behind the "write a review" button. It answers before the shopper types anything,
/// because a form that accepts eight hundred words and then refuses them is the worst possible place
/// to enforce the purchase rule.
/// </remarks>
/// <param name="CanReview">Whether there is at least one delivered line still unreviewed.</param>
/// <param name="Eligible">The lines they could review, newest delivery first.</param>
internal sealed record ReviewEligibilityResponse(bool CanReview, IReadOnlyList<EligiblePurchaseResponse> Eligible);

/// <summary>One delivered purchase a review could be written against.</summary>
/// <param name="OrderLineId">The line, which the write endpoint takes as proof.</param>
/// <param name="OrderNumber">The order it came from, so the shopper can tell two purchases apart.</param>
/// <param name="VariantId">What was bought.</param>
/// <param name="Sku">Its stock-keeping unit.</param>
/// <param name="Name">What it was called on the order.</param>
/// <param name="DeliveredAt">When it arrived.</param>
internal sealed record EligiblePurchaseResponse(
    Guid OrderLineId,
    string OrderNumber,
    Guid VariantId,
    string Sku,
    string Name,
    DateTimeOffset DeliveredAt);

/// <summary>An answer, as it is rendered under a question.</summary>
/// <param name="Id">The answer.</param>
/// <param name="Body">What was said.</param>
/// <param name="AuthorType">Customer, Vendor or Store — which is what gives it its weight.</param>
/// <param name="AuthorName">The name to show.</param>
/// <param name="PublishedAt">When it became visible.</param>
internal sealed record AnswerResponse(
    Guid Id,
    string Body,
    string AuthorType,
    string? AuthorName,
    DateTimeOffset? PublishedAt);

/// <summary>A question and the answers it has attracted.</summary>
/// <param name="Id">The question.</param>
/// <param name="ProductId">The product asked about.</param>
/// <param name="Body">What was asked.</param>
/// <param name="AuthorName">Who asked.</param>
/// <param name="AnswerCount">How many approved answers there are.</param>
/// <param name="Answers">The answers, oldest first.</param>
/// <param name="PublishedAt">When it became visible.</param>
internal sealed record QuestionResponse(
    Guid Id,
    Guid ProductId,
    string Body,
    string? AuthorName,
    int AnswerCount,
    IReadOnlyList<AnswerResponse> Answers,
    DateTimeOffset? PublishedAt);

/// <summary>A question as a moderator sees it, answers included whatever their state.</summary>
/// <param name="Id">The question.</param>
/// <param name="ProductId">The product.</param>
/// <param name="CustomerId">Who asked.</param>
/// <param name="Body">What was asked.</param>
/// <param name="AuthorName">The name shown.</param>
/// <param name="Status">Pending, Approved or Rejected.</param>
/// <param name="ReportCount">How many open complaints there are.</param>
/// <param name="Answers">Every answer, in whatever state.</param>
/// <param name="CreatedAt">When it was asked.</param>
internal sealed record ModeratedQuestionResponse(
    Guid Id,
    Guid ProductId,
    Guid CustomerId,
    string Body,
    string? AuthorName,
    string Status,
    int ReportCount,
    IReadOnlyList<ModeratedAnswerResponse> Answers,
    DateTimeOffset CreatedAt);

/// <summary>An answer as a moderator sees it.</summary>
/// <param name="Id">The answer.</param>
/// <param name="Body">What was said.</param>
/// <param name="AuthorType">Customer, Vendor or Store.</param>
/// <param name="AuthorName">The name shown.</param>
/// <param name="VendorId">The seller, when one answered.</param>
/// <param name="Status">Pending, Approved or Rejected.</param>
/// <param name="ReportCount">How many open complaints there are.</param>
/// <param name="CreatedAt">When it was written.</param>
internal sealed record ModeratedAnswerResponse(
    Guid Id,
    string Body,
    string AuthorType,
    string? AuthorName,
    Guid? VendorId,
    string Status,
    int ReportCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// One saved item, with everything the card needs resolved at read time.
/// </summary>
/// <remarks>
/// The price and availability come from the catalogue on every read rather than from the saved row.
/// A wishlist is a live shopping surface; one that quoted the price at the moment of saving would be
/// wrong for almost every item on it and would make the "add to basket" button a surprise.
/// </remarks>
/// <param name="VariantId">The sellable thing saved.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="ListingId">The offer currently winning the buy box, or null when nobody is selling it.</param>
/// <param name="Name">Its display name today.</param>
/// <param name="Slug">Its product's URL segment.</param>
/// <param name="Sku">Its stock-keeping unit.</param>
/// <param name="ImageFileId">The picture the card renders.</param>
/// <param name="ImageUrl">Where to fetch that picture.</param>
/// <param name="Mrp">Maximum retail price today.</param>
/// <param name="Price">The buy-box price today, or null when there is no offer.</param>
/// <param name="CurrencyCode">What both amounts are in.</param>
/// <param name="IsPurchasable">Whether it can be bought right now.</param>
/// <param name="RatingAverage">Its average review score.</param>
/// <param name="RatingCount">How many reviews that is over.</param>
/// <param name="Note">What the shopper wrote against it.</param>
/// <param name="Priority">Where it sits in the list.</param>
/// <param name="AddedAt">When it was saved.</param>
internal sealed record WishlistItemResponse(
    Guid VariantId,
    Guid ProductId,
    Guid? ListingId,
    string? Name,
    string? Slug,
    string? Sku,
    Guid? ImageFileId,
    string? ImageUrl,
    decimal? Mrp,
    decimal? Price,
    string? CurrencyCode,
    bool IsPurchasable,
    decimal? RatingAverage,
    int RatingCount,
    string? Note,
    int Priority,
    DateTimeOffset AddedAt);

/// <summary>A wishlist and what is on it.</summary>
/// <param name="Id">The list.</param>
/// <param name="Name">What the shopper calls it.</param>
/// <param name="IsDefault">Whether it is the one "save for later" writes to.</param>
/// <param name="ItemCount">How many items are on it.</param>
/// <param name="ShareToken">The share token, or null when it is private.</param>
/// <param name="Items">What is on it, resolved.</param>
internal sealed record WishlistResponse(
    Guid Id,
    string Name,
    bool IsDefault,
    int ItemCount,
    string? ShareToken,
    IReadOnlyList<WishlistItemResponse> Items);

/// <summary>A wishlist as somebody holding a share link sees it.</summary>
/// <remarks>
/// Deliberately narrower than <see cref="WishlistResponse"/>: no share token, because a viewer must
/// not be able to re-share what they were shown, and no note, because a note on a gift list is
/// usually written to the buyer and not to the recipient.
/// </remarks>
/// <param name="Name">What the list is called.</param>
/// <param name="ItemCount">How many items are on it.</param>
/// <param name="Items">What is on it.</param>
internal sealed record SharedWishlistResponse(
    string Name,
    int ItemCount,
    IReadOnlyList<WishlistItemResponse> Items);

/// <summary>A standing request to be told about something.</summary>
/// <param name="Id">The subscription.</param>
/// <param name="Kind">BackInStock or PriceDrop.</param>
/// <param name="VariantId">The sellable thing being watched.</param>
/// <param name="ProductId">Its product.</param>
/// <param name="Status">Active, Notified, Cancelled or Expired.</param>
/// <param name="TargetPrice">The price being waited for.</param>
/// <param name="PriceAtSubscription">What it cost when the shopper subscribed.</param>
/// <param name="NotifiedAt">When the alert fired.</param>
/// <param name="ExpiresAt">When it stops being watched.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record StockSubscriptionResponse(
    Guid Id,
    string Kind,
    Guid VariantId,
    Guid ProductId,
    string Status,
    decimal? TargetPrice,
    decimal? PriceAtSubscription,
    DateTimeOffset? NotifiedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>A complaint about something somebody wrote.</summary>
/// <param name="Id">The report.</param>
/// <param name="Target">Review, Question or Answer.</param>
/// <param name="TargetId">Which one.</param>
/// <param name="Reason">Why it was reported.</param>
/// <param name="Note">What the reporter said.</param>
/// <param name="Status">Open, Upheld or Dismissed.</param>
/// <param name="ResolvedAt">When it was closed.</param>
/// <param name="Resolution">What the moderator concluded.</param>
/// <param name="CreatedAt">When it was raised.</param>
internal sealed record AbuseReportResponse(
    Guid Id,
    string Target,
    Guid TargetId,
    string Reason,
    string? Note,
    string Status,
    DateTimeOffset? ResolvedAt,
    string? Resolution,
    DateTimeOffset CreatedAt);
