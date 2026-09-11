using KlaraHome.Contracts.IntegrationEvents;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Search.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Builds a small, real catalogue through <see cref="CatalogScenario"/> and reaches the Search
/// module's own seam directly where the alternative is re-proving another module's own debt.
/// </summary>
/// <remarks>
/// <para>
/// <c>ListingPublished</c>, <c>ListingDeactivated</c> and <c>StockLevelChanged</c> are raised for
/// real by Catalog and Inventory the moment a test opens or withdraws an offer, or adjusts stock, so
/// this scenario drains the real outbox for those. <c>PriceChanged</c> comes from a price list Step
/// 12 owns and <c>SubOrderConfirmed</c> from an order Steps 11–14 own; driving either the whole way
/// to prove Step 19's own debt would mean standing up a second module's journey to test a third
/// module's handler. <see cref="DispatchAsync{TEvent}"/> instead resolves the real, unmodified
/// <c>SearchProjectionHandlers</c> from the host's own container and calls it directly — the same
/// split Step 18's <c>SettlementScenario</c> used for <c>IOrderSettlement</c>, and for the same
/// reason: what is being proved is what Search does with the fact, not whether another module can
/// produce one.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="factory">The host under test, for its container and its outbox.</param>
/// <param name="database">Direct SQL, for indexing ground truth and staged setup.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class SearchScenario(
    HttpClient admin,
    CommerceApiFactory factory,
    Sql database,
    CancellationToken cancellationToken)
{
    private Task<OnboardedVendor>? _defaultSeller;

    /// <summary>The catalogue builder this scenario stands on.</summary>
    public CatalogScenario Catalog { get; } = new(admin, cancellationToken);

    /// <summary>
    /// One onboarded seller, created once and reused for every offer this scenario opens that does
    /// not care which seller it is.
    /// </summary>
    /// <remarks>
    /// A listing always belongs to a real seller — platform staff creating one must name a vendor,
    /// exactly as a seller creating their own is scoped to themselves — so a test proving the query
    /// engine or the projection, rather than a multi-seller buy box, needs exactly one of these
    /// rather than a fresh onboarding per offer.
    /// </remarks>
    public Task<OnboardedVendor> DefaultSellerAsync()
        => _defaultSeller ??= new VendorScenario(admin, cancellationToken).ActiveAsync();

    /// <summary>Runs the real outbox dispatcher, so a listing or stock event Catalog or Inventory raised reaches Search.</summary>
    public Task DrainAsync() => OutboxDrain.RunAsync(factory, database, cancellationToken);

    /// <summary>Dispatches an event straight to the real, unmodified handler, in a fresh scope.</summary>
    /// <remarks>
    /// Resolves <c>SearchProjectionHandlers</c> itself rather than
    /// <c>IIntegrationEventHandler&lt;TEvent&gt;</c> generically. Several of these events have more
    /// than one subscriber across the platform — <c>PriceChanged</c> also reaches Reviews'
    /// price-drop alert, <c>SubOrderConfirmed</c> reaches Payments, Reporting and Shipping too — and
    /// the production dispatcher resolves every one of them; a single <c>GetRequiredService</c> here
    /// would hand back whichever module happened to register last, silently exercising the wrong
    /// handler with no exception and no effect on the search index at all.
    /// </remarks>
    /// <typeparam name="TEvent">The event's type. Must be registered by <c>SearchModule</c>.</typeparam>
    /// <param name="integrationEvent">The event, exactly as the owning module would have raised it.</param>
    public async Task DispatchAsync<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        using var scope = factory.Services.CreateScope();

        var handlers = scope.ServiceProvider.GetRequiredService<SearchProjectionHandlers>();

        if (handlers is not IIntegrationEventHandler<TEvent> handler)
        {
            throw new InvalidOperationException(
                $"SearchProjectionHandlers does not implement IIntegrationEventHandler<{typeof(TEvent).Name}>.");
        }

        await handler.HandleAsync(integrationEvent, cancellationToken);
    }

    /// <summary>Reads one indexed row, or null when nothing has been written for the variant yet.</summary>
    /// <param name="variantId">The variant.</param>
    public async Task<IReadOnlyDictionary<string, object?>?> RowAsync(Guid variantId)
    {
        var rows = await database.RowsAsync(
            "SELECT * FROM search.product_search_projection WHERE variant_id = $1",
            cancellationToken,
            variantId);

        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>
    /// Polls until a variant is indexed, or fails the test.
    /// </summary>
    /// <remarks>
    /// The event pipeline in this host runs synchronously inside <see cref="DrainAsync"/> or
    /// <see cref="DispatchAsync{TEvent}"/>, so in practice this returns on the first check; the poll
    /// exists so a slow CI runner fails with a clear message rather than a null-reference three lines
    /// later.
    /// </remarks>
    /// <param name="variantId">The variant.</param>
    public async Task<IReadOnlyDictionary<string, object?>> WaitForRowAsync(Guid variantId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await RowAsync(variantId).ConfigureAwait(false) is { } row)
            {
                return row;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Variant {variantId} was never indexed.");
    }
}
