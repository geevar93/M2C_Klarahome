using KlaraHome.Modules.Content.Infrastructure.Blocks;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// The allow-list that stands between an editor's rich text and a shopper's browser.
/// </summary>
/// <remarks>
/// <para>
/// Tested while writing it, and it is the clearest case in this module for doing so: it is a pure
/// function, it has no infrastructure at all, and it is a <em>security control</em>
/// (docs/07-security-compliance.md §3). Rich text is written in an admin app, stored, and later
/// rendered into a page every shopper loads — the exact shape of a stored cross-site-scripting
/// vulnerability — and the fact that only staff can reach the editor does not change it.
/// </para>
/// <para>
/// These are the cases that were reasoned about while the allow-list was written. They are
/// deliberately <b>not</b> the adversarial suite: a real one belongs in Step 29 and has a row in
/// <c>TEST_DEBT.md</c> saying so.
/// </para>
/// </remarks>
public sealed class HtmlSanitizerTests
{
    /// <summary>Ordinary formatting survives untouched.</summary>
    /// <remarks>
    /// The half that is easy to get wrong in the other direction. A sanitiser that strips an editor's
    /// bold and their bullet list is one they route around by pasting into a custom-HTML block, which
    /// is a far worse outcome than the tag it removed.
    /// </remarks>
    [Theory]
    [InlineData("<p>Hello <strong>world</strong></p>")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>")]
    [InlineData("<h2>Delivery</h2><p>We ship across India.</p>")]
    [InlineData("<blockquote><em>Lovely cushions.</em></blockquote>")]
    public void Ordinary_formatting_survives(string html)
        => Assert.Equal(html, HtmlSanitizer.Sanitize(html));

    /// <summary>A script element and its body are both removed.</summary>
    /// <remarks>
    /// Content and all. Stripping only the tags would leave the script's body behind as text, which
    /// is at best a page full of JavaScript source.
    /// </remarks>
    [Fact]
    public void A_script_is_removed_with_its_body()
    {
        var clean = HtmlSanitizer.Sanitize("<p>Hi</p><script>steal(document.cookie)</script><p>Bye</p>");

        Assert.Equal("<p>Hi</p><p>Bye</p>", clean);
        Assert.DoesNotContain("steal", clean, StringComparison.Ordinal);
    }

    /// <summary>An unclosed dangerous element is removed too.</summary>
    /// <remarks>
    /// A browser acts on <c>&lt;script src=…&gt;</c> whether or not it is closed, so a sanitiser that
    /// only matched balanced pairs would let the most compact possible payload through.
    /// </remarks>
    [Fact]
    public void An_unclosed_dangerous_element_is_removed()
        => Assert.DoesNotContain(
            "script",
            HtmlSanitizer.Sanitize("<p>Hi</p><script src=\"//evil.example/x.js\">"),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Every event handler goes, whatever it is called.</summary>
    /// <remarks>
    /// Matched by prefix rather than by a list of names, because enumerating them is a list that is
    /// out of date the day a browser ships a new one.
    /// </remarks>
    [Theory]
    [InlineData("<p onclick=\"alert(1)\">Hi</p>", "<p>Hi</p>")]
    [InlineData("<img src=\"/a.png\" onerror=\"alert(1)\">", "<img src=\"/a.png\">")]
    [InlineData("<div onmouseover=\"x()\">Hi</div>", "<div>Hi</div>")]
    public void Event_handlers_are_removed(string html, string expected)
        => Assert.Equal(expected, HtmlSanitizer.Sanitize(html));

    /// <summary>A link may not carry a scripting scheme.</summary>
    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>", "<a>x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHN2Zz4=\">x</a>", "<a>x</a>")]
    public void Scripting_schemes_are_dropped(string html, string expected)
        => Assert.Equal(expected, HtmlSanitizer.Sanitize(html));

    /// <summary>The schemes a shop's copy legitimately uses are kept.</summary>
    [Theory]
    [InlineData("<a href=\"/pages/delivery\">Delivery</a>")]
    [InlineData("<a href=\"https://example.in/help\">Help</a>")]
    [InlineData("<a href=\"mailto:care@example.in\">Email us</a>")]
    public void Safe_link_targets_are_kept(string html)
        => Assert.Equal(html, HtmlSanitizer.Sanitize(html));

    /// <summary>An inline style is removed rather than trusted.</summary>
    /// <remarks>
    /// CSS is a scripting surface in several browsers' history and a layout-breaking one in all of
    /// them. A class the design system knows about is the supported way to make copy look different.
    /// </remarks>
    [Fact]
    public void Inline_styles_are_removed()
        => Assert.Equal(
            "<p class=\"lead\">Hi</p>",
            HtmlSanitizer.Sanitize("<p style=\"position:fixed;inset:0\" class=\"lead\">Hi</p>"));

    /// <summary>A link that opens in a new tab is given the attribute that makes it safe.</summary>
    /// <remarks>
    /// Without <c>noopener</c> the opened page gets a handle on the one it came from. It is a rule
    /// nobody remembers and the fix is one attribute, so it is added rather than demanded.
    /// </remarks>
    [Fact]
    public void A_new_tab_link_gains_noopener()
        => Assert.Contains(
            "rel=\"noopener noreferrer\"",
            HtmlSanitizer.Sanitize("<a href=\"https://example.in\" target=\"_blank\">x</a>"),
            StringComparison.Ordinal);

    /// <summary>A query string in a link is not corrupted by escaping.</summary>
    /// <remarks>
    /// The fiddly case. Escaping every ampersand would turn a URL an editor pasted into a link to a
    /// different page, and it would do so again on every save until the link was unrecognisable.
    /// </remarks>
    [Fact]
    public void An_already_escaped_ampersand_is_not_escaped_again()
    {
        const string html = "<a href=\"/c/lamps?a=1&amp;b=2\">Lamps</a>";

        Assert.Equal(html, HtmlSanitizer.Sanitize(html));
        Assert.Equal(html, HtmlSanitizer.Sanitize(HtmlSanitizer.Sanitize(html)));
    }

    /// <summary>Comments go, including the conditional form.</summary>
    [Fact]
    public void Comments_are_removed()
        => Assert.Equal("<p>Hi</p>", HtmlSanitizer.Sanitize("<p>Hi</p><!--[if IE]><script>x()</script><![endif]-->"));

    /// <summary>Nothing in, nothing out.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_is_empty_output(string? html)
        => Assert.Equal(string.Empty, HtmlSanitizer.Sanitize(html));

    /// <summary><c>IsClean</c> agrees with what the sanitiser would do.</summary>
    /// <remarks>
    /// It is what tells an editor that something was removed rather than storing less than they wrote
    /// in silence, so the two must not be able to disagree.
    /// </remarks>
    [Theory]
    [InlineData("<p>Hello</p>", true)]
    [InlineData("<p onclick=\"x()\">Hello</p>", false)]
    [InlineData("<script>x()</script>", false)]
    public void IsClean_agrees_with_the_sanitiser(string html, bool clean)
        => Assert.Equal(clean, HtmlSanitizer.IsClean(html));
}
