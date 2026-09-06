using System.Globalization;
using System.Text;

namespace KlaraHome.Modules.Catalog.Infrastructure.Import;

/// <summary>
/// A minimal RFC 4180 reader and writer.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a dependency, and that is a deliberate trade. CSV parsing is a
/// genuinely small problem — quotes, doubled quotes inside them, embedded newlines and commas —
/// and every mature .NET library for it is either a transitive dependency tree we would be
/// redistributing or carries a licence <c>Directory.Packages.props</c> forbids. Ninety lines that
/// do exactly what the spreadsheet a merchandiser exports produces is the cheaper answer.
/// </para>
/// <para>
/// What it deliberately does not do: infer types, guess a delimiter, or handle an encoding other
/// than UTF-8. A file with a byte-order mark is handled, because Excel writes one.
/// </para>
/// </remarks>
internal static class Csv
{
    /// <summary>
    /// Reads a CSV document into rows of fields.
    /// </summary>
    /// <remarks>
    /// A state machine over the whole text rather than a line reader, because a quoted field may
    /// contain a newline — a product description reliably does — and splitting on newlines first is
    /// the single most common way a CSV parser goes wrong.
    /// </remarks>
    /// <param name="content">The document.</param>
    public static List<string[]> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var index = 0;

        // Excel writes a UTF-8 byte-order mark, which would otherwise become part of the first
        // column's name and make every header lookup miss.
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            index = 1;
        }

        while (index < content.Length)
        {
            var character = content[index];

            if (quoted)
            {
                if (character == '"')
                {
                    // A doubled quote inside a quoted field is a literal quote.
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    quoted = false;
                    index++;
                    continue;
                }

                field.Append(character);
                index++;
                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    index++;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    index++;
                    break;

                case '\r':
                    index++;
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. fields]);
                    fields.Clear();
                    index++;
                    break;

                default:
                    field.Append(character);
                    index++;
                    break;
            }
        }

        // The last row, when the file does not end with a newline. A single empty trailing field is
        // the artefact of a file that does, and is not a row.
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }

    /// <summary>Writes rows of fields as a CSV document.</summary>
    /// <param name="rows">The rows, header first.</param>
    public static string Write(IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();

        foreach (var row in rows)
        {
            for (var index = 0; index < row.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                builder.Append(Escape(row[index]));
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Quotes a field if it needs it.
    /// </summary>
    /// <remarks>
    /// A leading or trailing space is quoted too. Excel strips one otherwise, and a SKU that comes
    /// back with a space silently removed is a round-trip that does not round-trip.
    /// </remarks>
    /// <param name="value">The field.</param>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.AsSpan().ContainsAny(',', '"', '\n')
            || value.Contains('\r', StringComparison.Ordinal)
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1]);

        return needsQuotes
            ? string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"")
            : value;
    }
}

/// <summary>
/// One row of a parsed CSV, addressed by column name rather than by position.
/// </summary>
/// <remarks>
/// Position-based access is how an import silently loads the barcode into the SKU column when
/// somebody reorders their spreadsheet. Names are matched case-insensitively and with surrounding
/// whitespace ignored, because that is what a real exported file looks like.
/// </remarks>
internal sealed class CsvRow
{
    private readonly Dictionary<string, int> _columns;
    private readonly string[] _fields;

    /// <param name="columns">The header, mapped to positions.</param>
    /// <param name="fields">This row's fields.</param>
    /// <param name="number">The line in the file, counting the header as line 1.</param>
    public CsvRow(Dictionary<string, int> columns, string[] fields, int number)
    {
        _columns = columns;
        _fields = fields;
        Number = number;
    }

    /// <summary>The line in the file, counting the header as line 1.</summary>
    public int Number { get; }

    /// <summary>Whether the row has nothing in it at all.</summary>
    public bool IsBlank => Array.TrueForAll(_fields, value => string.IsNullOrWhiteSpace(value));

    /// <summary>The value of one column, trimmed, or null when it is absent or empty.</summary>
    /// <param name="column">The column name.</param>
    public string? Text(string column)
    {
        if (!_columns.TryGetValue(column, out var index) || index >= _fields.Length)
        {
            return null;
        }

        var value = _fields[index].Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>The value of one column as a decimal, or null when it is absent or unparseable.</summary>
    /// <param name="column">The column name.</param>
    public decimal? Amount(string column)
        => decimal.TryParse(Text(column), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>The value of one column as an integer, or null.</summary>
    /// <param name="column">The column name.</param>
    public int? Integer(string column)
        => int.TryParse(Text(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// The value of one column as a flag. Accepts <c>true</c>, <c>yes</c>, <c>y</c> and <c>1</c>.
    /// </summary>
    /// <remarks>
    /// A spreadsheet does not have a boolean type, so what actually arrives in a "returnable"
    /// column is whatever the person typed. Refusing everything but the literal <c>True</c> would
    /// reject most real files.
    /// </remarks>
    /// <param name="column">The column name.</param>
    /// <param name="fallback">What to answer when the column is absent or empty.</param>
    public bool Flag(string column, bool fallback)
    {
        var value = Text(column);

        return value is null
            ? fallback
            : value.ToUpperInvariant() is "TRUE" or "YES" or "Y" or "1";
    }

    /// <summary>Builds the header map from a parsed CSV's first row.</summary>
    /// <param name="header">The header fields.</param>
    public static Dictionary<string, int> MapColumns(IReadOnlyList<string> header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < header.Count; index++)
        {
            var name = header[index].Trim().Trim('\uFEFF');

            if (name.Length > 0)
            {
                columns.TryAdd(name, index);
            }
        }

        return columns;
    }
}
