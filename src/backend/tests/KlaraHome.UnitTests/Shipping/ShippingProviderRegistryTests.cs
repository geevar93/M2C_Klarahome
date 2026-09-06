using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// Which adapter books a parcel, and which one is asked about the parcels already booked (ADR-018).
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1, because every failure here is silent
/// and none of them throws. A registry that resolved the wrong adapter would book against a courier
/// this deployment has no account with; one that fell back too eagerly would quietly stop using a
/// courier that was working; and one that resolved a stored provider name by configuration rather
/// than by the row would orphan every parcel in flight on the day a store changes courier.
/// </remarks>
public sealed class ShippingProviderRegistryTests
{
    [Fact]
    public void The_configured_key_chooses_the_adapter()
    {
        var registry = Registry(Configured("shiprocket"), new StubProvider("shiprocket", configured: true));

        Assert.Equal("shiprocket", registry.Default.Name);
        Assert.True(registry.HasCourierApi);
    }

    [Fact]
    public void A_blank_or_unknown_key_falls_back_to_hand_booking()
    {
        var adapter = new StubProvider("shiprocket", configured: true);

        // Nothing configured: the shippable state, and the one a fresh deployment is in.
        Assert.Equal(ShippingProviders.Manual, Registry(Configured(string.Empty), adapter).Default.Name);

        // A courier this build has no adapter for, and a typo. Both degrade to hand-booking rather
        // than failing: a misspelt provider must leave parcels bookable, not take fulfilment down.
        Assert.Equal(ShippingProviders.Manual, Registry(Configured("delhivery"), adapter).Default.Name);
        Assert.Equal(ShippingProviders.Manual, Registry(Configured("shiprockett"), adapter).Default.Name);
    }

    [Fact]
    public void An_adapter_without_credentials_is_not_used_even_when_it_is_named()
    {
        var registry = Registry(Configured("shiprocket"), new StubProvider("shiprocket", configured: false));

        // Named and unusable. This is the state the whole module ships in until an account exists.
        Assert.Equal(ShippingProviders.Manual, registry.Default.Name);
        Assert.False(registry.HasCourierApi);
    }

    [Fact]
    public void A_parcel_is_answered_by_the_adapter_that_booked_it()
    {
        var shiprocket = new StubProvider("shiprocket", configured: true);
        var registry = Registry(Configured(string.Empty), shiprocket);

        // Configuration has moved on to hand-booking, and a parcel Shiprocket holds still resolves
        // to Shiprocket. Tracking, labelling and cancelling follow the row, not the configuration —
        // which is what stops a change of courier from orphaning everything in flight.
        Assert.Equal("shiprocket", registry.For("shiprocket").Name);

        // A provider this build no longer has: the manual adapter accepts it, because an operator
        // still has to be able to record what happened to a parcel that exists.
        Assert.Equal(ShippingProviders.Manual, registry.For("some-retired-courier").Name);
    }

    [Fact]
    public void The_legacy_aggregator_name_resolves_to_the_configured_courier()
    {
        var shiprocket = new StubProvider("shiprocket", configured: true);

        // What Step 16 wrote before a courier was chosen. A parcel or a configuration file carrying
        // it means "whichever aggregator this deployment has", and must keep resolving.
        Assert.Equal("shiprocket", Registry(Configured("shiprocket"), shiprocket).For("aggregator").Name);

        // With no usable courier there is nothing for the alias to mean, and hand-booking answers.
        Assert.Equal(
            ShippingProviders.Manual,
            Registry(Configured(string.Empty), new StubProvider("shiprocket", configured: false))
                .For("aggregator")
                .Name);
    }

    private static ShippingProviderRegistry Registry(ShippingOptions options, params IShippingProvider[] providers)
        => new([.. providers, new ManualShippingProvider()], new StubOptions(options));

    private static ShippingOptions Configured(string provider)
        => new() { Provider = provider };

    /// <summary>An adapter that answers nothing, which is all the registry asks of one.</summary>
    private sealed class StubProvider(string name, bool configured) : IShippingProvider
    {
        public string Name => name;

        public bool IsConfigured => configured;

        public bool CanVerifyWebhooks => configured;

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

    private sealed class StubOptions(ShippingOptions value) : IOptionsMonitor<ShippingOptions>
    {
        public ShippingOptions CurrentValue => value;

        public ShippingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ShippingOptions, string?> listener) => null;
    }
}
