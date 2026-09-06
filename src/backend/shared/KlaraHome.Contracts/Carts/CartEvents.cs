using KlaraHome.Contracts.IntegrationEvents;

namespace KlaraHome.Contracts.Carts;

/// <summary>
/// A basket was left long enough to be treated as abandoned (docs/02-domain-model.md §6).
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of <em>abandoned-cart capture</em>: the row is retained rather than deleted,
/// and the fact is announced so a campaign can be built on it. Notifications sends the reminder,
/// Reporting counts the leakage, and neither of them may read the <c>carts</c> schema to find out.
/// </para>
/// <para>
/// Raised once per crossing rather than once per sweep — a cart that has already been marked
/// abandoned is not marked again — so a shopper who leaves one basket for a month is reminded
/// about it once and not thirty times.
/// </para>
/// </remarks>
/// <param name="CartId">The basket.</param>
/// <param name="CustomerId">The shopper, or null for a basket nobody signed in to. A campaign has
/// nowhere to send a reminder for an anonymous cart, and the null is what says so.</param>
/// <param name="LineCount">How many lines were left in it.</param>
/// <param name="EstimatedValue">What it was worth at the last price the cart saw, for prioritising.</param>
/// <param name="CurrencyCode">ISO 4217 code the value is in.</param>
/// <param name="LastActivityAt">When the shopper last touched it.</param>
public sealed record CartAbandoned(
    Guid CartId,
    Guid? CustomerId,
    int LineCount,
    decimal EstimatedValue,
    string CurrencyCode,
    DateTimeOffset LastActivityAt) : IntegrationEvent;

/// <summary>
/// A basket became an order.
/// </summary>
/// <remarks>
/// Published in the transaction that converts the cart, so a consumer reacting to it is reacting to
/// a basket that certainly closed. Reporting uses it for conversion; a reminder campaign uses it to
/// stop chasing a cart that has since been bought.
/// </remarks>
/// <param name="CartId">The basket.</param>
/// <param name="CheckoutSessionId">The session that closed it.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="OrderId">The order it became.</param>
/// <param name="OrderNumber">The human-readable number.</param>
/// <param name="GrandTotal">What the shopper agreed to pay.</param>
/// <param name="CurrencyCode">ISO 4217 code the total is in.</param>
public sealed record CartConverted(
    Guid CartId,
    Guid CheckoutSessionId,
    Guid CustomerId,
    Guid OrderId,
    string OrderNumber,
    decimal GrandTotal,
    string CurrencyCode) : IntegrationEvent;
