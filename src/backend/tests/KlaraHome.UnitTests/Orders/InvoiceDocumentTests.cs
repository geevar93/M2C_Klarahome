using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Invoicing;

namespace KlaraHome.UnitTests.Orders;

/// <summary>
/// The tax invoice as a document: the statutory content rule 46 of the CGST Rules asks for, and the
/// tax columns the supply type calls for.
/// </summary>
/// <remarks>
/// <para>
/// The builder is a pure function from a priced order to a <see cref="DocumentDefinition"/>, so this
/// is where its content belongs. What the integration suite proves alongside it is the half a unit
/// test cannot: that a PDF is actually rendered, stored and registered against the invoice.
/// </para>
/// <para>
/// Worth having at all because the failure mode is silent. Rendering is deliberately swallowed by
/// <c>InvoiceService</c> so a storage outage cannot hold up a dispatched parcel — which means a
/// builder that throws produces an invoice with no document and no complaint anywhere but a log
/// line.
/// </para>
/// </remarks>
public sealed class InvoiceDocumentTests
{
    /// <summary>
    /// An intra-state supply is set with CGST and SGST columns, and the amounts under each head.
    /// </summary>
    [Fact]
    public void An_intra_state_invoice_is_set_with_cgst_and_sgst_columns()
    {
        var document = Build(intraState: true);
        var table = Assert.Single(document.Blocks.OfType<DocumentTable>());
        var headers = table.Columns.Select(column => column.Header).ToList();

        Assert.Contains("CGST", headers, StringComparer.Ordinal);
        Assert.Contains("SGST", headers, StringComparer.Ordinal);
        Assert.DoesNotContain("IGST", headers, StringComparer.Ordinal);

        Assert.NotNull(table.Totals);
        Assert.Contains(table.Totals, field => field.Label == "CGST");
        Assert.Contains(table.Totals, field => field.Label == "SGST");
        Assert.DoesNotContain(table.Totals, field => field.Label == "IGST");
    }

    /// <summary>
    /// An inter-state supply is set with an IGST column and neither state head.
    /// </summary>
    /// <remarks>
    /// Printing all three heads and leaving one blank is the common shortcut and a bad one: a reader
    /// checking an invoice against a return has to know which head was charged, and a column that is
    /// always empty invites the wrong answer.
    /// </remarks>
    [Fact]
    public void An_inter_state_invoice_is_set_with_an_igst_column_and_neither_state_head()
    {
        var document = Build(intraState: false);
        var table = Assert.Single(document.Blocks.OfType<DocumentTable>());
        var headers = table.Columns.Select(column => column.Header).ToList();

        Assert.Contains("IGST", headers, StringComparer.Ordinal);
        Assert.DoesNotContain("CGST", headers, StringComparer.Ordinal);
        Assert.DoesNotContain("SGST", headers, StringComparer.Ordinal);

        Assert.NotNull(table.Totals);
        Assert.Contains(table.Totals, field => field.Label == "IGST");
        Assert.DoesNotContain(table.Totals, field => field.Label == "CGST");
    }

    /// <summary>
    /// Everything rule 46 asks for is on the page: the supplier and their GSTIN, the recipient, a
    /// serial number and its date, the HSN and per-head tax of every item, and the place of supply.
    /// </summary>
    [Fact]
    public void The_document_carries_the_statutory_content_of_rule_46()
    {
        var document = Build(intraState: true);

        // The serial number and its date, the order it belongs to, and where the supply was made.
        var fields = document.Blocks.OfType<DocumentFieldGrid>()
            .SelectMany(grid => grid.Fields)
            .ToDictionary(field => field.Label, field => field.Value, StringComparer.Ordinal);

        Assert.Equal("KLARA/2026-27/00042", fields["Invoice number"]);
        Assert.Equal("KH-2609-000184", fields["Order number"]);
        Assert.Equal("2026-27", fields["Financial year"]);
        Assert.Contains("36", fields["Place of supply"], StringComparison.Ordinal);
        Assert.Contains("Telangana", fields["Place of supply"], StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(fields["Invoice date"]));

        // The supplier is the seller, under the seller's registration — never the marketplace.
        var parties = Assert.Single(document.Blocks.OfType<DocumentPartyRow>()).Parties;
        var supplier = Assert.Single(parties, party => party.Caption == "Sold by");

        Assert.Contains("Panipat Textiles", supplier.Lines, StringComparer.Ordinal);
        Assert.Contains("GSTIN 36AAACP1234A1Z9", supplier.Lines, StringComparer.Ordinal);

        Assert.Contains(parties, party => party.Caption == "Billed to");
        Assert.Contains(parties, party => party.Caption == "Shipped to");

        var recipient = Assert.Single(parties, party => party.Caption == "Billed to");
        Assert.Contains("Asha Rao", recipient.Lines, StringComparer.Ordinal);

        // Per item: the HSN the rate came from, the taxable value, the rate and the tax under each
        // head that applies.
        var table = Assert.Single(document.Blocks.OfType<DocumentTable>());
        var row = Assert.Single(table.Rows);

        Assert.Contains("630222", row, StringComparer.Ordinal);
        Assert.Contains("5%", row, StringComparer.Ordinal);

        // The operator names itself as the electronic-commerce operator it legally is, and the
        // grievance officer is a statutory publication.
        var paragraphs = document.Blocks.OfType<DocumentParagraph>().Select(block => block.Text).ToList();

        Assert.Contains(paragraphs, text => text.Contains("electronic-commerce operator", StringComparison.Ordinal));
        Assert.Contains(paragraphs, text => text.Contains("Grievance officer: Rohit Menon", StringComparison.Ordinal));
    }

    /// <summary>
    /// Money is written with Indian digit grouping — lakhs and crores, not thousands.
    /// </summary>
    /// <remarks>
    /// The builder used to ask for the <c>en-IN</c> culture by name, which throws under the
    /// <c>InvariantGlobalization</c> the product builds with; the throw was swallowed and every
    /// invoice was issued with no document. Grouping is the whole of what the culture was wanted
    /// for, so this asserts the grouping rather than the lookup.
    /// </remarks>
    [Fact]
    public void Money_is_grouped_in_lakhs_rather_than_thousands()
    {
        var document = Build(intraState: true, lineTotal: 123456m);
        var table = Assert.Single(document.Blocks.OfType<DocumentTable>());
        var totals = table.Totals!;

        Assert.Equal("1,23,456.00", Assert.Single(totals, field => field.Label == "Invoice total").Value);
    }

    /// <summary>An invoiced sub-order, priced, with one line and one seller.</summary>
    /// <param name="intraState">Whether the supply is intra-state.</param>
    /// <param name="lineTotal">What the single line comes to.</param>
    [Fact]
    public void Document_is_headed_by_the_store_name()
    {
        var document = Build(intraState: true);

        var first = Assert.IsType<DocumentHeading>(document.Blocks[0]);
        Assert.Equal("Klara Home", first.Text);
        Assert.Equal(1, first.Level);
        Assert.StartsWith("Klara Home", document.FooterText, StringComparison.Ordinal);
    }

    private static DocumentDefinition Build(bool intraState, decimal lineTotal = 1998m)
    {
        var order = Order.Place(
            "KH-2609-000184",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INR");

        var address = new OrderAddressSnapshot
        {
            RecipientName = "Asha Rao",
            Mobile = "+919876543210",
            Line1 = "12 MG Road",
            City = "Hyderabad",
            StateId = Guid.NewGuid(),
            StateName = "Telangana",
            StateCode = "36",
            Pincode = "500034",
        };

        order.Capture(
            new OrderCustomerSnapshot { DisplayName = "Asha Rao", Email = "asha@example.test" },
            address,
            address);

        order.Price(
            OrderPaymentMethod.CashOnDelivery,
            lineTotal,
            0m,
            0m,
            95.14m,
            0m,
            0m,
            0m,
            lineTotal,
            lineTotal,
            couponCode: null,
            OrderChannels.Web,
            DateTimeOffset.Parse("2026-09-06T10:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture));

        var vendorId = Guid.NewGuid();
        var subOrder = SubOrder.Open(order.Id, vendorId, "KH-2609-000184-01", "INR");

        subOrder.CaptureVendor("KLARA", "Panipat Textiles", "36AAACP1234A1Z9");
        subOrder.Price(lineTotal, 0m, lineTotal - 95.14m, 95.14m, 0m, 0m, lineTotal);

        var line = OrderLine.Create(subOrder.Id, vendorId, Guid.NewGuid(), "SKU-000001", 2);

        line.Capture(
            Guid.NewGuid(),
            new ProductSnapshot { Name = "Cotton Cushion Cover — Beige", HsnCode = "630222", IsReturnable = true });

        line.Price(
            1299m,
            lineTotal / 2m,
            0m,
            lineTotal - 95.14m,
            5m,
            intraState ? 47.57m : 0m,
            intraState ? 47.57m : 0m,
            intraState ? 0m : 95.14m,
            0m,
            lineTotal);

        subOrder.AddLine(line);
        order.AddSubOrder(subOrder);

        var invoice = Invoice.Issue(
            order.Id,
            subOrder.Id,
            vendorId,
            "KLARA/2026-27/00042",
            "KLARA/2026-27",
            "2026-27",
            DateTimeOffset.Parse("2026-09-07T11:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture));

        invoice.Tax(
            "36",
            intraState,
            lineTotal - 95.14m,
            intraState ? 47.57m : 0m,
            intraState ? 47.57m : 0m,
            intraState ? 0m : 95.14m,
            0m,
            lineTotal,
            "INR");

        return InvoiceDocumentBuilder.Build(
            order,
            subOrder,
            invoice,
            new BrandingSettings { StoreName = "Klara Home", Tagline = "Everything for a home you love." },
            new LegalSettings { LegalEntityName = "Klara Home Retail Pvt Ltd", Gstin = "36AAACK9999A1Z1" },
            new SupportSettings
            {
                GrievanceOfficerName = "Rohit Menon",
                GrievanceOfficerEmail = "grievance@klarahome.test",
            });
    }
}
