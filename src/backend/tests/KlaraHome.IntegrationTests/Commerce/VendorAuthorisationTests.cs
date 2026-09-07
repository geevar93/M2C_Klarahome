using System.Net;
using System.Net.Http.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Step 9's security criteria: what a seller may do to their own record, and what they may not do
/// at all or to anybody else's.
/// </summary>
/// <remarks>
/// <para>
/// Two separate rules, deliberately proved separately. <em>Scope</em> says a seller cannot reach
/// another seller's rows, and the answer must be 404 rather than 403 — a "forbidden" confirms the
/// id exists, which is the disclosure <c>docs/04-api-specification.md</c> §2 rules out.
/// <em>Permission</em> says there are things no seller may do to any record, their own included:
/// approving themselves, verifying their own KYC, choosing their own commission plan.
/// </para>
/// <para>
/// The 403-versus-404 distinction is exactly the kind of thing that decays silently, because both
/// look like "it was refused" from the outside and only one of them is safe.
/// </para>
/// </remarks>
/// <param name="fixture">The migrated database.</param>
public sealed class VendorAuthorisationTests(KlaraHomeSchemaFixture fixture) : CommerceTestBase(fixture)
{
    /// <summary>
    /// A seller reaching another seller's record is told it does not exist, on every route.
    /// </summary>
    /// <remarks>
    /// Every route, not one of them. A single endpoint that answered 403 would be the leak, and the
    /// only way to know there is not one is to walk the surface.
    /// </remarks>
    [Fact]
    public async Task A_seller_cannot_reach_another_sellers_record_on_any_route()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var mine = await sellers.ActiveAsync();
        var theirs = await sellers.ActiveAsync();

        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            string[] routes =
            [
                $"/api/v1/admin/vendors/{theirs.Id}",
                $"/api/v1/admin/vendors/{theirs.Id}/readiness",
                $"/api/v1/admin/vendors/{theirs.Id}/kyc-documents",
                $"/api/v1/admin/vendors/{theirs.Id}/bank-accounts",
                $"/api/v1/admin/vendors/{theirs.Id}/pickup-locations",
                $"/api/v1/admin/vendors/{theirs.Id}/serviceable-regions",
                $"/api/v1/admin/vendors/{theirs.Id}/staff",
            ];

            foreach (var route in routes)
            {
                var response = await owner.GetAsync(new Uri(route, UriKind.Relative), Cancellation);

                Assert.True(
                    response.StatusCode == HttpStatusCode.NotFound,
                    $"{route} answered {(int)response.StatusCode}. Anything but 404 confirms the seller exists.");
            }

            // Writing to another seller is refused the same way, and for the same reason.
            var written = await owner.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{theirs.Id}/pickup-locations",
                new
                {
                    label = "Not mine",
                    contactName = "Nobody",
                    contactPhone = "9876500002",
                    line1 = "Somewhere",
                    line2 = (string?)null,
                    landmark = (string?)null,
                    city = "Hyderabad",
                    stateId = await sellers.StateIdAsync(),
                    pincode = "500034",
                    isActive = true,
                    makeDefault = false,
                },
                Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, written.StatusCode);
        }
    }

    /// <summary>A seller reading their own record without naming it gets their own.</summary>
    /// <remarks>
    /// The other half of the scope rule, and the half that makes the vendor portal possible: the
    /// same routes serve both audiences, and a seller's own id comes off their token.
    /// </remarks>
    [Fact]
    public async Task A_seller_reading_their_own_record_gets_it()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var own = await ReadAsync(await owner.GetAsync(
                new Uri("/api/v1/admin/vendors/me", UriKind.Relative),
                Cancellation));

            Assert.Equal(mine.Id, own.GetProperty("id").GetGuid());

            // Naming their own id in the path is allowed; it is the same seller either way.
            var byId = await ReadAsync(await owner.GetAsync(
                new Uri($"/api/v1/admin/vendors/{mine.Id}", UriKind.Relative),
                Cancellation));

            Assert.Equal(mine.Id, byId.GetProperty("id").GetGuid());
        }
    }

    /// <summary>
    /// A seller cannot create a seller, decide their own onboarding, or verify their own checks.
    /// </summary>
    /// <remarks>
    /// These are permission failures rather than scope failures, and they are the ones that keep a
    /// marketplace honest: a seller who could verify their own KYC and assign their own commission
    /// plan would be running the platform.
    /// </remarks>
    [Fact]
    public async Task A_seller_cannot_approve_activate_or_verify_themselves()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var applied = await sellers.ApplyAsync();
        var vendorId = applied.GetProperty("id").GetGuid();

        var kycId = await sellers.KycDocumentAsync(vendorId, "Pan", verify: false);
        var accountId = await sellers.BankAccountAsync(vendorId, verify: false);
        var otherPlan = await sellers.CommissionPlanAsync(1m);

        var (owner, _) = await SignedInVendorOwnerAsync(admin, vendorId);

        using (owner)
        {
            // Creating another seller outright.
            var created = await owner.PostAsJsonAsync(
                "/api/v1/admin/vendors",
                new
                {
                    legalName = "A seller of my own",
                    displayName = "A seller of my own",
                    businessType = "SoleProprietorship",
                    slug = $"mine-{Guid.NewGuid():N}"[..20],
                },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

            // Deciding their own onboarding.
            foreach (var transition in new[] { "approve", "activate" })
            {
                var moved = await owner.PostAsJsonAsync(
                    $"/api/v1/admin/vendors/{vendorId}/{transition}",
                    new { reason = (string?)null },
                    Cancellation);

                Assert.True(
                    moved.StatusCode == HttpStatusCode.Forbidden,
                    $"'{transition}' answered {(int)moved.StatusCode}; a seller must not decide it.");
            }

            // Accepting their own documents.
            var verifiedKyc = await owner.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/kyc-documents/{kycId}/verify",
                new { approve = true, rejectionReason = (string?)null },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, verifiedKyc.StatusCode);

            // Passing their own penny-drop.
            var verifiedBank = await owner.PostAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/bank-accounts/{accountId}/verify",
                new { verified = true, note = (string?)null },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, verifiedBank.StatusCode);

            // Choosing what the platform charges them.
            var repriced = await owner.PutAsJsonAsync(
                $"/api/v1/admin/vendors/{vendorId}/commission-plan",
                new { planId = otherPlan },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, repriced.StatusCode);
        }
    }

    /// <summary>A seller cannot browse the commission plans they are not on.</summary>
    /// <remarks>
    /// What the platform charges other sellers is commercially sensitive, and the plan surface is
    /// platform-only in full. A seller sees their own rate through their record and their
    /// statements.
    /// </remarks>
    [Fact]
    public async Task A_seller_cannot_browse_the_commission_plans()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var mine = await sellers.ActiveAsync();
        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var listed = await owner.GetAsync(
                new Uri("/api/v1/admin/commission-plans", UriKind.Relative),
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        }
    }

    /// <summary>An anonymous caller reaches none of the seller-management surface.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_the_admin_vendor_surface()
    {
        SkipWithoutDocker();

        using var anonymous = CreateClient();

        var listed = await anonymous.GetAsync(new Uri("/api/v1/admin/vendors", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
    }

    /// <summary>
    /// A KYC scan is minted as a short-lived link only after the permission and scope checks pass.
    /// </summary>
    /// <remarks>
    /// The document is in the private bucket, so there is no URL to guess; the risk is the link
    /// endpoint handing one out to a caller who should not have it.
    /// </remarks>
    [Fact]
    public async Task Another_sellers_kyc_scan_cannot_be_linked()
    {
        SkipWithoutDocker();

        var admin = await SignedInAdministratorAsync();
        var sellers = Sellers(admin);

        var mine = await sellers.ActiveAsync();
        var theirs = await sellers.ApplyAsync();
        var theirVendorId = theirs.GetProperty("id").GetGuid();

        var theirFileId = await sellers.PrivateFileAsync("their-pan.png");

        await ReadAsync(await admin.PostAsJsonAsync(
            $"/api/v1/admin/vendors/{theirVendorId}/kyc-documents",
            new { documentType = "Pan", fileId = theirFileId, number = "ABCDE1234F" },
            Cancellation));

        var (owner, _) = await SignedInVendorOwnerAsync(admin, mine.Id);

        using (owner)
        {
            var link = await owner.GetAsync(
                new Uri($"/api/v1/admin/media/{theirFileId}/link", UriKind.Relative),
                Cancellation);

            Assert.True(
                link.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
                $"A private scan belonging to another seller was linkable: {(int)link.StatusCode}.");
        }
    }
}
