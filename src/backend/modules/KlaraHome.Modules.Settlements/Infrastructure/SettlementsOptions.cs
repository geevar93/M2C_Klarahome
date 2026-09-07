using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Settlements.Infrastructure;

/// <summary>
/// What the Settlements module reads from configuration.
/// </summary>
/// <remarks>
/// Deliberately short, and none of it is policy. How often a seller is settled, what the platform
/// keeps, and the rates at which tax is collected and deducted at source are all business decisions
/// and all live in the <c>settlements</c> settings section, where an operator edits them without a
/// deploy. What is left here is the shape of a reference, the cadence of a background job and the
/// ceiling on a page — the three things that genuinely belong to a deployment.
/// </remarks>
internal sealed class SettlementsOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Settlements";

    /// <summary>The prefix on a payout batch reference, as <c>PAY-2609-000042</c>.</summary>
    [StringLength(8, MinimumLength = 1)]
    public string PayoutReferencePrefix { get; set; } = "PAY";

    /// <summary>How many digits a payout reference's counter is padded to.</summary>
    [Range(4, 10)]
    public int PayoutReferenceDigits { get; set; } = 6;

    /// <summary>
    /// The prefix on the platform's own tax invoices, as <c>KHC/2026-27/000042</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same series as a seller's own invoices: they are raised by different
    /// suppliers to different recipients, and a shared series would put two GSTINs on one run of
    /// numbers.
    /// </remarks>
    [StringLength(8, MinimumLength = 1)]
    public string CommissionInvoicePrefix { get; set; } = "KHC";

    /// <summary>How many digits a commission invoice's counter is padded to.</summary>
    [Range(4, 10)]
    public int CommissionInvoiceDigits { get; set; } = 6;

    /// <summary>
    /// Whether the cycle scheduler runs in this process.
    /// </summary>
    /// <remarks>
    /// Off in the API and on in the worker, exactly as every sweeper before it. A scheduler that ran
    /// in both would open two cycles for the same seller and the same period, and only the unique
    /// index would stop the second one — which is a safety net, not a design.
    /// </remarks>
    public bool SchedulerEnabled { get; set; }

    /// <summary>How often the scheduler looks for periods that are due to be closed, in minutes.</summary>
    /// <remarks>
    /// Hourly. A settlement period is drawn in days, so checking more often buys nothing; checking
    /// less often means a seller's statement appears at a time that depends on when the worker
    /// happened to restart.
    /// </remarks>
    [Range(1, 1440)]
    public int SchedulerIntervalMinutes { get; set; } = 60;

    /// <summary>How many sellers one scheduler pass settles.</summary>
    /// <remarks>
    /// A bound rather than a target. The pass runs again in an hour, and a run that tried to close
    /// every seller on a marketplace in one transaction would hold locks across the whole ledger.
    /// </remarks>
    [Range(1, 1000)]
    public int SchedulerBatchSize { get; set; } = 100;

    /// <summary>Whether the payout reconciliation sweep runs in this process.</summary>
    public bool ReconciliationEnabled { get; set; }

    /// <summary>How often in-flight transfers are re-read from the gateway, in minutes.</summary>
    /// <remarks>
    /// Fifteen, matching the payment reconciliation sweep. A bank transfer is not instant and a
    /// gateway's webhook for one can be lost, so the poll is the floor under the answer rather than
    /// the way it is usually learnt.
    /// </remarks>
    [Range(1, 1440)]
    public int ReconciliationIntervalMinutes { get; set; } = 15;

    /// <summary>How many in-flight transfers one sweep asks about.</summary>
    [Range(1, 500)]
    public int ReconciliationBatchSize { get; set; } = 50;

    /// <summary>
    /// How long a transfer may stay in flight before the sweep says so, in hours.
    /// </summary>
    /// <remarks>
    /// A payout that has neither succeeded nor failed for this long is a payout somebody has to
    /// chase. The sweep reports rather than repairs it: declaring a transfer failed because it is
    /// slow would free the cycle to be paid a second time.
    /// </remarks>
    [Range(1, 720)]
    public int StaleTransferHours { get; set; } = 24;

    /// <summary>The largest page any list endpoint in this module will return.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 50;

    /// <summary>The largest number of rows any report or statement export will produce.</summary>
    /// <remarks>
    /// An export is built in memory and sent as one file, so it needs a ceiling that a page size
    /// does not give it. Ten thousand rows is a large marketplace's month and a small CSV.
    /// </remarks>
    [Range(100, 100_000)]
    public int MaxExportRows { get; set; } = 10_000;
}

/// <summary>
/// Where vendor payouts are sent, and with what credentials
/// (docs/06-infrastructure-devops.md §4.1, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// A section of its own rather than a child of <see cref="SettlementsOptions"/>, so the environment
/// variables read <c>Payouts__Provider</c> and the gateway's own credentials stay under the
/// <c>Razorpay</c> section they were published under at Step 15. A deployment that has configured
/// payments has already supplied two of the three values a payout needs.
/// </para>
/// <para>
/// Every credential is blank by default and that is the shippable state, exactly as it is for the
/// gateway and the courier. A deployment with no payout account gets an adapter that reports itself
/// unusable and an endpoint that refuses with a named error — the batch is still built, the ledger
/// still says what is owed, and nothing pretends money moved.
/// </para>
/// </remarks>
internal sealed class PayoutOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Payouts";

    /// <summary>
    /// Which adapter sends the money: <c>route</c>, <c>x</c>, or blank for none.
    /// </summary>
    /// <remarks>
    /// Named rather than inferred from whichever adapter happens to have credentials. Route and X
    /// are both Razorpay and both configured from the same key pair, so registration order would
    /// otherwise decide which one a deployment used — and the two settle differently.
    /// </remarks>
    [StringLength(32)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The mode a direct bank payout is sent by: <c>IMPS</c>, <c>NEFT</c> or <c>RTGS</c>.
    /// </summary>
    /// <remarks>
    /// Read only by the RazorpayX adapter; Route transfers settle on the gateway's own schedule and
    /// take no mode. IMPS by default, because it clears at any hour and a seller waiting for money
    /// over a weekend is the complaint this avoids.
    /// </remarks>
    [StringLength(8)]
    public string Mode { get; set; } = "IMPS";

    /// <summary>
    /// The narration a seller sees on their bank statement.
    /// </summary>
    /// <remarks>
    /// Kept short and free of anything identifying beyond the store, because a bank truncates it and
    /// a truncated reference is worse than a generic one.
    /// </remarks>
    [StringLength(30)]
    public string Narration { get; set; } = "Marketplace payout";

    /// <summary>How many transfers one processing pass hands to the gateway.</summary>
    /// <remarks>
    /// A batch of five hundred sellers is five hundred HTTP calls, and a request that made them all
    /// would time out somewhere in the middle with no record of where. The pass is resumable: it
    /// sends what it can and the next pass picks up the items still pending.
    /// </remarks>
    [Range(1, 500)]
    public int SendBatchSize { get; set; } = 50;
}
