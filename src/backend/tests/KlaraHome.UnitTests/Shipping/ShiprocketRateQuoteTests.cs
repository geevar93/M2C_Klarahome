using System.Net;
using System.Text;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Courier.Shiprocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Shipping;

/// <summary>
/// How the Shiprocket adapter reads a courier's live price off the serviceability endpoint.
/// </summary>
/// <remarks>
/// Driven through the real adapter with the network replaced by a canned body in Shiprocket's
/// documented shape. The shape is <b>to be confirmed against the sandbox</b> (docs/08-integrations.md
/// §7); what this pins down is what the adapter does with it — which figure is the freight, which
/// courier is the recommended one, and that a blocked courier is never quoted.
/// </remarks>
public sealed class ShiprocketRateQuoteTests
{
    private const string Body = """
        {
          "status": 200,
          "data": {
            "recommended_courier_company_id": 51,
            "available_courier_companies": [
              {
                "courier_company_id": 51,
                "courier_name": "Xpressbees Surface",
                "freight_charge": 60.12,
                "cod_charges": 35,
                "rate": 95.12,
                "cod": 1,
                "blocked": 0,
                "estimated_delivery_days": "5"
              },
              {
                "courier_company_id": 10,
                "courier_name": "Delhivery Air",
                "freight_charge": 110.5,
                "rate": 110.5,
                "cod": 0,
                "blocked": 0,
                "estimated_delivery_days": "2 Days"
              },
              {
                "courier_company_id": 99,
                "courier_name": "Blocked Courier",
                "freight_charge": 10,
                "rate": 10,
                "cod": 1,
                "blocked": 1,
                "estimated_delivery_days": "3"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task Each_courier_is_quoted_on_its_freight_without_the_cash_charge()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Body);
        using var adapter = Adapter(handler);

        var quoted = await adapter.QuoteRatesAsync(
            new CourierRateRequest("500081", "400001", 1_200, 1_499.5m, IsCod: true),
            TestContext.Current.CancellationToken);

        Assert.True(quoted.IsSuccess);

        var rates = quoted.Value;

        // The blocked courier is gone: quoting a courier the account cannot book is quoting fiction.
        Assert.Equal(2, rates.Count);

        var surface = Assert.Single(rates, rate => rate.CourierId == "51");
        Assert.Equal("Xpressbees Surface", surface.Courier);
        Assert.Equal(60.12m, surface.Freight);
        Assert.Equal(5, surface.EtaDays);
        Assert.True(surface.CodOk);
        Assert.True(surface.IsRecommended);

        var air = Assert.Single(rates, rate => rate.CourierId == "10");
        Assert.Equal(2, air.EtaDays);
        Assert.False(air.CodOk);
        Assert.False(air.IsRecommended);
    }

    [Fact]
    public async Task The_route_weight_value_and_cash_flag_are_all_asked_about()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Body);
        using var adapter = Adapter(handler);

        await adapter.QuoteRatesAsync(
            new CourierRateRequest("500081", "400001", 1_200, 1_499.5m, IsCod: true),
            TestContext.Current.CancellationToken);

        var query = Assert.Single(handler.Requests).Query;

        Assert.Contains("pickup_postcode=500081", query, StringComparison.Ordinal);
        Assert.Contains("delivery_postcode=400001", query, StringComparison.Ordinal);
        Assert.Contains("weight=1.2", query, StringComparison.Ordinal);
        Assert.Contains("cod=1", query, StringComparison.Ordinal);
        Assert.Contains("declared_value=1499.5", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rates_go_to_the_serviceability_host_when_one_is_configured()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Body);

        // Shiprocket's sandbox: bookings on one host, serviceability and rates on another.
        using var adapter = Adapter(handler, settings =>
        {
            settings.BaseUrl = "https://api-sandbox.shiprocket.in";
            settings.ServiceabilityBaseUrl = "https://serviceability-sandbox.shiprocket.in";
        });

        await adapter.QuoteRatesAsync(
            new CourierRateRequest("500081", "400001", 500, 100m, IsCod: false),
            TestContext.Current.CancellationToken);

        Assert.Equal("serviceability-sandbox.shiprocket.in", Assert.Single(handler.Requests).Host);
    }

    [Fact]
    public async Task A_route_nobody_serves_is_an_empty_answer_not_a_failure()
    {
        using var adapter = Adapter(new CannedHandler(
            HttpStatusCode.OK,
            """{ "status": 404, "data": { "available_courier_companies": [] } }"""));

        var quoted = await adapter.QuoteRatesAsync(
            new CourierRateRequest("500081", "999999", 500, 100m, IsCod: false),
            TestContext.Current.CancellationToken);

        Assert.True(quoted.IsSuccess);
        Assert.Empty(quoted.Value);
    }

    [Fact]
    public async Task A_courier_error_is_a_failure_the_caller_can_fall_back_from()
    {
        using var adapter = Adapter(new CannedHandler(HttpStatusCode.InternalServerError, "{}"));

        var quoted = await adapter.QuoteRatesAsync(
            new CourierRateRequest("500081", "400001", 500, 100m, IsCod: false),
            TestContext.Current.CancellationToken);

        Assert.True(quoted.IsFailure);
    }

    private static ShiprocketShippingProvider Adapter(
        CannedHandler handler,
        Action<ShippingOptions>? configure = null)
    {
        var settings = new ShippingOptions
        {
            Provider = ShippingProviders.Shiprocket,
            BaseUrl = "https://apiv2.shiprocket.in",
            ApiKey = "static-test-token",
        };

        configure?.Invoke(settings);

        return new(
            new SingleClientFactory(handler),
            new FixedMonitor(settings),
            NullLogger<ShiprocketShippingProvider>.Instance);
    }

    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedMonitor(ShippingOptions value) : IOptionsMonitor<ShippingOptions>
    {
        public ShippingOptions CurrentValue => value;

        public ShippingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ShippingOptions, string?> listener) => null;
    }
}
