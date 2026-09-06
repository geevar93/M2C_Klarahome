using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Vendors.Application.Validation;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>A place a courier collects from, as the API states it.</summary>
/// <param name="Id">The location.</param>
/// <param name="VendorId">The seller.</param>
/// <param name="Label">What the seller calls it.</param>
/// <param name="ContactName">Who the courier asks for.</param>
/// <param name="ContactPhone">The number they ring.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The state.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="IsDefault">Whether shipments are booked against it by default.</param>
/// <param name="IsActive">Whether the seller still collects from here.</param>
/// <param name="CourierLocationCode">The aggregator's id for it, once registered.</param>
internal sealed record PickupLocationResponse(
    Guid Id,
    Guid? VendorId,
    string Label,
    string ContactName,
    string ContactPhone,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    bool IsDefault,
    bool IsActive,
    string? CourierLocationCode);

/// <summary>One serviceability rule, as the API states it.</summary>
/// <param name="Id">The rule.</param>
/// <param name="Scope">Whether it names a state or a PIN code prefix.</param>
/// <param name="StateId">The state, for a state rule.</param>
/// <param name="PincodePrefix">The prefix, for a PIN code rule.</param>
/// <param name="IsExcluded">Whether it takes a region away rather than adding one.</param>
internal sealed record ServiceableRegionResponse(
    Guid Id,
    ServiceableRegionScope Scope,
    Guid? StateId,
    string? PincodePrefix,
    bool IsExcluded);

/// <summary>Where a seller delivers.</summary>
/// <param name="ServesAllIndia">Whether they deliver everywhere, in which case the rules are ignored.</param>
/// <param name="Regions">The rules, when they do not.</param>
internal sealed record ServiceableRegionsResponse(
    bool ServesAllIndia,
    IReadOnlyList<ServiceableRegionResponse> Regions);

/// <summary>One serviceability rule, as the API accepts it.</summary>
/// <param name="Scope">Whether it names a state or a PIN code prefix.</param>
/// <param name="StateId">The state, for a state rule.</param>
/// <param name="PincodePrefix">The prefix, for a PIN code rule.</param>
/// <param name="IsExcluded">Whether it takes a region away rather than adding one.</param>
internal sealed record ServiceableRegionPayload(
    ServiceableRegionScope Scope,
    Guid? StateId,
    string? PincodePrefix,
    bool IsExcluded);

/// <summary>Lists a seller's pickup locations.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record ListPickupLocationsQuery(Guid? VendorId) : IQuery<IReadOnlyList<PickupLocationResponse>>;

/// <summary>Adds a pickup location.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="Label">What the seller calls it.</param>
/// <param name="ContactName">Who the courier asks for.</param>
/// <param name="ContactPhone">The number they ring.</param>
/// <param name="Line1">Building and unit.</param>
/// <param name="Line2">Street, area or locality.</param>
/// <param name="Landmark">A nearby landmark.</param>
/// <param name="City">City or town.</param>
/// <param name="StateId">The state.</param>
/// <param name="Pincode">Six-digit PIN code.</param>
/// <param name="MakeDefault">Whether shipments should be booked against it by default.</param>
internal sealed record AddPickupLocationCommand(
    Guid? VendorId,
    string Label,
    string ContactName,
    string ContactPhone,
    string Line1,
    string? Line2,
    string? Landmark,
    string City,
    Guid StateId,
    string Pincode,
    bool MakeDefault) : ICommand<PickupLocationResponse>;

/// <summary>Changes a pickup location.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="LocationId">The location.</param>
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
/// <param name="MakeDefault">Whether it should become the default.</param>
internal sealed record UpdatePickupLocationCommand(
    Guid? VendorId,
    Guid LocationId,
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
    bool MakeDefault) : ICommand<PickupLocationResponse>;

/// <summary>Removes a pickup location.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="LocationId">The location.</param>
internal sealed record RemovePickupLocationCommand(Guid? VendorId, Guid LocationId) : ICommand;

/// <summary>Reads where a seller delivers.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record GetServiceableRegionsQuery(Guid? VendorId) : IQuery<ServiceableRegionsResponse>;

/// <summary>Replaces the whole set of serviceability rules.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="ServesAllIndia">Whether they deliver everywhere.</param>
/// <param name="Regions">The rules, when they do not.</param>
internal sealed record SetServiceableRegionsCommand(
    Guid? VendorId,
    bool ServesAllIndia,
    IReadOnlyList<ServiceableRegionPayload> Regions) : ICommand<ServiceableRegionsResponse>;

/// <summary>Rules for a pickup location.</summary>
internal sealed class AddPickupLocationValidator : AbstractValidator<AddPickupLocationCommand>
{
    public AddPickupLocationValidator()
    {
        RuleFor(command => command.Label).NotEmpty().MaximumLength(80);
        RuleFor(command => command.ContactName).NotEmpty().MaximumLength(120);
        RuleFor(command => command.ContactPhone).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Line1).NotEmpty().MaximumLength(200);
        RuleFor(command => command.City).NotEmpty().MaximumLength(120);
        RuleFor(command => command.StateId).NotEmpty();
        RuleFor(command => command.Pincode)
            .Matches(VendorFormats.Pincode())
            .WithMessage("Enter a valid six-digit PIN code.");
    }
}

/// <summary>Rules for a change to a pickup location.</summary>
internal sealed class UpdatePickupLocationValidator : AbstractValidator<UpdatePickupLocationCommand>
{
    public UpdatePickupLocationValidator()
    {
        RuleFor(command => command.LocationId).NotEmpty();
        RuleFor(command => command.Label).NotEmpty().MaximumLength(80);
        RuleFor(command => command.ContactName).NotEmpty().MaximumLength(120);
        RuleFor(command => command.ContactPhone).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Line1).NotEmpty().MaximumLength(200);
        RuleFor(command => command.City).NotEmpty().MaximumLength(120);
        RuleFor(command => command.StateId).NotEmpty();
        RuleFor(command => command.Pincode)
            .Matches(VendorFormats.Pincode())
            .WithMessage("Enter a valid six-digit PIN code.");
    }
}

/// <summary>Rules for a set of serviceability rules.</summary>
internal sealed class SetServiceableRegionsValidator : AbstractValidator<SetServiceableRegionsCommand>
{
    /// <summary>How many rules one seller may declare.</summary>
    /// <remarks>
    /// India has 36 states and union territories and about 19 000 PIN codes. A seller expressing
    /// their reach in more than two hundred rules is pasting a spreadsheet, and the rule set is read
    /// on every serviceability check.
    /// </remarks>
    public const int MaxRegions = 200;

    public SetServiceableRegionsValidator()
    {
        RuleFor(command => command.Regions).NotNull();
        RuleFor(command => command.Regions.Count).LessThanOrEqualTo(MaxRegions);

        RuleForEach(command => command.Regions).ChildRules(region =>
        {
            region.RuleFor(rule => rule.StateId)
                .NotNull()
                .When(rule => rule.Scope == ServiceableRegionScope.State)
                .WithMessage("A state rule needs a state.");

            region.RuleFor(rule => rule.PincodePrefix!)
                .NotEmpty()
                .Matches(VendorFormats.PincodePrefix())
                .When(rule => rule.Scope == ServiceableRegionScope.PincodePrefix)
                .WithMessage("A PIN code prefix is two to six digits.");
        });
    }
}

/// <summary>Lists a seller's pickup locations.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class ListPickupLocationsQueryHandler(VendorsDbContext context, VendorScope scope)
    : IQueryHandler<ListPickupLocationsQuery, IReadOnlyList<PickupLocationResponse>>
{
    public async Task<Result<IReadOnlyList<PickupLocationResponse>>> HandleAsync(
        ListPickupLocationsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var locations = await context.PickupLocations
            .AsNoTracking()
            .Where(location => location.VendorId == resolved.Value)
            .OrderByDescending(location => location.IsDefault)
            .ThenBy(location => location.Label)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<PickupLocationResponse>>(
            locations.ConvertAll(LogisticsProjection.ToResponse));
    }
}

/// <summary>Adds a pickup location.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="reference">Validates the state.</param>
internal sealed class AddPickupLocationCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IReferenceData reference) : ICommandHandler<AddPickupLocationCommand, PickupLocationResponse>
{
    public async Task<Result<PickupLocationResponse>> HandleAsync(
        AddPickupLocationCommand command,
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

        var known = await reference.StateExistsAsync(command.StateId, cancellationToken).ConfigureAwait(false);

        if (!known)
        {
            return LogisticsProjection.UnknownState();
        }

        var existing = await context.PickupLocations
            .Where(location => location.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var location = VendorPickupLocation.Add(
            vendor.Id,
            command.Label.Trim(),
            command.ContactName.Trim(),
            command.ContactPhone.Trim(),
            command.Line1.Trim(),
            command.City.Trim(),
            command.StateId,
            command.Pincode.Trim());

        location.Update(
            command.Label.Trim(),
            command.ContactName.Trim(),
            command.ContactPhone.Trim(),
            command.Line1.Trim(),
            command.Line2,
            command.Landmark,
            command.City.Trim(),
            command.StateId,
            command.Pincode.Trim());

        // The first location is always the default, for the same reason the first bank account is
        // always primary: one location and no default is a shipment that cannot be booked.
        if (command.MakeDefault || existing.Count == 0)
        {
            foreach (var other in existing)
            {
                other.SetDefault(false);
            }

            location.SetDefault(true);
        }

        context.PickupLocations.Add(location);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(LogisticsProjection.ToResponse(location));
    }
}

/// <summary>Changes a pickup location.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="reference">Validates the state.</param>
internal sealed class UpdatePickupLocationCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IReferenceData reference) : ICommandHandler<UpdatePickupLocationCommand, PickupLocationResponse>
{
    public async Task<Result<PickupLocationResponse>> HandleAsync(
        UpdatePickupLocationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var known = await reference.StateExistsAsync(command.StateId, cancellationToken).ConfigureAwait(false);

        if (!known)
        {
            return LogisticsProjection.UnknownState();
        }

        var locations = await context.PickupLocations
            .Where(location => location.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var location = locations.Find(candidate => candidate.Id == command.LocationId);

        if (location is null)
        {
            return VendorErrors.ChildNotFound("pickup location");
        }

        location.Update(
            command.Label.Trim(),
            command.ContactName.Trim(),
            command.ContactPhone.Trim(),
            command.Line1.Trim(),
            command.Line2,
            command.Landmark,
            command.City.Trim(),
            command.StateId,
            command.Pincode.Trim());

        location.SetActive(command.IsActive);

        if (command.MakeDefault)
        {
            foreach (var other in locations)
            {
                other.SetDefault(other.Id == location.Id);
            }
        }

        // A retired location that is still the default would be chosen for every shipment and then
        // refused by the courier.
        if (!command.IsActive && location.IsDefault)
        {
            location.SetDefault(false);

            var replacement = locations.Find(candidate => candidate.Id != location.Id && candidate.IsActive);
            replacement?.SetDefault(true);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(LogisticsProjection.ToResponse(location));
    }
}

/// <summary>Removes a pickup location.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class RemovePickupLocationCommandHandler(VendorsDbContext context, VendorScope scope)
    : ICommandHandler<RemovePickupLocationCommand>
{
    public async Task<Result> HandleAsync(
        RemovePickupLocationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var locations = await context.PickupLocations
            .Where(location => location.VendorId == resolved.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var location = locations.Find(candidate => candidate.Id == command.LocationId);

        if (location is null)
        {
            return Result.Failure(VendorErrors.ChildNotFound("pickup location"));
        }

        if (locations.Count == 1)
        {
            return Result.Failure(VendorErrors.LastOne("pickup location"));
        }

        context.PickupLocations.Remove(location);

        if (location.IsDefault)
        {
            var replacement = locations.Find(candidate => candidate.Id != location.Id && candidate.IsActive)
                              ?? locations.Find(candidate => candidate.Id != location.Id);

            replacement?.SetDefault(true);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Reads where a seller delivers.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
internal sealed class GetServiceableRegionsQueryHandler(VendorsDbContext context, VendorScope scope)
    : IQueryHandler<GetServiceableRegionsQuery, ServiceableRegionsResponse>
{
    public async Task<Result<ServiceableRegionsResponse>> HandleAsync(
        GetServiceableRegionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        var regions = await context.ServiceableRegions
            .AsNoTracking()
            .Where(region => region.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new ServiceableRegionsResponse(
            vendor.ServesAllIndia,
            regions.ConvertAll(LogisticsProjection.ToResponse)));
    }
}

/// <summary>Replaces the whole set of serviceability rules.</summary>
/// <remarks>
/// Wholesale rather than one rule at a time, for the same reason commission rules are replaced
/// wholesale: the set is read as a whole and edited as a whole, and a partial edit of an
/// allow-list with exclusions is how a seller ends up delivering somewhere they meant to exclude.
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="reference">Validates each state named.</param>
internal sealed class SetServiceableRegionsCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IReferenceData reference) : ICommandHandler<SetServiceableRegionsCommand, ServiceableRegionsResponse>
{
    public async Task<Result<ServiceableRegionsResponse>> HandleAsync(
        SetServiceableRegionsCommand command,
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

        if (!command.ServesAllIndia && !command.Regions.Any(region => !region.IsExcluded))
        {
            return VendorErrors.NotReady(
                "A seller who does not serve all of India needs at least one region they do serve.");
        }

        foreach (var stateId in command.Regions
                     .Where(region => region.Scope == ServiceableRegionScope.State)
                     .Select(region => region.StateId!.Value)
                     .Distinct())
        {
            var known = await reference.StateExistsAsync(stateId, cancellationToken).ConfigureAwait(false);

            if (!known)
            {
                return LogisticsProjection.UnknownState();
            }
        }

        var existing = await context.ServiceableRegions
            .Where(region => region.VendorId == vendor.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.ServiceableRegions.RemoveRange(existing);

        var replacements = command.Regions
            .Select(region => region.Scope == ServiceableRegionScope.State
                ? VendorServiceableRegion.ForState(vendor.Id, region.StateId!.Value, region.IsExcluded)
                : VendorServiceableRegion.ForPincode(vendor.Id, region.PincodePrefix!, region.IsExcluded))
            .ToList();

        context.ServiceableRegions.AddRange(replacements);

        vendor.UpdateOperations(vendor.DispatchSlaHours, vendor.ReturnPolicy, command.ServesAllIndia);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new ServiceableRegionsResponse(
            vendor.ServesAllIndia,
            replacements.ConvertAll(LogisticsProjection.ToResponse)));
    }
}

/// <summary>Maps the logistics entities onto their API shapes.</summary>
internal static class LogisticsProjection
{
    /// <summary>The refusal for a state id that is not one.</summary>
    public static Error UnknownState()
        => Error.Validation(
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["stateId"] = ["That is not a state or union territory."],
            });

    /// <summary>Builds a pickup-location response.</summary>
    /// <param name="location">The location.</param>
    public static PickupLocationResponse ToResponse(VendorPickupLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return new PickupLocationResponse(
            location.Id,
            location.VendorId,
            location.Label,
            location.ContactName,
            location.ContactPhone,
            location.Line1,
            location.Line2,
            location.Landmark,
            location.City,
            location.StateId,
            location.Pincode,
            location.IsDefault,
            location.IsActive,
            location.CourierLocationCode);
    }

    /// <summary>Builds a serviceability-rule response.</summary>
    /// <param name="region">The rule.</param>
    public static ServiceableRegionResponse ToResponse(VendorServiceableRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);

        return new ServiceableRegionResponse(
            region.Id,
            region.Scope,
            region.StateId,
            region.PincodePrefix,
            region.IsExcluded);
    }
}
