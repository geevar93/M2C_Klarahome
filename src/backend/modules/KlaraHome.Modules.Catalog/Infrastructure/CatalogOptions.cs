using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Catalog.Infrastructure;

/// <summary>
/// How this deployment runs its catalogue.
/// </summary>
/// <remarks>
/// Configuration rather than store settings, deliberately, and the split is the same one the
/// Vendors module draws: an operator's compliance posture and the platform's own resource limits
/// live here, where a shopkeeper cannot lower them from an admin screen; the commercial decisions —
/// the buy-box rule, the return window — are store settings and belong to the Platform module.
/// </remarks>
internal sealed class CatalogOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Catalog";

    /// <summary>The prefix of a generated SKU, before the sequence — <c>SKU-000017</c>.</summary>
    [RegularExpression("^[A-Z][A-Z0-9]{1,7}$")]
    public string SkuPrefix { get; set; } = "SKU";

    /// <summary>
    /// Whether a seller's own product must be approved by the platform before it goes live.
    /// </summary>
    /// <remarks>
    /// On by default. An unmoderated marketplace publishes whatever a seller types, including the
    /// counterfeit and the mis-declared HSN code, and the platform carries the liability for both.
    /// </remarks>
    public bool RequireModeration { get; set; } = true;

    /// <summary>
    /// Whether the India mandatory disclosures are enforced before a product may be published.
    /// </summary>
    /// <remarks>
    /// On, and it should stay on: Legal Metrology and the Consumer Protection (E-Commerce) Rules
    /// 2020 make MRP, net quantity, country of origin and the manufacturer's details a condition of
    /// offering the goods, not a nice-to-have. The switch exists for seeding a demo catalogue, and
    /// turning it off in production is a decision the operator is knowingly taking.
    /// </remarks>
    public bool EnforceComplianceOnPublish { get; set; } = true;

    /// <summary>Whether a listing may be published while its seller is not trading.</summary>
    /// <remarks>
    /// Off. "A Listing cannot be Active if its Vendor is not Active"
    /// (docs/02-domain-model.md §4.1) — a purchasable offer nobody can fulfil is a cancellation
    /// waiting to be explained to a customer.
    /// </remarks>
    public bool AllowListingsFromInactiveVendors { get; set; }

    /// <summary>The largest import file this deployment will accept, in bytes.</summary>
    [Range(1024, 268_435_456)]
    public long MaxImportBytes { get; set; } = 33_554_432;

    /// <summary>The most data rows one import may carry.</summary>
    [Range(1, 200_000)]
    public int MaxImportRows { get; set; } = 20_000;

    /// <summary>Whether the worker in this host drains the catalogue job queue.</summary>
    /// <remarks>
    /// False in the API and true in the worker, exactly as the notification dispatcher is
    /// configured. Every API replica running the same loop would multiply the polling and contend
    /// for the same rows for no gain.
    /// </remarks>
    public bool JobRunnerEnabled { get; set; }

    /// <summary>Seconds between polls of the job queue.</summary>
    [Range(1, 300)]
    public int JobPollIntervalSeconds { get; set; } = 10;

    /// <summary>How many times a job is picked up before it is abandoned.</summary>
    [Range(1, 10)]
    public int MaxJobAttempts { get; set; } = 3;
}
