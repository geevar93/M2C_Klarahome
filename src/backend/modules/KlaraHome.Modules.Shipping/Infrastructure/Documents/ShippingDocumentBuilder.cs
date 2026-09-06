using System.Globalization;
using KlaraHome.Contracts.Documents;
using KlaraHome.Contracts.Orders;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Shipping.Domain;

namespace KlaraHome.Modules.Shipping.Infrastructure.Documents;

/// <summary>
/// Builds the two pieces of paper a dispatch produces.
/// </summary>
/// <remarks>
/// <para>
/// Both are fallbacks in the strict sense: where the aggregator publishes its own label or manifest,
/// that is what gets printed, because it carries the courier's barcode and routing marks. These are
/// what a deployment with no aggregator prints, and what an operator prints when the aggregator's
/// document cannot be fetched.
/// </para>
/// <para>
/// The label is 4×6 inches, which is the thermal format every Indian courier accepts, and the page
/// size is already in the shared document vocabulary for exactly this reason. It carries no barcode:
/// the renderer draws text, rules and tables and nothing else, so the air waybill is printed large
/// and read by a human. That is enough for a hand-booked parcel and is deliberately not enough for a
/// courier's own sortation — which is why an aggregator's label always wins when there is one.
/// </para>
/// <para>
/// Nothing here decides anything. Both methods are pure projections of a consignment and the order
/// behind it, which is what lets a reprint months later produce the same sheet.
/// </para>
/// </remarks>
internal static class ShippingDocumentBuilder
{
    /// <summary>Renders a shipping label for one parcel.</summary>
    /// <param name="shipment">The consignment.</param>
    /// <param name="view">The seller's part it came from, for the destination and the lines.</param>
    /// <param name="pickup">Where it is collected from, for the return address.</param>
    /// <param name="sellerName">The seller, as the shopper knows them.</param>
    public static DocumentDefinition Label(
        Shipment shipment,
        SubOrderFulfilmentView view,
        VendorPickupPoint? pickup,
        string? sellerName)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(view);

        var blocks = new List<DocumentBlock>
        {
            new DocumentHeading(
                shipment.Courier ?? "Shipping label",
                shipment.Awb is { Length: > 0 } awb ? $"AWB {awb}" : "Air waybill not yet assigned"),
            new DocumentRule(),
            new DocumentPartyRow(
            [
                new DocumentParty("Deliver to", AddressLines(view.Destination)),
                new DocumentParty(
                    "Return to",
                    pickup is null
                        ? [sellerName ?? "The seller"]
                        : PickupLines(pickup, sellerName)),
            ]),
            new DocumentSpacer(8),
            new DocumentFieldGrid(
            [
                new DocumentField("Order", view.OrderNumber),
                new DocumentField("Parcel", view.SubOrderNumber),
                new DocumentField("Weight", $"{shipment.WeightGrams} g"),
                new DocumentField(
                    "Payment",
                    shipment.IsCod
                        ? $"COLLECT {Amount(shipment.CodAmount ?? 0m, shipment.CurrencyCode)}"
                        : "PREPAID — do not collect"),
            ]),
            new DocumentSpacer(8),
            new DocumentTable(
            [
                new DocumentColumn("Item", 3),
                new DocumentColumn("SKU", 2),
                new DocumentColumn("Qty", 1, DocumentAlignment.Right),
            ],
            [
                .. shipment.Lines.Select(line => new[]
                {
                    line.Name,
                    line.Sku,
                    line.Quantity.ToString(CultureInfo.InvariantCulture),
                }),
            ]),
        };

        if (shipment.IsCod)
        {
            blocks.Add(new DocumentSpacer(6));
            blocks.Add(new DocumentParagraph(
                $"Cash on delivery: collect {Amount(shipment.CodAmount ?? 0m, shipment.CurrencyCode)} "
                + "before handing the parcel over.",
                Small: false));
        }

        return new DocumentDefinition(
            $"Label {shipment.Awb ?? view.SubOrderNumber}",
            blocks,
            DocumentPageSize.Label4x6,
            FooterText: view.OrderNumber,
            Author: sellerName);
    }

    /// <summary>Renders the handover sheet a courier's driver signs.</summary>
    /// <param name="manifest">The sheet.</param>
    /// <param name="shipments">The parcels on it.</param>
    /// <param name="pickup">Where they are being collected from.</param>
    /// <param name="sellerName">The seller handing them over.</param>
    public static DocumentDefinition Manifest(
        ShippingManifest manifest,
        IReadOnlyList<Shipment> shipments,
        VendorPickupPoint? pickup,
        string? sellerName)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(shipments);

        return new DocumentDefinition(
            $"Manifest {manifest.Reference}",
            [
                new DocumentHeading("Pickup manifest", manifest.Reference),
                new DocumentFieldGrid(
                [
                    new DocumentField("Courier", manifest.Courier),
                    new DocumentField(
                        "Date",
                        manifest.GeneratedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
                    new DocumentField("Seller", sellerName ?? "—"),
                    new DocumentField("Parcels", manifest.ShipmentCount.ToString(CultureInfo.InvariantCulture)),
                ]),
                new DocumentSpacer(6),
                new DocumentPartyRow(
                [
                    new DocumentParty(
                        "Collect from",
                        pickup is null ? [sellerName ?? "The seller"] : PickupLines(pickup, sellerName)),
                ]),
                new DocumentSpacer(6),
                new DocumentTable(
                    [
                        new DocumentColumn("AWB", 2),
                        new DocumentColumn("Order", 2),
                        new DocumentColumn("Destination", 2),
                        new DocumentColumn("Weight (g)", 1, DocumentAlignment.Right),
                        new DocumentColumn("COD", 1, DocumentAlignment.Right),
                    ],
                    [
                        .. shipments.Select(shipment => new[]
                        {
                            shipment.Awb ?? "—",
                            shipment.OrderNumber,
                            shipment.DestinationPincode,
                            shipment.WeightGrams.ToString(CultureInfo.InvariantCulture),
                            shipment.CodAmount is { } cash and > 0m
                                ? Amount(cash, shipment.CurrencyCode)
                                : "—",
                        }),
                    ],
                    [
                        new DocumentField(
                            "Total parcels",
                            shipments.Count.ToString(CultureInfo.InvariantCulture)),
                        new DocumentField(
                            "Total weight",
                            $"{shipments.Sum(shipment => shipment.WeightGrams)} g"),
                        new DocumentField(
                            "Total to collect",
                            Amount(
                                shipments.Sum(shipment => shipment.CodAmount ?? 0m),
                                shipments.Count > 0 ? shipments[0].CurrencyCode : "INR")),
                    ]),
                new DocumentSpacer(18),

                // The point of the sheet. Everything above it is a list; this is the evidence that
                // the parcels left, and it is what settles a courier saying they never had them.
                new DocumentParagraph(
                    "Received the parcels listed above in good condition.",
                    Small: false),
                new DocumentSpacer(24),
                new DocumentFieldGrid(
                    [
                        new DocumentField("Driver name", "____________________"),
                        new DocumentField("Signature", "____________________"),
                        new DocumentField("Vehicle", "____________________"),
                        new DocumentField("Time", "____________________"),
                    ],
                    Columns: 2),
            ],
            DocumentPageSize.A4Portrait,
            FooterText: $"{manifest.Courier} · {manifest.Reference}",
            Author: sellerName);
    }

    private static List<string> AddressLines(FulfilmentAddress address)
    {
        var lines = new List<string> { address.Name, address.Line1 };

        if (!string.IsNullOrWhiteSpace(address.Line2))
        {
            lines.Add(address.Line2);
        }

        if (!string.IsNullOrWhiteSpace(address.Landmark))
        {
            lines.Add($"Near {address.Landmark}");
        }

        lines.Add($"{address.City} {address.Pincode}");

        if (!string.IsNullOrWhiteSpace(address.Mobile))
        {
            lines.Add(address.Mobile);
        }

        return lines;
    }

    private static List<string> PickupLines(VendorPickupPoint pickup, string? sellerName)
    {
        var lines = new List<string> { sellerName ?? pickup.Label, pickup.Line1 };

        if (!string.IsNullOrWhiteSpace(pickup.Line2))
        {
            lines.Add(pickup.Line2);
        }

        lines.Add($"{pickup.City} {pickup.Pincode}");
        lines.Add(pickup.ContactPhone);

        return lines;
    }

    private static string Amount(decimal value, string currencyCode)
        => string.Create(CultureInfo.InvariantCulture, $"{currencyCode} {value:0.00}");
}
