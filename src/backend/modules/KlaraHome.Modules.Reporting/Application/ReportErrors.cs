using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Modules.Reporting.Application;

/// <summary>
/// Every failure this module reports, with the stable code the frontend switches on
/// (docs/04-api-specification.md §1.2).
/// </summary>
/// <remarks>
/// A short list, because there is very little a report can refuse. It cannot fail a business rule —
/// it has none — so what is left is a caller asking for a report that does not exist, a period that
/// is not a period, or a report they are not entitled to see.
/// </remarks>
internal static class ReportErrors
{
    /// <summary>No report answers to that key.</summary>
    /// <param name="key">What the caller asked for.</param>
    public static Error UnknownReport(string? key)
        => Error.NotFound("REPORT_UNKNOWN", $"There is no report called '{key}'.");

    /// <summary>The schedule or run does not exist.</summary>
    /// <param name="what">What was being looked for.</param>
    public static Error NotFound(string what)
        => Error.NotFound("REPORTING_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The period ends before it starts.</summary>
    public static Error InvalidPeriod { get; } =
        Error.Validation("REPORT_INVALID_PERIOD", "The period must end after it starts.");

    /// <summary>The period is longer than one query may cover.</summary>
    /// <param name="days">How many days one report may span.</param>
    public static Error PeriodTooLong(int days)
        => Error.Validation("REPORT_PERIOD_TOO_LONG", $"A report covers at most {days} days at a time.");

    /// <summary>
    /// A seller asked for a report that is about the platform rather than about a seller.
    /// </summary>
    /// <remarks>
    /// A 403 rather than a 404, and the distinction is deliberate here where it is not elsewhere in
    /// this codebase: the report's existence is public — it is in the catalogue this same caller can
    /// list — so pretending it does not exist would be a refusal they could disprove in one request.
    /// </remarks>
    public static Error NotAvailableToVendors { get; } =
        Error.Forbidden(
            "REPORT_NOT_VENDOR_SCOPED",
            "That report covers the whole store and is not available to sellers.");

    /// <summary>The run has not produced a file, so there is nothing to download.</summary>
    public static Error NothingToDownload { get; } =
        Error.Validation("REPORT_NO_FILE", "That report run produced no file.");

    /// <summary>Object storage is not configured, so an export cannot be stored or served.</summary>
    /// <remarks>
    /// A 503 rather than a 500. It is a dependency that is not there — the same shape the payment and
    /// courier adapters answer with when their credentials are blank — and an operator reading it
    /// should go and look at the configuration rather than at a stack trace.
    /// </remarks>
    public static Error StorageUnavailable { get; } =
        Error.Unavailable(
            "REPORT_STORAGE_UNAVAILABLE",
            "Object storage is not configured, so reports cannot be exported.");
}
