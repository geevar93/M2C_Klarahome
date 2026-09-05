using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Identity;

/// <summary>
/// The Step 7 acceptance criterion that a vendor user is <em>provably</em> denied another vendor's
/// data (docs/07-security-compliance.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The proof has to be a query, not a code review. The filter lives in the data layer, so what
/// these tests assert is that the rows never arrive — not that a handler remembered to check.
/// </para>
/// <para>
/// The seller ids are plain UUIDs because the Vendors module is Step 9. That changes nothing about
/// what is being proved: the scope comes from the token's <c>vendor_id</c> claim and the filter
/// compares it to the column, and neither depends on there being a <c>vendors</c> table yet.
/// </para>
/// </remarks>
[Collection(KlaraHomeSchema.CollectionName)]
public sealed class VendorScopeTests(KlaraHomeSchemaFixture fixture) : IdentityTestBase(fixture)
{
    [Fact]
    public async Task A_vendor_owner_listing_users_sees_their_own_organisation_and_no_other()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var sellerA = Guid.NewGuid();
        var sellerB = Guid.NewGuid();

        var (ownerA, sessionA) = await CreateVendorOwnerAsync(admin, sellerA);
        var (ownerB, sessionB) = await CreateVendorOwnerAsync(admin, sellerB);

        using (ownerA)
        using (ownerB)
        {
            var staffA = await CreateVendorStaffAsync(admin, sellerA);
            var staffB = await CreateVendorStaffAsync(admin, sellerB);

            var visible = await ListUserIdsAsync(ownerA);

            Assert.Contains(sessionA.UserId, visible);
            Assert.Contains(staffA, visible);

            // Not "filtered out by the handler" — never selected. The other seller's staff do not
            // exist as far as this query is concerned.
            Assert.DoesNotContain(sessionB.UserId, visible);
            Assert.DoesNotContain(staffB, visible);
        }
    }

    [Fact]
    public async Task A_vendor_owner_asking_for_another_sellers_user_by_id_is_told_it_does_not_exist()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (ownerA, _) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());
        var (ownerB, sessionB) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (ownerA)
        using (ownerB)
        {
            var response = await ownerA.GetAsync(
                new Uri($"/api/v1/admin/users/{sessionB.UserId}", UriKind.Relative),
                Cancellation);

            // 404, not 403. A "forbidden" would confirm the id exists, which is the leak §2 rules
            // out for out-of-scope resources.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("IDENTITY_USER_NOT_FOUND", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_vendor_owner_cannot_change_another_sellers_user()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (ownerA, _) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());
        var (ownerB, sessionB) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (ownerA)
        using (ownerB)
        {
            var roles = await ownerA.PutAsJsonAsync(
                $"/api/v1/admin/users/{sessionB.UserId}/roles",
                new { roleCodes = new[] { "vendor-staff" } },
                Cancellation);

            var status = await ownerA.PutAsJsonAsync(
                $"/api/v1/admin/users/{sessionB.UserId}/status",
                new { status = "Suspended" },
                Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, roles.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

            // And the other owner is still able to sign in, because nothing happened to them.
            var stillWorks = await ownerB.GetAsync(new Uri("/api/v1/admin/me", UriKind.Relative), Cancellation);
            Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
        }
    }

    [Fact]
    public async Task A_vendor_owner_cannot_grant_themselves_a_platform_role()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (owner, session) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (owner)
        {
            // The classic escalation path through a delegated user-management screen: the caller
            // holds identity.role.assign, so the only thing standing between them and
            // platform-admin is this check.
            var response = await owner.PutAsJsonAsync(
                $"/api/v1/admin/users/{session.UserId}/roles",
                new { roleCodes = new[] { "platform-admin" } },
                Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("IDENTITY_ROLE_NOT_GRANTABLE", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_vendor_owner_creating_a_user_creates_them_inside_their_own_seller()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var seller = Guid.NewGuid();
        var (owner, _) = await CreateVendorOwnerAsync(admin, seller);

        using (owner)
        {
            var created = await owner.PostAsJsonAsync(
                "/api/v1/admin/users",
                new
                {
                    email = NewEmail("hired"),
                    mobile = (string?)null,
                    userType = "Vendor",
                    roleCodes = new[] { "vendor-staff" },
                    vendorId = (Guid?)null,
                },
                Cancellation);

            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            // The seller comes from the caller's token, not from the request body.
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal(seller.ToString(), body.GetProperty("vendorId").GetString());
        }
    }

    [Fact]
    public async Task A_vendor_owner_naming_a_different_seller_in_the_request_is_refused()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (owner, _) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (owner)
        {
            var created = await owner.PostAsJsonAsync(
                "/api/v1/admin/users",
                new
                {
                    email = NewEmail("poached"),
                    mobile = (string?)null,
                    userType = "Vendor",
                    roleCodes = new[] { "vendor-staff" },
                    vendorId = Guid.NewGuid(),
                },
                Cancellation);

            Assert.Equal(HttpStatusCode.UnprocessableContent, created.StatusCode);

            var problem = await created.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("IDENTITY_VENDOR_SCOPE", problem.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task A_platform_administrator_has_no_vendor_scope_and_sees_every_seller()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (ownerA, sessionA) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());
        var (ownerB, sessionB) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (ownerA)
        using (ownerB)
        {
            var visible = await ListUserIdsAsync(admin, size: 200);

            // The filter is open for a caller with no vendor scope. That is what makes platform
            // staff platform-wide, and it is the same mechanism rather than an exemption from it.
            Assert.Contains(sessionA.UserId, visible);
            Assert.Contains(sessionB.UserId, visible);
        }
    }

    [Fact]
    public async Task A_vendor_owner_cannot_reach_the_settings_surface_at_all()
    {
        SkipWithoutDocker();

        using var admin = CreateClient();
        await SignInAsAdministratorAsync(admin);

        var (owner, _) = await CreateVendorOwnerAsync(admin, Guid.NewGuid());

        using (owner)
        {
            // Authenticated, and still refused: the token carries no platform.settings.manage.
            var response = await owner.GetAsync(new Uri("/api/v1/admin/settings", UriKind.Relative), Cancellation);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private static async Task<List<Guid>> ListUserIdsAsync(HttpClient client, int size = 50)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/admin/users?size={size}",
            Cancellation);

        return [.. page.GetProperty("items")
            .EnumerateArray()
            .Select(user => Guid.Parse(user.GetProperty("id").GetString()!))];
    }
}
