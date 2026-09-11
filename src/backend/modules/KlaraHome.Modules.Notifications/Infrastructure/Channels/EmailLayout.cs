namespace KlaraHome.Modules.Notifications.Infrastructure.Channels;

/// <summary>
/// Wraps a rendered template body in the HTML shell every transactional email shares.
/// </summary>
/// <remarks>
/// <para>
/// The seeded templates (<c>DefaultTemplates</c>) stay deliberately bare — a handful of
/// <c>&lt;p&gt;</c> fragments an operator can rewrite without touching markup. This is the one place
/// that wraps those fragments in the design system's chrome (docs/10-design-system.md), so every
/// template gets the same header, colours and typography without every template author having to
/// reproduce them.
/// </para>
/// <para>
/// Every style is inline. Most mail clients strip or ignore a <c>&lt;style&gt;</c> block, and none
/// of them fetch an external stylesheet, so this is not a preference — it is the only styling
/// mechanism that reaches an inbox. The same rule applies to the header mark: Fraunces is
/// self-hosted for the web app and does not travel with an email, so headings fall back to the
/// same email-safe serif stack the design token declares
/// (<c>--font-display</c>: <c>'Fraunces', 'Fraunces Fallback', 'Iowan Old Style', 'Palatino
/// Linotype', Georgia, serif</c>) with the two Fraunces links dropped, and the interface stack is
/// the token's own <c>--font-sans</c> web-safe tail, which needs no adjustment because it was
/// already OS-font-first.
/// </para>
/// <para>
/// The header mark is a PNG embedded in the message itself via a <c>Content-Id</c>
/// (<see cref="EmbeddedMark.ResourceName"/>), not an <c>&lt;img src="https://…"&gt;</c>. This
/// environment has no public origin to host a logo at yet, and a remote image would also be the
/// one most mail clients block by default until the recipient clicks "show images". A CID-embedded
/// image has neither problem and needs no deployment to prove: it renders in Mailpit today exactly
/// as it will in production.
/// </para>
/// </remarks>
internal static class EmailLayout
{
    /// <summary>The colour tokens this layout is allowed to reference (docs/10-design-system.md §1).</summary>
    private static class Tokens
    {
        public const string Bg = "#faf6f1"; // --brand-sand-050 / --color-bg
        public const string SurfaceRaised = "#ffffff"; // --color-surface-raised
        public const string Border = "#ddc2a6"; // --brand-tan-300 / --color-border
        public const string Text = "#340c00"; // --brand-ink-900 / --color-text
        public const string Primary = "#714c35"; // --brand-coffee-600 / --color-primary
        public const string OnPrimary = "#ffffff"; // --color-on-primary

        // Email-safe: the interface stack (--font-sans) is already OS-font-first, so it needs no
        // adjustment for a mail client.
        public const string FontSans =
            "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Noto Sans', 'Helvetica Neue', Arial, sans-serif";

        // Email-safe: --font-display with the two self-hosted Fraunces links dropped — a mail
        // client cannot fetch them, and font-display: swap has no meaning outside a browser.
        public const string FontDisplay = "'Iowan Old Style', 'Palatino Linotype', Georgia, serif";
    }

    /// <summary>Wraps a rendered body in the shared header/footer shell.</summary>
    /// <param name="storeName">The tenant's store name — text, not baked into the mark image, for the
    /// same reason the storefront header keeps the wordmark as text (docs/10-design-system.md §7):
    /// a second tenant's name is not "Klara Home".</param>
    /// <param name="bodyHtml">The already-rendered template body (a handful of block-level fragments).</param>
    /// <param name="preheaderText">A short summary shown in the inbox list before the message is opened,
    /// hidden in the body; null when the body itself is short enough to serve as one.</param>
    public static string Wrap(string storeName, string bodyHtml, string? preheaderText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);
        ArgumentNullException.ThrowIfNull(bodyHtml);

        var preheader = string.IsNullOrWhiteSpace(preheaderText)
            ? string.Empty
            : $"""
               <div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">{System.Net.WebUtility.HtmlEncode(preheaderText)}</div>
               """;

        var encodedStoreName = System.Net.WebUtility.HtmlEncode(storeName);

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{encodedStoreName}</title>
            </head>
            <body style="margin:0;padding:0;background-color:{Tokens.Bg};font-family:{Tokens.FontSans};color:{Tokens.Text};">
              {preheader}
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{Tokens.Bg};padding:24px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px;background-color:{Tokens.SurfaceRaised};border:1px solid {Tokens.Border};border-radius:8px;overflow:hidden;">
                      <tr>
                        <td style="padding:24px 32px;border-bottom:1px solid {Tokens.Border};">
                          <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                            <tr>
                              <td style="padding-right:10px;vertical-align:middle;">
                                <img src="cid:{EmbeddedMark.ContentId}" width="28" height="28" alt="" style="display:block;">
                              </td>
                              <td style="vertical-align:middle;">
                                <span style="font-family:{Tokens.FontDisplay};font-weight:600;font-size:20px;color:{Tokens.Text};">{encodedStoreName}</span>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;font-size:15px;line-height:1.6;color:{Tokens.Text};">
                          {ApplyBodyStyles(bodyHtml)}
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:20px 32px;border-top:1px solid {Tokens.Border};font-size:12px;line-height:1.5;color:{Tokens.Primary};">
                          This is a transactional message from {encodedStoreName}.
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Adds inline styles to the plain tags <see cref="Templating.TemplateRenderer"/> output produces
    /// (<c>&lt;p&gt;</c>, <c>&lt;strong&gt;</c>, <c>&lt;a&gt;</c>), without touching the templates
    /// themselves. Idempotent-ish and deliberately narrow: it targets only the exact untouched tags
    /// the seeded templates use, not a general HTML rewriter — a template that already carries its
    /// own <c>style</c> attribute is left alone.
    /// </summary>
    private static string ApplyBodyStyles(string html)
        => html
            .Replace("<p>", "<p style=\"margin:0 0 16px;\">", StringComparison.Ordinal)
            .Replace("<a href=", $"<a style=\"color:{Tokens.Primary};font-weight:600;\" href=", StringComparison.Ordinal);

    /// <summary>The embedded header-mark resource this layout references by <c>cid:</c>.</summary>
    public static class EmbeddedMark
    {
        /// <summary>The Content-Id every message attaches this resource under.</summary>
        public const string ContentId = "kh-email-mark";

        /// <summary>The manifest resource name the PNG bytes are embedded under (see the .csproj).</summary>
        public const string ResourceName = "KlaraHome.Modules.Notifications.Assets.mark-email.png";
    }
}
