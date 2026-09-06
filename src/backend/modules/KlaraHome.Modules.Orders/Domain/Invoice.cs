using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Orders.Domain;

/// <summary>Where an invoice stands.</summary>
internal enum InvoiceStatus
{
    /// <summary>Raised and valid.</summary>
    Issued = 0,

    /// <summary>Wholly reversed by a credit note. The row stays; the number is never reused.</summary>
    Cancelled = 1,
}

/// <summary>
/// A seller's tax invoice for one sub-order (docs/02-domain-model.md §7.2).
/// </summary>
/// <remarks>
/// <para>
/// One per sub-order, never one per order, and that is a legal fact rather than a design choice: the
/// supply is made by the <em>seller</em>, under the seller's GSTIN, so a basket from two sellers is
/// two supplies and two invoices however it was paid for.
/// </para>
/// <para>
/// The number is gapless within a seller within a financial year, which is what rule 46 of the CGST
/// Rules requires. Gapless is the hard part: a Postgres sequence is fast and is <em>not</em> gapless,
/// because a rolled-back transaction keeps the number it consumed. The allocation therefore goes
/// through a counter row taken with <c>SELECT ... FOR UPDATE</c>, so a rollback gives the number back
/// (docs/03-database-design.md §4.8).
/// </para>
/// <para>
/// <see cref="Irn"/> and <see cref="QrPayload"/> are empty in v1 and are here rather than added
/// later on purpose: e-invoicing becomes mandatory at a turnover threshold this platform's tenants
/// will cross, and a schema that has nowhere to put the IRN needs a migration on the day it does.
/// </para>
/// </remarks>
internal sealed class Invoice : Entity<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private Invoice(
        Guid id,
        Guid orderId,
        Guid subOrderId,
        Guid vendorId,
        string invoiceNumber,
        string series,
        string financialYear,
        DateTimeOffset issuedAt)
        : base(id)
    {
        OrderId = orderId;
        SubOrderId = subOrderId;
        VendorId = vendorId;
        InvoiceNumber = invoiceNumber;
        Series = series;
        FinancialYear = financialYear;
        IssuedAt = issuedAt;
        Status = InvoiceStatus.Issued;
        CurrencyCode = Money.Inr;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private Invoice()
    {
        InvoiceNumber = string.Empty;
        Series = string.Empty;
        FinancialYear = string.Empty;
        CurrencyCode = Money.Inr;
    }

    /// <summary>The order, denormalised so an order's invoices are one query.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>The seller's part it covers. Unique — a sub-order is invoiced once.</summary>
    public Guid SubOrderId { get; private set; }

    /// <summary>The seller whose GSTIN it is raised under.</summary>
    public Guid? VendorId { get; private set; }

    /// <summary>The gapless number, unique per seller per financial year.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>The series the number was drawn from — the seller's code and the year.</summary>
    public string Series { get; private set; }

    /// <summary>The Indian financial year, April to March, as <c>2026-27</c>.</summary>
    public string FinancialYear { get; private set; }

    /// <summary>The GST state code of the place of supply, printed and reported on.</summary>
    public string? PlaceOfSupplyStateCode { get; private set; }

    /// <summary>Whether the supply was intra-state, and therefore CGST + SGST rather than IGST.</summary>
    public bool IsIntraState { get; private set; }

    /// <summary>What the tax was computed on.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>Central GST.</summary>
    public decimal Cgst { get; private set; }

    /// <summary>State GST.</summary>
    public decimal Sgst { get; private set; }

    /// <summary>Integrated GST.</summary>
    public decimal Igst { get; private set; }

    /// <summary>Compensation cess.</summary>
    public decimal Cess { get; private set; }

    /// <summary>The invoice total, inclusive of tax.</summary>
    public decimal Total { get; private set; }

    /// <summary>ISO 4217 code every figure on it is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The stored PDF, or null when rendering was unavailable when it was raised.</summary>
    public Guid? FileId { get; private set; }

    /// <summary>The Invoice Reference Number from the IRP, once e-invoicing is switched on.</summary>
    public string? Irn { get; private set; }

    /// <summary>The signed QR payload the IRP returns with the IRN.</summary>
    public string? QrPayload { get; private set; }

    /// <summary>Where it stands.</summary>
    public InvoiceStatus Status { get; private set; }

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

    /// <summary>Raises an invoice.</summary>
    /// <param name="orderId">The order.</param>
    /// <param name="subOrderId">The seller's part it covers.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="invoiceNumber">The allocated number.</param>
    /// <param name="series">The series it came from.</param>
    /// <param name="financialYear">The financial year.</param>
    /// <param name="issuedAt">When it was raised.</param>
    public static Invoice Issue(
        Guid orderId,
        Guid subOrderId,
        Guid vendorId,
        string invoiceNumber,
        string series,
        string financialYear,
        DateTimeOffset issuedAt)
        => new(
            UuidV7.New(),
            Guard.NotEmpty(orderId),
            Guard.NotEmpty(subOrderId),
            Guard.NotEmpty(vendorId),
            Guard.NotNullOrWhiteSpace(invoiceNumber),
            Guard.NotNullOrWhiteSpace(series),
            Guard.NotNullOrWhiteSpace(financialYear),
            issuedAt);

    /// <summary>Records the tax split the invoice states.</summary>
    /// <param name="placeOfSupplyStateCode">The GST state code the split was decided against.</param>
    /// <param name="isIntraState">Whether it was an intra-state supply.</param>
    /// <param name="taxableValue">What the tax was computed on.</param>
    /// <param name="cgst">Central GST.</param>
    /// <param name="sgst">State GST.</param>
    /// <param name="igst">Integrated GST.</param>
    /// <param name="cess">Compensation cess.</param>
    /// <param name="total">The invoice total.</param>
    /// <param name="currencyCode">ISO 4217 code the figures are in.</param>
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
        TaxableValue = taxableValue;
        Cgst = cgst;
        Sgst = sgst;
        Igst = igst;
        Cess = cess;
        Total = total;
        CurrencyCode = currencyCode;
    }

    /// <summary>Attaches the rendered PDF.</summary>
    /// <param name="fileId">The stored file.</param>
    public void Attach(Guid? fileId) => FileId = fileId;

    /// <summary>Records the IRN and QR payload once the IRP has returned them.</summary>
    /// <param name="irn">The Invoice Reference Number.</param>
    /// <param name="qrPayload">The signed QR payload.</param>
    public void Register(string? irn, string? qrPayload)
    {
        Irn = irn;
        QrPayload = qrPayload;
    }

    /// <summary>
    /// Marks the invoice reversed.
    /// </summary>
    /// <remarks>
    /// The row stays and the number is never reused: a gap in a statutory series is a defect, and so
    /// is a number that appears twice. The money is reversed by a credit note, which is the Returns
    /// module's from Step 17.
    /// </remarks>
    public void Cancel() => Status = InvoiceStatus.Cancelled;
}
