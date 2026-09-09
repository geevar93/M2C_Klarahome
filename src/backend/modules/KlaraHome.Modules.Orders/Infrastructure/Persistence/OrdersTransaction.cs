using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// Runs a unit of work inside one database transaction, and commits it only if the work succeeded.
/// </summary>
/// <remarks>
/// <para>
/// Every write in this module that allocates a number has to go through here, and the reason is the
/// mechanism the numbers depend on. <see cref="Numbering.OrderNumbering"/> takes its counter row
/// with <c>SELECT … FOR UPDATE</c>, and a row lock lives exactly as long as the transaction that
/// took it. A handler that simply calls <c>SaveChangesAsync</c> has no transaction of its own, so
/// each statement runs in an implicit one: the lock is taken and released inside the
/// <em>select</em>, and two callers allocating at once both read the same value. The database
/// catches it — the series is uniquely indexed — but it catches it as a 500 on somebody's dispatch
/// rather than as the queue it should have been.
/// </para>
/// <para>
/// Rolling back on a failed <see cref="Result"/> is the other half. A refusal has to give the
/// number back, which is the entire reason the counter is a row rather than a Postgres sequence.
/// </para>
/// </remarks>
internal static class OrdersTransaction
{
    /// <summary>Runs work that produces a value, and commits it if it succeeded.</summary>
    /// <typeparam name="TValue">What the work produces.</typeparam>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="work">The writes to make. It must not save or commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result<TValue>> RunAsync<TValue>(
        OrdersDbContext context,
        Func<CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);

        var strategy = context.Database.CreateExecutionStrategy();
        Result<TValue> outcome = Application.OrdersErrors.NotFound("order");

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            outcome = await work(cancellationToken).ConfigureAwait(false);

            if (outcome.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return outcome;
    }

    /// <summary>Runs work that produces nothing but a verdict, and commits it if it succeeded.</summary>
    /// <param name="context">The Ordering data context.</param>
    /// <param name="work">The writes to make. It must not save or commit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Result> RunAsync(
        OrdersDbContext context,
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var outcome = await RunAsync<bool>(
                context,
                async token =>
                {
                    var done = await work(token).ConfigureAwait(false);

                    return done.IsSuccess ? Result.Success(true) : Result.Failure<bool>(done.Error);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error);
    }
}
