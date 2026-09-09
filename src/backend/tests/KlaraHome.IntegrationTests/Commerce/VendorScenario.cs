using System.Net.Http.Json;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>A seller that has been taken through onboarding, and the ids that came out of it.</summary>
/// <param name="Id">The seller.</param>
/// <param name="Code">Their generated seller code.</param>
/// <param name="Slug">Their storefront path segment.</param>
/// <param name="CommissionPlanId">The plan they were put on.</param>
/// <param name="BankAccountId">Their primary, verified account.</param>
/// <param name="PickupLocationId">Where a courier collects from.</param>
internal sealed record OnboardedVendor(
    Guid Id,
    string Code,
    string Slug,
    Guid CommissionPlanId,
    Guid BankAccountId,
    Guid PickupLocationId);

/// <summary>
/// Builds the seller that almost every later step needs to exist before it can prove anything.
/// </summary>
/// <remarks>
/// <para>
/// Every step of it goes through the API a person would use — apply, submit, upload, verify,
/// approve, activate — rather than writing rows. Two reasons. The first is that the onboarding
/// sequence is itself a Step 9 acceptance criterion, so driving it here means it is re-proved by
/// every test that needs a seller rather than by one test that could be deleted. The second is that
/// rows written behind the API are rows written without the events, the audit entries and the
/// derived state that the endpoints produce, and a catalogue test standing on those would be
/// standing on a seller the product could never have created.
/// </para>
/// <para>
/// It is deliberately not a fixture. Each call makes a <em>new</em> seller with its own code and
/// slug, so two tests in the same collection cannot collide over one, and a test that suspends its
/// seller cannot break the next one.
/// </para>
/// </remarks>
/// <param name="admin">A client signed in as platform staff.</param>
/// <param name="cancellationToken">Cancellation token.</param>
internal sealed class VendorScenario(HttpClient admin, CancellationToken cancellationToken)
{
    private Guid? _stateId;

    /// <summary>The client this scenario drives, for a test that wants to carry on from here.</summary>
    public HttpClient Admin => admin;

    /// <summary>A state id from the seeded Indian reference data.</summary>
    /// <remarks>
    /// Read once and remembered. Telangana by preference, because the default delivery coverage is
    /// Hyderabad and a pickup location outside it would make every shipping test fail for a reason
    /// that has nothing to do with what it is testing.
    /// </remarks>
    public async Task<Guid> StateIdAsync()
    {
        if (_stateId is { } known)
        {
            return known;
        }

        var states = await Rest.ReadAsync(
            await admin.GetAsync(new Uri("/api/v1/store/states", UriKind.Relative), cancellationToken),
            cancellationToken);

        var all = states.ValueKind == JsonValueKind.Array ? states : states.GetProperty("items");

        var telangana = all.EnumerateArray()
            .FirstOrDefault(state => state.GetProperty("name").GetString()?.Contains(
                "Telangana", StringComparison.OrdinalIgnoreCase) == true);

        var chosen = telangana.ValueKind == JsonValueKind.Object ? telangana : all.EnumerateArray().First();

        _stateId = chosen.GetProperty("id").GetGuid();
        return _stateId.Value;
    }

    /// <summary>Uploads a scan into the private bucket and answers its file id.</summary>
    /// <param name="name">A readable file name.</param>
    public async Task<Guid> PrivateFileAsync(string name)
    {
        var uploaded = await Rest.ReadAsync(
            await Rest.UploadAsync(admin, Rest.Png(80, 80), name, "image/png", "private", cancellationToken),
            cancellationToken);

        return uploaded.GetProperty("id").GetGuid();
    }

    /// <summary>Creates a commission plan nothing else is using.</summary>
    /// <param name="rate">The rate charged when no rule matches.</param>
    /// <param name="rules">Category or price-band overrides.</param>
    public async Task<Guid> CommissionPlanAsync(decimal rate = 10m, object[]? rules = null)
    {
        var created = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/commission-plans",
                new
                {
                    code = $"plan-{Guid.NewGuid():N}"[..16],
                    name = "Test plan",
                    description = "Created by an integration test.",
                    planType = "Percentage",
                    defaultRate = rate,
                    defaultFixedFee = 0m,
                    rules = rules ?? [],
                },
                cancellationToken),
            cancellationToken);

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Registers an application to sell, and answers the seller it created.</summary>
    /// <param name="legalName">The registered name, or null for a generated one.</param>
    /// <param name="gstin">A GST registration, or null for a seller below the threshold.</param>
    public async Task<JsonElement> ApplyAsync(string? legalName = null, string? gstin = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        return await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                "/api/v1/admin/vendors",
                new
                {
                    legalName = legalName ?? $"Test Seller {suffix}",
                    displayName = $"Seller {suffix}",
                    businessType = "SoleProprietorship",
                    slug = $"seller-{suffix}",
                    pan = NewPan(),
                    gstin,
                    supportEmail = $"support-{suffix}@klarahome.test",
                    supportPhone = "9876500000",
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Adds a bank account and, unless told not to, has it checked.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="verify">Whether to record that the penny-drop passed.</param>
    public async Task<Guid> BankAccountAsync(Guid vendorId, bool verify = true)
    {
        var account = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/bank-accounts",
                new
                {
                    accountName = "Test Seller",
                    accountNumber = $"9{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
                    ifsc = "HDFC0001234",
                    bankName = "HDFC Bank",
                    branchName = "Banjara Hills",
                    makePrimary = true,
                },
                cancellationToken),
            cancellationToken);

        var id = account.GetProperty("id").GetGuid();

        if (verify)
        {
            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/vendors/{vendorId}/bank-accounts/{id}/verify",
                    new { verified = true, note = (string?)null },
                    cancellationToken),
                cancellationToken);
        }

        return id;
    }

    /// <summary>Adds a pickup location a courier can collect from.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="pincode">Where it is. Hyderabad by default, which is the covered area.</param>
    public async Task<Guid> PickupLocationAsync(Guid vendorId, string pincode = "500034")
    {
        var location = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/pickup-locations",
                new
                {
                    label = "Warehouse",
                    contactName = "Warehouse Manager",
                    contactPhone = "9876500001",
                    line1 = "Plot 42",
                    line2 = "Road No 12",
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId = await StateIdAsync(),
                    pincode,
                    isActive = true,
                    makeDefault = true,
                },
                cancellationToken),
            cancellationToken);

        return location.GetProperty("id").GetGuid();
    }

    /// <summary>Submits a KYC document and, unless told not to, records that it was verified.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="documentType">Which document.</param>
    /// <param name="verify">Whether to accept it.</param>
    public async Task<Guid> KycDocumentAsync(Guid vendorId, string documentType, bool verify = true)
    {
        var document = await Rest.ReadAsync(
            await admin.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/kyc-documents",
                new
                {
                    documentType,
                    fileId = await PrivateFileAsync($"{documentType}.png"),
                    number = "ABCDE1234F",
                },
                cancellationToken),
            cancellationToken);

        var id = document.GetProperty("id").GetGuid();

        if (verify)
        {
            await Rest.ReadAsync(
                await admin.PostAsJsonAsync(
                    $"/api/v1/admin/vendors/{vendorId}/kyc-documents/{id}/verify",
                    new { approve = true, rejectionReason = (string?)null },
                    cancellationToken),
                cancellationToken);
        }

        return id;
    }

    /// <summary>Moves a seller through one life-cycle transition.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="transition">The route segment: <c>submit</c>, <c>approve</c>, <c>activate</c>…</param>
    /// <param name="reason">Why, for the transitions that require one.</param>
    public Task<HttpResponseMessage> TransitionAsync(Guid vendorId, string transition, string? reason = null)
        => admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/{transition}",
            new { reason },
            cancellationToken);

    /// <summary>Reads what is standing between a seller and trading.</summary>
    /// <param name="vendorId">The seller.</param>
    public async Task<JsonElement> ReadinessAsync(Guid vendorId)
        => await Rest.ReadAsync(
            await admin.GetAsync(
                new Uri($"/api/v1/admin/vendors/{vendorId}/readiness", UriKind.Relative),
                cancellationToken),
            cancellationToken);

    /// <summary>
    /// A seller taken all the way to <c>Active</c>, ready to be given a catalogue.
    /// </summary>
    /// <remarks>
    /// Sole proprietorship with no GSTIN, because that is the shortest legal form the readiness
    /// rules accept — PAN and an identity proof — and a test about stock should not be paying for an
    /// incorporation certificate it does not care about.
    /// </remarks>
    /// <param name="commissionRate">What the platform charges them.</param>
    /// <param name="legalName">The registered name, or null for a generated one.</param>
    public async Task<OnboardedVendor> ActiveAsync(decimal commissionRate = 10m, string? legalName = null)
    {
        var vendor = await ApplyAsync(legalName);
        var vendorId = vendor.GetProperty("id").GetGuid();

        var planId = await CommissionPlanAsync(commissionRate);

        await Rest.ReadAsync(
            await admin.PutAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/commission-plan",
                new { planId },
                cancellationToken),
            cancellationToken);

        await KycDocumentAsync(vendorId, "Pan");
        await KycDocumentAsync(vendorId, "IdentityProof");

        var bankAccountId = await BankAccountAsync(vendorId);
        var pickupLocationId = await PickupLocationAsync(vendorId);

        await Rest.ReadAsync(await TransitionAsync(vendorId, "submit"), cancellationToken);
        await Rest.ReadAsync(await TransitionAsync(vendorId, "approve"), cancellationToken);

        var activated = await Rest.ReadAsync(await TransitionAsync(vendorId, "activate"), cancellationToken);

        Assert.Equal("Active", activated.GetProperty("status").GetString());

        return new OnboardedVendor(
            vendorId,
            vendor.GetProperty("code").GetString()!,
            vendor.GetProperty("slug").GetString()!,
            planId,
            bankAccountId,
            pickupLocationId);
    }

    /// <summary>A structurally valid PAN nothing else is using.</summary>
    /// <remarks>Five letters, four digits, a letter — the shape the <c>CHECK</c> constraint enforces.</remarks>
    private static string NewPan()
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        var pan = new char[10];

        for (var index = 0; index < 5; index++)
        {
            pan[index] = letters[Random.Shared.Next(letters.Length)];
        }

        for (var index = 5; index < 9; index++)
        {
            pan[index] = (char)('0' + Random.Shared.Next(10));
        }

        pan[9] = letters[Random.Shared.Next(letters.Length)];

        return new string(pan);
    }
}
