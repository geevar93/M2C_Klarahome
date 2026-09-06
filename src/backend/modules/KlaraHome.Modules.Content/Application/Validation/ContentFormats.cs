using System.Text;
using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Content.Application.Validation;

/// <summary>
/// The formats this module normalises and validates before it stores anything.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated regular expressions: compiled at build time, so there is no pattern parsing at
/// startup and no cache to miss. Every pattern is anchored.
/// </para>
/// <para>
/// <see cref="ToSlug"/> is the same algorithm the Catalog and Vendors modules use, and the
/// duplication is deliberate for the same reason it is there: a module may not reference another,
/// and promoting a slugger into the shared kernel would put a presentation concern in the layer that
/// holds only domain primitives.
/// </para>
/// <para>
/// <see cref="NormalizePath"/> is this module's own, and it is the more important of the two. A
/// redirect rule is only ever as good as the agreement between how a path is stored and how it is
/// looked up, and the one place that agreement can be guaranteed is a single function both sides
/// call.
/// </para>
/// </remarks>
internal static partial class ContentFormats
{
    /// <summary>A slug: lowercase, digits and single hyphens.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Slug();

    /// <summary>A menu code: lowercase, digits and single hyphens.</summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    public static partial Regex Code();

    /// <summary>The characters a slug may be built from, once everything else has been removed.</summary>
    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();

    /// <summary>Two or more consecutive slashes, which are always a mistake in a path.</summary>
    [GeneratedRegex(@"/{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSlashes();

    /// <summary>
    /// The accented Latin letters this folds to their unaccented form.
    /// </summary>
    /// <remarks>
    /// An explicit table rather than Unicode decomposition, because this product runs with
    /// <c>InvariantGlobalization</c>, where <see cref="string.Normalize(NormalizationForm)"/> does not
    /// decompose — a normalise-and-strip-marks implementation would silently turn "Café" into "caf-".
    /// </remarks>
    private const string Accented =
        "àáâãäåāăąèéêëēĕėęěìíîïĩīĭįòóôõöøōŏőùúûüũūŭůűñńņňçćĉċčýÿŷšśŝžźżđðþß";

    private static readonly string[] Folded =
    [
        "a", "a", "a", "a", "a", "a", "a", "a", "a",
        "e", "e", "e", "e", "e", "e", "e", "e", "e",
        "i", "i", "i", "i", "i", "i", "i", "i",
        "o", "o", "o", "o", "o", "o", "o", "o", "o",
        "u", "u", "u", "u", "u", "u", "u", "u", "u",
        "n", "n", "n", "n",
        "c", "c", "c", "c", "c",
        "y", "y", "y",
        "s", "s", "s",
        "z", "z", "z",
        "d", "d", "th", "ss",
    ];

    /// <summary>
    /// Turns a title into a URL slug.
    /// </summary>
    /// <remarks>
    /// The result is a suggestion. Uniqueness is settled by the handler, which is the only place that
    /// can see the other rows.
    /// </remarks>
    /// <param name="value">The title to derive a slug from.</param>
    /// <param name="maxLength">The longest slug to produce.</param>
    public static string ToSlug(string value, int maxLength = 180)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var lowered = value.Trim().ToLowerInvariant();
        var folded = new StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            var index = Accented.IndexOf(character, StringComparison.Ordinal);
            folded.Append(index >= 0 ? Folded[index] : character);
        }

        var slug = NonSlug().Replace(folded.ToString(), "-").Trim('-');

        return slug.Length <= maxLength ? slug : slug[..maxLength].TrimEnd('-');
    }

    /// <summary>
    /// Puts a path into the one shape a redirect rule is stored and looked up in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lower case, one leading slash, no trailing slash, no repeated slashes, no fragment and no
    /// query string. Each of those is a way the same page gets asked for under a different string,
    /// and a rule keyed on any of them would fire for some visitors and not others.
    /// </para>
    /// <para>
    /// Dropping the query string is the decision worth stating. A redirect keyed on
    /// <c>?utm_source=…</c> would fail for the same link without the tag, and a link with a campaign
    /// tag is precisely the link that gets shared — so the query is discarded on both sides, and the
    /// storefront reattaches whatever the visitor arrived with when it issues the redirect.
    /// </para>
    /// </remarks>
    /// <param name="value">The path as it was typed or requested.</param>
    /// <returns>The normalised path, or null when nothing usable is left.</returns>
    public static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var path = value.Trim();

        // An absolute URL is accepted and reduced to its path: an editor pasting the address out of
        // their browser is the single most common way a rule gets written.
        //
        // Gated on an explicit scheme, and that guard is load-bearing rather than tidy. Uri.TryCreate
        // reads a value beginning with two slashes as a UNC path on Windows, so "//sale//summer//"
        // would parse as the host "sale" with the path "/summer/" — silently turning one rule into a
        // different and shorter one.
        if (path.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            path = absolute.AbsolutePath;
        }

        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        path = RepeatedSlashes().Replace(path.ToLowerInvariant(), "/");

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        return path.Length == 0 ? "/" : path;
    }

    /// <summary>
    /// Whether a value is something a block or a menu item may link to.
    /// </summary>
    /// <remarks>
    /// A site-relative path, or an absolute <c>http</c>/<c>https</c> URL. Everything else is refused,
    /// and the reason is <c>javascript:</c>: a link target is written into an <c>href</c> the browser
    /// will follow, and an editor who can store one scheme can store that one
    /// (docs/07-security-compliance.md §3).
    /// </remarks>
    /// <param name="value">The candidate.</param>
    public static bool IsLinkTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();

        // A protocol-relative URL inherits the page's scheme and is an absolute URL wearing a
        // disguise, so it is refused with everything else that is not a plain path.
        if (candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (candidate.StartsWith('/'))
        {
            return true;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
               && parsed.Scheme is "http" or "https";
    }
}
