using System.ComponentModel.DataAnnotations;

namespace KlaraHome.Modules.Returns.Infrastructure;

/// <summary>
/// What the Returns module reads from configuration.
/// </summary>
/// <remarks>
/// Deliberately short, and none of it is policy. How long a shopper has, who pays the freight, what
/// is auto-approved and what becomes of the goods are all business decisions and all live in the
/// <c>returns</c> settings section, where an operator edits them without a deploy. What is left here
/// is the shape of the numbers and the cadence of a background job — the two things that genuinely
/// belong to a deployment.
/// </remarks>
internal sealed class ReturnsOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Returns";

    /// <summary>The prefix on an RMA number, as <c>RMA-2609-000184</c>.</summary>
    [StringLength(8, MinimumLength = 1)]
    public string ReturnNumberPrefix { get; set; } = "RMA";

    /// <summary>How many digits an RMA number's counter is padded to.</summary>
    [Range(4, 10)]
    public int ReturnNumberDigits { get; set; } = 6;

    /// <summary>How many digits a credit note's counter is padded to.</summary>
    [Range(4, 10)]
    public int CreditNoteDigits { get; set; } = 5;

    /// <summary>
    /// Whether the sweep that chases stale returns runs in this process.
    /// </summary>
    /// <remarks>
    /// Off in the API and on in the worker, exactly as every sweeper before it. A job that ran in
    /// both would do its work twice and compete for the same rows.
    /// </remarks>
    public bool SweeperEnabled { get; set; }

    /// <summary>How often the sweep runs.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>How many returns one sweep looks at.</summary>
    [Range(1, 1000)]
    public int SweepBatchSize { get; set; } = 100;

    /// <summary>The largest page any list endpoint in this module will return.</summary>
    [Range(1, 200)]
    public int MaxPageSize { get; set; } = 50;
}
