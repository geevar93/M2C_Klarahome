using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Content.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// <para>
/// Declared in one place so that two handlers cannot answer the same situation with two different
/// codes. Almost all of these are shown to an <em>editor</em> rather than to a shopper, which changes
/// what a good message is: an editor can act on "a page is already published at that address", and
/// telling them so is cheaper than a support ticket asking why the save button did nothing.
/// </para>
/// <para>
/// The storefront reads have exactly one failure between them — a page that is not published is a
/// 404 — and that is the right number. A shopper who asks for a URL the store does not serve has
/// asked for a page that does not exist, and there is nothing else honest to say.
/// </para>
/// </remarks>
internal static class ContentErrors
{
    /// <summary>The page, menu, banner, collection or redirect does not exist.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error NotFound(string what)
        => Error.NotFound("CONTENT_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>Something else already answers to that address.</summary>
    /// <remarks>
    /// A conflict rather than a validation failure, because the value the editor typed is perfectly
    /// valid — it is the state of the world that refuses it, and the difference decides whether the
    /// admin screen highlights the field or offers to open the page that already has the slug.
    /// </remarks>
    /// <param name="slug">The address.</param>
    public static Error DuplicateSlug(string slug)
        => Error.Conflict("CONTENT_DUPLICATE_SLUG", $"Something is already published at /{slug}.");

    /// <summary>A menu already answers to that code.</summary>
    /// <param name="code">The code.</param>
    public static Error DuplicateCode(string code)
        => Error.Conflict("CONTENT_DUPLICATE_CODE", $"A menu with the code '{code}' already exists.");

    /// <summary>A redirect already claims that path.</summary>
    /// <param name="path">The path.</param>
    public static Error DuplicateRedirect(string path)
        => Error.Conflict("CONTENT_DUPLICATE_REDIRECT", $"A redirect already claims {path}.");

    /// <summary>The slug is not a slug.</summary>
    public static Error InvalidSlug { get; } =
        Error.Validation(
            "CONTENT_INVALID_SLUG",
            "An address may contain only lowercase letters, digits and single hyphens.");

    /// <summary>The path is not one this manager can match.</summary>
    public static Error InvalidPath { get; } =
        Error.Validation(
            "CONTENT_INVALID_PATH",
            "A path must begin with a slash and carry no query string.");

    /// <summary>A redirect that points at itself.</summary>
    /// <remarks>
    /// Refused rather than stored, because the storefront would answer it with a redirect to the
    /// same URL and the browser would give up after twenty of them. It is a typo an editor makes
    /// while renaming a page and it is trivial to catch here.
    /// </remarks>
    public static Error CircularRedirect { get; } =
        Error.Validation("CONTENT_CIRCULAR_REDIRECT", "A redirect cannot point at itself.");

    /// <summary>The workflow has no such move.</summary>
    /// <param name="from">Where the page is.</param>
    /// <param name="to">Where it was being moved to.</param>
    public static Error IllegalTransition(string from, string to)
        => Error.Validation(
            "CONTENT_ILLEGAL_TRANSITION",
            $"A page cannot go from {from} to {to}.");

    /// <summary>The workflow has the move, but not for this caller.</summary>
    /// <remarks>
    /// Told apart from the permission the endpoint asks for, and deliberately: an editor holding
    /// <c>content.page.manage</c> may write any page and publish none of them, and answering that
    /// with a bare 403 would send them to look for a missing role rather than to the publish queue.
    /// </remarks>
    /// <param name="to">Where it was being moved to.</param>
    public static Error TransitionNotAllowed(string to)
        => Error.Forbidden(
            "CONTENT_TRANSITION_NOT_ALLOWED",
            $"You do not have permission to move a page to {to}.");

    /// <summary>A page was scheduled for a time that has already passed.</summary>
    public static Error ScheduleInPast { get; } =
        Error.Validation(
            "CONTENT_SCHEDULE_IN_PAST",
            "A publish time must be in the future. Publish it now instead.");

    /// <summary>A second home page was published.</summary>
    /// <remarks>
    /// Refused rather than resolved, because there is no correct guess. Two published home pages is
    /// a storefront rendering whichever row the planner happened to return first, and an operator
    /// who wants a different one has to say which.
    /// </remarks>
    public static Error HomePageAlreadyPublished { get; } =
        Error.Conflict(
            "CONTENT_HOME_ALREADY_PUBLISHED",
            "A home page is already published. Unpublish it before publishing another.");

    /// <summary>A page that has been live was deleted.</summary>
    public static Error PublishedPageNotDeletable { get; } =
        Error.Conflict(
            "CONTENT_PAGE_NOT_DELETABLE",
            "A page that has been published cannot be deleted. Archive it instead.");

    /// <summary>The content of a frozen page was edited.</summary>
    public static Error PageNotEditable(string status)
        => Error.Conflict(
            "CONTENT_PAGE_NOT_EDITABLE",
            $"A page that is {status} cannot be edited. Move it back to draft first.");

    /// <summary>A block type nothing in this platform can render.</summary>
    /// <param name="type">What was asked for.</param>
    public static Error UnknownBlockType(string type)
        => Error.Validation("CONTENT_UNKNOWN_BLOCK_TYPE", $"'{type}' is not a block type this store renders.");

    /// <summary>A block's configuration does not match its type's schema.</summary>
    /// <remarks>
    /// Field-level, because a block editor has one form field per schema field and can put the
    /// message next to the box that is wrong. A single flat message would make an editor hunt
    /// through a form with fourteen inputs.
    /// </remarks>
    /// <param name="fieldErrors">The field messages, keyed by the JSON path within the block.</param>
    public static Error InvalidBlock(IReadOnlyDictionary<string, IReadOnlyList<string>> fieldErrors)
        => Error.Validation(
            fieldErrors,
            "CONTENT_INVALID_BLOCK",
            "One or more blocks are not configured correctly.");

    /// <summary>A custom-HTML block was written by somebody who may not write one.</summary>
    /// <remarks>
    /// Its own code and its own permission. Arbitrary markup on a page a shopper loads is a stored
    /// cross-site-scripting vector, and the person who may write copy is not automatically the person
    /// who may write script tags (docs/07-security-compliance.md §3).
    /// </remarks>
    public static Error CustomHtmlNotAllowed { get; } =
        Error.Forbidden(
            "CONTENT_CUSTOM_HTML_NOT_ALLOWED",
            "Custom HTML blocks need the custom-HTML permission and the feature switched on.");

    /// <summary>A page carries more blocks than one page may.</summary>
    /// <param name="maximum">How many it may.</param>
    public static Error TooManyBlocks(int maximum)
        => Error.Validation("CONTENT_TOO_MANY_BLOCKS", $"A page may hold at most {maximum} blocks.");

    /// <summary>A menu nests deeper than the storefront renders.</summary>
    /// <param name="maximum">How deep it may.</param>
    public static Error MenuTooDeep(int maximum)
        => Error.Validation("CONTENT_MENU_TOO_DEEP", $"A menu may nest at most {maximum} levels.");

    /// <summary>A menu item names a parent that is not in the same menu.</summary>
    public static Error MenuItemOrphaned { get; } =
        Error.Validation(
            "CONTENT_MENU_ITEM_ORPHANED",
            "Every menu item's parent must be another item in the same menu, listed before it.");

    /// <summary>A menu item does not carry what its link type needs.</summary>
    /// <param name="linkType">The link type.</param>
    public static Error MenuLinkIncomplete(string linkType)
        => Error.Validation(
            "CONTENT_MENU_LINK_INCOMPLETE",
            $"A {linkType} link needs a target.");

    /// <summary>A banner without the one thing its placement renders.</summary>
    /// <param name="placement">The placement.</param>
    public static Error BannerIncomplete(string placement)
        => Error.Validation(
            "CONTENT_BANNER_INCOMPLETE",
            $"A {placement} banner needs {(placement == nameof(Domain.BannerPlacement.AnnouncementBar)
                ? "a message"
                : "an image and alt text")}.");

    /// <summary>A window whose end is not after its start.</summary>
    public static Error InvalidWindow { get; } =
        Error.Validation("CONTENT_INVALID_WINDOW", "An end time must be after its start time.");

    /// <summary>A rule that names a condition this platform cannot evaluate.</summary>
    /// <param name="reason">What is wrong with it, in an editor's words.</param>
    public static Error InvalidRule(string reason)
        => Error.Validation("CONTENT_INVALID_RULE", reason);

    /// <summary>A rule was refreshed on a collection that has none.</summary>
    public static Error NotRuleBased { get; } =
        Error.Validation(
            "CONTENT_COLLECTION_NOT_RULE_BASED",
            "That collection is hand-picked and has no rule to refresh.");

    /// <summary>A version that does not belong to the page it was asked for.</summary>
    public static Error UnknownVersion(int version)
        => Error.NotFound("CONTENT_UNKNOWN_VERSION", $"That page has no version {version}.");

    /// <summary>The page cursor could not be read.</summary>
    public static Error InvalidCursor { get; } =
        Error.Malformed(
            "CONTENT_INVALID_CURSOR",
            "That page cursor is not valid. Start again from the first page.");

    /// <summary>The store has no canonical URL configured, and the caller asked for absolute ones.</summary>
    /// <remarks>
    /// The sitemap is the one surface that cannot fall back to a relative URL: the protocol requires
    /// <c>loc</c> to be absolute, and a sitemap of relative paths is a file every crawler rejects
    /// whole. Better to refuse it and say which setting is missing.
    /// </remarks>
    public static Error CanonicalUrlNotConfigured { get; } =
        Error.Validation(
            "CONTENT_CANONICAL_URL_NOT_CONFIGURED",
            "Set the canonical base URL in the SEO settings before generating a sitemap.");
}
