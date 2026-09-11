using KlaraHome.Modules.Notifications.Infrastructure.Channels;

namespace KlaraHome.UnitTests.Notifications;

/// <summary>
/// Pins down what <see cref="EmailLayout"/> promises: the tenant's store name (not a baked-in
/// wordmark), the design tokens as inline styles, the CID reference the header image is sent
/// under, and that it leaves the caller's body fragment intact rather than rewriting it.
/// </summary>
public sealed class EmailLayoutTests
{
    [Fact]
    public void Wrap_carries_the_tenant_store_name_as_text()
    {
        var html = EmailLayout.Wrap("Nimbus Living", "<p>Hello.</p>");

        Assert.Contains("Nimbus Living", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Klara Home", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_references_the_embedded_mark_by_content_id_not_a_remote_url()
    {
        var html = EmailLayout.Wrap("Klara Home", "<p>Hello.</p>");

        Assert.Contains($"cid:{EmailLayout.EmbeddedMark.ContentId}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_uses_only_inline_styles_no_external_stylesheet()
    {
        var html = EmailLayout.Wrap("Klara Home", "<p>Hello.</p>");

        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrap_uses_the_design_system_colour_tokens()
    {
        var html = EmailLayout.Wrap("Klara Home", "<p>Hello.</p>");

        Assert.Contains("#faf6f1", html, StringComparison.Ordinal); // --color-bg
        Assert.Contains("#340c00", html, StringComparison.Ordinal); // --color-text
        Assert.Contains("#714c35", html, StringComparison.Ordinal); // --color-primary
        Assert.Contains("#ddc2a6", html, StringComparison.Ordinal); // --color-border
    }

    [Fact]
    public void Wrap_preserves_the_caller_body_content()
    {
        var html = EmailLayout.Wrap("Klara Home", "<p>Your order <strong>KH-1</strong> has shipped.</p>");

        Assert.Contains("Your order <strong>KH-1</strong> has shipped.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_adds_a_preheader_only_when_one_is_supplied()
    {
        var withPreheader = EmailLayout.Wrap("Klara Home", "<p>Hello.</p>", "Your code expires soon");
        var without = EmailLayout.Wrap("Klara Home", "<p>Hello.</p>");

        Assert.Contains("Your code expires soon", withPreheader, StringComparison.Ordinal);
        Assert.DoesNotContain("mso-hide", without, StringComparison.Ordinal);
    }
}
