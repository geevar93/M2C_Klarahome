using KlaraHome.Modules.Catalog.Domain;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The bookkeeping a bulk job keeps about itself.
/// </summary>
/// <remarks>
/// The claim half of the queue — that two workers polling at the same instant never take the same
/// row — needs an engine and is proved by
/// <c>CatalogConstraintTests.A_queued_job_is_claimed_by_exactly_one_worker</c>. What is left is
/// arithmetic over a few fields, and it belongs here.
/// </remarks>
public sealed class CatalogJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A retry starts from zero rather than from where the failed attempt got to.</summary>
    /// <remarks>
    /// An import that got a third of the way through before storage went away would otherwise begin
    /// its retry with that third already counted, and the report would tell the merchandiser it had
    /// applied twice as many rows as it did. Re-running from the top is correct because the rows
    /// themselves are idempotent — a SKU that exists is updated — so the counters are the only
    /// thing that has to be put back.
    /// </remarks>
    [Fact]
    public void A_requeued_job_resets_its_counters_but_keeps_its_attempts()
    {
        var job = CatalogJob.QueueImport("catalogue.csv", "imports/catalogue.csv", vendorId: null);

        job.Start(Now);
        job.CountRows(300);

        for (var index = 0; index < 100; index++)
        {
            job.RowSucceeded();
        }

        job.RowFailed(new ImportRowError(102, "sku", "KH-1", "A SKU is required."));

        Assert.Equal(1, job.Attempts);
        Assert.Equal(101, job.ProcessedRows);
        Assert.Equal(100, job.SucceededRows);
        Assert.Equal(1, job.FailedRows);
        Assert.Single(job.Errors);

        job.Requeue();

        Assert.Equal(CatalogJobStatus.Queued, job.Status);
        Assert.Equal(0, job.ProcessedRows);
        Assert.Equal(0, job.SucceededRows);
        Assert.Equal(0, job.FailedRows);
        Assert.Empty(job.Errors);
        Assert.Null(job.StartedAt);

        // The attempt count is deliberately not reset: it is the budget the dispatcher spends
        // before abandoning the job, and clearing it would make a permanently failing import retry
        // for ever.
        Assert.Equal(1, job.Attempts);

        job.Start(Now.AddMinutes(1));
        Assert.Equal(2, job.Attempts);
    }

    /// <summary>The outcome is chosen from what actually happened, not from whether it ran.</summary>
    /// <remarks>
    /// The expected status arrives as its name rather than as the enum: <c>CatalogJobStatus</c> is
    /// internal to the Catalog module, and a public test method cannot take an internal parameter.
    /// </remarks>
    /// <param name="succeeded">How many rows applied.</param>
    /// <param name="failed">How many were rejected.</param>
    /// <param name="expected">The status the job should finish in.</param>
    [Theory]
    [InlineData(10, 0, "Succeeded")]
    [InlineData(9, 1, "PartiallySucceeded")]
    [InlineData(0, 10, "Failed")]
    public void A_finished_job_reports_the_outcome_its_rows_produced(
        int succeeded,
        int failed,
        string expected)
    {
        var job = CatalogJob.QueueImport("catalogue.csv", "imports/catalogue.csv", vendorId: null);

        job.Start(Now);
        job.CountRows(succeeded + failed);

        for (var index = 0; index < succeeded; index++)
        {
            job.RowSucceeded();
        }

        for (var index = 0; index < failed; index++)
        {
            job.RowFailed(new ImportRowError(index + 2, null, null, "Rejected."));
        }

        job.Complete(Now.AddMinutes(2));

        Assert.Equal(expected, job.Status.ToString());
        Assert.Equal(Now.AddMinutes(2), job.CompletedAt);
    }

    /// <summary>The report is capped, so a file of ten thousand bad rows is still readable.</summary>
    /// <remarks>
    /// The counters keep counting past the cap. A merchandiser needs to know that nine thousand
    /// rows failed even when only the first five hundred are listed, and a report that stopped
    /// counting where it stopped listing would understate the damage.
    /// </remarks>
    [Fact]
    public void The_report_is_capped_but_the_counters_are_not()
    {
        var job = CatalogJob.QueueImport("catalogue.csv", "imports/catalogue.csv", vendorId: null);

        job.Start(Now);

        var rows = CatalogJob.MaxReportedErrors + 50;
        job.CountRows(rows);

        for (var index = 0; index < rows; index++)
        {
            job.RowFailed(new ImportRowError(index + 2, null, null, "Rejected."));
        }

        Assert.Equal(CatalogJob.MaxReportedErrors, job.Errors.Count);
        Assert.Equal(rows, job.FailedRows);
        Assert.Equal(rows, job.ProcessedRows);
    }
}
