using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Errors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Vendors.Application;
using KlaraHome.Modules.Vendors.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Vendors.Endpoints;

/// <summary>Query-string filters for the seller listing.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="Search">A fragment of a code, legal name or display name.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">Page size.</param>
internal sealed record VendorListFilter(string? Status, string? Search, string? Cursor, int? Size);

/// <summary>The body of an application to sell.</summary>
/// <param name="LegalName">The registered name.</param>
/// <param name="DisplayName">The name shoppers see. Defaults to the legal name.</param>
/// <param name="BusinessType">The legal form.</param>
/// <param name="Code">A code to use, or null to take the next one.</param>
/// <param name="Slug">A slug to use, or null to derive one from the display name.</param>
/// <param name="Pan">Their PAN.</param>
/// <param name="Gstin">Their GSTIN, or null.</param>
/// <param name="RegisteredAddress">Their registered address.</param>
/// <param name="SupportEmail">Where an escalation reaches them.</param>
/// <param name="SupportPhone">Their support number.</param>
internal sealed record CreateVendorBody(
    string LegalName,
    string? DisplayName,
    VendorBusinessType BusinessType,
    string? Code,
    string? Slug,
    string? Pan,
    string? Gstin,
    AddressPayload? RegisteredAddress,
    string? SupportEmail,
    string? SupportPhone);

/// <summary>The body of a change to the registration details.</summary>
/// <param name="LegalName">The registered name.</param>
/// <param name="BusinessType">The legal form.</param>
/// <param name="Pan">Their PAN.</param>
/// <param name="Gstin">Their GSTIN, or null.</param>
/// <param name="RegisteredAddress">Their registered address.</param>
internal sealed record UpdateVendorBody(
    string LegalName,
    VendorBusinessType BusinessType,
    string? Pan,
    string? Gstin,
    AddressPayload RegisteredAddress);

/// <summary>The body of a storefront-profile change.</summary>
/// <param name="DisplayName">The name shoppers see.</param>
/// <param name="About">The storefront blurb.</param>
/// <param name="LogoFileId">Their logo, as a media file id.</param>
/// <param name="BannerFileId">Their banner, as a media file id.</param>
/// <param name="SupportEmail">Where an escalation reaches them.</param>
/// <param name="SupportPhone">Their support number.</param>
internal sealed record UpdateVendorProfileBody(
    string DisplayName,
    string? About,
    Guid? LogoFileId,
    Guid? BannerFileId,
    string? SupportEmail,
    string? SupportPhone);

/// <summary>The body of an operational-settings change.</summary>
/// <param name="DispatchSlaHours">Hours to hand a parcel to a courier.</param>
/// <param name="ReturnPolicy">What they promise about returns.</param>
/// <param name="ServesAllIndia">Whether they deliver everywhere.</param>
internal sealed record UpdateVendorOperationsBody(
    int DispatchSlaHours,
    ReturnPolicyPayload ReturnPolicy,
    bool ServesAllIndia);

/// <summary>The body of a life-cycle move that needs an explanation.</summary>
/// <param name="Reason">Why. Shown to the seller and written to the audit trail.</param>
internal sealed record VendorStatusBody(string? Reason);

/// <summary>The body of a commission-plan assignment.</summary>
/// <param name="PlanId">The plan, or null to remove the assignment.</param>
internal sealed record AssignCommissionPlanBody(Guid? PlanId);

/// <summary>The body of a KYC submission.</summary>
/// <param name="DocumentType">What the document is.</param>
/// <param name="FileId">The private media file holding the scan.</param>
/// <param name="Number">The identifier on it. Only its mask is kept.</param>
internal sealed record SubmitKycBody(KycDocumentType DocumentType, Guid FileId, string? Number);

/// <summary>The body of a verification decision.</summary>
/// <param name="Approve">Whether it is accepted.</param>
/// <param name="RejectionReason">Why it was refused.</param>
internal sealed record VerifyKycBody(bool Approve, string? RejectionReason);

/// <summary>The body of a new bank account.</summary>
/// <param name="AccountName">The name the account is held in.</param>
/// <param name="AccountNumber">The account number.</param>
/// <param name="Ifsc">The branch IFSC.</param>
/// <param name="BankName">The bank.</param>
/// <param name="BranchName">The branch.</param>
/// <param name="MakePrimary">Whether payouts should be sent here.</param>
internal sealed record AddBankAccountBody(
    string AccountName,
    string AccountNumber,
    string Ifsc,
    string? BankName,
    string? BranchName,
    bool MakePrimary);

/// <summary>The body of a bank-account check.</summary>
/// <param name="Verified">Whether the check passed.</param>
/// <param name="Note">Why, for a failure.</param>
internal sealed record VerifyBankAccountBody(bool Verified, string? Note);

/// <summary>The body of a pickup location.</summary>
/// <param name="Label">What the seller calls it.</param>
/// <param name="ContactName">Who the courier asks for.</param>
/// <param name="ContactPhone">The number they ring.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The state.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="IsActive">Whether the seller still collects from here.</param>
/// <param name="MakeDefault">Whether shipments should be booked against it by default.</param>
internal sealed record PickupLocationBody(
    string Label,
    string ContactName,
    string ContactPhone,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    bool IsActive,
    bool MakeDefault);

/// <summary>The body of a serviceability change.</summary>
/// <param name="ServesAllIndia">Whether they deliver everywhere.</param>
/// <param name="Regions">The rules, when they do not.</param>
internal sealed record SetServiceableRegionsBody(
    bool ServesAllIndia,
    IReadOnlyList<ServiceableRegionPayload> Regions);

/// <summary>The body of a staff addition or change.</summary>
/// <param name="UserId">The Identity account. Ignored on a change.</param>
/// <param name="IsOwner">Whether they are the legal owner.</param>
/// <param name="JobTitle">What they do for the seller.</param>
internal sealed record VendorStaffBody(Guid UserId, bool IsOwner, string? JobTitle);

/// <summary>
/// The seller-management surface (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// <para>
/// One set of URLs for two audiences. Platform staff name a seller in the path; a vendor owner uses
/// the same routes with their own id and cannot address anybody else's — the scope comes from their
/// token, never from the path, and a seller outside it answers 404 rather than 403
/// (docs/04-api-specification.md §2).
/// </para>
/// <para>
/// The life-cycle routes are verbs, not a status field on the resource, because they are not the
/// same operation with a different argument: activating runs the readiness checks and announces the
/// seller to four other modules, and suspending demands a reason. A single <c>PATCH status</c>
/// would hide both behind a value.
/// </para>
/// </remarks>
internal static class AdminVendorEndpoints
{
    /// <summary>Maps the administrative seller surface beneath <c>/admin</c>.</summary>
    /// <param name="admin">The <c>/admin</c> group.</param>
    public static IEndpointRouteBuilder MapAdminVendorEndpoints(this IEndpointRouteBuilder admin)
    {
        ArgumentNullException.ThrowIfNull(admin);

        var vendors = admin.MapGroup("/vendors").WithTags("Vendors");

        MapVendors(vendors);
        MapLifecycle(vendors);
        MapKyc(vendors);
        MapBankAccounts(vendors);
        MapPickupLocations(vendors);
        MapServiceableRegions(vendors);
        MapStaff(vendors);

        return admin;
    }

    private static void MapVendors(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/", async (
                [AsParameters] VendorListFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new ListVendorsQuery(filter.Status, filter.Search, filter.Cursor, filter.Size);
                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorsList")
            .WithSummary("Lists sellers, newest first. A vendor caller sees only their own.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<PagedResult<VendorListItem>>();

        // How a vendor application finds out which seller it is. Platform staff have no "mine" and
        // get the same 404 as an unknown id.
        vendors.MapGet("/me", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetVendorQuery(null), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorGetMine")
            .WithSummary("Returns the seller the caller acts for.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<VendorResponse>();

        vendors.MapPost("/", async (CreateVendorBody body, IDispatcher dispatcher, HttpContext context) =>
            {
                var command = new CreateVendorCommand(
                    body.LegalName,
                    body.DisplayName,
                    body.BusinessType,
                    body.Code,
                    body.Slug,
                    body.Pan,
                    body.Gstin,
                    body.RegisteredAddress,
                    body.SupportEmail,
                    body.SupportPhone);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.Match(
                    vendor => Results.Created($"{context.Request.Path}/{vendor.Id}", vendor),
                    error => error.ToProblemResult(context));
            })
            .WithName("adminVendorCreate")
            .WithSummary("Registers an application to sell. The seller starts in Applied.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorResponse>(StatusCodes.Status201Created);

        vendors.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetVendorQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorGet")
            .WithSummary("Reads one seller. Answers 404 for a seller outside the caller's scope.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<VendorResponse>();

        vendors.MapGet("/{id:guid}/readiness", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetVendorReadinessQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorReadiness")
            .WithSummary("Lists everything standing between this seller and activation.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<VendorReadiness>();

        vendors.MapPut("/{id:guid}", async (
                Guid id,
                UpdateVendorBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateVendorBusinessCommand(
                    id,
                    body.LegalName,
                    body.BusinessType,
                    body.Pan,
                    body.Gstin,
                    body.RegisteredAddress);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorUpdate")
            .WithSummary("Updates the registration details an approval depends on.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorResponse>();

        vendors.MapPut("/{id:guid}/profile", async (
                Guid id,
                UpdateVendorProfileBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateVendorProfileCommand(
                    id,
                    body.DisplayName,
                    body.About,
                    body.LogoFileId,
                    body.BannerFileId,
                    body.SupportEmail,
                    body.SupportPhone);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorUpdateProfile")
            .WithSummary("Updates the storefront profile: name, blurb, logo, banner and support contacts.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorResponse>();

        vendors.MapPut("/{id:guid}/operations", async (
                Guid id,
                UpdateVendorOperationsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateVendorOperationsCommand(
                    id,
                    body.DispatchSlaHours,
                    body.ReturnPolicy,
                    body.ServesAllIndia);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorUpdateOperations")
            .WithSummary("Updates the dispatch SLA and return policy fulfilment reads.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorResponse>();

        vendors.MapPut("/{id:guid}/commission-plan", async (
                Guid id,
                AssignCommissionPlanBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new AssignCommissionPlanCommand(id, body.PlanId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorAssignCommissionPlan")
            .WithSummary("Puts a seller on a commission plan.")
            .RequirePermission(VendorPermissions.CommissionManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorResponse>();
    }

    /// <summary>
    /// The onboarding life cycle, one route per transition
    /// (Applied → UnderReview → Approved → Active ⇄ Suspended → Offboarded).
    /// </summary>
    private static void MapLifecycle(IEndpointRouteBuilder vendors)
    {
        Map("submit", VendorStatus.UnderReview, "adminVendorSubmit", "Sends an application for review.");
        Map("return", VendorStatus.Applied, "adminVendorReturn",
            "Sends an application back to the seller for more information.");
        Map("approve", VendorStatus.Approved, "adminVendorApprove",
            "Records that the checks passed. The seller still cannot trade until they are activated.");
        Map("activate", VendorStatus.Active, "adminVendorActivate",
            "Lets the seller trade. Refused unless every onboarding requirement is met.");
        Map("suspend", VendorStatus.Suspended, "adminVendorSuspend",
            "Stops new sales. Open orders are unaffected. A reason is required.");
        Map("offboard", VendorStatus.Offboarded, "adminVendorOffboard",
            "Ends the relationship. Terminal, and a reason is required.");

        void Map(string segment, VendorStatus status, string name, string summary)
            => vendors.MapPost($"/{{id:guid}}/{segment}", async (
                    Guid id,
                    VendorStatusBody? body,
                    IDispatcher dispatcher,
                    HttpContext context) =>
                {
                    var command = new ChangeVendorStatusCommand(id, status, body?.Reason);
                    var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                    return result.ToOk(context);
                })
                .WithName(name)
                .WithSummary(summary)
                .RequirePermission(VendorPermissions.VendorApprove)
                .RequireRateLimiting(RateLimitPolicies.AdminWrite)
                .Produces<VendorResponse>();
    }

    private static void MapKyc(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/{id:guid}/kyc-documents", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListKycDocumentsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorKycList")
            .WithSummary("Lists a seller's documents, what their legal form requires, and what is missing. "
                         + "Each document carries a short-lived link to its scan.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<KycSummaryResponse>();

        vendors.MapPost("/{id:guid}/kyc-documents", async (
                Guid id,
                SubmitKycBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new SubmitKycDocumentCommand(id, body.DocumentType, body.FileId, body.Number);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorKycSubmit")
            .WithSummary("Submits a document, replacing any previous one of the same kind. "
                         + "The file must already have been uploaded as private media.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<KycDocumentResponse>();

        vendors.MapPost("/{id:guid}/kyc-documents/{documentId:guid}/verify", async (
                Guid id,
                Guid documentId,
                VerifyKycBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new VerifyKycDocumentCommand(id, documentId, body.Approve, body.RejectionReason);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorKycVerify")
            .WithSummary("Accepts or refuses a document. A refusal must say why.")
            .RequirePermission(VendorPermissions.KycVerify)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<KycDocumentResponse>();
    }

    private static void MapBankAccounts(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/{id:guid}/bank-accounts", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListBankAccountsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorBankAccountsList")
            .WithSummary("Lists a seller's payout accounts. Account numbers are never returned.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<IReadOnlyList<BankAccountResponse>>();

        vendors.MapPost("/{id:guid}/bank-accounts", async (
                Guid id,
                AddBankAccountBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AddBankAccountCommand(
                    id,
                    body.AccountName,
                    body.AccountNumber,
                    body.Ifsc,
                    body.BankName,
                    body.BranchName,
                    body.MakePrimary);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorBankAccountAdd")
            .WithSummary("Records a payout account. The number is encrypted at the column and never read back.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BankAccountResponse>();

        vendors.MapPut("/{id:guid}/bank-accounts/{accountId:guid}/primary", async (
                Guid id,
                Guid accountId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SetPrimaryBankAccountCommand(id, accountId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorBankAccountSetPrimary")
            .WithSummary("Sends this seller's payouts to this account.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BankAccountResponse>();

        vendors.MapPost("/{id:guid}/bank-accounts/{accountId:guid}/verify", async (
                Guid id,
                Guid accountId,
                VerifyBankAccountBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new VerifyBankAccountCommand(id, accountId, body.Verified, body.Note);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorBankAccountVerify")
            .WithSummary("Records the outcome of a penny drop or a cancelled-cheque check.")
            .RequirePermission(VendorPermissions.KycVerify)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<BankAccountResponse>();

        vendors.MapDelete("/{id:guid}/bank-accounts/{accountId:guid}", async (
                Guid id,
                Guid accountId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemoveBankAccountCommand(id, accountId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminVendorBankAccountRemove")
            .WithSummary("Removes an account the seller no longer uses.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapPickupLocations(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/{id:guid}/pickup-locations", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListPickupLocationsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorPickupLocationsList")
            .WithSummary("Lists where couriers collect from, the default first.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<IReadOnlyList<PickupLocationResponse>>();

        vendors.MapPost("/{id:guid}/pickup-locations", async (
                Guid id,
                PickupLocationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AddPickupLocationCommand(
                    id,
                    body.Label,
                    body.ContactName,
                    body.ContactPhone,
                    body.Line1,
                    body.Line2,
                    body.Landmark,
                    body.City,
                    body.StateId,
                    body.Pincode,
                    body.MakeDefault);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorPickupLocationAdd")
            .WithSummary("Adds a place a courier may collect from.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PickupLocationResponse>();

        vendors.MapPut("/{id:guid}/pickup-locations/{locationId:guid}", async (
                Guid id,
                Guid locationId,
                PickupLocationBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePickupLocationCommand(
                    id,
                    locationId,
                    body.Label,
                    body.ContactName,
                    body.ContactPhone,
                    body.Line1,
                    body.Line2,
                    body.Landmark,
                    body.City,
                    body.StateId,
                    body.Pincode,
                    body.IsActive,
                    body.MakeDefault);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminVendorPickupLocationUpdate")
            .WithSummary("Changes a pickup location. Changing the address clears its courier registration.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<PickupLocationResponse>();

        vendors.MapDelete("/{id:guid}/pickup-locations/{locationId:guid}", async (
                Guid id,
                Guid locationId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemovePickupLocationCommand(id, locationId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminVendorPickupLocationRemove")
            .WithSummary("Removes a pickup location. A seller must keep at least one.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }

    private static void MapServiceableRegions(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/{id:guid}/serviceable-regions", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetServiceableRegionsQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorServiceableRegionsGet")
            .WithSummary("Reads where this seller is willing to deliver.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<ServiceableRegionsResponse>();

        vendors.MapPut("/{id:guid}/serviceable-regions", async (
                Guid id,
                SetServiceableRegionsBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new SetServiceableRegionsCommand(id, body.ServesAllIndia, body.Regions);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorServiceableRegionsSet")
            .WithSummary("Replaces the whole set of serviceability rules.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<ServiceableRegionsResponse>();
    }

    private static void MapStaff(IEndpointRouteBuilder vendors)
    {
        vendors.MapGet("/{id:guid}/staff", async (Guid id, IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListVendorStaffQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorStaffList")
            .WithSummary("Lists who operates this seller's account, the owner first.")
            .RequirePermission(VendorPermissions.VendorRead)
            .Produces<IReadOnlyList<VendorStaffResponse>>();

        vendors.MapPost("/{id:guid}/staff", async (
                Guid id,
                VendorStaffBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new AddVendorStaffCommand(id, body.UserId, body.IsOwner, body.JobTitle);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorStaffAdd")
            .WithSummary("Puts an existing user account on this seller. Create the account, and grant its "
                         + "vendor role, through the users endpoints first.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorStaffResponse>();

        vendors.MapPut("/{id:guid}/staff/{membershipId:guid}", async (
                Guid id,
                Guid membershipId,
                VendorStaffBody body,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateVendorStaffCommand(id, membershipId, body.IsOwner, body.JobTitle);
                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminVendorStaffUpdate")
            .WithSummary("Changes a member's job title, or moves the ownership to them.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<VendorStaffResponse>();

        vendors.MapDelete("/{id:guid}/staff/{membershipId:guid}", async (
                Guid id,
                Guid membershipId,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RemoveVendorStaffCommand(id, membershipId), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToNoContent(context);
            })
            .WithName("adminVendorStaffRemove")
            .WithSummary("Takes a person off this seller. Their user account is untouched.")
            .RequirePermission(VendorPermissions.VendorManage)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite);
    }
}
