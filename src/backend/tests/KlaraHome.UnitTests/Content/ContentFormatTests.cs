using KlaraHome.Modules.Content.Application.Validation;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// Path normalisation, which is the whole of whether the redirect manager works.
/// </summary>
/// <remarks>
/// Tested while writing it. A redirect rule is only ever as good as the agreement between how a path
/// is stored and how it is looked up, and every one of these cases is a way the same page gets asked
/// for under a different string. A rule that fires for half its visitors is worse than no rule: the
/// traffic still bounces and nobody can reproduce it.
/// </remarks>
public sealed class ContentFormatTests
{
    /// <summary>Casing, trailing slashes, doubled slashes and query strings all fold away.</summary>
    [Theory]
    [InlineData("/sale", "/sale")]
    [InlineData("/Sale", "/sale")]
    [InlineData("/sale/", "/sale")]
    [InlineData("sale", "/sale")]
    [InlineData("//sale//summer//", "/sale/summer")]
    [InlineData("/sale?utm_source=email", "/sale")]
    [InlineData("/sale#top", "/sale")]
    [InlineData("  /Sale/Summer/  ", "/sale/summer")]
    [InlineData("https://example.in/Sale/", "/sale")]
    [InlineData("/", "/")]
    public void A_path_normalises_to_one_form(string input, string expected)
        => Assert.Equal(expected, ContentFormats.NormalizePath(input));

    /// <summary>Nothing usable normalises to nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_is_null(string? input)
        => Assert.Null(ContentFormats.NormalizePath(input));

    /// <summary>Normalising twice changes nothing.</summary>
    /// <remarks>
    /// The property the whole manager rests on: a stored rule and an inbound request go through the
    /// same function, so if it were not idempotent a rule would stop matching itself.
    /// </remarks>
    [Theory]
    [InlineData("/Sale/Summer/?a=1")]
    [InlineData("https://example.in/Pages/Returns")]
    [InlineData("///")]
    public void Normalisation_is_idempotent(string input)
    {
        var once = ContentFormats.NormalizePath(input);

        Assert.Equal(once, ContentFormats.NormalizePath(once));
    }

    /// <summary>A title becomes the slug somebody would have typed.</summary>
    [Theory]
    [InlineData("Summer Sale", "summer-sale")]
    [InlineData("  Café  Lighting  ", "cafe-lighting")]
    [InlineData("Under ₹999", "under-999")]
    [InlineData("Terms & Conditions", "terms-conditions")]
    public void A_title_becomes_a_slug(string title, string expected)
        => Assert.Equal(expected, ContentFormats.ToSlug(title));

    /// <summary>A link target is a site path or an http address, and nothing else.</summary>
    /// <remarks>
    /// The scheme check is the point. A link target is written into an <c>href</c> the browser will
    /// follow, and an editor who can store one scheme can store <c>javascript:</c>
    /// (docs/07-security-compliance.md §3). A protocol-relative URL is refused with it: it inherits
    /// the page's scheme and is an absolute URL wearing a disguise.
    /// </remarks>
    [Theory]
    [InlineData("/collections/sale", true)]
    [InlineData("https://example.in/help", true)]
    [InlineData("http://example.in/help", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//evil.example/x", false)]
    [InlineData("data:text/html;base64,PHN2Zz4=", false)]
    [InlineData("collections/sale", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_link_target_is_a_path_or_an_http_address(string? candidate, bool allowed)
        => Assert.Equal(allowed, ContentFormats.IsLinkTarget(candidate));
}
