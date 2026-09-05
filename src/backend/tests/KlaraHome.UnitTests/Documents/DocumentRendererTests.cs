using KlaraHome.Contracts.Documents;
using KlaraHome.Infrastructure.Documents;
using KlaraHome.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Documents;

/// <summary>
/// The PDF pipeline (ADR-015). Enough to prove a document renders, that the model's whole
/// vocabulary is handled, and that a deployment with no font says so instead of producing a page
/// of empty boxes.
/// </summary>
public sealed class DocumentRendererTests
{
    /// <summary>An invoice-shaped document, using every block the model defines.</summary>
    private static DocumentDefinition Invoice() => new(
        "Tax invoice KH-2026-000123",
        [
            new DocumentHeading("Tax invoice", "KH-2026-000123"),
            new DocumentPartyRow(
            [
                new DocumentParty("Sold by", ["Klara Home Retail LLP", "Bengaluru, Karnataka", "GSTIN 29ABCDE1234F1Z5"]),
                new DocumentParty("Billed to", ["Asha Kumar", "12 MG Road", "Bengaluru 560001"]),
            ]),
            new DocumentFieldGrid(
            [
                new DocumentField("Invoice date", "05 Sep 2026"),
                new DocumentField("Order number", "KH-2026-000123"),
                new DocumentField("Place of supply", "Karnataka (29)"),
                new DocumentField("Payment", "Prepaid"),
            ]),
            new DocumentRule(),
            new DocumentTable(
                [
                    new DocumentColumn("Description", 4),
                    new DocumentColumn("HSN", 1, DocumentAlignment.Center),
                    new DocumentColumn("Qty", 1, DocumentAlignment.Right),
                    new DocumentColumn("Amount", 2, DocumentAlignment.Right),
                ],
                [
                    ["Cotton cushion cover 40x40", "6304", "2", "1,098.00"],
                    ["Jute table runner", "5702", "1", "649.00"],
                ],
                [
                    new DocumentField("Taxable value", "1,480.51"),
                    new DocumentField("CGST 9%", "133.25"),
                    new DocumentField("SGST 9%", "133.24"),
                    new DocumentField("Total", "1,747.00"),
                ]),
            new DocumentSpacer(),
            new DocumentParagraph("This is a computer-generated invoice and needs no signature.", Small: true),
        ],
        FooterText: "Klara Home Retail LLP",
        Author: "Klara Home");

    [Fact]
    public void A_document_renders_to_a_pdf()
    {
        SkipWithoutAFont();

        var bytes = Renderer().Render(Invoice());

        Assert.NotEmpty(bytes);
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public void A_label_renders_on_its_own_page_geometry()
    {
        SkipWithoutAFont();

        var label = new DocumentDefinition(
            "Shipping label",
            [
                new DocumentHeading("KLARA HOME", "AWB 123456789012"),
                new DocumentParty("Ship to", ["Asha Kumar", "12 MG Road", "Bengaluru 560001"]) is var party
                    ? new DocumentPartyRow([party])
                    : throw new InvalidOperationException(),
                new DocumentParagraph("Prepaid - do not collect"),
            ],
            DocumentPageSize.Label4x6);

        var bytes = Renderer().Render(label);

        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public void A_landscape_document_renders()
    {
        SkipWithoutAFont();

        var manifest = new DocumentDefinition(
            "Manifest",
            [new DocumentHeading("Pickup manifest"), new DocumentTable([new DocumentColumn("AWB")], [["1234"]])],
            DocumentPageSize.A4Landscape);

        Assert.Equal("%PDF-"u8.ToArray(), Renderer().Render(manifest)[..5]);
    }

    [Fact]
    public void A_deployment_with_no_font_is_told_where_the_renderer_looked()
    {
        // The alternative is a PDF of empty boxes, discovered on a statutory document in
        // production. The message has to be actionable.
        var resolver = new FileFontResolver(new DocumentOptions
        {
            FontDirectories = [Path.Combine(Path.GetTempPath(), "klarahome-no-fonts-here")],
            PreferredFamilies = ["DejaVuSans"],
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => resolver.ResolveTypeface("anything", bold: false, italic: false));

        Assert.Contains("No usable font", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DejaVuSans", exception.Message, StringComparison.Ordinal);
        Assert.Contains("klarahome-no-fonts-here", exception.Message, StringComparison.Ordinal);
    }

    private static MigraDocRenderer Renderer()
        => new(Options.Create(new DocumentOptions()));

    /// <summary>
    /// Skips rather than fails where no font family is installed.
    /// </summary>
    /// <remarks>
    /// The container image copies DejaVu in at build time and both developer platforms carry one of
    /// the three candidate families, so this normally runs. It skips rather than fails because a
    /// bare build agent with no fonts is an environment problem, and a red test would say the
    /// renderer is broken when it is not.
    /// </remarks>
    private static void SkipWithoutAFont()
    {
        var options = new DocumentOptions();

        var found = options.FontDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => options.PreferredFamilies.Select(family => (directory, family)))
            .Any(candidate => File.Exists(Path.Combine(candidate.directory, candidate.family + ".ttf"))
                              || File.Exists(Path.Combine(candidate.directory, candidate.family + "-Regular.ttf")));

        Assert.SkipUnless(found, "No candidate font family is installed on this machine.");
    }
}

/// <summary>The download filename a signed URL forces.</summary>
public sealed class StorageFileNameTests
{
    [Fact]
    public void A_quote_cannot_terminate_the_content_disposition_header()
    {
        // Otherwise an uploader writes their own response headers.
        var sanitised = S3FileStorage.Sanitise("invoice\".pdf");

        Assert.DoesNotContain('"', sanitised);
    }

    [Fact]
    public void A_newline_cannot_inject_a_header()
    {
        var sanitised = S3FileStorage.Sanitise("invoice\r\nX-Injected: yes.pdf");

        Assert.DoesNotContain('\r', sanitised);
        Assert.DoesNotContain('\n', sanitised);
    }

    [Fact]
    public void A_long_name_is_truncated_rather_than_rejected()
        => Assert.True(S3FileStorage.Sanitise(new string('a', 500)).Length <= 120);

    [Fact]
    public void An_empty_name_falls_back_to_something_usable()
        => Assert.Equal("download", S3FileStorage.Sanitise("\r\n"));
}
