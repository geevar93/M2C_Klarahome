using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Shipping.Domain;

/// <summary>
/// The handover document for a batch of parcels (docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// What the courier's driver signs. One sheet lists every air waybill being collected at one address
/// on one day, and the signed copy is the platform's evidence that the parcels left — which is the
/// document that settles "the courier says they never received it".
/// </para>
/// <para>
/// It is a real row rather than a rendered report because the shipments point back at it. A parcel
/// that went out on Tuesday's manifest and vanished is traceable to the sheet, the address and the
/// driver; a manifest that existed only as a PDF would leave that as a filing exercise.
/// </para>
/// <para>
/// Closed once printed. A manifest that could still gain parcels after the driver signed it would be
/// evidence of nothing at all.
/// </para>
/// </remarks>
internal sealed class ShippingManifest : AggregateRoot<Guid>, ITenantScoped, IVendorScoped, IAuditable
{
    private ShippingManifest(Guid id, string reference, string courier, DateTimeOffset generatedAt)
        : base(id)
    {
        Reference = Guard.NotNullOrWhiteSpace(reference);
        Courier = Guard.NotNullOrWhiteSpace(courier);
        GeneratedAt = generatedAt;
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private ShippingManifest()
    {
        Reference = string.Empty;
        Courier = string.Empty;
    }

    /// <summary>The number printed on the sheet, which is what the driver and the office both quote.</summary>
    public string Reference { get; private set; }

    /// <summary>The courier collecting.</summary>
    public string Courier { get; private set; }

    /// <summary>The seller whose parcels these are.</summary>
    /// <inheritdoc />
    public Guid? VendorId { get; private set; }

    /// <summary>The address being collected from.</summary>
    public Guid? PickupLocationId { get; private set; }

    /// <summary>How many parcels are on the sheet.</summary>
    public int ShipmentCount { get; private set; }

    /// <summary>What they weigh together, in grams.</summary>
    public int TotalWeightGrams { get; private set; }

    /// <summary>The stored PDF, once one has been rendered.</summary>
    public Guid? FileId { get; private set; }

    /// <summary>The aggregator's own id for the handover, where it issues one.</summary>
    public string? ProviderManifestId { get; private set; }

    /// <summary>When the sheet was produced.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Who produced it.</summary>
    public Guid? GeneratedBy { get; private set; }

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

    /// <summary>Opens a handover sheet for a batch that has already been chosen.</summary>
    /// <param name="reference">The number printed on it.</param>
    /// <param name="courier">The courier collecting.</param>
    /// <param name="vendorId">The seller whose parcels these are.</param>
    /// <param name="pickupLocationId">The address being collected from.</param>
    /// <param name="shipmentCount">How many parcels.</param>
    /// <param name="totalWeightGrams">What they weigh together.</param>
    /// <param name="generatedBy">Who produced it.</param>
    /// <param name="generatedAt">When.</param>
    public static ShippingManifest Create(
        string reference,
        string courier,
        Guid? vendorId,
        Guid? pickupLocationId,
        int shipmentCount,
        int totalWeightGrams,
        Guid? generatedBy,
        DateTimeOffset generatedAt)
        => new(UuidV7.New(), reference, courier, generatedAt)
        {
            VendorId = vendorId,
            PickupLocationId = pickupLocationId,
            ShipmentCount = Math.Max(0, shipmentCount),
            TotalWeightGrams = Math.Max(0, totalWeightGrams),
            GeneratedBy = generatedBy,
        };

    /// <summary>Attaches the printable sheet and, where the aggregator issued one, its own id.</summary>
    /// <param name="fileId">The stored PDF.</param>
    /// <param name="providerManifestId">The aggregator's id for the handover.</param>
    public void Attach(Guid? fileId, string? providerManifestId)
    {
        FileId = fileId ?? FileId;
        ProviderManifestId = providerManifestId ?? ProviderManifestId;
    }
}
