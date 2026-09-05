using KlaraHome.Modules.Notifications.Infrastructure.Templating;

namespace KlaraHome.UnitTests.Notifications;

/// <summary>
/// The renderer is deliberately not a templating language: no conditionals, no loops, no
/// expressions. These tests pin that down as much as they pin down the substitution.
/// </summary>
public sealed class TemplateRendererTests
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal)
    {
        ["name"] = "Asha",
        ["orderNumber"] = "KH-2026-000123",
        ["amount"] = "1,299.00",
    };

    [Fact]
    public void Placeholders_are_replaced_with_their_values()
    {
        var result = TemplateRenderer.Render(
            "Hello {{name}}, order {{orderNumber}} is on its way.",
            Values,
            escapeHtml: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello Asha, order KH-2026-000123 is on its way.", result.Text);
    }

    [Fact]
    public void Whitespace_inside_the_braces_is_tolerated()
    {
        var result = TemplateRenderer.Render("Hello {{ name }}.", Values, escapeHtml: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello Asha.", result.Text);
    }

    [Fact]
    public void A_missing_variable_fails_the_render_rather_than_leaving_a_gap()
    {
        // "Your order  has shipped" is worse than no message, and a silent gap hides a caller's bug.
        var result = TemplateRenderer.Render("Order {{missing}} shipped.", Values, escapeHtml: false);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Text);
        Assert.Equal(["missing"], result.MissingVariables);
    }

    [Fact]
    public void Every_missing_variable_is_reported_at_once()
    {
        var result = TemplateRenderer.Render("{{a}} and {{b}} and {{name}}", Values, escapeHtml: false);

        Assert.Equal(["a", "b"], result.MissingVariables);
    }

    [Fact]
    public void Html_is_escaped_for_an_email_body()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "<script>alert(1)</script>",
        };

        var result = TemplateRenderer.Render("<p>Hello {{name}}</p>", values, escapeHtml: true);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("<script>", result.Text, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_is_not_escaped_for_a_text_channel()
    {
        // Encoding an ampersand into &amp; in a text message is a visible defect.
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["store"] = "Salt & Pepper" };

        var result = TemplateRenderer.Render("Welcome to {{store}}", values, escapeHtml: false);

        Assert.Equal("Welcome to Salt & Pepper", result.Text);
    }

    [Fact]
    public void A_value_that_looks_like_a_placeholder_is_not_substituted_again()
    {
        // Otherwise a customer could name themselves "{{code}}" and read somebody's variables.
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "{{secret}}" };

        var result = TemplateRenderer.Render("Hello {{name}}", values, escapeHtml: false);

        Assert.Equal("Hello {{secret}}", result.Text);
    }

    [Fact]
    public void A_template_with_no_placeholders_renders_unchanged()
    {
        var result = TemplateRenderer.Render("Your order has shipped.", Values, escapeHtml: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("Your order has shipped.", result.Text);
    }

    [Fact]
    public void Placeholder_names_are_listed_once_and_in_order()
    {
        var names = TemplateRenderer.PlaceholdersIn("{{b}} {{a}} {{b}} {{c}}");

        Assert.Equal(["b", "a", "c"], names);
    }

    [Fact]
    public void Braces_that_are_not_a_variable_name_are_left_alone()
    {
        // A typo should stay visible in the output rather than be interpreted.
        var result = TemplateRenderer.Render("{{ not a name }} {{name}}", Values, escapeHtml: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("{{ not a name }} Asha", result.Text);
    }
}
