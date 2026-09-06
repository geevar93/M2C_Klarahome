using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Modules.Vendors.Application.Validation;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Payments;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>An address, as the API states and accepts it.</summary>
/// <param name="Line1">House, flat or building.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The <c>platform.states</c> row.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
internal sealed record AddressPayload(string Line1, string? Line2, string City, Guid StateId, string Pincode);

/// <summary>A return policy, as the API states and accepts it.</summary>
/// <param name="AcceptsReturns">Whether the seller accepts returns at all.</param>
/// <param name="WindowDays">How many days after delivery a return may be raised.</param>
/// <param name="AcceptsExchanges">Whether an exchange is offered as well as a refund.</param>
/// <param name="CustomerPaysReturnShipping">Who pays return shipping when the reason is not a defect.</param>
/// <param name="Notes">The seller's own wording.</param>
internal sealed record ReturnPolicyPayload(
    bool AcceptsReturns,
    int WindowDays,
    bool AcceptsExchanges,
    bool CustomerPaysReturnShipping,
    string? Notes);

/// <summary>A seller in a list.</summary>
/// <param name="Id">The seller.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">The registered name.</param>
/// <param name="DisplayName">The name shoppers see.</param>
/// <param name="Slug">The storefront path segment.</param>
/// <param name="Status">Where they are in the life cycle.</param>
/// <param name="Rating">Their average review score.</param>
/// <param name="CommissionPlanId">The plan they are on.</param>
/// <param name="OnboardedAt">When they first started trading.</param>
/// <param name="CreatedAt">When they applied.</param>
internal sealed record VendorListItem(
    Guid Id,
    string Code,
    string LegalName,
    string DisplayName,
    string Slug,
    VendorStatus Status,
    decimal? Rating,
    Guid? CommissionPlanId,
    DateTimeOffset? OnboardedAt,
    DateTimeOffset CreatedAt);

/// <summary>Everything an administrator or the seller themselves sees about a seller.</summary>
/// <param name="Id">The seller.</param>
/// <param name="Code">Their short code.</param>
/// <param name="LegalName">The registered name, which appears on the tax invoice.</param>
/// <param name="DisplayName">The name shoppers see.</param>
/// <param name="Slug">The storefront path segment.</param>
/// <param name="Status">Where they are in the life cycle.</param>
/// <param name="StatusReason">Why they were last suspended or offboarded.</param>
/// <param name="BusinessType">The legal form they trade as.</param>
/// <param name="Pan">Their PAN.</param>
/// <param name="Gstin">Their GSTIN, or null.</param>
/// <param name="RegisteredAddress">Their registered address.</param>
/// <param name="SupportEmail">Where an escalation reaches them.</param>
/// <param name="SupportPhone">Their support number.</param>
/// <param name="About">Their storefront blurb.</param>
/// <param name="LogoFileId">Their logo, as a media file id.</param>
/// <param name="BannerFileId">Their banner, as a media file id.</param>
/// <param name="CommissionPlanId">The plan they are on.</param>
/// <param name="DispatchSlaHours">Hours to hand a parcel to a courier.</param>
/// <param name="ReturnPolicy">What they promise about returns.</param>
/// <param name="ServesAllIndia">Whether they deliver everywhere.</param>
/// <param name="Rating">Their average review score.</param>
/// <param name="GatewayAccountId">Their payout account at the gateway, once one exists.</param>
/// <param name="OnboardedAt">When they first started trading.</param>
/// <param name="CreatedAt">When they applied.</param>
internal sealed record VendorResponse(
    Guid Id,
    string Code,
    string LegalName,
    string DisplayName,
    string Slug,
    VendorStatus Status,
    string? StatusReason,
    VendorBusinessType BusinessType,
    string? Pan,
    string? Gstin,
    AddressPayload RegisteredAddress,
    string? SupportEmail,
    string? SupportPhone,
    string? About,
    Guid? LogoFileId,
    Guid? BannerFileId,
    Guid? CommissionPlanId,
    int DispatchSlaHours,
    ReturnPolicyPayload ReturnPolicy,
    bool ServesAllIndia,
    decimal? Rating,
    string? GatewayAccountId,
    DateTimeOffset? OnboardedAt,
    DateTimeOffset CreatedAt);

/// <summary>A seller's public storefront profile.</summary>
/// <param name="Id">The seller.</param>
/// <param name="DisplayName">The name shoppers see.</param>
/// <param name="Slug">The storefront path segment.</param>
/// <param name="About">Their blurb.</param>
/// <param name="LogoFileId">Their logo.</param>
/// <param name="BannerFileId">Their banner.</param>
/// <param name="Rating">Their average review score.</param>
/// <param name="DispatchSlaHours">How quickly they dispatch.</param>
/// <param name="ReturnPolicy">What they promise about returns.</param>
/// <param name="OnboardedAt">Since when they have sold here.</param>
internal sealed record StorefrontVendorResponse(
    Guid Id,
    string DisplayName,
    string Slug,
    string? About,
    Guid? LogoFileId,
    Guid? BannerFileId,
    decimal? Rating,
    int DispatchSlaHours,
    ReturnPolicyPayload ReturnPolicy,
    DateTimeOffset? OnboardedAt);

/// <summary>Why a seller cannot yet be activated.</summary>
/// <param name="IsReady">Whether every requirement is met.</param>
/// <param name="Blockers">What is missing, in words an operator can act on. Empty when ready.</param>
internal sealed record VendorReadiness(bool IsReady, IReadOnlyList<string> Blockers);

/// <summary>Lists sellers, newest first.</summary>
/// <param name="Status">Restrict to one life-cycle state.</param>
/// <param name="Search">A fragment of a code, legal name or display name.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListVendorsQuery(string? Status, string? Search, string? Cursor, int? Size)
    : IQuery<PagedResult<VendorListItem>>;

/// <summary>Reads one seller.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record GetVendorQuery(Guid? VendorId) : IQuery<VendorResponse>;

/// <summary>Reads a seller's public storefront profile by slug.</summary>
/// <param name="Slug">The storefront path segment.</param>
internal sealed record GetStorefrontVendorQuery(string Slug) : IQuery<StorefrontVendorResponse>;

/// <summary>Reports whether a seller meets every requirement for activation.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record GetVendorReadinessQuery(Guid? VendorId) : IQuery<VendorReadiness>;

/// <summary>Registers an application to sell.</summary>
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
internal sealed record CreateVendorCommand(
    string LegalName,
    string? DisplayName,
    VendorBusinessType BusinessType,
    string? Code,
    string? Slug,
    string? Pan,
    string? Gstin,
    AddressPayload? RegisteredAddress,
    string? SupportEmail,
    string? SupportPhone) : ICommand<VendorResponse>;

/// <summary>Updates the registration details an approval depends on.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="LegalName">The registered name.</param>
/// <param name="BusinessType">The legal form.</param>
/// <param name="Pan">Their PAN.</param>
/// <param name="Gstin">Their GSTIN, or null.</param>
/// <param name="RegisteredAddress">Their registered address.</param>
internal sealed record UpdateVendorBusinessCommand(
    Guid? VendorId,
    string LegalName,
    VendorBusinessType BusinessType,
    string? Pan,
    string? Gstin,
    AddressPayload RegisteredAddress) : ICommand<VendorResponse>;

/// <summary>Updates the storefront profile.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="DisplayName">The name shoppers see.</param>
/// <param name="About">The storefront blurb.</param>
/// <param name="LogoFileId">Their logo, as a media file id.</param>
/// <param name="BannerFileId">Their banner, as a media file id.</param>
/// <param name="SupportEmail">Where an escalation reaches them.</param>
/// <param name="SupportPhone">Their support number.</param>
internal sealed record UpdateVendorProfileCommand(
    Guid? VendorId,
    string DisplayName,
    string? About,
    Guid? LogoFileId,
    Guid? BannerFileId,
    string? SupportEmail,
    string? SupportPhone) : ICommand<VendorResponse>;

/// <summary>Updates the operational settings fulfilment reads.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="DispatchSlaHours">Hours to hand a parcel to a courier.</param>
/// <param name="ReturnPolicy">What they promise about returns.</param>
/// <param name="ServesAllIndia">Whether they deliver everywhere.</param>
internal sealed record UpdateVendorOperationsCommand(
    Guid? VendorId,
    int DispatchSlaHours,
    ReturnPolicyPayload ReturnPolicy,
    bool ServesAllIndia) : ICommand<VendorResponse>;

/// <summary>Moves a seller through the onboarding life cycle.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="Status">Where to take them.</param>
/// <param name="Reason">Why, for a suspension or an offboarding.</param>
internal sealed record ChangeVendorStatusCommand(Guid VendorId, VendorStatus Status, string? Reason)
    : ICommand<VendorResponse>;

/// <summary>Puts a seller on a commission plan.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="PlanId">The plan, or null to remove the assignment.</param>
internal sealed record AssignCommissionPlanCommand(Guid VendorId, Guid? PlanId) : ICommand<VendorResponse>;

/// <summary>Rules an application can get wrong before anything is stored.</summary>
internal sealed class CreateVendorValidator : AbstractValidator<CreateVendorCommand>
{
    public CreateVendorValidator()
    {
        RuleFor(command => command.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(command => command.DisplayName!).MaximumLength(120).When(command => command.DisplayName is not null);

        RuleFor(command => command.Code!)
            .MaximumLength(32)
            .Matches(VendorFormats.Slug())
            .WithMessage("A vendor code may contain only lowercase letters, digits and hyphens.")
            .When(command => !string.IsNullOrWhiteSpace(command.Code));

        RuleFor(command => command.Slug!)
            .MaximumLength(140)
            .Matches(VendorFormats.Slug())
            .WithMessage("A slug may contain only lowercase letters, digits and hyphens.")
            .When(command => !string.IsNullOrWhiteSpace(command.Slug));

        Include(new VendorIdentifierRules<CreateVendorCommand>(
            command => command.Pan,
            command => command.Gstin));

        RuleFor(command => command.SupportEmail!)
            .EmailAddress()
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.SupportEmail));
    }
}

/// <summary>Rules for a change to the registration details.</summary>
internal sealed class UpdateVendorBusinessValidator : AbstractValidator<UpdateVendorBusinessCommand>
{
    public UpdateVendorBusinessValidator()
    {
        RuleFor(command => command.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(command => command.RegisteredAddress).NotNull().SetValidator(new AddressPayloadValidator());

        Include(new VendorIdentifierRules<UpdateVendorBusinessCommand>(
            command => command.Pan,
            command => command.Gstin));
    }
}

/// <summary>Rules for a change to the storefront profile.</summary>
internal sealed class UpdateVendorProfileValidator : AbstractValidator<UpdateVendorProfileCommand>
{
    public UpdateVendorProfileValidator()
    {
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(120);
        RuleFor(command => command.About!).MaximumLength(4000).When(command => command.About is not null);

        RuleFor(command => command.SupportEmail!)
            .EmailAddress()
            .MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.SupportEmail));
    }
}

/// <summary>Rules for a change to the operational settings.</summary>
internal sealed class UpdateVendorOperationsValidator : AbstractValidator<UpdateVendorOperationsCommand>
{
    public UpdateVendorOperationsValidator()
    {
        RuleFor(command => command.DispatchSlaHours).InclusiveBetween(1, Vendor.MaxDispatchSlaHours);
        RuleFor(command => command.ReturnPolicy).NotNull();
        RuleFor(command => command.ReturnPolicy.WindowDays).InclusiveBetween(0, 90);
        RuleFor(command => command.ReturnPolicy.Notes!)
            .MaximumLength(2000)
            .When(command => command.ReturnPolicy?.Notes is not null);
    }
}

/// <summary>Rules for a life-cycle move.</summary>
internal sealed class ChangeVendorStatusValidator : AbstractValidator<ChangeVendorStatusCommand>
{
    public ChangeVendorStatusValidator()
    {
        RuleFor(command => command.VendorId).NotEmpty();

        // A suspension nobody explained is a seller who cannot tell their customers why their
        // listings vanished, and a support call the operator cannot answer either.
        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(500)
            .When(command => command.Status is VendorStatus.Suspended or VendorStatus.Offboarded)
            .WithMessage("Say why. The seller is told this, and so is whoever reads the audit trail.");
    }
}

/// <summary>The address rules, shared by every command that accepts one.</summary>
internal sealed class AddressPayloadValidator : AbstractValidator<AddressPayload>
{
    public AddressPayloadValidator()
    {
        RuleFor(address => address.Line1).NotEmpty().MaximumLength(200);
        RuleFor(address => address.Line2!).MaximumLength(200).When(address => address.Line2 is not null);
        RuleFor(address => address.City).NotEmpty().MaximumLength(120);
        RuleFor(address => address.StateId).NotEmpty();
        RuleFor(address => address.Pincode)
            .NotEmpty()
            .Matches(VendorFormats.Pincode())
            .WithMessage("Enter a valid six-digit PIN code.");
    }
}

/// <summary>
/// The PAN and GSTIN rules, written once and included by every command that carries the pair.
/// </summary>
/// <remarks>
/// A rule set rather than a copied block, because the cross-check — that the GSTIN embeds the PAN —
/// is the kind of rule that gets added on one command and forgotten on the other.
/// </remarks>
/// <typeparam name="TCommand">The command carrying the identifiers.</typeparam>
/// <param name="pan">Reads the PAN off the command.</param>
/// <param name="gstin">Reads the GSTIN off the command.</param>
internal sealed class VendorIdentifierRules<TCommand>(
    Func<TCommand, string?> pan,
    Func<TCommand, string?> gstin) : AbstractValidator<TCommand>
{
    private readonly Func<TCommand, string?> _pan = pan;
    private readonly Func<TCommand, string?> _gstin = gstin;

    /// <inheritdoc />
    public override FluentValidation.Results.ValidationResult Validate(
        FluentValidation.ValidationContext<TCommand> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        var pan = Normalize(_pan(context.InstanceToValidate));
        var gstin = Normalize(_gstin(context.InstanceToValidate));

        if (pan is not null && !VendorFormats.Pan().IsMatch(pan))
        {
            failures.Add(new FluentValidation.Results.ValidationFailure(
                "pan",
                "A PAN is five letters, four digits and a letter — for example ABCDE1234F."));
        }

        if (gstin is not null)
        {
            if (!VendorFormats.Gstin().IsMatch(gstin))
            {
                failures.Add(new FluentValidation.Results.ValidationFailure(
                    "gstin",
                    "A GSTIN is fifteen characters: a state code, a PAN, an entity number, Z and a check digit."));
            }
            else if (!VendorFormats.HasKnownStateCode(gstin))
            {
                failures.Add(new FluentValidation.Results.ValidationFailure(
                    "gstin",
                    "That GSTIN begins with a state code that does not exist."));
            }
            else if (!VendorFormats.PanMatchesGstin(pan, gstin))
            {
                failures.Add(new FluentValidation.Results.ValidationFailure(
                    "gstin",
                    "That GSTIN belongs to a different PAN. Characters 3 to 12 of a GSTIN are the holder's PAN."));
            }
        }

        return new FluentValidation.Results.ValidationResult(failures);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

/// <summary>Lists sellers.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Confines a vendor caller to their own seller.</param>
internal sealed class ListVendorsQueryHandler(VendorsDbContext context, VendorScope scope)
    : IQueryHandler<ListVendorsQuery, PagedResult<VendorListItem>>
{
    public async Task<Result<PagedResult<VendorListItem>>> HandleAsync(
        ListVendorsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var vendors = context.Vendors.AsNoTracking().AsQueryable();

        // A vendor user listing sellers sees exactly one: their own. The vendors table cannot carry
        // the global vendor filter — its scope column is its primary key — so the restriction is
        // applied here, at the only listing that reads it.
        if (scope.CallerVendorId is { } own)
        {
            vendors = vendors.Where(vendor => vendor.Id == own);
        }

        if (Enum.TryParse<VendorStatus>(query.Status, ignoreCase: true, out var status))
        {
            vendors = vendors.Where(vendor => vendor.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ILIKE rather than a lowered Contains: Postgres does the case folding, so the search
            // is case-insensitive in the database instead of depending on the collation the column
            // happens to have. The caller's own % and _ are escaped, or a search for "50%" would
            // match every seller.
            var term = query.Search.Trim()
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);

            var pattern = $"%{term}%";

            vendors = vendors.Where(vendor =>
                EF.Functions.ILike(vendor.Code, pattern, "\\")
                || EF.Functions.ILike(vendor.LegalName, pattern, "\\")
                || EF.Functions.ILike(vendor.DisplayName, pattern, "\\"));
        }

        // UUIDv7 ids are time-ordered, so the id alone is a stable keyset cursor for a newest-first
        // list: no second sort column, and no row skipped or repeated when a seller applies
        // mid-page.
        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            vendors = vendors.Where(vendor => vendor.Id.CompareTo(after) < 0);
        }

        var page = await vendors
            .OrderByDescending(vendor => vendor.Id)
            .Take(size + 1)
            .Select(vendor => new VendorListItem(
                vendor.Id,
                vendor.Code,
                vendor.LegalName,
                vendor.DisplayName,
                vendor.Slug,
                vendor.Status,
                vendor.Rating,
                vendor.CommissionPlanId,
                vendor.OnboardedAt,
                vendor.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<VendorListItem>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one seller.</summary>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class GetVendorQueryHandler(VendorScope scope) : IQueryHandler<GetVendorQuery, VendorResponse>
{
    public async Task<Result<VendorResponse>> HandleAsync(GetVendorQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        return vendor is null
            ? VendorErrors.NotFound
            : Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>Reads a seller's public profile.</summary>
/// <param name="context">The Vendors data context.</param>
internal sealed class GetStorefrontVendorQueryHandler(VendorsDbContext context)
    : IQueryHandler<GetStorefrontVendorQuery, StorefrontVendorResponse>
{
    public async Task<Result<StorefrontVendorResponse>> HandleAsync(
        GetStorefrontVendorQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var slug = query.Slug.Trim().ToLowerInvariant();

        var vendor = await context.Vendors
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        // A seller who is not trading has no storefront page, whatever the reason. Answering 404
        // rather than "suspended" keeps a commercial decision between the platform and the seller
        // instead of publishing it to every shopper who kept the link.
        return vendor is null || vendor.Status != VendorStatus.Active
            ? VendorErrors.NotFound
            : Result.Success(new StorefrontVendorResponse(
                vendor.Id,
                vendor.DisplayName,
                vendor.Slug,
                vendor.About,
                vendor.LogoFileId,
                vendor.BannerFileId,
                vendor.Rating,
                vendor.DispatchSlaHours,
                VendorProjection.ToPayload(vendor.ReturnPolicy),
                vendor.OnboardedAt));
    }
}

/// <summary>Reports what still stands between a seller and activation.</summary>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="readiness">Performs the checks.</param>
internal sealed class GetVendorReadinessQueryHandler(VendorScope scope, VendorReadinessService readiness)
    : IQueryHandler<GetVendorReadinessQuery, VendorReadiness>
{
    public async Task<Result<VendorReadiness>> HandleAsync(
        GetVendorReadinessQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        return vendor is null
            ? VendorErrors.NotFound
            : Result.Success(await readiness.EvaluateAsync(vendor, cancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Registers an application to sell.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Refuses a vendor caller, and mints the code.</param>
/// <param name="reference">Validates the state of the registered address.</param>
/// <param name="options">Supplies the starting SLA and return window.</param>
/// <param name="audit">Records the application.</param>
internal sealed class CreateVendorCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IReferenceData reference,
    IOptions<VendorOptions> options,
    IAuditLogger audit) : ICommandHandler<CreateVendorCommand, VendorResponse>
{
    /// <summary>The audited action for a new application.</summary>
    public const string AuditAction = "vendors.vendor.applied";

    /// <summary>The entity type recorded against every vendor action.</summary>
    public const string AuditEntityType = "Vendor";

    public async Task<Result<VendorResponse>> HandleAsync(
        CreateVendorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A seller cannot create a seller. The permission alone would allow it — a vendor owner
        // holds vendors.vendor.manage so they can edit their own profile — so the scope is what
        // stops it.
        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? command.LegalName.Trim()
            : command.DisplayName.Trim();

        var slug = string.IsNullOrWhiteSpace(command.Slug)
            ? VendorFormats.ToSlug(displayName)
            : command.Slug.Trim().ToLowerInvariant();

        if (slug.Length == 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["slug"] = ["That name produces an empty slug. Supply one explicitly."],
                });
        }

        var code = string.IsNullOrWhiteSpace(command.Code)
            ? await scope.NextCodeAsync(cancellationToken).ConfigureAwait(false)
            : command.Code.Trim().ToUpperInvariant();

        var clash = await context.Vendors
            .AnyAsync(vendor => vendor.Code == code || vendor.Slug == slug, cancellationToken)
            .ConfigureAwait(false);

        if (clash)
        {
            return VendorErrors.Duplicate("code or storefront slug");
        }

        if (command.RegisteredAddress is { } address)
        {
            var known = await reference
                .StateExistsAsync(address.StateId, cancellationToken)
                .ConfigureAwait(false);

            if (!known)
            {
                return Error.Validation(
                    new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                    {
                        ["registeredAddress.stateId"] = ["That is not a state or union territory."],
                    });
            }
        }

        var vendor = Vendor.Apply(code, command.LegalName.Trim(), displayName, slug, command.BusinessType);

        vendor.DescribeBusiness(
            command.LegalName.Trim(),
            command.BusinessType,
            command.Pan,
            command.Gstin,
            VendorProjection.ToAddress(command.RegisteredAddress));

        vendor.UpdateProfile(displayName, null, null, null, command.SupportEmail, command.SupportPhone);

        vendor.UpdateOperations(
            options.Value.DefaultDispatchSlaHours,
            new ReturnPolicy { AcceptsReturns = true, WindowDays = options.Value.DefaultReturnWindowDays },
            servesAllIndia: true);

        // A new seller is put on the default plan if there is one. Without it activation would
        // refuse them for a reason nobody chose, and an operator would have to assign a plan to
        // every applicant by hand.
        var defaultPlan = await context.CommissionPlans
            .AsNoTracking()
            .Where(plan => plan.IsDefault && plan.IsActive)
            .Select(plan => (Guid?)plan.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        vendor.AssignCommissionPlan(defaultPlan);

        context.Vendors.Add(vendor);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = vendor.Id.ToString(),
                After = new
                {
                    vendor.Code,
                    vendor.LegalName,
                    vendor.DisplayName,
                    BusinessType = vendor.BusinessType.ToString(),
                    vendor.Gstin,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>Updates the registration details.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="reference">Validates the state of the registered address.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateVendorBusinessCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IReferenceData reference,
    IAuditLogger audit) : ICommandHandler<UpdateVendorBusinessCommand, VendorResponse>
{
    /// <summary>The audited action for a change to the registration details.</summary>
    public const string AuditAction = "vendors.vendor.business-updated";

    public async Task<Result<VendorResponse>> HandleAsync(
        UpdateVendorBusinessCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        var known = await reference
            .StateExistsAsync(command.RegisteredAddress.StateId, cancellationToken)
            .ConfigureAwait(false);

        if (!known)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["registeredAddress.stateId"] = ["That is not a state or union territory."],
                });
        }

        var before = new { vendor.LegalName, vendor.Pan, vendor.Gstin, BusinessType = vendor.BusinessType.ToString() };

        vendor.DescribeBusiness(
            command.LegalName,
            command.BusinessType,
            command.Pan,
            command.Gstin,
            VendorProjection.ToAddress(command.RegisteredAddress));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateVendorCommandHandler.AuditEntityType,
                EntityId = vendor.Id.ToString(),
                Before = before,
                After = new
                {
                    vendor.LegalName,
                    vendor.Pan,
                    vendor.Gstin,
                    BusinessType = vendor.BusinessType.ToString(),
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>Updates the storefront profile.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class UpdateVendorProfileCommandHandler(VendorsDbContext context, VendorScope scope)
    : ICommandHandler<UpdateVendorProfileCommand, VendorResponse>
{
    public async Task<Result<VendorResponse>> HandleAsync(
        UpdateVendorProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        // Not audited. A blurb and a logo are business content, and putting every wording change
        // into the audit trail would bury the grants and suspensions it exists to make findable
        // (docs/07-security-compliance.md §7).
        vendor.UpdateProfile(
            command.DisplayName,
            command.About,
            command.LogoFileId,
            command.BannerFileId,
            command.SupportEmail,
            command.SupportPhone);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>Updates the operational settings.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class UpdateVendorOperationsCommandHandler(VendorsDbContext context, VendorScope scope)
    : ICommandHandler<UpdateVendorOperationsCommand, VendorResponse>
{
    public async Task<Result<VendorResponse>> HandleAsync(
        UpdateVendorOperationsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        // Clearing "serves all India" with no region rules would leave a seller who delivers
        // nowhere and does not know it — their listings would simply stop being buyable.
        if (!command.ServesAllIndia)
        {
            var hasRules = await context.ServiceableRegions
                .AnyAsync(region => region.VendorId == vendor.Id && !region.IsExcluded, cancellationToken)
                .ConfigureAwait(false);

            if (!hasRules)
            {
                return VendorErrors.NotReady(
                    "Add at least one serviceable region before turning off delivery to all of India.");
            }
        }

        vendor.UpdateOperations(
            command.DispatchSlaHours,
            new ReturnPolicy
            {
                AcceptsReturns = command.ReturnPolicy.AcceptsReturns,
                WindowDays = command.ReturnPolicy.WindowDays,
                AcceptsExchanges = command.ReturnPolicy.AcceptsExchanges,
                CustomerPaysReturnShipping = command.ReturnPolicy.CustomerPaysReturnShipping,
                Notes = command.ReturnPolicy.Notes,
            },
            command.ServesAllIndia);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>
/// Moves a seller through the onboarding life cycle, and announces the moves other modules care
/// about.
/// </summary>
/// <remarks>
/// One handler for every transition rather than five, because the guards are the same for all of
/// them and the differences — a readiness check on the way into <see cref="VendorStatus.Active"/>,
/// an event on the way in and out of it — are two <c>if</c>s. Five handlers would be five places to
/// forget the audit entry.
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller.</param>
/// <param name="readiness">Checks the activation requirements.</param>
/// <param name="publisher">Announces activation, suspension and offboarding.</param>
/// <param name="payouts">Provisions the gateway payout account on first activation.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the move.</param>
internal sealed class ChangeVendorStatusCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    VendorReadinessService readiness,
    VendorEventPublisher publisher,
    VendorPayoutProvisioner payouts,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<ChangeVendorStatusCommand, VendorResponse>
{
    /// <summary>The audited action for a life-cycle move.</summary>
    public const string AuditAction = "vendors.vendor.status-changed";

    public async Task<Result<VendorResponse>> HandleAsync(
        ChangeVendorStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Only platform staff move a seller through the life cycle. A seller approving themselves
        // is the failure this line exists to make impossible, whatever roles they hold.
        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var vendor = await scope.FindAsync(command.VendorId, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        var before = vendor.Status;

        if (!Vendor.IsTransitionAllowed(before, command.Status))
        {
            return VendorErrors.InvalidTransition(before, command.Status);
        }

        if (command.Status == VendorStatus.Active)
        {
            var state = await readiness.EvaluateAsync(vendor, cancellationToken).ConfigureAwait(false);

            if (!state.IsReady)
            {
                return VendorErrors.NotReady(string.Join(" ", state.Blockers));
            }
        }

        vendor.TransitionTo(command.Status, clock.UtcNow, command.Reason);

        // Enqueued before the save, so the event and the state change are one transaction: a
        // seller who is Active and an event that says so cannot come apart (ADR-003).
        publisher.Announce(vendor, command.Reason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateVendorCommandHandler.AuditEntityType,
                EntityId = vendor.Id.ToString(),
                Before = new { status = before.ToString() },
                After = new { status = vendor.Status.ToString(), reason = command.Reason },
            },
            cancellationToken).ConfigureAwait(false);

        // After the commit, and never allowed to undo it. A seller who is activated but whose
        // gateway account could not be created can trade and cannot yet be paid, which is a state
        // Settlements already handles; refusing the activation would be worse.
        if (vendor.Status == VendorStatus.Active && vendor.GatewayAccountId is null)
        {
            await payouts.TryProvisionAsync(vendor, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>Puts a seller on a commission plan.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller.</param>
/// <param name="audit">Records the change — this one decides money.</param>
internal sealed class AssignCommissionPlanCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IAuditLogger audit) : ICommandHandler<AssignCommissionPlanCommand, VendorResponse>
{
    /// <summary>The audited action for a plan assignment.</summary>
    public const string AuditAction = "vendors.vendor.commission-plan-assigned";

    public async Task<Result<VendorResponse>> HandleAsync(
        AssignCommissionPlanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A seller choosing what the platform charges them is not a thing.
        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var vendor = await scope.FindAsync(command.VendorId, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        if (command.PlanId is { } planId)
        {
            var usable = await context.CommissionPlans
                .AnyAsync(plan => plan.Id == planId && plan.IsActive, cancellationToken)
                .ConfigureAwait(false);

            if (!usable)
            {
                return VendorErrors.PlanNotFound;
            }
        }

        var before = vendor.CommissionPlanId;
        vendor.AssignCommissionPlan(command.PlanId);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateVendorCommandHandler.AuditEntityType,
                EntityId = vendor.Id.ToString(),
                Before = new { commissionPlanId = before },
                After = new { commissionPlanId = command.PlanId },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(VendorProjection.ToResponse(vendor));
    }
}

/// <summary>
/// Decides whether a seller may be activated (docs/02-domain-model.md §5).
/// </summary>
/// <remarks>
/// <para>
/// The aggregate owns which transitions <em>exist</em>; this owns which are currently
/// <em>earned</em>, because the answer depends on the seller's documents, their bank account and
/// their pickup points — three other aggregates the vendor cannot see from inside itself.
/// </para>
/// <para>
/// It reports every blocker rather than the first. An operator working through an onboarding queue
/// should be told the seller needs a verified PAN <em>and</em> a bank account, not sent round the
/// loop twice.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="options">Which requirements this deployment enforces.</param>
internal sealed class VendorReadinessService(VendorsDbContext context, IOptions<VendorOptions> options)
{
    /// <summary>
    /// The documents each legal form must produce. A company has to show its incorporation
    /// certificate; a person trading under their own name has nothing to show but their PAN.
    /// </summary>
    private static readonly KycDocumentType[] CompanyDocuments =
    [
        KycDocumentType.Pan,
        KycDocumentType.IncorporationCertificate,
        KycDocumentType.AddressProof,
    ];

    private static readonly KycDocumentType[] IndividualDocuments =
    [
        KycDocumentType.Pan,
        KycDocumentType.IdentityProof,
    ];

    /// <summary>Everything standing between this seller and trading.</summary>
    /// <param name="vendor">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<VendorReadiness> EvaluateAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        var settings = options.Value;
        var blockers = new List<string>();

        if (string.IsNullOrWhiteSpace(vendor.Pan))
        {
            blockers.Add("The seller has no PAN recorded.");
        }

        if (settings.RequireGstin && string.IsNullOrWhiteSpace(vendor.Gstin))
        {
            blockers.Add("This marketplace requires a GST registration.");
        }

        if (vendor.CommissionPlanId is null)
        {
            blockers.Add("No commission plan is assigned.");
        }

        if (settings.RequireVerifiedKyc)
        {
            var verified = await context.KycDocuments
                .AsNoTracking()
                .Where(document =>
                    document.VendorId == vendor.Id && document.Status == KycVerificationStatus.Verified)
                .Select(document => document.DocumentType)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var required = RequiredDocuments(vendor);
            var missing = required.Where(document => !verified.Contains(document)).ToList();

            if (missing.Count > 0)
            {
                blockers.Add($"These documents are not verified: {string.Join(", ", missing)}.");
            }
        }

        if (settings.RequireVerifiedBankAccount)
        {
            var payable = await context.BankAccounts
                .AsNoTracking()
                .AnyAsync(
                    account => account.VendorId == vendor.Id
                               && account.IsPrimary
                               && account.VerificationStatus == BankVerificationStatus.Verified,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!payable)
            {
                blockers.Add("There is no primary, verified bank account to pay them into.");
            }
        }

        if (settings.RequirePickupLocation)
        {
            var collectable = await context.PickupLocations
                .AsNoTracking()
                .AnyAsync(
                    location => location.VendorId == vendor.Id && location.IsActive,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!collectable)
            {
                blockers.Add("There is no active pickup location for a courier to collect from.");
            }
        }

        return new VendorReadiness(blockers.Count == 0, blockers);
    }

    /// <summary>The documents a seller's legal form has to produce.</summary>
    /// <param name="vendor">The seller.</param>
    internal static IReadOnlyList<KycDocumentType> RequiredDocuments(Vendor vendor)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        List<KycDocumentType> required = vendor.BusinessType switch
        {
            VendorBusinessType.Individual or VendorBusinessType.SoleProprietorship => [.. IndividualDocuments],
            _ => [.. CompanyDocuments],
        };

        // A seller who claims a GSTIN has to show the certificate for it. A seller below the
        // threshold has nothing to show, and demanding it would exclude them.
        if (!string.IsNullOrWhiteSpace(vendor.Gstin))
        {
            required.Add(KycDocumentType.Gstin);
        }

        return required;
    }
}

/// <summary>
/// Announces the life-cycle facts other modules act on.
/// </summary>
/// <remarks>
/// The outbox is asked for by the context it must join. The unkeyed <c>IOutbox</c> belongs to
/// whichever module registered first, so publishing through it from here would put the row in
/// another context's change tracker and lose it at save time — silently. Naming the context is what
/// makes the event and the state change one transaction.
/// </remarks>
/// <param name="outbox">This module's outbox.</param>
internal sealed class VendorEventPublisher(
    [FromKeyedServices(typeof(VendorsDbContext))] IOutbox outbox)
{
    /// <summary>Publishes the event, if any, that this seller's new status represents.</summary>
    /// <param name="vendor">The seller, already transitioned.</param>
    /// <param name="reason">Why, for a suspension or an offboarding.</param>
    public void Announce(Vendor vendor, string? reason)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        switch (vendor.Status)
        {
            case VendorStatus.Active:
                outbox.Enqueue(new VendorActivated(vendor.Id, vendor.Code, vendor.DisplayName));
                break;

            case VendorStatus.Suspended:
                outbox.Enqueue(new VendorSuspended(vendor.Id, reason));
                break;

            case VendorStatus.Offboarded:
                outbox.Enqueue(new VendorOffboarded(vendor.Id, reason));
                break;

            default:
                // Applied, UnderReview and Approved are internal to onboarding. Nothing outside
                // this module changes behaviour because a document is being read.
                break;
        }
    }
}

/// <summary>Maps the aggregate onto the API shapes.</summary>
internal static class VendorProjection
{
    /// <summary>Builds the administrative response.</summary>
    /// <param name="vendor">The seller.</param>
    public static VendorResponse ToResponse(Vendor vendor)
    {
        ArgumentNullException.ThrowIfNull(vendor);

        return new VendorResponse(
            vendor.Id,
            vendor.Code,
            vendor.LegalName,
            vendor.DisplayName,
            vendor.Slug,
            vendor.Status,
            vendor.StatusReason,
            vendor.BusinessType,
            vendor.Pan,
            vendor.Gstin,
            new AddressPayload(
                vendor.RegisteredAddress.Line1,
                vendor.RegisteredAddress.Line2,
                vendor.RegisteredAddress.City,
                vendor.RegisteredAddress.StateId,
                vendor.RegisteredAddress.Pincode),
            vendor.SupportEmail,
            vendor.SupportPhone,
            vendor.About,
            vendor.LogoFileId,
            vendor.BannerFileId,
            vendor.CommissionPlanId,
            vendor.DispatchSlaHours,
            ToPayload(vendor.ReturnPolicy),
            vendor.ServesAllIndia,
            vendor.Rating,
            vendor.GatewayAccountId,
            vendor.OnboardedAt,
            vendor.CreatedAt);
    }

    /// <summary>Maps a stored return policy onto its API shape.</summary>
    /// <param name="policy">The stored policy.</param>
    public static ReturnPolicyPayload ToPayload(ReturnPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new ReturnPolicyPayload(
            policy.AcceptsReturns,
            policy.WindowDays,
            policy.AcceptsExchanges,
            policy.CustomerPaysReturnShipping,
            policy.Notes);
    }

    /// <summary>Maps an API address onto the stored shape, tolerating an absent one.</summary>
    /// <param name="address">The address the caller sent, or null.</param>
    public static RegisteredAddress ToAddress(AddressPayload? address)
        => address is null
            ? new RegisteredAddress()
            : new RegisteredAddress
            {
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                StateId = address.StateId,
                Pincode = address.Pincode,
            };
}
