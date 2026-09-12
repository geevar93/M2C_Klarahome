using System.Globalization;
using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Orders.Domain;

namespace KlaraHome.Modules.Orders.Infrastructure.Invoicing;

/// <summary>
/// Writes a seller's tax invoice as a <see cref="DocumentDefinition"/>.
/// </summary>
/// <remarks>
/// <para>
/// Everything rule 46 of the CGST Rules asks for on a tax invoice, and nothing that would make it a
/// design exercise: the supplier's name, address and GSTIN; the recipient's; a consecutive serial
/// number and its date; the HSN of every item, the taxable value, the rate and the amount of tax
/// under each head; the place of supply; and the total in figures.
/// </para>
/// <para>
/// The seller is the supplier and the marketplace is not. That is the whole reason invoices are per
/// sub-order: the goods are supplied by the seller under the seller's registration, and the operator
/// appears on the document only as the electronic-commerce operator it legally is.
/// </para>
/// <para>
/// It renders no money it was not given. Every figure comes off the frozen order line, which came
/// off the quote the shopper agreed to — an invoice that recomputed its own tax would be a second
/// opinion about what a customer owes.
/// </para>
/// </remarks>
internal static class InvoiceDocumentBuilder
{
    /// <summary>
    /// The Indian number format every figure on the document is written in.
    /// </summary>
    /// <remarks>
    /// Built from the invariant culture rather than looked up as <c>en-IN</c>, because the product
    /// ships with <c>InvariantGlobalization</c> and the lookup therefore throws — silently, since
    /// the caller swallows a rendering failure to keep a parcel moving, which is how every invoice
    /// came to be issued with no document at all. The only thing the lookup was wanted for is the
    /// lakh–crore grouping, and that is one property.
    /// </remarks>
    private static readonly CultureInfo India = IndianFormat();

    /// <summary>Builds the invoice document.</summary>
    /// <param name="order">The order it belongs to.</param>
    /// <param name="subOrder">The seller's part it covers.</param>
    /// <param name="invoice">The invoice, already numbered and taxed.</param>
    /// <param name="branding">The store's name and tagline, which head the page.</param>
    /// <param name="legal">The operator's own legal identity, for the operator block.</param>
    /// <param name="support">Where a customer complains, which is a statutory publication.</param>
    public static DocumentDefinition Build(
        Order order,
        SubOrder subOrder,
        Invoice invoice,
        BrandingSettings branding,
        LegalSettings legal,
        SupportSettings support)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(subOrder);
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(support);

        var lines = subOrder.Lines.Where(line => !line.IsFullyCancelled).ToArray();

        // The marketplace's own name heads the page. The seller is the supplier of record and is
        // named in the party row below, but the customer bought from the store, and an invoice
        // with no store name on it reads as if it came from nowhere - which was the case until
        // this heading existed and the legal entity name was left blank.
        var storeName = string.IsNullOrWhiteSpace(branding.StoreName) ? "Klara Home" : branding.StoreName.Trim();
        var operatorName = string.IsNullOrWhiteSpace(legal.LegalEntityName) ? storeName : legal.LegalEntityName.Trim();

        List<DocumentBlock> blocks =
        [
            new DocumentHeading(storeName, string.IsNullOrWhiteSpace(branding.Tagline) ? null : branding.Tagline.Trim()),
            new DocumentHeading("Tax Invoice", $"{invoice.InvoiceNumber} · {order.OrderNumber}", Level: 2),
            new DocumentFieldGrid(
                [
                    new DocumentField("Invoice number", invoice.InvoiceNumber),
                    new DocumentField("Invoice date", invoice.IssuedAt.ToString("dd MMM yyyy", India)),
                    new DocumentField("Order number", order.OrderNumber),
                    new DocumentField("Order date", order.PlacedAt.ToString("dd MMM yyyy", India)),
                    new DocumentField("Place of supply", PlaceOfSupply(order, invoice)),
                    new DocumentField("Financial year", invoice.FinancialYear),
                ],
                Columns: 3),
            new DocumentSpacer(),
            new DocumentPartyRow(
                [
                    new DocumentParty("Sold by", SellerLines(subOrder)),
                    new DocumentParty("Billed to", AddressLines(order.BillingAddress, order.CustomerSnapshot)),
                    new DocumentParty("Shipped to", AddressLines(order.ShippingAddress, null)),
                ]),
            new DocumentSpacer(),
            new DocumentTable(Columns(invoice.IsIntraState), [.. lines.Select(line => Row(line, invoice.IsIntraState))], Totals(invoice, subOrder)),
            new DocumentSpacer(),
            new DocumentParagraph(
                "All amounts are in Indian Rupees and are inclusive of GST unless stated otherwise. "
                + "Tax is charged on the taxable value shown, at the rate applicable to each item's HSN.",
                Small: true),
        ];

        blocks.Add(new DocumentParagraph(
            $"Sold through {operatorName}"
            + (string.IsNullOrWhiteSpace(legal.Gstin) ? string.Empty : $", GSTIN {legal.Gstin}")
            + ", acting as an electronic-commerce operator under section 52 of the CGST Act. "
            + "The seller named above is the supplier of these goods.",
            Small: true));

        if (!string.IsNullOrWhiteSpace(support.GrievanceOfficerName))
        {
            blocks.Add(new DocumentParagraph(
                $"Grievance officer: {support.GrievanceOfficerName}"
                + Suffix(support.GrievanceOfficerEmail)
                + Suffix(support.GrievanceOfficerPhone)
                + $". Complaints are acknowledged within {support.AcknowledgementHours} hours "
                + $"and resolved within {support.ResolutionDays} days.",
                Small: true));
        }

        blocks.Add(new DocumentRule());
        blocks.Add(new DocumentParagraph(
            "This is a computer-generated invoice and does not require a signature.",
            Small: true));

        return new DocumentDefinition(
            $"Tax invoice {invoice.InvoiceNumber}",
            blocks,
            DocumentPageSize.A4Portrait,
            FooterText: $"{storeName} · {invoice.InvoiceNumber} · {order.OrderNumber}",
            Author: operatorName);
    }

    /// <summary>
    /// The columns, which differ by supply type.
    /// </summary>
    /// <remarks>
    /// An intra-state supply carries CGST and SGST in equal halves and an inter-state one carries
    /// IGST. Printing all three and leaving one empty is a common shortcut and a bad one: a reader
    /// checking an invoice against a return has to know which head was charged, and three columns
    /// where one is always blank invites the wrong answer.
    /// </remarks>
    private static IReadOnlyList<DocumentColumn> Columns(bool isIntraState)
        => isIntraState
            ?
            [
                new DocumentColumn("Item", 3.4),
                new DocumentColumn("HSN", 0.8),
                new DocumentColumn("Qty", 0.5, DocumentAlignment.Right),
                new DocumentColumn("Taxable", 1, DocumentAlignment.Right),
                new DocumentColumn("Rate", 0.6, DocumentAlignment.Right),
                new DocumentColumn("CGST", 0.9, DocumentAlignment.Right),
                new DocumentColumn("SGST", 0.9, DocumentAlignment.Right),
                new DocumentColumn("Total", 1, DocumentAlignment.Right),
            ]
            :
            [
                new DocumentColumn("Item", 3.9),
                new DocumentColumn("HSN", 0.8),
                new DocumentColumn("Qty", 0.5, DocumentAlignment.Right),
                new DocumentColumn("Taxable", 1.1, DocumentAlignment.Right),
                new DocumentColumn("Rate", 0.6, DocumentAlignment.Right),
                new DocumentColumn("IGST", 1.1, DocumentAlignment.Right),
                new DocumentColumn("Total", 1.1, DocumentAlignment.Right),
            ];

    /// <summary>One line of the item table.</summary>
    private static IReadOnlyList<string> Row(OrderLine line, bool isIntraState)
        => isIntraState
            ?
            [
                line.Snapshot.Name,
                line.Snapshot.HsnCode ?? "-",
                line.QuantityLive.ToString(India),
                Money(line.TaxableValue),
                Percent(line.GstRate),
                Money(line.Cgst),
                Money(line.Sgst),
                Money(line.LineTotal - line.CancelledValue),
            ]
            :
            [
                line.Snapshot.Name,
                line.Snapshot.HsnCode ?? "-",
                line.QuantityLive.ToString(India),
                Money(line.TaxableValue),
                Percent(line.GstRate),
                Money(line.Igst),
                Money(line.LineTotal - line.CancelledValue),
            ];

    /// <summary>The block of totals beneath the table.</summary>
    private static List<DocumentField> Totals(Invoice invoice, SubOrder subOrder)
    {
        List<DocumentField> totals =
        [
            new DocumentField("Taxable value", Money(invoice.TaxableValue)),
        ];

        if (invoice.IsIntraState)
        {
            totals.Add(new DocumentField("CGST", Money(invoice.Cgst)));
            totals.Add(new DocumentField("SGST", Money(invoice.Sgst)));
        }
        else
        {
            totals.Add(new DocumentField("IGST", Money(invoice.Igst)));
        }

        if (invoice.Cess > 0m)
        {
            totals.Add(new DocumentField("Cess", Money(invoice.Cess)));
        }

        if (subOrder.ShippingTotal > 0m)
        {
            totals.Add(new DocumentField("Delivery", Money(subOrder.ShippingTotal)));
        }

        if (subOrder.DiscountTotal > 0m)
        {
            totals.Add(new DocumentField("Discount", $"-{Money(subOrder.DiscountTotal)}"));
        }

        totals.Add(new DocumentField("Invoice total", Money(invoice.Total)));

        return totals;
    }

    /// <summary>The supplier block: the seller, never the marketplace.</summary>
    private static List<string> SellerLines(SubOrder subOrder)
    {
        List<string> lines = [subOrder.VendorName ?? subOrder.VendorCode ?? "Seller"];

        if (!string.IsNullOrWhiteSpace(subOrder.VendorGstin))
        {
            lines.Add($"GSTIN {subOrder.VendorGstin}");
        }

        if (!string.IsNullOrWhiteSpace(subOrder.VendorCode))
        {
            lines.Add($"Seller code {subOrder.VendorCode}");
        }

        lines.Add($"Order reference {subOrder.SubOrderNumber}");

        return lines;
    }

    /// <summary>An address block, with the customer's name and GSTIN when it is the billing one.</summary>
    private static List<string> AddressLines(OrderAddressSnapshot address, OrderCustomerSnapshot? customer)
    {
        List<string> lines = [address.RecipientName];

        if (!string.IsNullOrWhiteSpace(address.Line1))
        {
            lines.Add(address.Line1);
        }

        if (!string.IsNullOrWhiteSpace(address.Line2))
        {
            lines.Add(address.Line2!);
        }

        if (!string.IsNullOrWhiteSpace(address.Landmark))
        {
            lines.Add(address.Landmark!);
        }

        lines.Add($"{address.City} {address.Pincode}".Trim());

        if (!string.IsNullOrWhiteSpace(address.StateName))
        {
            lines.Add(address.StateName!);
        }

        if (!string.IsNullOrWhiteSpace(address.Mobile))
        {
            lines.Add(address.Mobile);
        }

        var gstin = address.Gstin ?? customer?.Gstin;

        if (!string.IsNullOrWhiteSpace(gstin))
        {
            lines.Add($"GSTIN {gstin}");
        }

        return lines;
    }

    /// <summary>The place of supply, as a state code and name where both are known.</summary>
    private static string PlaceOfSupply(Order order, Invoice invoice)
    {
        var code = invoice.PlaceOfSupplyStateCode ?? order.ShippingAddress.StateCode;
        var name = order.ShippingAddress.StateName;

        return (code, name) switch
        {
            (not null, not null) => $"{code} — {name}",
            (not null, null) => code,
            (null, not null) => name,
            _ => "-",
        };
    }

    /// <summary>
    /// The invariant culture with Indian digit grouping: <c>1,23,456.00</c> rather than
    /// <c>123,456.00</c>.
    /// </summary>
    /// <remarks>
    /// Grouping is the whole of what an Indian reader needs from the format here. Dates are written
    /// with an explicit <c>dd MMM yyyy</c> pattern whose month names are the same in both cultures,
    /// and the currency symbol is deliberately not printed — the document names the currency once,
    /// in words.
    /// </remarks>
    private static CultureInfo IndianFormat()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberGroupSizes = [3, 2];
        culture.NumberFormat.CurrencyGroupSizes = [3, 2];
        culture.NumberFormat.PercentGroupSizes = [3, 2];

        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>Money, in Indian grouping, with the symbol left off — the document says the currency once.</summary>
    private static string Money(decimal amount) => amount.ToString("N2", India);

    /// <summary>A percentage, trimmed of the trailing zeros a stored rate carries.</summary>
    private static string Percent(decimal rate) => $"{rate.ToString("0.##", India)}%";

    /// <summary>Appends a contact detail when there is one, with the separator.</summary>
    private static string Suffix(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $", {value}";
}
