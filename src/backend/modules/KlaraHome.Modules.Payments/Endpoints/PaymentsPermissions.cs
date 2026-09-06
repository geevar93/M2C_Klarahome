namespace KlaraHome.Modules.Payments.Endpoints;

/// <summary>
/// The permissions this module's endpoints declare.
/// </summary>
/// <remarks>
/// <para>
/// Declared here and mirrored in the Identity module's permission catalogue, which is what the admin
/// UI lists and what a role grants. The duplication is deliberate and is what the module boundary
/// costs: a module may not reference another module, so the two lists are kept in step by a test
/// that asserts every permission an endpoint asks for appears in the catalogue — the same
/// arrangement Media, Vendors, Catalog, Inventory, Pricing, Cart and Orders use.
/// </para>
/// <para>
/// The storefront surface takes none. A shopper's authority over their own payment comes from the
/// token that names them, and every storefront handler resolves the order by
/// <em>(order, customer)</em> before it looks at any money.
/// </para>
/// <para>
/// Six permissions, and the split between them is the split between looking, repairing, giving money
/// back, signing that off, working the webhook log and handling cash. They are deliberately not one:
/// this is the module where a single over-broad grant is the difference between an operator who can
/// answer a support call and an operator who can move money out of the business.
/// </para>
/// </remarks>
internal static class PaymentsPermissions
{
    /// <summary>List payments and refunds, and read one in full.</summary>
    /// <remarks>
    /// A read of financial data about a named person, so it is a grant rather than something every
    /// operator has. It is what support needs to answer "did my payment go through".
    /// </remarks>
    public const string PaymentRead = "payments.payment.read";

    /// <summary>
    /// Re-read a payment from the gateway and apply what it says, and capture an authorised one.
    /// </summary>
    /// <remarks>
    /// The repair surface, and deliberately the <em>only</em> way a payment moves by hand. There is
    /// no endpoint anywhere that sets a payment's status directly: a platform where a human can
    /// declare an order paid is a platform where an order can be paid without money.
    /// </remarks>
    public const string PaymentManage = "payments.payment.manage";

    /// <summary>Raise a refund.</summary>
    /// <remarks>
    /// Separate from reading, because it is money leaving. Holding it does not mean a refund is sent:
    /// anything above the configured threshold still waits for <see cref="RefundApprove"/>.
    /// </remarks>
    public const string RefundInitiate = "payments.refund.initiate";

    /// <summary>
    /// Approve or refuse a refund that is above the threshold.
    /// </summary>
    /// <remarks>
    /// The checker half of maker-checker (docs/07-security-compliance.md §4). It is a separate
    /// permission precisely so that it can be granted to different people from
    /// <see cref="RefundInitiate"/> — granting both to one role does not defeat the control, because
    /// the handler and a check constraint both refuse a self-approval, but it does defeat the point.
    /// </remarks>
    public const string RefundApprove = "payments.refund.approve";

    /// <summary>
    /// Read the webhook log and its dead-letter queue, replay an event, import settlements and run
    /// reconciliation.
    /// </summary>
    /// <remarks>
    /// The plumbing, and a different job from either support or finance: whoever holds this is
    /// diagnosing why the platform and the gateway disagree. The raw payloads are behind it because
    /// they carry whatever the gateway chose to put in them about a customer's payment.
    /// </remarks>
    public const string GatewayManage = "payments.gateway.manage";

    /// <summary>Record cash taken at a door and a courier's remittance.</summary>
    /// <remarks>
    /// Operations work, and separate from the gateway surface because it is a different kind of
    /// money: nobody is reconciling an API here, they are agreeing a figure with a courier.
    /// </remarks>
    public const string CodManage = "payments.cod.manage";
}
