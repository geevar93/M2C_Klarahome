using KlaraHome.Modules.Catalog.Infrastructure.Import;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The CSV reader and writer behind bulk import and export.
/// </summary>
/// <remarks>
/// A parser is the other thing the build sprint's rule 1 names outright. This one is hand-written
/// rather than a dependency, it has to survive whatever Excel produces, and every one of its edge
/// cases — a quoted comma, a doubled quote, a newline inside a field, a byte-order mark — is a
/// silent data-corruption bug rather than a crash if it is wrong.
/// </remarks>
public sealed class CsvTests
{
    [Fact]
    public void A_plain_document_parses_into_rows_and_fields()
    {
        var rows = Csv.Parse("sku,mrp\nSKU-1,499\nSKU-2,899\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["sku", "mrp"], rows[0]);
        Assert.Equal(["SKU-2", "899"], rows[2]);
    }

    [Fact]
    public void A_final_row_without_a_trailing_newline_is_not_lost()
    {
        var rows = Csv.Parse("sku\nSKU-1");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["SKU-1"], rows[1]);
    }

    [Fact]
    public void A_quoted_field_may_contain_a_comma()
    {
        var rows = Csv.Parse("sku,name\nSKU-1,\"Cushion, beige\"\n");

        Assert.Equal(["SKU-1", "Cushion, beige"], rows[1]);
    }

    [Fact]
    public void A_doubled_quote_inside_a_quoted_field_is_one_literal_quote()
    {
        var rows = Csv.Parse("sku,name\nSKU-1,\"40\"\" cushion\"\n");

        Assert.Equal(["SKU-1", "40\" cushion"], rows[1]);
    }

    [Fact]
    public void A_quoted_field_may_contain_a_newline()
    {
        // The reason this is a state machine over the whole text rather than a line reader: a
        // product description reliably contains one, and splitting on newlines first would turn one
        // row into two malformed ones.
        var rows = Csv.Parse("sku,description\nSKU-1,\"Line one\nLine two\"\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Line one\nLine two", rows[1][1]);
    }

    [Fact]
    public void Carriage_returns_are_ignored_so_a_windows_file_reads_the_same()
    {
        var rows = Csv.Parse("sku,mrp\r\nSKU-1,499\r\n");

        Assert.Equal(["SKU-1", "499"], rows[1]);
    }

    [Fact]
    public void A_byte_order_mark_does_not_become_part_of_the_first_column_name()
    {
        // Excel writes one. Without this the header lookup for "sku" misses and every row is
        // rejected for having no SKU.
        var rows = Csv.Parse("\uFEFFsku,mrp\nSKU-1,499\n");
        var columns = CsvRow.MapColumns(rows[0]);

        Assert.True(columns.ContainsKey("sku"));
    }

    [Fact]
    public void Columns_are_matched_by_name_case_insensitively_and_ignoring_surrounding_space()
    {
        var rows = Csv.Parse("SKU , Mrp\nSKU-1,499\n");
        var row = new CsvRow(CsvRow.MapColumns(rows[0]), rows[1], 2);

        // Position-based access is how an import loads the barcode into the SKU column when
        // somebody reorders their spreadsheet.
        Assert.Equal("SKU-1", row.Text("sku"));
        Assert.Equal(499m, row.Amount("mrp"));
    }

    [Fact]
    public void An_absent_or_empty_column_reads_as_null_rather_than_as_a_value()
    {
        var rows = Csv.Parse("sku,mrp\nSKU-1,\n");
        var row = new CsvRow(CsvRow.MapColumns(rows[0]), rows[1], 2);

        // Blank means "leave what is there" to the importer, so it must not come back as zero.
        Assert.Null(row.Text("mrp"));
        Assert.Null(row.Amount("mrp"));
        Assert.Null(row.Text("barcode"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("Yes", true)]
    [InlineData("y", true)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    public void A_flag_accepts_what_a_person_actually_types_in_a_spreadsheet(string value, bool expected)
    {
        var rows = Csv.Parse($"sku,is_returnable\nSKU-1,{value}\n");
        var row = new CsvRow(CsvRow.MapColumns(rows[0]), rows[1], 2);

        Assert.Equal(expected, row.Flag("is_returnable", !expected));
    }

    [Fact]
    public void A_blank_row_is_recognised_so_the_importer_can_skip_it()
    {
        var rows = Csv.Parse("sku,mrp\n,\nSKU-1,499\n");

        Assert.True(new CsvRow(CsvRow.MapColumns(rows[0]), rows[1], 2).IsBlank);
        Assert.False(new CsvRow(CsvRow.MapColumns(rows[0]), rows[2], 3).IsBlank);
    }

    [Fact]
    public void Writing_quotes_only_the_fields_that_need_it()
    {
        var written = Csv.Write([["SKU-1", "Cushion, beige", "40\" wide", " padded ", null]]);

        Assert.Equal("SKU-1,\"Cushion, beige\",\"40\"\" wide\",\" padded \",\n", written);
    }

    [Fact]
    public void What_is_written_reads_back_identically()
    {
        // Round-tripping is the whole point of the export: an operator exports, edits in a
        // spreadsheet and re-uploads.
        string?[] original = ["SKU-1", "Cushion, beige", "40\" wide", "Line one\nLine two", string.Empty];

        var rows = Csv.Parse(Csv.Write([original]));

        Assert.Single(rows);
        Assert.Equal(original, rows[0]);
    }
}
