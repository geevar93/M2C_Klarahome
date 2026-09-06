using System.Text;
using KlaraHome.Modules.Reporting.Application;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Export;

namespace KlaraHome.UnitTests.Reporting;

/// <summary>
/// What a produced report actually contains.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1, and it is the one test in this step that
/// is a security control rather than an arithmetic one. A report's cells include product names and
/// SKUs a seller supplied (docs/07-security-compliance.md §3); a cell beginning <c>=</c> is executed
/// by a spreadsheet when the file is opened, and the person opening it is the store's own finance
/// team.
/// </remarks>
public sealed class CsvWriterTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The header is the declared labels, and the rows follow the declared order.</summary>
    /// <remarks>
    /// The columns are walked rather than the row's own keys, so a report whose query returns its
    /// values in a different order still writes them under the right headings.
    /// </remarks>
    [Fact]
    public void The_columns_are_written_in_their_declared_order()
    {
        var csv = Write(
            [
                new("day", "Day", ReportColumnKind.Date),
                new("units", "Units", ReportColumnKind.Count),
                new("netValue", "Net", ReportColumnKind.Money),
            ],
            [
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["netValue"] = 1_234.5m,
                    ["day"] = new DateOnly(2026, 9, 2),
                    ["units"] = 7,
                },
            ]);

        var lines = Lines(csv);

        Assert.Equal("Day,Units,Net", lines[0]);
        Assert.Equal("2026-09-02,7,1234.50", lines[1]);
    }

    /// <summary>
    /// A cell a spreadsheet would execute is defused.
    /// </summary>
    /// <remarks>
    /// The reason this file is hand-written rather than delegated. A seller who names a product
    /// <c>=cmd|'/c calc'!A0</c> is not writing a product name; the apostrophe makes a spreadsheet
    /// read the whole thing as text.
    /// </remarks>
    [Fact]
    public void A_formula_is_defused()
    {
        var csv = Write(
            [new("productName", "Product", ReportColumnKind.Text)],
            [Row("productName", "=1+1")]);

        Assert.Contains("'=1+1", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=1+1", csv, StringComparison.Ordinal);
    }

    /// <summary>A cell containing a comma or a quote is quoted, and its quotes are doubled.</summary>
    [Fact]
    public void A_cell_with_a_separator_in_it_is_quoted()
    {
        var csv = Write(
            [new("productName", "Product", ReportColumnKind.Text)],
            [Row("productName", "Cushion, 18\" square")]);

        Assert.Contains("\"Cushion, 18\"\" square\"", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file begins with a byte-order mark.
    /// </summary>
    /// <remarks>
    /// Without it, Excel on a machine set to an Indian locale opens a UTF-8 file as its own code page
    /// and renders every rupee sign as mojibake — and whoever receives the report concludes the
    /// store's data is corrupt.
    /// </remarks>
    [Fact]
    public void The_file_carries_a_byte_order_mark()
    {
        var bytes = CsvWriter.Write(Result(
            [new("day", "Day", ReportColumnKind.Date)],
            [Row("day", new DateOnly(2026, 9, 2))]));

        Assert.True(bytes.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    /// <summary>A percentage is written as a percentage, not as the ratio it is stored as.</summary>
    /// <remarks>
    /// A column headed "Return rate" showing <c>0.0834</c> gets misread as eight percent exactly once
    /// before somebody makes a buying decision on it.
    /// </remarks>
    [Fact]
    public void A_rate_is_written_as_a_percentage()
    {
        var csv = Write(
            [new("returnRate", "Return rate", ReportColumnKind.Percent)],
            [Row("returnRate", 0.0834m)]);

        Assert.Contains("8.34%", csv, StringComparison.Ordinal);
    }

    /// <summary>The totals row is labelled in the first column.</summary>
    [Fact]
    public void The_totals_row_is_labelled()
    {
        var result = new ReportResult(
            "sales-by-day",
            "Sales by day",
            [
                new("day", "Day", ReportColumnKind.Date),
                new("units", "Units", ReportColumnKind.Count),
            ],
            From,
            To,
            GroupBy: null,
            "INR",
            [Row("day", new DateOnly(2026, 9, 2))],
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["units"] = 41 },
            Truncated: false);

        var lines = Lines(Encoding.UTF8.GetString(CsvWriter.Write(result)));

        Assert.Equal("Total,41", lines[^1]);
    }

    /// <summary>A missing value is an empty cell rather than a crash.</summary>
    /// <remarks>
    /// Reports declare columns some of their rows genuinely have no value for — the category of an
    /// uncategorised line, the seller on a totals row — and the writer has to render the shape rather
    /// than refuse it.
    /// </remarks>
    [Fact]
    public void A_missing_value_is_an_empty_cell()
    {
        var csv = Write(
            [
                new("categoryName", "Category", ReportColumnKind.Text),
                new("units", "Units", ReportColumnKind.Count),
            ],
            [Row("units", 3)]);

        Assert.Equal(",3", Lines(csv)[1]);
    }

    /// <summary>An age band is the one a buyer thinks in, and an unknown age is its own band.</summary>
    /// <remarks>
    /// Included here because it is the same class of decision: a stock line whose balance was opened
    /// by an adjustment has no arrival date, and filing it under the freshest band would hide the
    /// oldest goods in the store at the top of the report.
    /// </remarks>
    [Fact]
    public void An_unknown_stock_age_is_its_own_band()
    {
        Assert.Equal("Unknown", InventoryAgeFact.BucketFor(null));
        Assert.Equal("0-29", InventoryAgeFact.BucketFor(0));
        Assert.Equal("0-29", InventoryAgeFact.BucketFor(29));
        Assert.Equal("30-59", InventoryAgeFact.BucketFor(30));
        Assert.Equal("90-179", InventoryAgeFact.BucketFor(179));
        Assert.Equal("180+", InventoryAgeFact.BucketFor(180));
    }

    private static Dictionary<string, object?> Row(string key, object? value)
        => new(StringComparer.Ordinal) { [key] = value };

    private static ReportResult Result(
        IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => new("test", "Test", columns, From, To, GroupBy: null, "INR", rows, Totals: null, Truncated: false);

    private static string Write(
        IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => Encoding.UTF8.GetString(CsvWriter.Write(Result(columns, rows)));

    /// <summary>The file's lines, with the byte-order mark and the trailing blank removed.</summary>
    private static string[] Lines(string csv)
        => csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
}
