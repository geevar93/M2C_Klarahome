using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Reviews.Infrastructure;

/// <summary>
/// What the Reviews module reads from configuration.
/// </summary>
/// <remarks>
/// <para>
/// The one entry here that is a policy rather than a deployment concern is
/// <see cref="AutoApproveReviews"/>, and it is here rather than in store settings for a reason worth
/// stating: turning moderation off is a decision about legal exposure, not about merchandising. In
/// India an intermediary that publishes user content without any review has a different position
/// under the IT Rules than one that moderates (docs/07-security-compliance.md §5), and that is not a
/// switch a merchandiser should be able to flip from an admin screen at four on a Friday.
/// </para>
/// <para>
/// Everything else is a ceiling or a cadence: how hard the alert sweeps are allowed to work, and the
/// limits that stop one caller making one request expensive for everybody.
/// </para>
/// </remarks>
internal sealed class ReviewsOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Reviews";

    /// <summary>
    /// Whether a review is visible the moment it is written.
    /// </summary>
    /// <remarks>
    /// Off. A store that has just launched has nobody watching the queue, and the failure mode of
    /// moderation-by-default is a delay; the failure mode of publish-by-default is the store's own
    /// product pages carrying whatever anybody types into them. The queue is the safer default and
    /// an operator who wants the other behaviour has made that choice deliberately.
    /// </remarks>
    public bool AutoApproveReviews { get; set; }

    /// <summary>Whether a question is visible the moment it is asked.</summary>
    /// <remarks>
    /// Off, and for a sharper reason than reviews. A question is unverified by construction — there
    /// is no purchase behind it — so it is the cheapest place on the site to publish arbitrary text,
    /// and it is the first thing a spammer finds.
    /// </remarks>
    public bool AutoApproveQuestions { get; set; }

    /// <summary>Whether an answer is visible the moment it is written.</summary>
    public bool AutoApproveAnswers { get; set; }

    /// <summary>
    /// Whether a seller's own answer skips the queue.
    /// </summary>
    /// <remarks>
    /// On, and the exception is defensible: a seller answering a question about their own product is
    /// an identified party under a contract with the store, which is not the same as an anonymous
    /// shopper. Holding their answers behind moderation makes the Q&amp;A useless — the whole value
    /// of it is that somebody who knows the answer replies while the shopper is still on the page.
    /// </remarks>
    public bool AutoApproveVendorAnswers { get; set; } = true;

    /// <summary>How many days after delivery a review may still be written.</summary>
    /// <remarks>
    /// A year. Long enough that a shopper who came back to complain about something that failed in
    /// month eleven can say so, and short enough that a line delivered in a previous business is not
    /// still reviewable after a migration.
    /// </remarks>
    [Range(1, 3_650)]
    public int ReviewWindowDays { get; set; } = 365;

    /// <summary>The largest page of reviews, questions or saved items this API will serve.</summary>
    [Range(1, 100)]
    public int MaxPageSize { get; set; } = 20;

    /// <summary>How many days a stock alert is watched before it is assumed to be stale.</summary>
    /// <remarks>
    /// Ninety. An alert on something that has been out of stock for three months is almost always an
    /// alert the shopper has forgotten asking for, and sending it then is closer to spam than to
    /// service.
    /// </remarks>
    [Range(1, 730)]
    public int SubscriptionExpiryDays { get; set; } = 90;

    /// <summary>The most alerts one event will fan out to.</summary>
    /// <remarks>
    /// A restock of something popular can have thousands of people waiting. Bounded so one stock
    /// movement cannot hold a transaction over all of them; the sweep takes the rest on its next
    /// pass, which is why the handler records where it got to rather than assuming it finished.
    /// </remarks>
    [Range(10, 5_000)]
    public int MaxAlertsPerEvent { get; set; } = 500;

    /// <summary>Whether this process runs the subscription sweep. On in the worker, off in the API.</summary>
    public bool SweepEnabled { get; set; }

    /// <summary>How often the sweep runs, in minutes.</summary>
    public int SweepIntervalMinutes { get; set; } = 30;

    /// <summary>How many subscriptions one sweep pass touches.</summary>
    [Range(10, 5_000)]
    public int SweepBatchSize { get; set; } = 500;

    /// <summary>The most images one review may carry, on top of the domain's own ceiling.</summary>
    [Range(0, 6)]
    public int MaxReviewImages { get; set; } = 6;

    /// <summary>How long a share link lives before it has to be reissued, in days. Zero for forever.</summary>
    /// <remarks>
    /// Zero by default. A gift list that stopped working a week before the birthday would be worse
    /// than one that stays live, and the token can be revoked by the owner at any time — which is a
    /// better control than an expiry nobody chose.
    /// </remarks>
    [Range(0, 3_650)]
    public int ShareLinkDays { get; set; }
}
