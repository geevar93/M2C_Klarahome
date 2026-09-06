using System.Buffers;
using System.Globalization;
using System.Text;

namespace KlaraHome.Modules.Settlements.Infrastructure.Reporting;

/// <summary>
/// A minimal RFC 4180 writer.
/// </summary>
/// <remarks>
/// <para>
/// The write half of the Catalog module's reader, duplicated here because a module may not reference
/// another's types — the same boundary cost that duplicates the financial-year arithmetic. It is
/// forty lines and it does exactly what a finance team's spreadsheet needs.
/// </para>
/// <para>
/// It writes a byte-order mark, which the Catalog reader deliberately tolerates on the way in. Excel
/// on a Windows machine set to an Indian locale opens a UTF-8 file without one as Latin-1, and a
/// seller's name with a rupee sign or a Devanagari character in it comes out as mojibake — on the
/// document their accountant files.
/// </para>
/// <para>
/// Every value is written in the invariant culture. A settlement extract read on a machine whose
/// locale uses a comma for a decimal point would be a file whose columns silently shift by one.
/// </para>
/// </remarks>
internal static class Csv
{
    /// <summary>The media type these files are served as.</summary>
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>India Standard Time, as a fixed offset. Every date in an export is rendered in it.</summary>
    private static readonly TimeSpan IndiaOffset = new(5, 30, 0);

    /// <summary>The characters that force a field to be quoted, per RFC 4180.</summary>
    /// <remarks>
    /// A <c>SearchValues</c> rather than four comparisons, because every cell of a ten-thousand-row
    /// export goes through it and the vectorised search costs nothing the loop does not.
    /// </remarks>
    private static readonly SearchValues<char> QuotableCharacters =
        SearchValues.Create([',', '"', '\r', '\n']);

    /// <summary>Writes rows of fields as a CSV document, with a byte-order mark.</summary>
    /// <param name="rows">The rows, the first of which is usually the header.</param>
    public static byte[] Write(IEnumerable<IReadOnlyList<string?>> rows)
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

            // CRLF, which is what RFC 4180 asks for and what Excel is happiest with.
            builder.Append("\r\n");
        }

        var body = Encoding.UTF8.GetBytes(builder.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var file = new byte[preamble.Length + body.Length];

        preamble.CopyTo(file, 0);
        body.CopyTo(file, preamble.Length);

        return file;
    }

    /// <summary>Money as a plain decimal string: two places, invariant, no separators.</summary>
    /// <remarks>
    /// No thousands separator and no currency symbol. A spreadsheet formats a number; a number that
    /// arrives already formatted arrives as text, and text does not add up.
    /// </remarks>
    /// <param name="amount">The amount.</param>
    public static string Money(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>An instant as an ISO 8601 date, in India Standard Time.</summary>
    /// <param name="instant">The instant, or null for an empty cell.</param>
    public static string Date(DateTimeOffset? instant)
        => instant is null
            ? string.Empty
            : instant.Value.ToOffset(IndiaOffset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Quotes a field where it needs quoting, and doubles any quote inside it.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(QuotableCharacters) >= 0
            ? string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"")
            : value;
    }
}
