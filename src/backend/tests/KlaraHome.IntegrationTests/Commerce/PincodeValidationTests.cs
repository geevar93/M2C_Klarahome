using KlaraHome.IntegrationTests.Database;
using System.Net.Http.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 16A's PIN-code validation: a courier's refusal stays <c>PINCODE_NOT_SERVICEABLE</c> rather
/// than the coverage code, its city and state are cached, and the platform's own reference data
/// resolves a seeded PIN code (ADR-018).
/// </summary>
/// <param name="fixture">The migrated database.</param>
public sealed class PincodeValidationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A PIN code inside the delivery area that no courier will carry to answers
    /// <c>PINCODE_NOT_SERVICEABLE</c> — not <c>DELIVERY_AREA_NOT_COVERED</c> — and the refresh writes
    /// the courier's own city and state onto the cache row.
    /// </summary>
    /// <remarks>
    /// The two refusals are kept apart on purpose (Step 16A's own acceptance criterion): one is the
    /// store's trading decision and the other is a fact about India's logistics, and a shopper inside
    /// Hyderabad whom no courier will reach needs the second message, not the first.
    /// </remarks>
    [Fact]
    public async Task A_pincode_the_courier_refuses_answers_not_serviceable_and_caches_city_and_state()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        // Inside the default Hyderabad coverage (prefix 500) but excluded from what the courier will
        // carry to — the seam that keeps the two refusals distinct.
        const string reachable = "500034";
        const string refused = "500091";

        Factory.Courier.Serviceable = [reachable];

        // Reads are answered from the cache alone; a miss is optimistic. Both PIN codes must be
        // refreshed before the read can tell them apart.
        await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipping/serviceability/{reachable}/refresh",
                new { },
                Cancellation),
            Cancellation);

        var refreshedRefusal = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/shipping/serviceability/{refused}/refresh",
                new { },
                Cancellation),
            Cancellation);

        Assert.False(refreshedRefusal.GetProperty("isServiceable").GetBoolean());

        var answer = await Rest.ReadAsync(
            await CreateClient().GetAsync(
                new Uri($"/api/v1/store/shipping/serviceability/{refused}", UriKind.Relative),
                Cancellation),
            Cancellation);

        Assert.False(answer.GetProperty("deliverable").GetBoolean());
        Assert.True(answer.GetProperty("covered").GetBoolean(), "The default coverage area includes 500091.");
        Assert.False(answer.GetProperty("isServiceable").GetBoolean());
        Assert.Equal("PINCODE_NOT_SERVICEABLE", answer.GetProperty("reason").GetString());

        var reachableAnswer = await Rest.ReadAsync(
            await CreateClient().GetAsync(
                new Uri($"/api/v1/store/shipping/serviceability/{reachable}", UriKind.Relative),
                Cancellation),
            Cancellation);

        // FakeShippingProvider answers Hyderabad/Telangana for anything it says is reachable. The
        // refresh is what is under test: that the answer actually lands on the cache row rather than
        // being read once and discarded.
        Assert.Equal("Hyderabad", reachableAnswer.GetProperty("city").GetString());
        Assert.Equal("Telangana", reachableAnswer.GetProperty("state").GetString());

        var cached = await Database.RowsAsync(
            "SELECT city, state FROM shipping.serviceability_cache WHERE pincode = $1",
            Cancellation,
            reachable);

        var row = Assert.Single(cached);
        Assert.Equal("Hyderabad", row["city"]);
        Assert.Equal("Telangana", row["state"]);
    }

    /// <summary>
    /// <c>IReferenceData.PincodeAsync</c> resolves a seeded PIN code to its city and state through
    /// the storefront lookup, and answers nothing for one the platform has no row for — the fallback
    /// path the delivery-coverage check then takes to a courier's own answer.
    /// </summary>
    [Fact]
    public async Task Seeded_reference_data_resolves_a_pincode_and_answers_nothing_for_an_unseeded_one()
    {
        SkipWithoutDocker();

        Factory.Features["platform.pincode-lookup"] = true;

        var admin = await SignedInAdministratorAsync();
        var orders = new OrderScenario(admin, Cancellation);
        var stateId = await orders.HomeStateAsync();

        // No dataset is mounted on this host — Platform:PincodeDataPath is unconfigured, which is
        // the state every deployment starts in — so platform.pincodes is empty until an operator
        // imports one. A row is written directly here, standing in for that import, because there is
        // no API surface that creates reference data (it is read-only by design).
        var code = $"5{Random.Shared.Next(10_000, 99_999)}";

        await Database.ExecuteAsync(
            "INSERT INTO platform.pincodes (id, code, city, district, state_id, zone) "
            + "VALUES (gen_random_uuid(), $1, 'Hyderabad', 'Hyderabad', $2, 'South')",
            Cancellation,
            code,
            stateId.Id);

        var client = CreateClient();

        var known = await Rest.ReadAsync(
            await client.GetAsync(new Uri($"/api/v1/store/pincodes/{code}", UriKind.Relative), Cancellation),
            Cancellation);

        Assert.Equal(code, known.GetProperty("pincode").GetString());
        Assert.Equal("Hyderabad", known.GetProperty("city").GetString());
        Assert.False(string.IsNullOrWhiteSpace(known.GetProperty("stateName").GetString()));

        // A PIN code with the right shape but that names no real place. Nothing seeds it, so the
        // platform's own reference data has no row and the lookup answers not-found — which is the
        // fallback path the coverage check then takes to a courier's own answer.
        var unknown = await client.GetAsync(
            new Uri("/api/v1/store/pincodes/199999", UriKind.Relative),
            Cancellation);

        await RefusedAsync(unknown, System.Net.HttpStatusCode.NotFound);
    }
}
