using KlaraHome.Contracts.Vendors;
using KlaraHome.Modules.Shipping.Application;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Quoting;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// The courier's live price as the shopper's delivery charge, and every way it falls back.
/// </summary>
/// <remarks>
/// Each failure here is silent and costs money: a live price that never falls back stops checkout
/// when the courier is down, one that falls back on "nobody carries it" sells a delivery that cannot
/// happen, and a tax convention applied the wrong way round mis-prices every parcel by eighteen
/// percent.
/// </remarks>
public sealed class AggregatorChargeSourceTests
{
    private static readonly Guid Seller = Guid.Parse("0190c7a5-0000-7000-8000-000000000001");

    private static readonly PricedService RateCardAnswer =
        new(ShippingMethod.Standard, Carrier: null, 49m, 7.47m, 4, 8, IsCodAllowed: true);

    [Fact]
    public async Task The_cheapest_couriers_freight_is_passed_on_with_gst_added()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
            [
                new CourierRate("Cheap Surface", "10", 50m, EtaDays: 6, CodOk: true, IsRecommended: false),
                new CourierRate("Recommended Air", "20", 100m, EtaDays: 3, CodOk: true, IsRecommended: true),
            ]));

        var (source, fallback) = Build(courier);

        var priced = Assert.Single(await source.PriceAsync(Request(), TestContext.Current.CancellationToken));

        // The cheapest, not the aggregator's recommendation: on the first sandbox route checked the
        // recommended courier cost 3.4 times the cheapest (the User's decision, ADR-022).
        Assert.Equal("Cheap Surface", priced.Carrier);
        Assert.Equal(59m, priced.Amount);
        Assert.Equal(9m, priced.TaxAmount);
        Assert.Equal((6, 8), (priced.EtaMinDays, priced.EtaMaxDays));
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task A_tax_inclusive_convention_is_not_taxed_twice()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
                [new CourierRate("Only", "1", 118m, EtaDays: null, CodOk: true, IsRecommended: false)]));

        var (source, _) = Build(courier, settings => settings.AggregatorRatesIncludeTax = true);

        var priced = Assert.Single(await source.PriceAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(118m, priced.Amount);
        Assert.Equal(18m, priced.TaxAmount);
    }

    [Fact]
    public async Task A_tie_on_price_goes_to_the_aggregators_recommendation()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
            [
                new CourierRate("Same price", "1", 40m, EtaDays: 5, CodOk: true, IsRecommended: false),
                new CourierRate("Same price, recommended", "2", 40m, EtaDays: 3, CodOk: true, IsRecommended: true),
                new CourierRate("Dear", "3", 90m, EtaDays: 2, CodOk: true, IsRecommended: false),
            ]));

        var (source, _) = Build(courier, settings => settings.AggregatorRatesIncludeTax = true);

        Assert.Equal(
            "Same price, recommended",
            Assert.Single(await source.PriceAsync(Request(), TestContext.Current.CancellationToken)).Carrier);
    }

    [Fact]
    public async Task A_courier_that_cannot_be_asked_falls_back_to_the_rate_card()
    {
        var (source, fallback) = Build(
            new StubCourier(Result.Failure<IReadOnlyList<CourierRate>>(ShippingErrors.ProviderFailed())));

        var priced = await source.PriceAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(RateCardAnswer, Assert.Single(priced));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task A_courier_that_is_too_slow_falls_back_to_the_rate_card()
    {
        var (source, fallback) = Build(
            new StubCourier(delay: Timeout.InfiniteTimeSpan),
            settings => settings.LiveRateTimeoutMilliseconds = 250);

        var priced = await source.PriceAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(RateCardAnswer, Assert.Single(priced));
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task A_seller_without_a_pickup_address_falls_back_without_asking_the_courier()
    {
        var courier = new StubCourier(Result.Success<IReadOnlyList<CourierRate>>([]));
        var (source, fallback) = Build(courier, pickupPincode: null);

        await source.PriceAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(0, courier.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task No_configured_courier_means_the_rate_card_alone()
    {
        var courier = new StubCourier(Result.Success<IReadOnlyList<CourierRate>>([]), configured: false);
        var (source, fallback) = Build(courier);

        await source.PriceAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(0, courier.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Nobody_carrying_it_is_an_answer_and_is_not_overruled_by_the_rate_card()
    {
        var (source, fallback) = Build(new StubCourier(Result.Success<IReadOnlyList<CourierRate>>([])));

        Assert.Empty(await source.PriceAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task A_cash_parcel_is_priced_only_on_couriers_that_collect_cash()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
            [
                new CourierRate("Prepaid only", "1", 40m, EtaDays: 3, CodOk: false, IsRecommended: true),
                new CourierRate("Takes cash", "2", 60m, EtaDays: 4, CodOk: true, IsRecommended: false),
            ]));

        var (source, _) = Build(courier, settings => settings.AggregatorRatesIncludeTax = true);

        var priced = Assert.Single(await source.PriceAsync(Request(isCod: true), TestContext.Current.CancellationToken));

        Assert.Equal("Takes cash", priced.Carrier);
        Assert.True(priced.IsCodAllowed);
    }

    [Fact]
    public async Task A_weightless_basket_is_priced_on_the_default_parcel_weight()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
                [new CourierRate("Only", "1", 50m, EtaDays: 3, CodOk: true, IsRecommended: true)]));

        var (source, _) = Build(courier);

        await source.PriceAsync(Request(weightGrams: 0), TestContext.Current.CancellationToken);

        Assert.Equal(500, Assert.Single(courier.Requests).WeightGrams);
        Assert.Equal("500081", courier.Requests[0].OriginPincode);
    }

    [Fact]
    public async Task The_same_route_is_asked_once_while_its_price_is_fresh()
    {
        var courier = new StubCourier(
            Result.Success<IReadOnlyList<CourierRate>>(
                [new CourierRate("Only", "1", 50m, EtaDays: 3, CodOk: true, IsRecommended: true)]));

        var (source, _) = Build(courier);

        // Loading the options and then choosing one: the price shown must be the price recorded.
        var shown = await source.PriceAsync(Request(), TestContext.Current.CancellationToken);
        var chosen = await source.PriceAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(shown, chosen);
        Assert.Equal(1, courier.Calls);
    }

    private static DeliveryChargeRequest Request(bool isCod = false, int weightGrams = 800)
        => new(Seller, DestinationStateId: null, "400001", weightGrams, 1_250m, isCod);

    private static (AggregatorChargeSource Source, StubFallback Fallback) Build(
        StubCourier courier,
        Action<ShippingOptions>? configure = null,
        string? pickupPincode = "500081")
    {
        var settings = new ShippingOptions { Provider = courier.Name, FreightGstRate = 18m };
        configure?.Invoke(settings);

        var registry = new ShippingProviderRegistry(
            [courier, new ManualShippingProvider()],
            new StaticMonitor(settings));

        var pickups = Substitute.For<IVendorPickupPoints>();
        pickups.FindAsync(Seller, null, Arg.Any<CancellationToken>())
            .Returns(pickupPincode is null
                ? null
                : new VendorPickupPoint(
                    Guid.NewGuid(), Seller, "Warehouse", "Desk", "+919999999999", "Line 1", null, null,
                    "Hyderabad", Guid.NewGuid(), pickupPincode, IsDefault: true, CourierLocationCode: null));

        var fallback = new StubFallback();

        var source = new AggregatorChargeSource(
            registry,
            pickups,
            fallback,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(settings),
            NullLogger<AggregatorChargeSource>.Instance);

        return (source, fallback);
    }

    /// <summary>The rate card, reduced to a counter and one fixed answer.</summary>
    private sealed class StubFallback : IDeliveryChargeSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<PricedService>> PriceAsync(
            DeliveryChargeRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PricedService>>([RateCardAnswer]);
        }
    }

    /// <summary>A courier that answers rate questions and nothing else.</summary>
    private sealed class StubCourier(
        Result<IReadOnlyList<CourierRate>>? answer = null,
        bool configured = true,
        TimeSpan? delay = null) : IShippingProvider
    {
        public List<CourierRateRequest> Requests { get; } = [];

        public int Calls => Requests.Count;

        public string Name => ShippingProviders.Shiprocket;

        public bool IsConfigured => configured;

        public bool CanVerifyWebhooks => false;

        public async Task<Result<IReadOnlyList<CourierRate>>> QuoteRatesAsync(
            CourierRateRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (delay is { } wait)
            {
                await Task.Delay(wait, cancellationToken);
            }

            return answer ?? Result.Failure<IReadOnlyList<CourierRate>>(ShippingErrors.ProviderFailed());
        }

        public Task<Result<CourierServiceability>> CheckServiceabilityAsync(
            string pincode,
            string? pickupPincode,
            int weightGrams,
            bool isCod,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CourierBooking>> CreateShipmentAsync(
            CourierBookingRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<byte[]>> GenerateLabelAsync(
            string? providerShipmentId,
            string awb,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CourierManifest>> GenerateManifestAsync(
            IReadOnlyList<string> awbs,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<DateTimeOffset>> SchedulePickupAsync(
            string? providerShipmentId,
            string awb,
            DateTimeOffset pickupAt,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result> CancelShipmentAsync(
            string? providerShipmentId,
            string awb,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<CourierTracking>> TrackAsync(string awb, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool VerifyWebhookSignature(string rawBody, string? signature) => false;

        public Result<CourierWebhookEnvelope> ReadWebhook(string rawBody) => throw new NotSupportedException();
    }

    private sealed class StaticMonitor(ShippingOptions value) : IOptionsMonitor<ShippingOptions>
    {
        public ShippingOptions CurrentValue => value;

        public ShippingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ShippingOptions, string?> listener) => null;
    }
}
