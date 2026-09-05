namespace KlaraHome.Contracts.Documents;

/// <summary>The page geometry a document is rendered onto.</summary>
public enum DocumentPageSize
{
    /// <summary>A4 portrait. Invoices, credit notes, statements.</summary>
    A4Portrait = 0,

    /// <summary>A4 landscape. Manifests and wide tabular exports.</summary>
    A4Landscape = 1,

    /// <summary>4 x 6 inches — the thermal shipping-label format every Indian courier accepts.</summary>
    Label4x6 = 2,
}

/// <summary>How a table column is aligned, and whether it carries a number.</summary>
public enum DocumentAlignment
{
    /// <summary>Left. The default for text.</summary>
    Left = 0,

    /// <summary>Centred.</summary>
    Center = 1,

    /// <summary>Right. Always used for money and quantities, so digits line up by place value.</summary>
    Right = 2,
}

/// <summary>One piece of a document. Closed on purpose: the renderer must handle every case.</summary>
/// <remarks>
/// A small vocabulary, chosen from the documents this platform actually has to produce — a GST
/// invoice, a credit note, a shipping label and a manifest. It is not a page-description language,
/// and it should not grow into one: a document that cannot be said in these terms is a sign that
/// the renderer, not the model, needs the work.
/// </remarks>
public abstract record DocumentBlock
{
    private protected DocumentBlock()
    {
    }
}

/// <summary>A heading, optionally with a line of secondary text beneath it.</summary>
/// <param name="Text">The heading.</param>
/// <param name="Subtext">A subtitle, or null.</param>
/// <param name="Level">1 for the document title, 2 for a section heading.</param>
public sealed record DocumentHeading(string Text, string? Subtext = null, int Level = 1) : DocumentBlock;

/// <summary>A paragraph of running text — terms, declarations, a statutory notice.</summary>
/// <param name="Text">The text.</param>
/// <param name="Small">Whether to set it smaller than body text.</param>
public sealed record DocumentParagraph(string Text, bool Small = false) : DocumentBlock;

/// <summary>A labelled block of address-shaped text, such as "Sold by" or "Ship to".</summary>
/// <param name="Caption">The block's label.</param>
/// <param name="Lines">The lines, already ordered for display.</param>
public sealed record DocumentParty(string Caption, IReadOnlyList<string> Lines);

/// <summary>Two or three party blocks side by side, as the top of an invoice sets them.</summary>
/// <param name="Parties">The blocks, left to right.</param>
public sealed record DocumentPartyRow(IReadOnlyList<DocumentParty> Parties) : DocumentBlock;

/// <summary>One labelled value in a field grid.</summary>
/// <param name="Label">The label.</param>
/// <param name="Value">The value, already formatted for display.</param>
public sealed record DocumentField(string Label, string Value);

/// <summary>A compact grid of labelled values: invoice number, date, order number, place of supply.</summary>
/// <param name="Fields">The fields, in reading order.</param>
/// <param name="Columns">How many fields per row.</param>
public sealed record DocumentFieldGrid(IReadOnlyList<DocumentField> Fields, int Columns = 2) : DocumentBlock;

/// <summary>A table column.</summary>
/// <param name="Header">The header text.</param>
/// <param name="Width">Relative width. The renderer normalises these across the printable width.</param>
/// <param name="Alignment">How the cells are aligned.</param>
public sealed record DocumentColumn(string Header, double Width = 1, DocumentAlignment Alignment = DocumentAlignment.Left);

/// <summary>A line-item table, with an optional block of totals under it.</summary>
/// <param name="Columns">The columns.</param>
/// <param name="Rows">The rows, each with one cell per column.</param>
/// <param name="Totals">Label/value pairs rendered right-aligned beneath the table.</param>
/// <remarks>
/// The header row repeats on every page and the totals are kept with the last row, because an
/// invoice whose totals land alone on page three is a support call.
/// </remarks>
public sealed record DocumentTable(
    IReadOnlyList<DocumentColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<DocumentField>? Totals = null) : DocumentBlock;

/// <summary>Vertical space.</summary>
/// <param name="Points">Height in points.</param>
public sealed record DocumentSpacer(double Points = 12) : DocumentBlock;

/// <summary>A horizontal rule.</summary>
public sealed record DocumentRule : DocumentBlock;

/// <summary>
/// A complete document, ready to render. Everything on the page is here; the renderer adds no
/// content of its own beyond page numbering.
/// </summary>
/// <param name="Title">The PDF's title metadata, and the filename a browser suggests.</param>
/// <param name="Blocks">The body, in order.</param>
/// <param name="PageSize">The page geometry.</param>
/// <param name="FooterText">Repeated at the foot of every page, beside the page number.</param>
/// <param name="Author">The PDF's author metadata. The store's legal entity, normally.</param>
public sealed record DocumentDefinition(
    string Title,
    IReadOnlyList<DocumentBlock> Blocks,
    DocumentPageSize PageSize = DocumentPageSize.A4Portrait,
    string? FooterText = null,
    string? Author = null);
