using System.Text.Json.Serialization;

namespace KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;

/// <summary>The note keys this platform sends to the gateway and reads back.</summary>
/// <remarks>
/// Notes are the only channel that survives the round trip through a hosted checkout, so they carry
/// the two identifiers a returning webhook needs to find its way home. Nothing personal goes in
/// them: they are stored by a third party and echoed into logs on both sides.
/// </remarks>
internal static class RazorpayNotes
{
    /// <summary>Our own collection id, so an event can be matched without a lookup by order id.</summary>
    public const string PaymentId = "kh_payment_id";

    /// <summary>The order number, which is what appears on a support call.</summary>
    public const string OrderNumber = "kh_order_number";
}

/// <summary>A page of results, as this gateway returns collections.</summary>
/// <typeparam name="TItem">What the page holds.</typeparam>
internal sealed class RazorpayList<TItem>
{
    /// <summary>How many the gateway says there are.</summary>
    public int Count { get; set; }

    /// <summary>The page.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<TItem>? Items { get; set; }
}

/// <summary>An order, as the gateway describes it.</summary>
internal sealed class RazorpayOrder
{
    /// <summary>The gateway's order id, which the checkout widget is opened with.</summary>
    public string? Id { get; set; }

    /// <summary>What it is for, in paise.</summary>
    public long Amount { get; set; }

    /// <summary>What has been paid against it, in paise.</summary>
    [JsonPropertyName("amount_paid")]
    public long AmountPaid { get; set; }

    /// <summary>ISO 4217 code.</summary>
    public string? Currency { get; set; }

    /// <summary>What we sent as the receipt: the order number.</summary>
    public string? Receipt { get; set; }

    /// <summary>The gateway's status word.</summary>
    public string? Status { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }

    /// <summary>What we sent in the notes, echoed back.</summary>
    public Dictionary<string, string>? Notes { get; set; }
}

/// <summary>
/// A payment, as the gateway describes it.
/// </summary>
/// <remarks>
/// Only the fields this platform is willing to keep are declared. The gateway returns more — card
/// tokens, issuer identifiers, the full virtual payment address — and the way to guarantee none of
/// it is ever written is to have nowhere for it to land (docs/07-security-compliance.md §4).
/// </remarks>
internal sealed class RazorpayPayment
{
    /// <summary>The gateway's payment id.</summary>
    public string? Id { get; set; }

    /// <summary>Its order id, when the payment was made against one.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    /// <summary>The gateway's status word: <c>created</c>, <c>authorized</c>, <c>captured</c>, <c>failed</c>.</summary>
    public string? Status { get; set; }

    /// <summary>Whether the money has been taken.</summary>
    public bool Captured { get; set; }

    /// <summary>The rail: <c>upi</c>, <c>card</c>, <c>netbanking</c>, <c>wallet</c>, <c>emi</c>.</summary>
    public string? Method { get; set; }

    /// <summary>What the payment is for, in paise.</summary>
    public long Amount { get; set; }

    /// <summary>What has gone back out of it, in paise.</summary>
    [JsonPropertyName("amount_refunded")]
    public long AmountRefunded { get; set; }

    /// <summary>ISO 4217 code.</summary>
    public string? Currency { get; set; }

    /// <summary>The bank, for a net-banking payment.</summary>
    public string? Bank { get; set; }

    /// <summary>The wallet, for a wallet payment.</summary>
    public string? Wallet { get; set; }

    /// <summary>The virtual payment address, for UPI. Only its domain half is ever stored.</summary>
    public string? Vpa { get; set; }

    /// <summary>Whether the gateway reported an international instrument.</summary>
    public bool? International { get; set; }

    /// <summary>The number of instalments, for EMI.</summary>
    [JsonPropertyName("emi_plan_months")]
    public int? EmiMonths { get; set; }

    /// <summary>The card network. Flattened out of the nested card object by the gateway on read.</summary>
    [JsonPropertyName("card_network")]
    public string? CardNetwork { get; set; }

    /// <summary>The last four digits of the card. Never more.</summary>
    [JsonPropertyName("card_last4")]
    public string? CardLast4 { get; set; }

    /// <summary>Credit, debit or prepaid.</summary>
    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }

    /// <summary>The gateway's refusal code.</summary>
    [JsonPropertyName("error_code")]
    public string? ErrorCode { get; set; }

    /// <summary>Its description of the refusal.</summary>
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }

    /// <summary>What we sent in the notes, echoed back.</summary>
    public Dictionary<string, string>? Notes { get; set; }
}

/// <summary>A refund, as the gateway describes it.</summary>
internal sealed class RazorpayRefund
{
    /// <summary>The gateway's refund id.</summary>
    public string? Id { get; set; }

    /// <summary>The payment it came out of.</summary>
    [JsonPropertyName("payment_id")]
    public string? PaymentId { get; set; }

    /// <summary>The order, where the gateway carries it.</summary>
    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    /// <summary>What went back, in paise.</summary>
    public long Amount { get; set; }

    /// <summary>The gateway's status word: <c>pending</c>, <c>processed</c>, <c>failed</c>.</summary>
    public string? Status { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }

    /// <summary>What we sent in the notes, echoed back.</summary>
    public Dictionary<string, string>? Notes { get; set; }
}

/// <summary>A settlement, as the gateway describes it.</summary>
internal sealed class RazorpaySettlement
{
    /// <summary>The gateway's settlement id.</summary>
    public string? Id { get; set; }

    /// <summary>What reached the bank, in paise.</summary>
    public long Amount { get; set; }

    /// <summary>What the gateway kept, in paise.</summary>
    public long Fees { get; set; }

    /// <summary>The GST on those fees, in paise.</summary>
    public long Tax { get; set; }

    /// <summary>The bank reference the money arrived under.</summary>
    public string? Utr { get; set; }

    /// <summary>The gateway's status word.</summary>
    public string? Status { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }
}

/// <summary>One line of a settlement reconciliation report.</summary>
internal sealed class RazorpaySettlementEntry
{
    /// <summary>The gateway's id for whatever the line is about.</summary>
    [JsonPropertyName("entity_id")]
    public string? EntityId { get; set; }

    /// <summary>What kind of movement it is: <c>payment</c>, <c>refund</c>, <c>adjustment</c>, <c>transfer</c>.</summary>
    [JsonPropertyName("type")]
    public string? EntityType { get; set; }

    /// <summary>The payment it concerns, where the line names one separately.</summary>
    [JsonPropertyName("payment_id")]
    public string? PaymentId { get; set; }

    /// <summary>The gross amount, in paise.</summary>
    public long Amount { get; set; }

    /// <summary>The gateway's fee, in paise.</summary>
    public long Fee { get; set; }

    /// <summary>The GST on that fee, in paise.</summary>
    public long Tax { get; set; }

    /// <summary>What left the merchant balance, in paise.</summary>
    public long Debit { get; set; }

    /// <summary>What entered it, in paise.</summary>
    public long Credit { get; set; }

    /// <summary>Unix seconds the movement settled.</summary>
    [JsonPropertyName("settled_at")]
    public long? SettledAt { get; set; }

    /// <summary>Unix seconds the movement was created.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }
}

/// <summary>The error body this gateway returns with a non-success status.</summary>
internal sealed class RazorpayErrorEnvelope
{
    /// <summary>The error.</summary>
    public RazorpayError? Error { get; set; }
}

/// <summary>What the gateway says went wrong.</summary>
internal sealed class RazorpayError
{
    /// <summary>Its own code for the class of failure.</summary>
    public string? Code { get; set; }

    /// <summary>A sentence. Often safe to show a shopper — "your card was declined by the bank".</summary>
    public string? Description { get; set; }

    /// <summary>Which field it objected to, for a request the gateway would not accept.</summary>
    public string? Field { get; set; }

    /// <summary>Whether the gateway blames the customer or itself.</summary>
    public string? Source { get; set; }
}

/// <summary>A webhook body.</summary>
internal sealed class RazorpayWebhook
{
    /// <summary>The gateway's id for the event, where the plan includes one.</summary>
    public string? Id { get; set; }

    /// <summary>What happened: <c>payment.captured</c>, <c>refund.processed</c>, and so on.</summary>
    public string? Event { get; set; }

    /// <summary>Unix seconds.</summary>
    [JsonPropertyName("created_at")]
    public long? CreatedAt { get; set; }

    /// <summary>What the event is about.</summary>
    public RazorpayWebhookPayload? Payload { get; set; }
}

/// <summary>The entities a webhook carries. Which of them are present depends on the event.</summary>
internal sealed class RazorpayWebhookPayload
{
    /// <summary>The payment, on a payment event.</summary>
    public RazorpayEntity<RazorpayPayment>? Payment { get; set; }

    /// <summary>The refund, on a refund event.</summary>
    public RazorpayEntity<RazorpayRefund>? Refund { get; set; }

    /// <summary>The order, on <c>order.paid</c>.</summary>
    public RazorpayEntity<RazorpayOrder>? Order { get; set; }
}

/// <summary>The gateway's one-key wrapper around every entity in a webhook payload.</summary>
/// <typeparam name="TEntity">What is wrapped.</typeparam>
internal sealed class RazorpayEntity<TEntity>
{
    /// <summary>The entity.</summary>
    public TEntity? Entity { get; set; }
}
