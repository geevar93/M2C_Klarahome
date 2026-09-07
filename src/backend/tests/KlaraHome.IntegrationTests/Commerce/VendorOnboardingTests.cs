using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 9's full acceptance criteria, against a real database and the real endpoints.
/// </summary>
/// <remarks>
/// The build sprint wrote the Vendors module and deferred every one of these
/// (<c>docs/TEST_DEBT.md</c>, Step 9). They are the criteria as the step card stated them, not a
/// weaker restatement: a seller reaches <c>Active</c> only by satisfying every requirement, the
/// refusal names all of them rather than the first, the money-bearing fields never come back out,
/// and the events other modules depend on are written in the same transaction as the change that
/// caused them.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class VendorOnboardingTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// The step's headline criterion: apply → submit → KYC → bank → pickup → approve → activate.
    /// </summary>
    /// <remarks>
    /// Driven end to end through the API, because the sequence is the criterion. Each stage asserts
    /// the state the seller is actually in, so a break tells you which transition stopped working
    /// rather than only that the last one did.
    /// </remarks>
    [Fact]
    public async Task A_seller_onboarded_through_the_api_reaches_active()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        Assert.Equal("Applied", applied.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(applied.GetProperty("code").GetString()));

        // A seller with nothing behind them cannot be activated, whatever an operator clicks.
        await RefusedAsync(
            await sellers.TransitionAsync(vendorId, "activate"),
            HttpStatusCode.Conflict);

        await sellers.KycDocumentAsync(vendorId, "Pan");
        await sellers.KycDocumentAsync(vendorId, "IdentityProof");
        await sellers.BankAccountAsync(vendorId);
        await sellers.PickupLocationAsync(vendorId);

        var planId = await sellers.CommissionPlanAsync(12.5m);

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/commission-plan",
            new { planId },
            Cancellation));

        var readiness = await sellers.ReadinessAsync(vendorId);

        Assert.True(
            readiness.GetProperty("isReady").GetBoolean(),
            $"Still blocked: {readiness.GetProperty("blockers")}");

        var underReview = await ReadAsync(await sellers.TransitionAsync(vendorId, "submit"));
        Assert.Equal("UnderReview", underReview.GetProperty("status").GetString());

        var approved = await ReadAsync(await sellers.TransitionAsync(vendorId, "approve"));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        var active = await ReadAsync(await sellers.TransitionAsync(vendorId, "activate"));

        Assert.Equal("Active", active.GetProperty("status").GetString());
        Assert.NotNull(active.GetProperty("onboardedAt").GetString());
        Assert.Equal(planId, active.GetProperty("commissionPlanId").GetGuid());
    }

    /// <summary>
    /// Activation is refused while any requirement is unmet, and the answer names every blocker.
    /// </summary>
    /// <remarks>
    /// Every one of them, not the first. A seller told to fix one thing, who fixes it and is then
    /// told about the next, is a seller who gives up — and the readiness endpoint exists precisely
    /// so the back office can show the whole list at once.
    /// </remarks>
    [Fact]
    public async Task Activation_is_refused_and_names_every_blocker_at_once()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var readiness = await sellers.ReadinessAsync(vendorId);

        Assert.False(readiness.GetProperty("isReady").GetBoolean());

        var blockers = readiness.GetProperty("blockers")
            .EnumerateArray()
            .Select(blocker => blocker.GetString() ?? string.Empty)
            .ToList();

        // Three separate reasons, reported together: no verified documents, no account to pay into,
        // nowhere to collect from. A commission plan is not among them — a new seller is put on the
        // tenant default at application, and only a deployment with no default plan can lack one.
        var reported = string.Join(" | ", blockers);

        Assert.True(blockers.Count >= 3, $"Expected several blockers at once, got: {reported}");
        Assert.Contains(blockers, blocker => blocker.Contains("not verified", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, blocker => blocker.Contains("bank account", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, blocker => blocker.Contains("pickup", StringComparison.OrdinalIgnoreCase));

        await RefusedAsync(await sellers.TransitionAsync(vendorId, "activate"), HttpStatusCode.Conflict);

        // Fixing two of the three is still not enough, and the third is still named on its own.
        await sellers.KycDocumentAsync(vendorId, "Pan");
        await sellers.KycDocumentAsync(vendorId, "IdentityProof");
        await sellers.BankAccountAsync(vendorId);

        var stillBlocked = await sellers.ReadinessAsync(vendorId);

        Assert.False(stillBlocked.GetProperty("isReady").GetBoolean());

        var remaining = stillBlocked.GetProperty("blockers")
            .EnumerateArray()
            .Select(blocker => blocker.GetString()!)
            .ToList();

        Assert.Contains(remaining, blocker => blocker.Contains("pickup", StringComparison.OrdinalIgnoreCase));

        await RefusedAsync(await sellers.TransitionAsync(vendorId, "activate"), HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A KYC document that has been submitted but not verified does not count towards readiness.
    /// </summary>
    /// <remarks>
    /// The distinction the whole KYC surface exists for. Uploading a scan is the seller's work;
    /// deciding it is genuine is the platform's, and a marketplace that treated the first as the
    /// second would be onboarding sellers on the strength of a file name.
    /// </remarks>
    [Fact]
    public async Task An_unverified_document_does_not_satisfy_the_kyc_requirement()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        await sellers.KycDocumentAsync(vendorId, "Pan", verify: false);
        await sellers.KycDocumentAsync(vendorId, "IdentityProof", verify: false);

        var blockers = (await sellers.ReadinessAsync(vendorId)).GetProperty("blockers");

        Assert.Contains(
            blockers.EnumerateArray().Select(blocker => blocker.GetString()!),
            blocker => blocker.Contains("not verified", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A bank account number is written as ciphertext and never comes back out of any endpoint.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither implies the other. The API could mask a value it stores in
    /// clear, and it could store ciphertext while handing the plaintext back on a detail route — so
    /// this reads the column directly as well as reading every route that returns an account.
    /// </remarks>
    [Fact]
    public async Task A_bank_account_number_is_encrypted_at_rest_and_never_returned()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var accountNumber = "912345678901";

        var created = await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/bank-accounts",
            new
            {
                accountName = "Test Seller",
                accountNumber,
                ifsc = "HDFC0001234",
                bankName = "HDFC Bank",
                branchName = "Banjara Hills",
                makePrimary = true,
            },
            Cancellation));

        // What the API says: the last four digits, and nothing else.
        Assert.Equal("8901", created.GetProperty("accountNumberLast4").GetString());
        Assert.DoesNotContain(accountNumber, created.ToString(), StringComparison.Ordinal);

        var listed = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendorId}/bank-accounts", UriKind.Relative),
            Cancellation));

        Assert.DoesNotContain(accountNumber, listed.ToString(), StringComparison.Ordinal);

        // What the database holds: not the number. Read straight from the column, because a value
        // converter is exactly the thing being tested and asking EF would let it agree with itself.
        var stored = await Database.RowsAsync(
            "SELECT account_number_encrypted, account_number_last4 FROM vendors.vendor_bank_accounts "
            + "WHERE vendor_id = $1",
            Cancellation,
            vendorId);

        var row = Assert.Single(stored);
        var ciphertext = Assert.IsType<string>(row["account_number_encrypted"]);

        Assert.DoesNotContain(accountNumber, ciphertext, StringComparison.Ordinal);
        Assert.NotEqual(accountNumber, ciphertext);
        Assert.Equal("8901", row["account_number_last4"]);
    }

    /// <summary>
    /// The life-cycle events land in the outbox, in the transaction that changed the status.
    /// </summary>
    /// <remarks>
    /// Four modules act on <c>VendorActivated</c> and <c>VendorSuspended</c>. If the row were written
    /// after the commit, a crash between the two would leave a seller who is active here and unknown
    /// everywhere else — which is the failure ADR-003 exists to make impossible.
    /// </remarks>
    [Fact]
    public async Task The_lifecycle_events_are_written_to_the_outbox_with_the_status_change()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var vendor = await sellers.ActiveAsync();

        Assert.Equal(1, await EventsAbout(vendor.Id, "VendorActivated"));

        await ReadAsync(await sellers.TransitionAsync(vendor.Id, "suspend", "Repeated late dispatch."));
        Assert.Equal(1, await EventsAbout(vendor.Id, "VendorSuspended"));

        await ReadAsync(await sellers.TransitionAsync(vendor.Id, "offboard", "Ceased trading."));
        Assert.Equal(1, await EventsAbout(vendor.Id, "VendorOffboarded"));

        // The status the events describe is the status that was committed beside them.
        var offboarded = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendor.Id}", UriKind.Relative),
            Cancellation));

        Assert.Equal("Offboarded", offboarded.GetProperty("status").GetString());
    }

    /// <summary>
    /// A suspension or an offboarding without a reason is refused.
    /// </summary>
    /// <remarks>
    /// Both are decisions somebody has to answer for later, and a status change with no recorded
    /// reason is one nobody can. The transitions that are ordinary progress do not require one.
    /// </remarks>
    [Fact]
    public async Task Suspending_a_seller_requires_a_reason()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var vendor = await sellers.ActiveAsync();

        await RefusedAsync(await sellers.TransitionAsync(vendor.Id, "suspend"), HttpStatusCode.UnprocessableContent);

        var suspended = await ReadAsync(
            await sellers.TransitionAsync(vendor.Id, "suspend", "Repeated late dispatch."));

        Assert.Equal("Suspended", suspended.GetProperty("status").GetString());
        Assert.Equal("Repeated late dispatch.", suspended.GetProperty("statusReason").GetString());
    }

    /// <summary>
    /// A commission plan resolves through the resolver and the database, not only in memory.
    /// </summary>
    /// <remarks>
    /// The rate-selection algorithm has unit tests; what those cannot prove is that the plan is
    /// loaded with its rules attached. A missing <c>Include</c> would make every seller fall through
    /// to the default rate — silently, and wrongly, on every settlement thereafter.
    /// </remarks>
    [Fact]
    public async Task A_commission_plan_resolves_with_its_rules_loaded_from_the_database()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        // A default of 15%, with a band that charges 8% on anything above ₹5,000.
        var planId = await sellers.CommissionPlanAsync(
            15m,
            [new { categoryId = (Guid?)null, minPrice = 5000m, maxPrice = (decimal?)null, rate = 8m, fixedFee = 0m }]);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        await ReadAsync(await admin.PutAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/commission-plan",
            new { planId },
            Cancellation));

        var cheap = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/commission-plans/preview?vendorId={vendorId}&unitPrice=1000", UriKind.Relative),
            Cancellation));

        var dear = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/commission-plans/preview?vendorId={vendorId}&unitPrice=9000", UriKind.Relative),
            Cancellation));

        Assert.Equal(planId, cheap.GetProperty("planId").GetGuid());
        Assert.Equal(15m, cheap.GetProperty("ratePercent").GetDecimal());
        Assert.Equal(8m, dear.GetProperty("ratePercent").GetDecimal());
    }

    /// <summary>
    /// At most one primary bank account and one default pickup location per seller.
    /// </summary>
    /// <remarks>
    /// Enforced by partial unique indexes rather than by the handler, so two requests racing cannot
    /// produce two primaries. Asserted through the API — making a second account primary must move
    /// the flag, not add a second one.
    /// </remarks>
    [Fact]
    public async Task A_seller_has_at_most_one_primary_bank_account()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        await sellers.BankAccountAsync(vendorId);
        await sellers.BankAccountAsync(vendorId);

        var accounts = await ReadAsync(await admin.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendorId}/bank-accounts", UriKind.Relative),
            Cancellation));

        Assert.Equal(2, accounts.GetArrayLength());
        Assert.Single(accounts.EnumerateArray(), account => account.GetProperty("isPrimary").GetBoolean());

        var primaries = await Database.CountAsync(
            "SELECT COUNT(*) FROM vendors.vendor_bank_accounts WHERE vendor_id = $1 AND is_primary",
            Cancellation,
            vendorId);

        Assert.Equal(1, primaries);
    }

    /// <summary>
    /// A malformed PAN or GSTIN is refused as a validation failure, naming the field.
    /// </summary>
    /// <remarks>
    /// A well-formed PAN typed in lower case is <em>accepted</em> — it is normalised on the way in,
    /// and refusing it would be refusing a person for their shift key. What must be refused is a
    /// value that is not a PAN at all, and it must be refused as a 422 that names the field rather
    /// than as a 500 from the <c>CHECK</c> constraint underneath, which tells the caller nothing.
    /// </remarks>
    [Theory]
    [InlineData("NOTAPAN", "pan", "seven characters is not a PAN")]
    [InlineData("ABCDE1234FG", "pan", "eleven characters is not a PAN")]
    [InlineData("12345ABCDE", "pan", "the letters and digits are the wrong way round")]
    public async Task A_malformed_identifier_is_refused_as_a_validation_failure(
        string pan,
        string field,
        string because)
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await CreateWithPanAsync(admin, pan);
        var body = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.True(
            response.StatusCode == HttpStatusCode.UnprocessableContent,
            $"Expected 422 because {because}, got {(int)response.StatusCode}: {body}");

        Assert.Contains($"\"{field}\"", body, StringComparison.Ordinal);
    }

    /// <summary>A well-formed PAN in lower case is normalised and accepted.</summary>
    [Fact]
    public async Task A_lower_case_pan_is_normalised_rather_than_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var created = await ReadAsync(await CreateWithPanAsync(admin, "abcde1234f"));

        Assert.Equal("ABCDE1234F", created.GetProperty("pan").GetString());
    }

    /// <summary>Two sellers created concurrently get two different seller codes.</summary>
    /// <remarks>
    /// The code comes off a sequence, and the reason to prove it under concurrency is that the
    /// obvious wrong implementation — count the rows and add one — passes every serial test.
    /// </remarks>
    [Fact]
    public async Task Concurrent_applications_get_distinct_seller_codes()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applications = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => sellers.ApplyAsync()));

        var codes = applications.Select(vendor => vendor.GetProperty("code").GetString()!).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The ILIKE search escapes a caller's own wildcards rather than running them.</summary>
    /// <remarks>
    /// A search for <c>%</c> that returns every seller is a search that has handed the caller a
    /// wildcard. Harmless here and not harmless on a bigger table.
    /// </remarks>
    [Fact]
    public async Task The_seller_search_escapes_a_callers_wildcards()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        await sellers.ApplyAsync();

        var all = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/vendors?size=50", UriKind.Relative),
            Cancellation));

        var wildcard = await ReadAsync(await admin.GetAsync(
            new Uri("/api/v1/admin/vendors?search=%25&size=50", UriKind.Relative),
            Cancellation));

        Assert.NotEmpty(all.GetProperty("items").EnumerateArray());
        Assert.Empty(wildcard.GetProperty("items").EnumerateArray());
    }

    /// <summary>Applies to sell with a given PAN and nothing else unusual.</summary>
    /// <param name="admin">A client signed in as platform staff.</param>
    /// <param name="pan">The PAN to send, valid or not.</param>
    private static Task<HttpResponseMessage> CreateWithPanAsync(HttpClient admin, string pan)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        return admin.PostAsJsonAsync(
            "/api/v1/admin/vendors",
            new
            {
                legalName = $"PAN Test {suffix}",
                displayName = $"PAN Test {suffix}",
                businessType = "SoleProprietorship",
                slug = $"pan-test-{suffix}",
                pan,
                supportEmail = $"support-{suffix}@klarahome.test",
            },
            Cancellation);
    }

    /// <summary>How many events of a kind the outbox holds about a seller.</summary>
    private async Task<long> EventsAbout(Guid vendorId, string eventType)
        => await Database.CountAsync(
            "SELECT COUNT(*) FROM platform.outbox_messages "
            + "WHERE type LIKE $1 AND payload::text LIKE $2",
            Cancellation,
            $"%{eventType}%",
            $"%{vendorId}%");
}
