using System.Text;
using System.Text.RegularExpressions;

namespace KlaraHome.Modules.Content.Infrastructure.Blocks;

/// <summary>
/// Reduces an editor's formatted copy to the tags and attributes the storefront will render.
/// </summary>
/// <remarks>
/// <para>
/// Rich text arrives from a WYSIWYG editor in an admin app, is stored, and is later written into a
/// page a shopper loads. That is the exact shape of a stored cross-site-scripting vulnerability
/// (docs/07-security-compliance.md §3), and the fact that only staff can reach the editor does not
/// change it — a compromised staff account would otherwise own every browser that visits the store.
/// </para>
/// <para>
/// An allow-list, not a block-list, and that is the only design that works. A block-list is a race
/// against every encoding trick a browser tolerates; an allow-list refuses everything it has not been
/// told about, so a tag nobody thought of is simply gone. What survives is the small set a shop's
/// copy actually needs: headings, paragraphs, emphasis, lists, links, images and tables. What does
/// not includes <c>script</c>, <c>style</c>, <c>iframe</c>, <c>object</c>, every <c>on*</c> handler
/// and every URL scheme but <c>http</c>, <c>https</c> and <c>mailto</c>.
/// </para>
/// <para>
/// This is deliberately <b>not</b> applied to a <c>CustomHtml</c> block. That block exists precisely
/// to carry markup this would strip — an embedded video, a campaign widget — which is why writing one
/// needs its own permission and its own feature flag rather than merely the ability to edit a page.
/// Sanitising it would make the block useless; leaving it unsanitised is why so few people may write
/// one.
/// </para>
/// <para>
/// It is a regular-expression sanitiser over a tag stream rather than a full HTML5 parser, and that
/// is a deliberate trade recorded rather than hidden: a parser is the stronger answer and a
/// dependency this solution does not have. The pass is conservative in the direction that matters —
/// anything it cannot confidently recognise as an allowed tag is removed, not kept.
/// </para>
/// </remarks>
internal static partial class HtmlSanitizer
{
    /// <summary>The elements an editor's copy may use.</summary>
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "span", "div",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "strong", "b", "em", "i", "u", "s", "sub", "sup", "small", "mark",
        "ul", "ol", "li", "dl", "dt", "dd",
        "blockquote", "pre", "code",
        "a", "img", "figure", "figcaption",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
    };

    /// <summary>The attributes those elements may carry.</summary>
    /// <remarks>
    /// <c>style</c> is absent on purpose. CSS is a scripting surface in several browsers' history and
    /// a layout-breaking one in all of them; a class the design system knows about is the supported
    /// way to make copy look different.
    /// </remarks>
    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "title", "alt", "src", "width", "height", "colspan", "rowspan", "scope", "class",
        "target", "rel", "loading",
    };

    /// <summary>The URL schemes an <c>href</c> or <c>src</c> may use.</summary>
    private static readonly string[] AllowedSchemes = ["http:", "https:", "mailto:", "tel:"];

    /// <summary>Any tag, open or close, with its attributes.</summary>
    [GeneratedRegex(@"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    /// <summary>One attribute of a tag.</summary>
    [GeneratedRegex(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s""'>]+))",
        RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    /// <summary>An HTML comment, including the conditional-comment form.</summary>
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex Comment();

    /// <summary>
    /// A script, style, iframe, object, embed or svg element and everything inside it.
    /// </summary>
    /// <remarks>
    /// Removed content and all, before the tag pass. Stripping only the tags would leave the script's
    /// body behind as text, which is at best a page full of JavaScript source and at worst a payload
    /// that finds its way back into an element.
    /// </remarks>
    [GeneratedRegex(@"<\s*(script|style|iframe|object|embed|svg|math|template|noscript|frame|frameset)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousElement();

    /// <summary>An unclosed dangerous element, which a browser would still act on.</summary>
    [GeneratedRegex(@"<\s*/?\s*(script|style|iframe|object|embed|svg|math|template|noscript|frame|frameset)\b[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DangerousTag();

    /// <summary>Strips everything outside the allow-list from a fragment of rich text.</summary>
    /// <param name="html">The editor's markup.</param>
    /// <returns>The markup with everything unrecognised removed.</returns>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var working = Comment().Replace(html, string.Empty);
        working = DangerousElement().Replace(working, string.Empty);
        working = DangerousTag().Replace(working, string.Empty);

        return Tag().Replace(working, match =>
        {
            var isClosing = match.Groups[1].Value.Length > 0;
            var name = match.Groups[2].Value.ToLowerInvariant();

            if (!AllowedTags.Contains(name))
            {
                return string.Empty;
            }

            if (isClosing)
            {
                return $"</{name}>";
            }

            var attributes = SanitizeAttributes(name, match.Groups[3].Value);

            return attributes.Length == 0 ? $"<{name}>" : $"<{name}{attributes}>";
        });
    }

    /// <summary>Whether a value would survive sanitisation unchanged.</summary>
    /// <remarks>
    /// Used by the validator to tell an editor that something was removed, rather than silently
    /// storing less than they wrote. Silence here is how a merchandiser discovers three weeks later
    /// that half a page never rendered.
    /// </remarks>
    /// <param name="html">The candidate.</param>
    public static bool IsClean(string? html)
        => string.Equals(Sanitize(html), html?.Trim() ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Rebuilds a tag's attribute list from only the attributes that are allowed.</summary>
    private static string SanitizeAttributes(string tag, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (Match attribute in Attribute().Matches(raw))
        {
            var name = attribute.Groups[1].Value.ToLowerInvariant();

            // Every event handler starts with "on", and enumerating them would be a list that is out
            // of date the day a browser ships a new one.
            if (name.StartsWith("on", StringComparison.Ordinal) || !AllowedAttributes.Contains(name))
            {
                continue;
            }

            var value = attribute.Groups[3].Success ? attribute.Groups[3].Value
                : attribute.Groups[4].Success ? attribute.Groups[4].Value
                : attribute.Groups[5].Value;

            if ((name is "href" or "src") && !IsSafeUrl(value))
            {
                continue;
            }

            builder.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');
        }

        // A link that opens in a new tab and does not say `noopener` hands the opened page a handle
        // on the one it came from. Added rather than demanded of the editor, because it is a rule
        // nobody remembers and the fix is one attribute.
        if (tag == "a"
            && builder.ToString().Contains("target=\"_blank\"", StringComparison.OrdinalIgnoreCase)
            && !builder.ToString().Contains(" rel=", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append(" rel=\"noopener noreferrer\"");
        }

        return builder.ToString();
    }

    /// <summary>Whether a URL uses a scheme a link may use.</summary>
    private static bool IsSafeUrl(string value)
    {
        var candidate = value.Trim();

        if (candidate.Length == 0)
        {
            return false;
        }

        // A relative URL, a fragment or a root-relative path carries no scheme and is safe by
        // construction.
        if (candidate.StartsWith('/') || candidate.StartsWith('#') || candidate.StartsWith('?'))
        {
            return !candidate.StartsWith("//", StringComparison.Ordinal);
        }

        var colon = candidate.IndexOf(':', StringComparison.Ordinal);

        if (colon < 0)
        {
            return !candidate.Contains("javascript", StringComparison.OrdinalIgnoreCase);
        }

        var scheme = candidate[..(colon + 1)];

        return AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>An ampersand that is not already the start of a character reference.</summary>
    [GeneratedRegex(@"&(?![a-zA-Z][a-zA-Z0-9]{1,31};|#[0-9]{1,7};|#x[0-9a-fA-F]{1,6};)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BareAmpersand();

    /// <summary>
    /// Escapes the characters that would end an attribute or open a tag.
    /// </summary>
    /// <remarks>
    /// The ampersand rule is the fiddly one: escaping every <c>&amp;</c> would turn a query string an
    /// editor pasted — <c>?a=1&amp;amp;b=2</c> — into a link to a different page every time the block
    /// was saved. Only a bare ampersand is escaped, so a value that is already correct stays correct
    /// however many times it round-trips.
    /// </remarks>
    private static string Escape(string value)
        => BareAmpersand()
            .Replace(value, "&amp;")
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
