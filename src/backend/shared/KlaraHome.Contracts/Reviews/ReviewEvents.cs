using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Reviews;

/// <summary>
/// A review became visible to shoppers (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// Raised when a review is approved, which for a store with auto-approval on is the same instant it
/// is written and for every other store is the moment a moderator releases it. Nothing is published
/// when a review is <em>submitted</em>, because a review awaiting moderation is not yet a fact about
/// the product — it is a fact about the queue.
/// </para>
/// <para>
/// It carries the review rather than the aggregate. A consumer that wants "what do people think of
/// this product" wants <see cref="ProductRatingChanged"/>, which is published alongside this one and
/// is the event a projection should subscribe to; this one exists for the consumers that care about
/// the individual opinion — a seller being told they have been reviewed, and a moderator's activity
/// feed.
/// </para>
/// </remarks>
/// <param name="ReviewId">The review.</param>
/// <param name="ProductId">The product reviewed.</param>
/// <param name="VariantId">The exact variant bought, which is finer than the review is displayed at.</param>
/// <param name="VendorId">The seller who sold it. A review is always of one seller's sale.</param>
/// <param name="CustomerId">Who wrote it.</param>
/// <param name="Rating">One to five.</param>
/// <param name="Title">Its headline, or null.</param>
/// <param name="IsVerifiedPurchase">
/// Whether it is tied to a delivered order line. Always true in this platform — an unverified review
/// cannot be written at all — and carried anyway so a consumer never has to assume it.
/// </param>
/// <param name="PublishedAt">When it became visible.</param>
public sealed record ReviewPublished(
    Guid ReviewId,
    Guid ProductId,
    Guid VariantId,
    Guid VendorId,
    Guid CustomerId,
    int Rating,
    string? Title,
    bool IsVerifiedPurchase,
    DateTimeOffset PublishedAt) : IntegrationEvent;

/// <summary>
/// A product's aggregate rating moved.
/// </summary>
/// <remarks>
/// <para>
/// The event every read-model wants, and it is deliberately not the same thing as a review being
/// published. A rating moves when a review is approved, when an approved one is rejected or removed,
/// and when a moderator edits a score — three causes for one effect, and a consumer that subscribed
/// to the cause would show a stale average for the other two.
/// </para>
/// <para>
/// It carries the recomputed aggregate rather than the delta, because the Reviews module is the only
/// place that can compute it and a consumer adding a delta to its own copy would drift the first time
/// a message was redelivered. Applying this event is therefore idempotent by construction: the
/// consumer stores two numbers it was handed.
/// </para>
/// </remarks>
/// <param name="ProductId">The product.</param>
/// <param name="RatingAverage">
/// The mean of every approved review's score, to one decimal place, or null when there are none.
/// Null rather than zero: a product nobody has reviewed has no opinion, and rendering it as nought
/// out of five is a lie the storefront would have to un-tell.
/// </param>
/// <param name="RatingCount">How many approved reviews that is over.</param>
public sealed record ProductRatingChanged(
    Guid ProductId,
    decimal? RatingAverage,
    int RatingCount) : IntegrationEvent;

/// <summary>
/// A seller's aggregate rating moved.
/// </summary>
/// <remarks>
/// Separate from <see cref="ProductRatingChanged"/> because it is a different aggregate over the same
/// reviews, and it moves on a different schedule: one review changes one product's average and one
/// seller's, and a seller's average is over every product they have ever sold. Keeping them apart is
/// what lets the Catalog module subscribe to products and the Vendors module to sellers, without
/// either being handed a number it has no column for.
/// </remarks>
/// <param name="VendorId">The seller.</param>
/// <param name="RatingAverage">The mean of every approved review of their sales, or null.</param>
/// <param name="RatingCount">How many approved reviews that is over.</param>
public sealed record VendorRatingChanged(
    Guid VendorId,
    decimal? RatingAverage,
    int RatingCount) : IntegrationEvent;
