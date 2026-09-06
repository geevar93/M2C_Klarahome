using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Returns.Domain;

/// <summary>
/// A credit note against a seller's tax invoice (docs/03-database-design.md §4.11).
/// </summary>
/// <remarks>
/// <para>
/// The document that reduces a seller's output tax. Under section 34 of the CGST Act a supplier may
/// only reduce their liability by issuing one, and rule 53 asks for a consecutive serial number
/// unique within a financial year — so this carries its own gapless series, drawn per seller, and
/// not a number derived from the refund.
/// </para>
/// <para>
/// It is a separate aggregate from the refund on purpose. A refund and a credit note answer
/// different questions — "did the shopper get their money" and "did the seller stop owing tax on the
/// sale" — and they can legitimately disagree in time: a refund to store credit still reduces the
/// supply, and a refund the gateway has not yet processed does not un-issue a note.
/// </para>
/// <para>
/// It is never edited and never deleted. A note issued in error is followed by a debit note, which
/// is what the statute expects and what an audit can follow.
/// </para>
/// </remarks>
internal sealed class CreditNote : Entity<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private CreditNote(
        Guid id,
        Guid returnId,
        Guid orderId,
        Guid subOrderId,
        Guid vendorId,
        Guid customerId,
        string creditNoteNumber,
        string series,
        string financialYear,
        DateTimeOffset issuedAt)
        : base(id)
    {
        ReturnId = returnId;
        OrderId = orderId;
        SubOrderId = subOrderId;
        VendorId = vendorId;
        CustomerId = customerId;
        CreditNoteNumber = creditNoteNumber;
        Series = series;
        FinancialYear = financialYear;
        IssuedAt = issuedAt;
        CurrencyCode = Money.Inr;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CreditNote()
    {
        CreditNoteNumber = string.Empty;
        Series = string.Empty;
        FinancialYear = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <summary>The RMA that caused it.</summary>
    public Guid ReturnId { get; private set; }

    /// <summary>The order.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part.</summary>
    public Guid SubOrderId { get; private set; }

    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The shopper it is issued to.</summary>
    public Guid CustomerId { get; private set; }

    /// <summary>The tax invoice it credits, when the order knows one.</summary>
    /// <remarks>
    /// Nullable, because a credit note against a supply whose invoice was never rendered is still a
    /// credit note. Rule 53 requires the original invoice's number and date "where available", and
    /// refusing to issue one because a PDF is missing would leave the seller owing tax on goods they
    /// no longer have.
    /// </remarks>
    public Guid? InvoiceId { get; private set; }

    /// <summary>That invoice's number, repeated on the face of the note.</summary>
    public string? InvoiceNumber { get; private set; }

    /// <summary>Its gapless number, unique per seller per financial year.</summary>
    public string CreditNoteNumber { get; private set; }

    /// <summary>The series it was drawn from.</summary>
    public string Series { get; private set; }

    /// <summary>The Indian financial year it belongs to, as <c>2026-27</c>.</summary>
    public string FinancialYear { get; private set; }

    /// <summary>The two-digit GST code of the state the goods went to.</summary>
    public string? PlaceOfSupplyStateCode { get; private set; }

    /// <summary>Whether the supply was CGST + SGST rather than IGST.</summary>
    public bool IsIntraState { get; private set; }

    /// <summary>What the tax was computed on.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>Central GST being credited.</summary>
    public decimal Cgst { get; private set; }

    /// <summary>State GST being credited.</summary>
    public decimal Sgst { get; private set; }

    /// <summary>Integrated GST being credited.</summary>
    public decimal Igst { get; private set; }

    /// <summary>Compensation cess being credited.</summary>
    public decimal Cess { get; private set; }

    /// <summary>The note total, inclusive of tax.</summary>
    public decimal Total { get; private set; }

    /// <summary>ISO 4217 code every amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The stored PDF, or null when rendering was unavailable.</summary>
    public Guid? FileId { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? UpdatedBy { get; private set; }

    /// <summary>Every tax head together.</summary>
    public decimal TaxAmount => Cgst + Sgst + Igst + Cess;

    /// <summary>Raises a credit note.</summary>
    /// <param name="returnId">The RMA.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="subOrderId">The seller's part.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="customerId">The shopper.</param>
    /// <param name="creditNoteNumber">The allocated number.</param>
    /// <param name="series">The series it came from.</param>
    /// <param name="financialYear">The financial year.</param>
    /// <param name="issuedAt">When.</param>
    public static CreditNote Issue(
        Guid returnId,
        Guid orderId,
        Guid subOrderId,
        Guid vendorId,
        Guid customerId,
        string creditNoteNumber,
        string series,
        string financialYear,
        DateTimeOffset issuedAt)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(returnId),
            Guard.NotEmpty(orderId),
            Guard.NotEmpty(subOrderId),
            Guard.NotEmpty(vendorId),
            Guard.NotEmpty(customerId),
            Guard.NotNullOrWhiteSpace(creditNoteNumber),
            Guard.NotNullOrWhiteSpace(series),
            Guard.NotNullOrWhiteSpace(financialYear),
            issuedAt);

    /// <summary>Names the invoice being credited.</summary>
    /// <param name="invoiceId">The invoice.</param>
    /// <param name="invoiceNumber">Its number.</param>
    public void Credits(Guid? invoiceId, string? invoiceNumber)
    {
        InvoiceId = invoiceId;
        InvoiceNumber = invoiceNumber;
    }

    /// <summary>Records the amounts and how the tax splits.</summary>
    /// <param name="placeOfSupplyStateCode">The two-digit GST code of the destination state.</param>
    /// <param name="isIntraState">Whether the supply was intra-state.</param>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="cgst">Central GST.</param>
    /// <param name="sgst">State GST.</param>
    /// <param name="igst">Integrated GST.</param>
    /// <param name="cess">Compensation cess.</param>
    /// <param name="total">The total, inclusive of tax.</param>
    /// <param name="currencyCode">The currency.</param>
    public void Tax(
        string? placeOfSupplyStateCode,
        bool isIntraState,
        decimal taxableValue,
        decimal cgst,
        decimal sgst,
        decimal igst,
        decimal cess,
        decimal total,
        string currencyCode)
    {
        PlaceOfSupplyStateCode = placeOfSupplyStateCode;
        IsIntraState = isIntraState;
        TaxableValue = Guard.NotNegative(taxableValue);
        Cgst = Guard.NotNegative(cgst);
        Sgst = Guard.NotNegative(sgst);
        Igst = Guard.NotNegative(igst);
        Cess = Guard.NotNegative(cess);
        Total = Guard.NotNegative(total);
        CurrencyCode = currencyCode;
    }

    /// <summary>Attaches the rendered PDF.</summary>
    /// <param name="fileId">The stored file.</param>
    public void Attach(Guid? fileId) => FileId = fileId;
}
