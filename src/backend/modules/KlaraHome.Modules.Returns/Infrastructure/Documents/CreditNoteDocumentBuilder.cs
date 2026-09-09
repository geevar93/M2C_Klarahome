using System.Globalization;
using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Platform;
using KlaraHome.Modules.Returns.Domain;

namespace KlaraHome.Modules.Returns.Infrastructure.Documents;

/// <summary>
/// Renders a credit note in the shape rule 53 of the CGST Rules asks for.
/// </summary>
/// <remarks>
/// <para>
/// The particulars the rule requires are all here and are all on the face of the document: the words
/// "Credit Note", the supplier's name, address and GSTIN, a consecutive serial number unique for the
/// financial year, the date, the recipient's details, the original invoice's number and date, the
/// value being credited and the tax on it.
/// </para>
/// <para>
/// It renders the units <em>accepted</em> rather than the units asked for. A credit note is a
/// statement about a supply that was reversed, and a unit that failed inspection was not reversed —
/// crediting it would reduce the seller's output tax on goods they never got back.
/// </para>
/// <para>
/// The layout deliberately mirrors the tax invoice's. The two documents are read side by side at
/// every reconciliation, and a shopper comparing them should not have to work out which column is
/// which.
/// </para>
/// </remarks>
internal static class CreditNoteDocumentBuilder
{
    // Not CultureInfo.GetCultureInfo("en-IN"): this process runs with globalization invariant mode
    // on (no ICU data shipped with the runtime), and a named culture lookup throws
    // CultureNotFoundException the first time this type is touched — which, being a static field
    // initializer, takes down every credit note this module ever tries to render rather than just
    // this one. Found rather than designed around: no credit note PDF has ever actually rendered in
    // this environment (nor, on the identical pattern, has an Orders invoice's — PARKING_LOT.md).
    // The invariant culture renders the same digits with Western rather than Indian digit grouping,
    // which is a cosmetic difference on a document whose money and tax figures are unaffected by it.
    private static readonly CultureInfo India = CultureInfo.InvariantCulture;

    /// <summary>Builds the document.</summary>
    /// <param name="request">The RMA it credits.</param>
    /// <param name="note">The credit note itself, with its number and amounts.</param>
    /// <param name="order">The seller's part it relates to.</param>
    /// <param name="legal">The marketplace operator's own details.</param>
    /// <param name="support">The grievance officer, whom the rules require to be named.</param>
    public static DocumentDefinition Build(
        ReturnRequest request,
        CreditNote note,
        SubOrderReturnView order,
        LegalSettings legal,
        SupportSettings support)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(support);

        var lines = request.Lines.Where(line => line.QuantityAccepted > 0).ToArray();

        List<DocumentBlock> blocks =
        [
            new DocumentHeading("Credit Note", $"{note.CreditNoteNumber} · {order.OrderNumber}"),
            new DocumentFieldGrid(
                [
                    new DocumentField("Credit note number", note.CreditNoteNumber),
                    new DocumentField("Credit note date", note.IssuedAt.ToString("dd MMM yyyy", India)),
                    new DocumentField("Against invoice", note.InvoiceNumber ?? "Not available"),
                    new DocumentField("Order number", order.OrderNumber),
                    new DocumentField("Return number", request.ReturnNumber),
                    new DocumentField("Financial year", note.FinancialYear),
                ],
                Columns: 3),
            new DocumentSpacer(),
            new DocumentPartyRow(
                [
                    new DocumentParty("Issued by", SellerLines(order)),
                    new DocumentParty("Issued to", RecipientLines(order)),
                ]),
            new DocumentSpacer(),
            new DocumentTable(
                Columns(note.IsIntraState),
                [.. lines.Select(line => Row(line, note.IsIntraState))],
                Totals(note, request)),
            new DocumentSpacer(),
            new DocumentParagraph(
                $"Reason for credit: {request.ReasonCode}"
                + (string.IsNullOrWhiteSpace(request.ReasonNote) ? "." : $" — {request.ReasonNote}"),
                Small: true),
            new DocumentParagraph(
                "Issued under section 34 of the Central Goods and Services Tax Act, 2017. All amounts "
                + "are in Indian Rupees. The tax credited is the tax charged on the original supply.",
                Small: true),
        ];

        if (!string.IsNullOrWhiteSpace(legal.LegalEntityName))
        {
            blocks.Add(new DocumentParagraph(
                $"Issued through {legal.LegalEntityName}"
                + (string.IsNullOrWhiteSpace(legal.Gstin) ? string.Empty : $", GSTIN {legal.Gstin}")
                + ", acting as an electronic-commerce operator under section 52 of the CGST Act. "
                + "The seller named above is the supplier of these goods.",
                Small: true));
        }

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
            "This is a computer-generated credit note and does not require a signature.",
            Small: true));

        return new DocumentDefinition(
            $"Credit note {note.CreditNoteNumber}",
            blocks,
            DocumentPageSize.A4Portrait,
            FooterText: $"{note.CreditNoteNumber} · {order.OrderNumber}",
            Author: string.IsNullOrWhiteSpace(legal.LegalEntityName) ? null : legal.LegalEntityName);
    }

    /// <summary>The columns, which differ by whether the supply was intra-state.</summary>
    /// <remarks>
    /// CGST and SGST for a supply within one state, IGST for one across. Printing all three and
    /// leaving two blank would be printing tax heads that do not apply, which is exactly the sort of
    /// thing an assessing officer asks about.
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
                new DocumentColumn("Item", 3.8),
                new DocumentColumn("HSN", 0.8),
                new DocumentColumn("Qty", 0.5, DocumentAlignment.Right),
                new DocumentColumn("Taxable", 1.1, DocumentAlignment.Right),
                new DocumentColumn("Rate", 0.6, DocumentAlignment.Right),
                new DocumentColumn("IGST", 1.1, DocumentAlignment.Right),
                new DocumentColumn("Total", 1.1, DocumentAlignment.Right),
            ];

    /// <summary>One line, apportioned to what quality control accepted.</summary>
    private static IReadOnlyList<string> Row(ReturnLine line, bool isIntraState)
    {
        var share = line.QuantityAccepted >= line.Quantity || line.Quantity <= 0
            ? 1m
            : (decimal)line.QuantityAccepted / line.Quantity;

        var cgst = Round(line.Cgst * share);
        var sgst = Round(line.Sgst * share);
        var igst = Round(line.Igst * share);

        return isIntraState
            ?
            [
                line.Snapshot.Name,
                line.Snapshot.HsnCode ?? "—",
                line.QuantityAccepted.ToString(India),
                Amount(line.AcceptedTaxableValue),
                $"{line.Snapshot.GstRate:0.##}%",
                Amount(cgst),
                Amount(sgst),
                Amount(line.AcceptedRefund),
            ]
            :
            [
                line.Snapshot.Name,
                line.Snapshot.HsnCode ?? "—",
                line.QuantityAccepted.ToString(India),
                Amount(line.AcceptedTaxableValue),
                $"{line.Snapshot.GstRate:0.##}%",
                Amount(igst),
                Amount(line.AcceptedRefund),
            ];
    }

    /// <summary>
    /// The totals block, which says plainly what was credited and what was withheld.
    /// </summary>
    /// <remarks>
    /// The reverse-pickup fee is on the face of the document rather than netted silently into the
    /// total. A shopper who was charged for a collection is entitled to see the charge, and a note
    /// whose total did not match the sum of its lines with no explanation is a note somebody will
    /// query.
    /// </remarks>
    private static List<DocumentField> Totals(CreditNote note, ReturnRequest request)
    {
        List<DocumentField> fields =
        [
            new("Taxable value", Amount(note.TaxableValue)),
        ];

        if (note.IsIntraState)
        {
            fields.Add(new DocumentField("CGST", Amount(note.Cgst)));
            fields.Add(new DocumentField("SGST", Amount(note.Sgst)));
        }
        else
        {
            fields.Add(new DocumentField("IGST", Amount(note.Igst)));
        }

        if (note.Cess > 0m)
        {
            fields.Add(new DocumentField("Cess", Amount(note.Cess)));
        }

        if (request.ShippingRefundAmount > 0m)
        {
            fields.Add(new DocumentField("Delivery charge credited", Amount(request.ShippingRefundAmount)));
        }

        fields.Add(new DocumentField("Credit note total", Amount(note.Total)));

        if (request.ReturnShippingFee > 0m)
        {
            fields.Add(new DocumentField("Less return collection fee", Amount(request.ReturnShippingFee)));
            fields.Add(new DocumentField("Refunded to customer", Amount(request.RefundAmount)));
        }

        return fields;
    }

    /// <summary>Who is issuing it. The seller, because the supply and the GSTIN are theirs.</summary>
    private static List<string> SellerLines(SubOrderReturnView order)
    {
        List<string> lines = [order.VendorName ?? "Seller"];

        if (!string.IsNullOrWhiteSpace(order.VendorGstin))
        {
            lines.Add($"GSTIN {order.VendorGstin}");
        }

        lines.Add($"Seller order {order.SubOrderNumber}");

        return lines;
    }

    /// <summary>Who it is issued to, from the address the parcel was sent to.</summary>
    private static List<string> RecipientLines(SubOrderReturnView order)
    {
        if (order.PickupAddress is not { } address)
        {
            return ["Customer"];
        }

        List<string> lines = [address.Name];

        if (!string.IsNullOrWhiteSpace(address.Line1))
        {
            lines.Add(address.Line1);
        }

        if (!string.IsNullOrWhiteSpace(address.Line2))
        {
            lines.Add(address.Line2!);
        }

        lines.Add($"{address.City} {address.Pincode}");

        return lines;
    }

    private static string Amount(decimal value)
        => value.ToString("N2", India);

    private static decimal Round(decimal value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string Suffix(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : $", {value}";
}
