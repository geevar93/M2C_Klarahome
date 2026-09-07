using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Settlements.Domain;

/// <summary>
/// The tax invoice the platform raises on a seller for what it charged them
/// (docs/02-domain-model.md §7.2, docs/07-security-compliance.md §4).
/// </summary>
/// <remarks>
/// <para>
/// The other half of the money. A seller's own invoice is raised to the shopper for the goods; this
/// one is raised by the marketplace to the seller for commission, the marketplace fee and the
/// payment-gateway fee — and for the GST on all three, which the ledger has been recording as a
/// <c>platform_tax</c> entry since Step 18 with no document behind it. Without the document the
/// seller cannot claim input credit against tax they have already borne, which makes it a real cost
/// to them rather than a missing feature (Step 28B, deliverable 23).
/// </para>
/// <para>
/// One per settlement cycle, raised when the cycle closes and never edited afterwards. A cycle is
/// already the period over which the platform's charges are drawn together and agreed, so inventing
/// a second period for the invoice would produce two answers to what a seller was charged in
/// January.
/// </para>
/// <para>
/// The delivery fee is deliberately not on it. It is recharged at cost and its GST was charged by
/// the courier to the platform, so invoicing it again here would be charging tax on tax
/// (<c>SettlementCharges.Tax</c> excludes it for the same reason).
/// </para>
/// </remarks>
internal sealed class CommissionInvoice : AggregateRoot<Guid>, ITenantScoped, IAuditable
{
    private CommissionInvoice(
        Guid id,
        Guid settlementCycleId,
        Guid vendorId,
        string invoiceNumber,
        DateTimeOffset issuedAt,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string currencyCode)
        : base(id)
    {
        SettlementCycleId = Guard.NotEmpty(settlementCycleId);
        VendorId = Guard.NotEmpty(vendorId);
        InvoiceNumber = Guard.NotNullOrWhiteSpace(invoiceNumber);
        IssuedAt = issuedAt;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        CurrencyCode = Guard.NotNullOrWhiteSpace(currencyCode);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private CommissionInvoice()
    {
        InvoiceNumber = string.Empty;
        CurrencyCode = string.Empty;
    }

    /// <summary>The cycle whose charges this invoices.</summary>
    public Guid SettlementCycleId { get; private set; }

    /// <summary>The seller being charged.</summary>
    public Guid VendorId { get; private set; }

    /// <summary>The gapless, financial-year-scoped number. Statutory, and never reused.</summary>
    public string InvoiceNumber { get; private set; }

    /// <summary>When it was raised.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>The first instant of the period it covers.</summary>
    public DateTimeOffset PeriodStart { get; private set; }

    /// <summary>The last instant of the period it covers.</summary>
    public DateTimeOffset PeriodEnd { get; private set; }

    /// <summary>Commission charged on the cycle's sales, before tax.</summary>
    public decimal Commission { get; private set; }

    /// <summary>The marketplace fee, before tax.</summary>
    public decimal PlatformFee { get; private set; }

    /// <summary>The payment-gateway fee recharged to the seller, before tax.</summary>
    public decimal PaymentFee { get; private set; }

    /// <summary>What the GST was computed on: the three charges above.</summary>
    public decimal TaxableValue { get; private set; }

    /// <summary>The GST rate applied to platform services, as a percentage.</summary>
    public decimal GstRate { get; private set; }

    /// <summary>Central GST, on an intra-state supply.</summary>
    public decimal Cgst { get; private set; }

    /// <summary>State GST, on an intra-state supply.</summary>
    public decimal Sgst { get; private set; }

    /// <summary>Integrated GST, on an inter-state supply.</summary>
    public decimal Igst { get; private set; }

    /// <summary>What the invoice comes to, inclusive of tax.</summary>
    public decimal Total { get; private set; }

    /// <summary>ISO 4217 code every amount is in.</summary>
    public string CurrencyCode { get; private set; }

    /// <summary>The platform's own GSTIN, as it stood when the invoice was raised.</summary>
    public string? SupplierGstin { get; private set; }

    /// <summary>The seller's GSTIN, frozen. Absent for a seller who is not registered.</summary>
    public string? RecipientGstin { get; private set; }

    /// <summary>The two-digit GST state code the supply is made to.</summary>
    public string? PlaceOfSupplyStateCode { get; private set; }

    /// <summary>The rendered PDF, once one exists. Null when rendering failed and can be retried.</summary>
    public Guid? FileId { get; private set; }

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

    /// <summary>The tax on the invoice, however it was split.</summary>
    public decimal TaxTotal => Cgst + Sgst + Igst;

    /// <summary>Whether the supply was treated as inter-state.</summary>
    public bool IsInterState => Igst > 0m;

    /// <summary>Raises the invoice for a cycle's charges.</summary>
    /// <param name="settlementCycleId">The cycle.</param>
    /// <param name="vendorId">The seller.</param>
    /// <param name="invoiceNumber">The allocated number.</param>
    /// <param name="issuedAt">When it is raised.</param>
    /// <param name="periodStart">The first instant of the period.</param>
    /// <param name="periodEnd">The last instant of the period.</param>
    /// <param name="currencyCode">The currency.</param>
    public static CommissionInvoice Raise(
        Guid settlementCycleId,
        Guid vendorId,
        string invoiceNumber,
        DateTimeOffset issuedAt,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        string currencyCode)
        => new(
            UuidV7.New(),
            settlementCycleId,
            vendorId,
            invoiceNumber,
            issuedAt,
            periodStart,
            periodEnd,
            currencyCode);

    /// <summary>
    /// States what was charged and how the tax splits.
    /// </summary>
    /// <remarks>
    /// The split is passed in rather than derived here, because deciding intra- from inter-state
    /// needs both parties' GSTINs and the domain holds neither until it is told them. What this
    /// method does guarantee is that the total is the sum of its parts and not a fourth figure
    /// somebody computed separately.
    /// </remarks>
    /// <param name="commission">Commission before tax.</param>
    /// <param name="platformFee">Marketplace fee before tax.</param>
    /// <param name="paymentFee">Gateway fee before tax.</param>
    /// <param name="gstRate">The rate applied, as a percentage.</param>
    /// <param name="cgst">Central GST, or zero on an inter-state supply.</param>
    /// <param name="sgst">State GST, or zero on an inter-state supply.</param>
    /// <param name="igst">Integrated GST, or zero on an intra-state supply.</param>
    public void Charge(
        decimal commission,
        decimal platformFee,
        decimal paymentFee,
        decimal gstRate,
        decimal cgst,
        decimal sgst,
        decimal igst)
    {
        Commission = commission;
        PlatformFee = platformFee;
        PaymentFee = paymentFee;
        TaxableValue = commission + platformFee + paymentFee;
        GstRate = gstRate;
        Cgst = cgst;
        Sgst = sgst;
        Igst = igst;
        Total = TaxableValue + TaxTotal;
    }

    /// <summary>Freezes the two parties' tax identities and the place of supply.</summary>
    /// <param name="supplierGstin">The platform's GSTIN.</param>
    /// <param name="recipientGstin">The seller's, or null.</param>
    /// <param name="placeOfSupplyStateCode">The two-digit state code the supply is made to.</param>
    public void Identify(string? supplierGstin, string? recipientGstin, string? placeOfSupplyStateCode)
    {
        SupplierGstin = supplierGstin;
        RecipientGstin = recipientGstin;
        PlaceOfSupplyStateCode = placeOfSupplyStateCode;
    }

    /// <summary>Records the rendered document.</summary>
    /// <param name="fileId">The stored PDF.</param>
    public void Attach(Guid fileId) => FileId = fileId;
}
