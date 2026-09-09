using System.Net;
using KlaraHome.Contracts.Orders;
using KlaraHome.IntegrationTests.Database;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// The two place-order criteria that are about a collaborator misbehaving rather than refusing: an
/// attempt that <em>throws</em>, and an attempt that is still <em>running</em> when the same key
/// arrives again.
/// </summary>
/// <remarks>
/// <para>
/// Both need <see cref="IOrderPlacement"/> to do something on cue that nothing real can be asked to
/// do — block until told, or throw. The Ordering module underneath is the real one and does all of
/// its real work; the wrapper only decides when it is entered. A compensating release that runs on a
/// well-formed refusal and not on an exception is exactly the bug these rows exist to catch, and it
/// cannot be caught any other way.
/// </para>
/// <para>
/// Its own class, so the gate exists on one host and no other test can be affected by it.
/// </para>
/// </remarks>
public sealed class CheckoutPlacementFaultTests : CommerceTestBase
{
    /// <summary>What the injected failure says, so a rethrow can be recognised rather than guessed at.</summary>
    private const string Boom = "Ordering fell over while an integration test was watching.";

    private readonly GatedOrderPlacement _gate = new();

    /// <param name="fixture">The migrated database.</param>
    public CheckoutPlacementFaultTests(KlaraHomeSchemaFixture fixture)
        : base(fixture)
        => Factory.Overlays.Add(services => GatedOrderPlacement.Install(services, _gate));

    /// <summary>
    /// An attempt that throws releases every unit it held, hands the key back, and leaves the shopper
    /// able to try again.
    /// </summary>
    /// <remarks>
    /// The failure path that had no compensation before Step 29. Its two costs are separate and both
    /// bad: the units stay off sale until Inventory's sweeper expires the hold, and the placement row
    /// stays <c>InProgress</c> for ever, so every later press of <em>Pay</em> carrying the same key
    /// is answered with a conflict rather than with an order.
    /// </remarks>
    [Fact]
    public async Task An_attempt_that_throws_releases_its_holds_and_leaves_the_key_usable()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 199m, stock: 20);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 3);
        var cartId = cart.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);
        var key = CartScenario.NewIdempotencyKey("threw");

        HttpStatusCode? status;

        try
        {
            _gate.Before = _ => throw new InvalidOperationException(Boom);

            try
            {
                using var response = await scenario.PlaceOrderAsync(shopper, sessionId, key);
                status = response.StatusCode;
            }
            catch (InvalidOperationException exception) when (exception.Message == Boom)
            {
                // Whether the host turns an unhandled exception into a problem document or the test
                // server rethrows it is a pipeline detail. Either way this request produced no order.
                status = HttpStatusCode.InternalServerError;
            }
        }
        finally
        {
            _gate.Before = null;
        }

        Assert.Equal(HttpStatusCode.InternalServerError, status);

        // The units are back on sale rather than waiting on the reservation sweeper.
        Assert.Equal(0, await HeldAsync(cartId));

        Assert.Equal(
            0,
            await Database.ScalarAsync<int>(
                "SELECT quantity_reserved FROM inventory.stock_items WHERE id = $1",
                Cancellation,
                offer.StockItemId));

        // They really were held and really were given back.
        Assert.Equal(
            "Released",
            await Database.ScalarAsync<string>(
                "SELECT status FROM inventory.stock_reservations WHERE reference_type = 'cart' AND reference_id = $1",
                Cancellation,
                cartId));

        // The key is spent but not stuck: a failed attempt created no order, so it may be used again.
        var placement = Assert.Single(await Database.RowsAsync(
            "SELECT status, failure_code FROM carts.checkout_placements WHERE idempotency_key = $1",
            Cancellation,
            key));

        Assert.Equal("Failed", placement["status"]!.ToString());
        Assert.Equal("ORDER_PLACEMENT_FAILED", placement["failure_code"]!.ToString());

        Assert.Equal(
            "PaymentSet",
            await Database.ScalarAsync<string>(
                "SELECT status FROM carts.checkout_sessions WHERE id = $1",
                Cancellation,
                sessionId));

        Assert.Equal(0, await OrdersForAsync(cartId));

        // And the shopper presses Pay again, with the key their client already had.
        var placed = await ReadAsync(await scenario.PlaceOrderAsync(shopper, sessionId, key));

        Assert.False(string.IsNullOrWhiteSpace(placed.GetProperty("orderNumber").GetString()));
        Assert.Equal(1, await OrdersForAsync(cartId));
    }

    /// <summary>
    /// A key whose attempt is still running is refused with a conflict that says so, and the attempt
    /// it was competing with still produces its order.
    /// </summary>
    /// <remarks>
    /// The code matters as much as the refusal. A storefront switching on <c>CHECKOUT_CLOSED</c>
    /// sends the shopper back to their basket; <c>ORDER_PLACEMENT_IN_PROGRESS</c> tells it to wait,
    /// which is what is actually happening while the first request is inside Ordering.
    /// </remarks>
    [Fact]
    public async Task A_key_whose_attempt_is_still_running_is_refused_and_the_first_attempt_still_wins()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var scenario = new CartScenario(admin, Cancellation);

        var seller = await scenario.SellerAsync();
        var offer = await scenario.OfferAsync(seller, sellingPrice: 249m, stock: 20);

        var (shopper, _) = await SignedInShopperAsync();
        var address = await scenario.AddressAsync(shopper);

        var cart = await scenario.AddAsync(shopper, offer, quantity: 2);
        var cartId = cart.GetProperty("id").GetGuid();

        var sessionId = await scenario.ReadyCheckoutAsync(shopper, address);
        var key = CartScenario.NewIdempotencyKey("in-flight");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _gate.Before = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var running = scenario.PlaceOrderAsync(shopper, sessionId, key);

        try
        {
            // The first request is inside Ordering: its key is claimed and its stock is held.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(60), Cancellation);

            Assert.Equal(
                "InProgress",
                await Database.ScalarAsync<string>(
                    "SELECT status FROM carts.checkout_placements WHERE idempotency_key = $1",
                    Cancellation,
                    key));

            await RefusedAsync(
                await scenario.PlaceOrderAsync(shopper, sessionId, key),
                HttpStatusCode.Conflict,
                "ORDER_PLACEMENT_IN_PROGRESS");

            // A different key against the same running session is refused the same way, because the
            // session itself is not the shopper's to act on while an order is being created.
            await RefusedAsync(
                await scenario.PlaceOrderAsync(shopper, sessionId, CartScenario.NewIdempotencyKey("second-key")),
                HttpStatusCode.Conflict,
                "ORDER_PLACEMENT_IN_PROGRESS");
        }
        finally
        {
            release.TrySetResult();
            _gate.Before = null;
        }

        using var first = await running;
        var placed = await ReadAsync(first);

        Assert.False(string.IsNullOrWhiteSpace(placed.GetProperty("orderNumber").GetString()));

        // One order, and one claim on the key, however many requests arrived.
        Assert.Equal(1, await OrdersForAsync(cartId));

        Assert.Equal(
            1,
            await Database.CountAsync(
                "SELECT COUNT(*) FROM carts.checkout_placements WHERE checkout_session_id = $1",
                Cancellation,
                sessionId));
    }

    /// <summary>How many live holds this cart is keeping off sale.</summary>
    private Task<long> HeldAsync(Guid cartId)
        => Database.CountAsync(
            """
            SELECT COUNT(*) FROM inventory.stock_reservations
            WHERE reference_type = 'cart' AND reference_id = $1 AND status = 'Held'
            """,
            Cancellation,
            cartId);

    /// <summary>How many orders exist for one basket.</summary>
    private Task<long> OrdersForAsync(Guid cartId)
        => Database.CountAsync(
            "SELECT COUNT(*) FROM orders.orders WHERE cart_id = $1",
            Cancellation,
            cartId);

    /// <summary>
    /// Wraps the real <see cref="IOrderPlacement"/> so a test can decide when it is entered.
    /// </summary>
    /// <remarks>
    /// A wrapper rather than a replacement: everything Ordering does — the number, the sub-order
    /// split, the stock commit, the collection — is the production implementation, and it still runs.
    /// The only thing this controls is the moment before it starts.
    /// </remarks>
    private sealed class GatedOrderPlacement
    {
        /// <summary>What to do before Ordering is entered, or null to enter it straight away.</summary>
        public Func<PlaceOrderRequest, Task>? Before { get; set; }

        /// <summary>Puts this gate in front of whatever implementation is registered.</summary>
        /// <param name="services">The container.</param>
        /// <param name="gate">The gate to install.</param>
        public static void Install(IServiceCollection services, GatedOrderPlacement gate)
        {
            var registered = services.LastOrDefault(service => service.ServiceType == typeof(IOrderPlacement))
                             ?? throw new InvalidOperationException("No IOrderPlacement is registered.");

            services.Remove(registered);

            services.Add(ServiceDescriptor.Describe(
                typeof(IOrderPlacement),
                provider => new Gated(gate, Original(provider, registered)),
                registered.Lifetime));
        }

        /// <summary>Builds whatever the removed descriptor would have built.</summary>
        private static IOrderPlacement Original(IServiceProvider provider, ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationInstance is IOrderPlacement instance)
            {
                return instance;
            }

            if (descriptor.ImplementationFactory is not null)
            {
                return (IOrderPlacement)descriptor.ImplementationFactory(provider);
            }

            return (IOrderPlacement)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
        }

        private sealed class Gated(GatedOrderPlacement gate, IOrderPlacement inner) : IOrderPlacement
        {
            public async Task<Result<PlacedOrder>> PlaceAsync(
                PlaceOrderRequest request,
                CancellationToken cancellationToken = default)
            {
                if (gate.Before is { } before)
                {
                    await before(request);
                }

                return await inner.PlaceAsync(request, cancellationToken);
            }
        }
    }
}
