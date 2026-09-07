using System.Globalization;
using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Settlements.Domain;

namespace KlaraHome.Modules.Settlements.Infrastructure.Invoicing;

/// <summary>
/// Lays out the platform's own tax invoice.
/// </summary>
/// <remarks>
/// <para>
/// A GST tax invoice has a required minimum: both parties named with their GSTINs, a unique number
/// and date, a description of the supply with its HSN/SAC code, the taxable value, the rate, the tax
/// split by head, the total, the place of supply, and a signature block. Every one of them is here,
/// and none of it is decoration.
/// </para>
/// <para>
/// The SAC code is fixed at 998599 — "other support services n.e.c." — which is what a marketplace
/// commission is classified under. It is a constant rather than a setting because it describes what
/// this platform does, and a deployment that does something else needs a different invoice rather
/// than a different code.
/// </para>
/// <para>
/// It builds and does not render. The renderer is Media's, behind <c>IDocumentStore</c>, exactly as
/// the sub-order invoice is — which is what keeps this module free of a PDF library.
/// </para>
/// </remarks>
internal static class CommissionInvoiceDocumentBuilder
{
    /// <summary>The service accounting code marketplace commission is supplied under.</summary>
    public const string ServiceAccountingCode = "998599";

    /// <summary>Builds the document.</summary>
    /// <param name="invoice">The invoice being rendered.</param>
    /// <param name="vendor">Who it is raised on.</param>
    /// <param name="legal">The platform's own legal identity.</param>
    /// <param name="support">Where a query about it goes.</param>
    public static DocumentDefinition Build(
        CommissionInvoice invoice,
        VendorPayoutProfile? vendor,
        LegalSettings legal,
        SupportSettings support)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(legal);
        ArgumentNullException.ThrowIfNull(support);

        var blocks = new List<DocumentBlock>
        {
            new DocumentHeading(
                "Tax invoice",
                "Marketplace services supplied by the platform to the seller"),

            new DocumentPartyRow(
            [
                new DocumentParty("Supplied by", SupplierLines(legal)),
                new DocumentParty("Supplied to", RecipientLines(invoice, vendor)),
            ]),

            new DocumentFieldGrid(
            [
                new DocumentField("Invoice number", invoice.InvoiceNumber),
                new DocumentField("Invoice date", Date(invoice.IssuedAt)),
                new DocumentField("Period", $"{Date(invoice.PeriodStart)} to {Date(invoice.PeriodEnd)}"),
                new DocumentField("Place of supply", invoice.PlaceOfSupplyStateCode ?? "Not stated"),
                new DocumentField("Reverse charge", "No"),
                new DocumentField("Currency", invoice.CurrencyCode),
            ]),

            new DocumentSpacer(),
            Charges(invoice),
            new DocumentSpacer(),
            new DocumentRule(),

            new DocumentParagraph(
                "This invoice is for services supplied by the marketplace to the seller. It is not an "
                + "invoice for goods, and the goods sold in this period were invoiced by the seller to "
                + "their own customers under their own GSTIN.",
                Small: true),

            new DocumentParagraph(
                $"Any query about this invoice: {Contact(support, legal)}.",
                Small: true),

            new DocumentSpacer(24),

            new DocumentParagraph(
                $"For {SupplierName(legal)} — this is a computer-generated invoice and "
                + "needs no signature."),
        };

        return new DocumentDefinition(
            $"Tax invoice {invoice.InvoiceNumber}",
            blocks,
            DocumentPageSize.A4Portrait,
            NullIfBlank(legal.LegalEntityName),
            NullIfBlank(legal.LegalEntityName));
    }

    /// <summary>
    /// The charge table: one line per head, then the tax split, then the total.
    /// </summary>
    /// <remarks>
    /// Only the heads that were actually charged appear. A zero row for a fee this seller does not
    /// pay is a line an accountant has to read and then discard, and it invites the question of
    /// whether it was meant to be zero.
    /// </remarks>
    private static DocumentTable Charges(CommissionInvoice invoice)
    {
        var rows = new List<IReadOnlyList<string>>();

        Add("Marketplace commission", invoice.Commission);
        Add("Marketplace fee", invoice.PlatformFee);
        Add("Payment gateway fee recharged", invoice.PaymentFee);

        var totals = new List<DocumentField>
        {
            new("Taxable value", Money(invoice.TaxableValue)),
        };

        // The split is stated by head, not summed into "GST". A recipient claiming input credit
        // files CGST, SGST and IGST separately, and a single figure would have to be taken apart by
        // hand against a rule the invoice never stated.
        if (invoice.IsInterState)
        {
            totals.Add(new DocumentField(
                $"IGST @ {Rate(invoice.GstRate)}",
                Money(invoice.Igst)));
        }
        else
        {
            totals.Add(new DocumentField($"CGST @ {Rate(invoice.GstRate / 2m)}", Money(invoice.Cgst)));
            totals.Add(new DocumentField($"SGST @ {Rate(invoice.GstRate / 2m)}", Money(invoice.Sgst)));
        }

        totals.Add(new DocumentField("Total payable", Money(invoice.Total)));

        return new DocumentTable(
            [
                new DocumentColumn("Description", 4),
                new DocumentColumn("SAC", 1, DocumentAlignment.Center),
                new DocumentColumn("Amount", 1.4, DocumentAlignment.Right),
            ],
            rows,
            totals);

        void Add(string description, decimal amount)
        {
            if (amount > 0m)
            {
                rows.Add([description, ServiceAccountingCode, Money(amount)]);
            }
        }
    }

    private static List<string> SupplierLines(LegalSettings legal)
    {
        var lines = new List<string>();

        AddIf(lines, legal.LegalEntityName);

        foreach (var line in AddressLines(legal.RegisteredAddress))
        {
            lines.Add(line);
        }

        AddIf(lines, legal.Gstin is { Length: > 0 } gstin ? $"GSTIN: {gstin}" : null);
        AddIf(lines, legal.Pan is { Length: > 0 } pan ? $"PAN: {pan}" : null);
        AddIf(lines, legal.Cin is { Length: > 0 } cin ? $"CIN: {cin}" : null);

        return lines.Count > 0 ? lines : ["The platform"];
    }

    private static List<string> RecipientLines(CommissionInvoice invoice, VendorPayoutProfile? vendor)
    {
        var lines = new List<string>();

        AddIf(lines, vendor?.LegalName ?? vendor?.DisplayName);
        AddIf(lines, vendor?.Code is { Length: > 0 } code ? $"Seller code: {code}" : null);

        AddIf(
            lines,
            invoice.RecipientGstin is { Length: > 0 } gstin
                ? $"GSTIN: {gstin}"
                : "GSTIN: not registered");

        AddIf(lines, vendor?.Pan is { Length: > 0 } pan ? $"PAN: {pan}" : null);

        return lines.Count > 0 ? lines : ["The seller"];
    }

    /// <summary>The registered address, one readable line per part, skipping what is not set.</summary>
    private static IEnumerable<string> AddressLines(PostalAddress address)
    {
        if (address is null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(address.Line1))
        {
            yield return address.Line1;
        }

        if (!string.IsNullOrWhiteSpace(address.Line2))
        {
            yield return address.Line2;
        }

        var locality = string.Join(
            " ",
            new[] { address.City, address.State, address.Pincode }.Where(part => !string.IsNullOrWhiteSpace(part)));

        if (locality.Length > 0)
        {
            yield return locality;
        }
    }

    /// <summary>The platform's own name, or a neutral stand-in on a deployment that has not set one.</summary>
    private static string SupplierName(LegalSettings legal)
        => string.IsNullOrWhiteSpace(legal.LegalEntityName) ? "the platform" : legal.LegalEntityName;

    /// <summary>Where a query about the invoice goes, in the order the settings prefer.</summary>
    private static string Contact(SupportSettings support, LegalSettings legal)
    {
        if (!string.IsNullOrWhiteSpace(support.Email))
        {
            return support.Email;
        }

        return SupplierName(legal);
    }

    private static void AddIf(List<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(value);
        }
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Money(decimal amount)
        => amount.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));

    private static string Rate(decimal percent)
        => percent.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Date(DateTimeOffset instant)
        => instant.ToOffset(FinancialYear.IndiaOffset).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
