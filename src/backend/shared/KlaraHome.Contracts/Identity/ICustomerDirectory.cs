namespace KlaraHome.Contracts.Identity;

/// <summary>What every other module is allowed to know about a shopper.</summary>
/// <remarks>
/// Deliberately the union of what Cart, Checkout, Orders and Notifications each need to address a
/// person and bill them, and nothing more. A module that wants their password state, their sessions
/// or their consent history is asking a question the Identity module answers through its own
/// endpoints, not one it exports.
/// </remarks>
/// <param name="Id">The account id, which is the <c>customer_id</c> other schemas store.</param>
/// <param name="DisplayName">The name to greet them by, or their mobile number when they gave none.</param>
/// <param name="Email">Their email address, or null. Personal data: masked in logs.</param>
/// <param name="Mobile">Their mobile number in E.164, or null.</param>
/// <param name="Gstin">Their default GST registration, for a B2B invoice.</param>
/// <param name="IsActive">Whether the account may still transact.</param>
public sealed record CustomerSummary(
    Guid Id,
    string DisplayName,
    string? Email,
    string? Mobile,
    string? Gstin,
    bool IsActive);

/// <summary>
/// One of a shopper's saved addresses, in the shape a checkout snapshot and a shipping label need.
/// </summary>
/// <remarks>
/// Every field is copied onto the checkout session rather than referenced, so an address the
/// shopper edits after placing an order does not rewrite where the parcel was promised
/// (docs/03-database-design.md §4.7).
/// </remarks>
/// <param name="Id">The address.</param>
/// <param name="Label">What the customer calls it: Home, Office.</param>
/// <param name="RecipientName">Who the courier asks for at the door.</param>
/// <param name="Mobile">The delivery contact number in E.164.</param>
/// <param name="Line1">House or flat number and building.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark. Not decoration in India — couriers navigate by it.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">
/// The <c>platform.states</c> row. It decides the place of supply, so a wrong value here is a wrong
/// tax on the invoice rather than merely a late parcel.
/// </param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="Gstin">A GSTIN specific to this address, for a shipment billed to it.</param>
/// <param name="IsBusiness">Whether it is a business address; couriers price and time them differently.</param>
public sealed record CustomerAddress(
    Guid Id,
    string? Label,
    string RecipientName,
    string Mobile,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    string? Gstin,
    bool IsBusiness);

/// <summary>
/// Reads shoppers and their addresses from outside the Identity module
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// A checkout has an address id and needs an address; an order needs the person's name on the
/// invoice. Neither may join to <c>identity.addresses</c>, so this contract is the whole of their
/// access. It is the mirror of <c>IVendorDirectory</c> and <c>IProductCatalog</c>, and exists for
/// the same reason.
/// </para>
/// <para>
/// Every read is scoped to the owning customer, and that is not a convenience: an address is looked
/// up by <em>(customer, address)</em> rather than by id alone, so a caller that has somebody else's
/// address id still cannot ship to it.
/// </para>
/// </remarks>
public interface ICustomerDirectory
{
    /// <summary>The published facts about one shopper, or null when there is no such account.</summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One of a shopper's own addresses, or null when it is not theirs or does not exist. The two
    /// cases are deliberately the same answer: distinguishing them would confirm that somebody
    /// else's address id is real.
    /// </summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="addressId">The address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<CustomerAddress?> FindAddressAsync(
        Guid customerId,
        Guid addressId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A shopper's addresses, defaults first. Used by a checkout that wants to preselect one rather
    /// than ask a returning customer to choose again.
    /// </summary>
    /// <param name="customerId">The shopper.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IReadOnlyList<CustomerAddress>> ListAddressesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
}
