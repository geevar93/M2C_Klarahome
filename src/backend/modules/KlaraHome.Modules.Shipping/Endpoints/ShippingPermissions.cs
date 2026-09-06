namespace KlaraHome.Modules.Shipping.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another module, so the two lists are kept in step by a test
/// that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement every module since Media has used.
/// </para>
/// <para>
/// The storefront surface takes none. Serviceability is anonymous, because a shopper has to be able
/// to ask "do you deliver to my PIN code" before they have an account, and the answer reveals
/// nothing about anybody.
/// </para>
/// <para>
/// Five permissions, and the split is the split between four different jobs. A seller packs and
/// dispatches. Operations works failed deliveries. Whoever is diagnosing a courier integration
/// reads the webhook log. Finance agrees cash with a courier. And the rate card is a commercial
/// decision that is none of theirs.
/// </para>
/// </remarks>
internal static class ShippingPermissions
{
    /// <summary>List parcels and read one, with everywhere it has been.</summary>
    /// <remarks>
    /// A read of a delivery address and a telephone number, so it is a grant rather than something
    /// every operator has. It is what support needs to answer "where is my order" beyond what the
    /// order timeline already says.
    /// </remarks>
    public const string ShipmentRead = "shipping.shipment.read";

    /// <summary>
    /// Pack a parcel, weigh it, book it with a courier, print its label and hand it over.
    /// </summary>
    /// <remarks>
    /// The seller's daily work, and Operations doing it for them. It is one permission rather than
    /// five because they are one job: nobody packs a box they may not weigh.
    /// </remarks>
    public const string ShipmentManage = "shipping.shipment.manage";

    /// <summary>Work the failed-delivery queue.</summary>
    /// <remarks>
    /// Separate from dispatching because it is a different decision and usually a different person:
    /// this is the one that rings a customer, changes an address, or gives up and sends the goods
    /// back. A seller holds it for their own parcels; Operations holds it for everybody's.
    /// </remarks>
    public const string NdrManage = "shipping.ndr.manage";

    /// <summary>Edit the delivery map and the rate card.</summary>
    /// <remarks>
    /// What delivery costs is a commercial decision, and it is deliberately not the packer's. A
    /// seller holding it can only reach their own overrides — the vendor scope sees to that — and
    /// the platform-wide card is staff's.
    /// </remarks>
    public const string RateManage = "shipping.rate.manage";

    /// <summary>
    /// Read the courier webhook log and its dead-letter queue, replay an event, refresh
    /// serviceability, and record a courier's cash remittance.
    /// </summary>
    /// <remarks>
    /// The plumbing, and a different job from either packing or support: whoever holds this is
    /// diagnosing why this platform and a courier disagree. The raw payloads are behind it because
    /// they carry whatever the courier chose to put in them about a customer's address.
    /// </remarks>
    public const string CourierManage = "shipping.courier.manage";
}
