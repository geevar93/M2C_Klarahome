using KlaraHome.Contracts.Documents;
using Microsoft.Extensions.Options;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace KlaraHome.Infrastructure.Documents;

/// <summary>
/// The one implementation of <see cref="IDocumentRenderer"/>: MigraDoc lays the document out,
/// PDFsharp writes the file (ADR-015).
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton and safe to call concurrently. MigraDoc's document objects are not
/// thread-safe, but nothing is shared between renders — a fresh <see cref="Document"/> per call —
/// and the only process-wide state is the font resolver, which is set once and only read
/// afterwards.
/// </para>
/// <para>
/// Deliberately plain. Aesthetics are Step 30's, and a GST invoice is not where they would start
/// anyway: what matters here is that every value the law requires is present, legible and in the
/// same place on every copy.
/// </para>
/// </remarks>
public sealed class MigraDocRenderer : IDocumentRenderer
{
    /// <summary>Body text size in points.</summary>
    private const double BodySize = 9;

    /// <summary>The font family name written into the document; the resolver maps it to a file.</summary>
    private const string FontFamily = "KlaraHome Document";

    /// <summary>
    /// The design-system colour tokens this renderer is allowed to use (docs/10-design-system.md §1),
    /// applied to rules, captions and shading only — never to the typeface.
    /// </summary>
    /// <remarks>
    /// Two things this renderer deliberately does <em>not</em> do, both recorded here rather than
    /// left implicit:
    /// <list type="bullet">
    /// <item>
    /// <b>No Fraunces.</b> The design system's display face ships one weight (600), latin-subset,
    /// with no bold or italic face — a heading here still needs a real bold, and MigraDoc's
    /// document-wide "Normal" font is also the body font, so a face with one weight cannot serve
    /// both. Swapping the interface font (DejaVu/Liberation/Arial, resolved by
    /// <see cref="FileFontResolver"/>) for a display face on a statutory document was also never
    /// the intent recorded on the Orders and Returns modules' invoice/credit-note document
    /// builders: "aesthetics are Step 30's, and a GST invoice is not where they would start
    /// anyway."
    /// </item>
    /// <item>
    /// <b>No brand accent colours (bronze/coffee) on the item table or totals.</b> Colour here
    /// replaces what used to be a hard-coded neutral gray with the ramp's own ink-derived neutrals,
    /// so a shadow or a rule reads as "this brand's document" rather than as generic PDF-library
    /// gray — the same reasoning §3 of the design doc applies to on-screen shadows. It stops short
    /// of colouring monetary figures or table headers in the action colour, because a coloured
    /// total is the one thing on a tax document that must never be mistaken for emphasis or a
    /// status.
    /// </item>
    /// </list>
    /// </remarks>
    private static class Tokens
    {
        /// <summary>Hairline rules, table borders — <c>--color-border</c> / <c>--brand-tan-300</c>.</summary>
        public static readonly Color Border = Color.Parse("#ddc2a6");

        /// <summary>Table header shading — <c>--color-surface-sunken</c> / <c>--brand-sand-200</c>.</summary>
        public static readonly Color SurfaceSunken = Color.Parse("#ecddce");

        /// <summary>Captions, the footer, subtext — <c>--color-text-muted</c> / <c>--brand-coffee-600</c>,
        /// 7.00:1 on white (docs/10-design-system.md §1.2): a real colour, not a grey wash, and still
        /// comfortably clear of the 4.5:1 floor for small print.</summary>
        public static readonly Color TextMuted = Color.Parse("#714c35");
    }

    private static readonly object FontResolverGate = new();
    private static bool _fontResolverInstalled;

    private readonly DocumentOptions _options;

    /// <param name="options">Font search settings.</param>
    public MigraDocRenderer(IOptions<DocumentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        InstallFontResolver(_options);
    }

    /// <inheritdoc />
    public byte[] Render(DocumentDefinition document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rendered = new Document
        {
            Info =
            {
                Title = document.Title,
                Author = document.Author ?? string.Empty,
            },
        };

        rendered.Styles["Normal"]!.Font.Name = FontFamily;
        rendered.Styles["Normal"]!.Font.Size = BodySize;

        var section = rendered.AddSection();
        ConfigurePage(section, document.PageSize);
        AddFooter(section, document);

        foreach (var block in document.Blocks)
        {
            Add(section, block);
        }

        var renderer = new PdfDocumentRenderer { Document = rendered };
        renderer.RenderDocument();

        using var buffer = new MemoryStream();
        renderer.PdfDocument.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Installs the font resolver exactly once per process.
    /// </summary>
    /// <remarks>
    /// PDFsharp keeps the resolver in a static and refuses a second assignment once a glyph has
    /// been measured. The lock is not for contention — it is so two hosted services starting in
    /// parallel cannot both decide they are the first.
    /// </remarks>
    private static void InstallFontResolver(DocumentOptions options)
    {
        if (_fontResolverInstalled)
        {
            return;
        }

        lock (FontResolverGate)
        {
            if (_fontResolverInstalled)
            {
                return;
            }

            GlobalFontSettings.FontResolver = new FileFontResolver(options);
            _fontResolverInstalled = true;
        }
    }

    private static void ConfigurePage(Section section, DocumentPageSize size)
    {
        var setup = section.PageSetup;

        switch (size)
        {
            case DocumentPageSize.A4Landscape:
                setup.PageFormat = PageFormat.A4;
                setup.Orientation = Orientation.Landscape;
                setup.LeftMargin = Unit.FromMillimeter(12);
                setup.RightMargin = Unit.FromMillimeter(12);
                setup.TopMargin = Unit.FromMillimeter(14);
                setup.BottomMargin = Unit.FromMillimeter(14);
                break;

            case DocumentPageSize.Label4x6:
                // A thermal label has no margin to spare: the printable area is the label, and
                // anything outside 3 mm is routinely clipped by the printer.
                setup.PageWidth = Unit.FromInch(4);
                setup.PageHeight = Unit.FromInch(6);
                setup.LeftMargin = Unit.FromMillimeter(4);
                setup.RightMargin = Unit.FromMillimeter(4);
                setup.TopMargin = Unit.FromMillimeter(4);
                setup.BottomMargin = Unit.FromMillimeter(4);
                break;

            default:
                setup.PageFormat = PageFormat.A4;
                setup.Orientation = Orientation.Portrait;
                setup.LeftMargin = Unit.FromMillimeter(16);
                setup.RightMargin = Unit.FromMillimeter(16);
                setup.TopMargin = Unit.FromMillimeter(16);
                setup.BottomMargin = Unit.FromMillimeter(18);
                break;
        }
    }

    /// <summary>
    /// A footer carrying the caller's text and "Page n of m".
    /// </summary>
    /// <remarks>
    /// The page count matters more than it looks: a printed invoice that has lost its second page
    /// is indistinguishable from a one-page invoice unless the first page says how many there were.
    /// A label gets no footer — there is no room, and it is never more than one page.
    /// </remarks>
    private static void AddFooter(Section section, DocumentDefinition definition)
    {
        if (definition.PageSize == DocumentPageSize.Label4x6)
        {
            return;
        }

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = BodySize - 2;
        footer.Format.Font.Color = Tokens.TextMuted;
        footer.Format.Alignment = ParagraphAlignment.Left;

        if (!string.IsNullOrWhiteSpace(definition.FooterText))
        {
            footer.AddText(definition.FooterText);
            footer.AddTab();
        }

        footer.Format.TabStops.AddTabStop(Unit.FromPoint(UsableWidth(section)), TabAlignment.Right);

        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void Add(Section section, DocumentBlock block)
    {
        switch (block)
        {
            case DocumentHeading heading:
                AddHeading(section, heading);
                break;

            case DocumentParagraph paragraph:
                AddParagraph(section, paragraph);
                break;

            case DocumentPartyRow parties:
                AddParties(section, parties);
                break;

            case DocumentFieldGrid grid:
                AddFieldGrid(section, grid);
                break;

            case DocumentTable table:
                AddTable(section, table);
                break;

            case DocumentSpacer spacer:
                section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(spacer.Points);
                break;

            case DocumentRule:
                var rule = section.AddParagraph();
                rule.Format.Borders.Bottom.Width = 0.5;
                rule.Format.Borders.Bottom.Color = Tokens.Border;
                rule.Format.SpaceAfter = Unit.FromPoint(8);
                break;

            default:
                throw new NotSupportedException(
                    $"The document renderer has no case for {block.GetType().Name}. Every "
                    + "DocumentBlock must be handled: an unrendered block is a missing value on a "
                    + "document somebody relies on.");
        }
    }

    private static void AddHeading(Section section, DocumentHeading heading)
    {
        var paragraph = section.AddParagraph(heading.Text);
        paragraph.Format.Font.Bold = true;
        paragraph.Format.Font.Size = heading.Level <= 1 ? BodySize + 6 : BodySize + 2;
        paragraph.Format.SpaceAfter = Unit.FromPoint(heading.Subtext is null ? 8 : 2);

        if (heading.Subtext is null)
        {
            return;
        }

        var subtext = section.AddParagraph(heading.Subtext);
        subtext.Format.Font.Color = Tokens.TextMuted;
        subtext.Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void AddParagraph(Section section, DocumentParagraph block)
    {
        var paragraph = section.AddParagraph(block.Text);
        paragraph.Format.Font.Size = block.Small ? BodySize - 1.5 : BodySize;
        paragraph.Format.SpaceAfter = Unit.FromPoint(6);
    }

    /// <summary>
    /// Party blocks laid out with a borderless table rather than with tab stops, so a long address
    /// wraps within its own column instead of colliding with the block beside it.
    /// </summary>
    private static void AddParties(Section section, DocumentPartyRow row)
    {
        if (row.Parties.Count == 0)
        {
            return;
        }

        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Rows.LeftIndent = 0;

        var width = UsableWidth(section) / row.Parties.Count;

        foreach (var _ in row.Parties)
        {
            table.AddColumn(Unit.FromPoint(width));
        }

        var cells = table.AddRow();
        cells.VerticalAlignment = VerticalAlignment.Top;

        for (var index = 0; index < row.Parties.Count; index++)
        {
            var party = row.Parties[index];
            var cell = cells.Cells[index];

            var caption = cell.AddParagraph(party.Caption.ToUpperInvariant());
            caption.Format.Font.Size = BodySize - 2;
            caption.Format.Font.Color = Tokens.TextMuted;
            caption.Format.SpaceAfter = Unit.FromPoint(2);

            foreach (var line in party.Lines)
            {
                cell.AddParagraph(line);
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static void AddFieldGrid(Section section, DocumentFieldGrid grid)
    {
        if (grid.Fields.Count == 0)
        {
            return;
        }

        var columns = Math.Max(1, grid.Columns);
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Rows.LeftIndent = 0;

        var width = UsableWidth(section) / columns;

        for (var index = 0; index < columns; index++)
        {
            table.AddColumn(Unit.FromPoint(width));
        }

        Row? current = null;

        for (var index = 0; index < grid.Fields.Count; index++)
        {
            if (index % columns == 0)
            {
                current = table.AddRow();
            }

            var field = grid.Fields[index];
            var cell = current!.Cells[index % columns];

            var label = cell.AddParagraph(field.Label.ToUpperInvariant());
            label.Format.Font.Size = BodySize - 2;
            label.Format.Font.Color = Tokens.TextMuted;

            var value = cell.AddParagraph(field.Value);
            value.Format.SpaceAfter = Unit.FromPoint(6);
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);
    }

    private static void AddTable(Section section, DocumentTable model)
    {
        if (model.Columns.Count == 0)
        {
            return;
        }

        var table = section.AddTable();
        table.Borders.Width = 0.4;
        table.Borders.Color = Tokens.Border;
        table.Rows.LeftIndent = 0;

        var usable = UsableWidth(section);
        var total = model.Columns.Sum(column => column.Width);

        foreach (var column in model.Columns)
        {
            var added = table.AddColumn(Unit.FromPoint(usable * column.Width / total));
            added.Format.Alignment = Align(column.Alignment);
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Tokens.SurfaceSunken;

        for (var index = 0; index < model.Columns.Count; index++)
        {
            header.Cells[index].AddParagraph(model.Columns[index].Header);
        }

        foreach (var values in model.Rows)
        {
            var row = table.AddRow();

            for (var index = 0; index < model.Columns.Count; index++)
            {
                row.Cells[index].AddParagraph(index < values.Count ? values[index] : string.Empty);
            }
        }

        if (model.Totals is { Count: > 0 })
        {
            AddTotals(section, model.Totals);
        }
        else
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        }
    }

    /// <summary>
    /// Totals in a two-column block pinned to the right, kept with the table above it so they
    /// cannot be orphaned onto a page of their own.
    /// </summary>
    private static void AddTotals(Section section, IReadOnlyList<DocumentField> totals)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Rows.LeftIndent = Unit.FromPoint(UsableWidth(section) * 0.55);

        table.AddColumn(Unit.FromPoint(UsableWidth(section) * 0.25));
        table.AddColumn(Unit.FromPoint(UsableWidth(section) * 0.20)).Format.Alignment = ParagraphAlignment.Right;

        for (var index = 0; index < totals.Count; index++)
        {
            var row = table.AddRow();

            // Every line but the last is kept with the one below it, so the block cannot be split
            // across a page break and leave the amount payable stranded on its own.
            row.KeepWith = index == totals.Count - 1 ? 0 : 1;

            row.Cells[0].AddParagraph(totals[index].Label);
            row.Cells[1].AddParagraph(totals[index].Value);

            // The last line is the amount payable, and it is the one number anybody looks for.
            if (index == totals.Count - 1)
            {
                row.Format.Font.Bold = true;
                row.Cells[0].Borders.Top.Width = 0.5;
                row.Cells[1].Borders.Top.Width = 0.5;
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
    }

    private static ParagraphAlignment Align(DocumentAlignment alignment) => alignment switch
    {
        DocumentAlignment.Center => ParagraphAlignment.Center,
        DocumentAlignment.Right => ParagraphAlignment.Right,
        _ => ParagraphAlignment.Left,
    };

    private static double UsableWidth(Section section)
    {
        var setup = section.PageSetup;
        var width = setup.PageWidth.Point;

        if (width <= 0)
        {
            // PageWidth is only populated when it was set explicitly; a PageFormat leaves it at
            // zero until rendering, so A4 is assumed rather than a zero-width table produced.
            width = Unit.FromMillimeter(setup.Orientation == Orientation.Landscape ? 297 : 210).Point;
        }

        return width - setup.LeftMargin.Point - setup.RightMargin.Point;
    }
}
