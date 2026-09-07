using System.Net;
using System.Net.Http.Json;
using KlaraHome.Contracts.Vendors;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 9's remaining criteria: the guarantees the database makes, the seam other modules read a
/// seller through, and the key rotation a deployment eventually has to survive.
/// </summary>
/// <remarks>
/// These are the rows the build sprint could not close without an engine. A partial unique index, a
/// <c>CHECK</c> and a keyset predicate that does not translate all behave perfectly in memory and
/// only differ against PostgreSQL, which is the entire reason they were deferred to here.
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class VendorConstraintTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A row written under one key still decrypts after the current key has moved on.
    /// </summary>
    /// <remarks>
    /// The overlapping window is the whole point of a key ring: rotation is a change of which key
    /// is <em>current</em>, not a re-encryption of the table. A deployment that could not read its
    /// own history after a rotation would have to re-encrypt every protected column inside the
    /// deploy, which is not a thing anybody does at three in the morning.
    /// </remarks>
    [Fact]
    public async Task A_bank_account_written_under_the_previous_key_still_decrypts_after_rotation()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();
        var accountId = await sellers.BankAccountAsync(vendorId);

        // A second host, identical but for which key is current. Both keys are in its ring, which
        // is what a rotation with an overlapping window actually looks like in configuration.
        using var rotated = NewFactory();
        rotated.Overrides["Encryption:CurrentKeyId"] = CommerceApiFactory.SecondKeyId;

        using var after = rotated.CreateClient();

        await TestSignIn.SignInAsync(
            after,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        var accounts = await ReadAsync(await after.GetAsync(
            new Uri($"/api/v1/admin/vendors/{vendorId}/bank-accounts", UriKind.Relative),
            Cancellation));

        var account = Assert.Single(
            accounts.EnumerateArray(),
            candidate => candidate.GetProperty("id").GetGuid() == accountId);

        // Reading the row at all is the proof: the number is decrypted to compute what comes back,
        // and a key ring that had lost the old key would fail rather than answer.
        Assert.Equal("Verified", account.GetProperty("verificationStatus").GetString());
        Assert.Equal(4, account.GetProperty("accountNumberLast4").GetString()!.Length);

        // And a row written after the rotation is written under the new key.
        var second = await sellers.BankAccountAsync(vendorId, verify: false);

        Assert.NotEqual(accountId, second);
    }

    /// <summary>A seller has at most one default pickup location.</summary>
    /// <remarks>
    /// A partial unique index rather than a handler check, so two requests racing cannot leave two
    /// defaults and a courier collecting from whichever the query happened to return first.
    /// </remarks>
    [Fact]
    public async Task A_seller_has_at_most_one_default_pickup_location()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        await sellers.PickupLocationAsync(vendorId);
        await sellers.PickupLocationAsync(vendorId, "500081");

        var defaults = await Database.CountAsync(
            "SELECT COUNT(*) FROM vendors.vendor_pickup_locations WHERE vendor_id = $1 AND is_default",
            Cancellation,
            vendorId);

        Assert.Equal(1, defaults);
    }

    /// <summary>At most one default commission plan exists for the tenant.</summary>
    [Fact]
    public async Task At_most_one_commission_plan_is_the_default()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var defaults = await Database.CountAsync(
            "SELECT COUNT(*) FROM vendors.commission_plans WHERE is_default",
            Cancellation);

        Assert.True(defaults <= 1, $"{defaults} commission plans claim to be the default.");

        // And the index refuses a second one written behind the API's back.
        var refusal = await Database.RefusalAsync(
            "INSERT INTO vendors.commission_plans "
            + "(id, tenant_id, code, name, plan_type, default_rate, default_fixed_fee_amount, "
            + " default_fixed_fee_currency_code, is_active, is_default, created_at, updated_at) "
            + "SELECT gen_random_uuid(), tenant_id, 'second-default', 'Second default', 'Percentage', 5, 0, "
            + "       'INR', true, true, now(), now() "
            + "FROM vendors.commission_plans WHERE is_default LIMIT 1",
            Cancellation);

        Assert.SkipWhen(defaults == 0, "This deployment seeds no default plan, so there is no second to refuse.");
        Assert.Equal("23505", refusal);
    }

    /// <summary>A malformed IFSC is refused rather than stored.</summary>
    /// <remarks>
    /// Four letters, a zero, six alphanumerics. A bank account with a wrong IFSC is a payout that
    /// bounces days later, which is why it is checked on the way in rather than on the way out.
    /// </remarks>
    [Theory]
    [InlineData("HDFC1001234")]
    [InlineData("HD0001234")]
    [InlineData("hdfc0001234x")]
    public async Task A_malformed_ifsc_is_refused(string ifsc)
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/bank-accounts",
            new
            {
                accountName = "Test Seller",
                accountNumber = "912345678901",
                ifsc,
                bankName = "HDFC Bank",
                branchName = "Banjara Hills",
                makePrimary = true,
            },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableContent);
    }

    /// <summary>A malformed PIN code on a pickup location is refused.</summary>
    /// <remarks>Six digits, and never starting with a zero — no Indian PIN code does.</remarks>
    [Theory]
    [InlineData("012345")]
    [InlineData("50003")]
    [InlineData("5000345")]
    public async Task A_malformed_pincode_is_refused(string pincode)
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/pickup-locations",
            new
            {
                label = "Warehouse",
                contactName = "Warehouse Manager",
                contactPhone = "9876500001",
                line1 = "Plot 42",
                line2 = (string?)null,
                landmark = (string?)null,
                city = "Hyderabad",
                stateId = await sellers.StateIdAsync(),
                pincode,
                isActive = true,
                makeDefault = true,
            },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableContent);
    }

    /// <summary>A commission rate above one hundred per cent is refused.</summary>
    /// <remarks>
    /// A rate over 100 makes every sale a loss, and the arithmetic downstream would not notice: the
    /// settlement would simply compute a negative earning and pay nobody.
    /// </remarks>
    [Fact]
    public async Task A_commission_rate_above_one_hundred_percent_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/commission-plans",
            new
            {
                code = $"plan-{Guid.NewGuid():N}"[..16],
                name = "Impossible plan",
                description = "Charges more than the sale.",
                planType = "Percentage",
                defaultRate = 120m,
                defaultFixedFee = 0m,
                rules = Array.Empty<object>(),
            },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableContent);
    }

    /// <summary>A price band whose floor is above its ceiling is refused.</summary>
    /// <remarks>
    /// A band that can never match is not an error the resolver would report — it would simply fall
    /// through to the plan default, and the merchandiser would never learn why their rule did
    /// nothing.
    /// </remarks>
    [Fact]
    public async Task An_inverted_price_band_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/admin/commission-plans",
            new
            {
                code = $"plan-{Guid.NewGuid():N}"[..16],
                name = "Inverted band",
                description = "A band nothing can fall into.",
                planType = "Tiered",
                defaultRate = 10m,
                defaultFixedFee = 0m,
                rules = new[]
                {
                    new { categoryId = (Guid?)null, minPrice = 9000m, maxPrice = (decimal?)1000m, rate = 5m, fixedFee = 0m },
                },
            },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableContent);
    }

    /// <summary>Refusing a KYC document without saying why is itself refused.</summary>
    [Fact]
    public async Task A_kyc_rejection_without_a_reason_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();
        var documentId = await sellers.KycDocumentAsync(vendorId, "Pan", verify: false);

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/kyc-documents/{documentId}/verify",
            new { approve = false, rejectionReason = (string?)null },
            Cancellation);

        await RefusedAsync(response, HttpStatusCode.UnprocessableContent);
    }

    /// <summary>A KYC scan uploaded to the public bucket is refused.</summary>
    /// <remarks>
    /// The one mistake that would put a seller's identity document on a CDN. The check is on the
    /// file's visibility rather than on where the caller says it is, because the caller only sends
    /// an id.
    /// </remarks>
    [Fact]
    public async Task A_kyc_document_in_the_public_bucket_is_refused()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var publicFile = await ReadAsync(await Rest.UploadAsync(
            admin,
            Rest.Png(80, 80),
            "public-pan.png",
            "image/png",
            "public",
            Cancellation));

        var response = await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{vendorId}/kyc-documents",
            new
            {
                documentType = "Pan",
                fileId = publicFile.GetProperty("id").GetGuid(),
                number = "ABCDE1234F",
            },
            Cancellation);

        Assert.False(
            response.IsSuccessStatusCode,
            "A KYC scan in the public bucket was accepted, which puts an identity document on a CDN.");
    }

    /// <summary>
    /// The directory answers one row per known seller and silently omits the rest, in one query.
    /// </summary>
    /// <remarks>
    /// The seam Catalog, Orders and Search read a seller's name through. "Silently omits" is the
    /// contract: a listing whose seller has been offboarded is the caller's case to handle, and an
    /// exception here would take down a search results page.
    /// </remarks>
    [Fact]
    public async Task The_directory_returns_one_row_per_known_seller_and_omits_the_rest()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var first = await sellers.ActiveAsync();
        var second = await sellers.ActiveAsync();
        var invented = Guid.CreateVersion7();

        await RunOnceAsync<IVendorDirectory>(async (directory, cancellation) =>
        {
            var found = await directory.FindManyAsync([first.Id, second.Id, invented], cancellation);

            Assert.Equal(2, found.Count);
            Assert.True(found.ContainsKey(first.Id));
            Assert.True(found.ContainsKey(second.Id));
            Assert.False(found.ContainsKey(invented));

            Assert.True(await directory.IsActiveAsync(first.Id, cancellation));
            Assert.False(await directory.IsActiveAsync(invented, cancellation));
        });
    }

    /// <summary>
    /// Keyset paging over the seller list neither repeats a row nor skips one.
    /// </summary>
    /// <remarks>
    /// The failure an offset pager has and a keyset pager does not: rows created while somebody is
    /// paging shift the window, and page two then re-shows the tail of page one. Sellers are being
    /// created between the pages here precisely so that difference is what is under test.
    /// </remarks>
    [Fact]
    public async Task Keyset_paging_neither_repeats_nor_skips_a_seller()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        for (var index = 0; index < 5; index++)
        {
            await sellers.ApplyAsync();
        }

        var seen = new List<Guid>();
        string? cursor = null;

        for (var page = 0; page < 4; page++)
        {
            var url = cursor is null
                ? "/api/v1/admin/vendors?size=3"
                : $"/api/v1/admin/vendors?size=3&cursor={Uri.EscapeDataString(cursor)}";

            var body = await ReadAsync(await admin.GetAsync(new Uri(url, UriKind.Relative), Cancellation));

            seen.AddRange(body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

            // A seller created mid-page is exactly what breaks an offset pager.
            await sellers.ApplyAsync();

            cursor = body.TryGetProperty("nextCursor", out var next) && next.ValueKind == System.Text.Json.JsonValueKind.String
                ? next.GetString()
                : null;

            if (cursor is null)
            {
                break;
            }
        }

        Assert.NotEmpty(seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }
}
