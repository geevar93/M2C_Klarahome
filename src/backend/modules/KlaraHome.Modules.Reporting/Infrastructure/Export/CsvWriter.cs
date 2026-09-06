using System.Buffers;
using System.Globalization;
using System.Text;
using KlaraHome.Modules.Reporting.Application;

namespace KlaraHome.Modules.Reporting.Infrastructure.Export;

/// <summary>
/// Turns a report result into CSV.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written rather than a library, and for once that is the smaller decision. The whole of RFC
/// 4180 that matters here is one escaping rule, the alternative is a dependency on the redistributed
/// artefact for forty lines of code, and the two things that actually go wrong with a spreadsheet
/// export are neither of them the parser's problem.
/// </para>
/// <para>
/// The first is the byte-order mark. Without one, Excel on a Windows machine set to an Indian locale
/// opens a UTF-8 file as its own code page and renders every rupee sign and every Devanagari
/// character as mojibake — and the person who receives the report concludes the store's data is
/// corrupt.
/// </para>
/// <para>
/// The second is formula injection. A cell beginning <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is
/// executed by a spreadsheet when the file is opened, and the values in these reports include
/// product names and SKUs that a seller supplied (docs/07-security-compliance.md §3). Those are
/// prefixed with an apostrophe, which every spreadsheet reads as "this is text".
/// </para>
/// </remarks>
internal static class CsvWriter
{
    /// <summary>The characters that make a spreadsheet treat a cell as a formula.</summary>
    private static readonly char[] FormulaLeaders = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>The characters that force a cell to be quoted.</summary>
    private static readonly SearchValues<char> MustQuote = SearchValues.Create([',', '"', '\n', '\r']);

    /// <summary>Writes a report as CSV bytes, ready to store or to send.</summary>
    /// <param name="result">The report and its rows.</param>
    public static byte[] Write(ReportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        builder.AppendLine(string.Join(',', result.Columns.Select(column => Escape(column.Label))));

        foreach (var row in result.Rows)
        {
            builder.AppendLine(string.Join(
                ',',
                result.Columns.Select(column => Escape(Format(row.GetValueOrDefault(column.Key), column.Kind)))));
        }

        // The totals row is labelled in the first column and left blank where the report has no
        // meaningful total, which is how a spreadsheet reader expects to find it.
        if (result.Totals is { } totals)
        {
            var cells = result.Columns
                .Select((column, index) => index == 0
                    ? "Total"
                    : Escape(Format(totals.GetValueOrDefault(column.Key), column.Kind)))
                .ToArray();

            builder.AppendLine(string.Join(',', cells));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    /// <summary>
    /// Renders one value the way its declared kind says it should be read.
    /// </summary>
    /// <remarks>
    /// Invariant culture throughout, and deliberately: a CSV is read by a machine as often as by a
    /// person, and a decimal comma in a comma-separated file is a corruption that survives every
    /// parser. A percentage is written as a percentage rather than as the ratio it is stored as,
    /// because a column headed "Return rate" showing <c>0.0834</c> gets misread as eight percent
    /// exactly once before somebody makes a decision on it.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <param name="kind">How to render it.</param>
    private static string Format(object? value, ReportColumnKind kind)
        => value switch
        {
            null => string.Empty,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset instant => instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal number when kind == ReportColumnKind.Percent =>
                (number * 100m).ToString("0.00", CultureInfo.InvariantCulture) + "%",
            decimal number when kind == ReportColumnKind.Money =>
                number.ToString("0.00", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            Guid id => id.ToString(),
            _ => value.ToString() ?? string.Empty,
        };

    /// <summary>Quotes a cell where it needs quoting, and defuses it where a spreadsheet would run it.</summary>
    /// <param name="value">The rendered value.</param>
    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var cell = value;

        // The apostrophe is inside the quotes, so the cell still reads correctly as text; what it
        // stops is the spreadsheet evaluating it as an expression when the file is opened.
        if (Array.IndexOf(FormulaLeaders, cell[0]) >= 0)
        {
            cell = "'" + cell;
        }

        return cell.AsSpan().IndexOfAny(MustQuote) >= 0
            ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : cell;
    }
}
