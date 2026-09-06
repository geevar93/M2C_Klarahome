using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Shipping.Application.Rates;

/// <summary>Lists the delivery map.</summary>
/// <param name="IncludeInactive">Whether to include zones that have been switched off.</param>
internal sealed record ListZonesQuery(bool IncludeInactive) : IQuery<IReadOnlyList<ShippingZoneResponse>>;

/// <summary>Opens a zone.</summary>
/// <param name="Code">The stable code rate rules refer to.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Priority">Lower wins where two zones both match.</param>
/// <param name="States">The states it covers.</param>
/// <param name="PincodeRanges">The PIN-code runs it covers.</param>
internal sealed record CreateZoneCommand(
    string Code,
    string Name,
    int Priority,
    IReadOnlyList<Guid> States,
    IReadOnlyList<PincodeRangeModel> PincodeRanges) : ICommand<ShippingZoneResponse>;

/// <summary>Redraws a zone.</summary>
/// <param name="ZoneId">The zone.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Priority">Lower wins where two zones both match.</param>
/// <param name="States">The states it covers.</param>
/// <param name="PincodeRanges">The PIN-code runs it covers.</param>
/// <param name="IsActive">Whether it is used when a rate is looked up.</param>
internal sealed record UpdateZoneCommand(
    Guid ZoneId,
    string Name,
    int Priority,
    IReadOnlyList<Guid> States,
    IReadOnlyList<PincodeRangeModel> PincodeRanges,
    bool IsActive) : ICommand<ShippingZoneResponse>;

/// <summary>Lists the rate card.</summary>
/// <param name="ZoneId">Filter to one zone.</param>
/// <param name="VendorId">Filter to one seller's overrides.</param>
/// <param name="Method">Filter to one service.</param>
/// <param name="IncludeInactive">Whether to include rules that have been switched off.</param>
internal sealed record ListRatesQuery(
    Guid? ZoneId,
    Guid? VendorId,
    string? Method,
    bool IncludeInactive) : IQuery<IReadOnlyList<ShippingRateResponse>>;

/// <summary>Everything a rate rule charges and promises.</summary>
/// <param name="MinWeightGrams">The lightest parcel it applies to.</param>
/// <param name="MaxWeightGrams">The heaviest.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxOrderValue">The largest, or null for none.</param>
/// <param name="BaseRate">The band's floor price, inclusive of tax.</param>
/// <param name="PerKgRate">What each further kilogram adds.</param>
/// <param name="FreeAbove">The basket value at or above which delivery is free.</param>
/// <param name="CodFee">What cash on delivery adds.</param>
/// <param name="IsCodAllowed">Whether cash on delivery may be chosen on this service.</param>
/// <param name="EtaMinDays">The earliest delivery, in days from dispatch.</param>
/// <param name="EtaMaxDays">The latest.</param>
internal sealed record RateTerms(
    int MinWeightGrams,
    int MaxWeightGrams,
    decimal MinOrderValue,
    decimal? MaxOrderValue,
    decimal BaseRate,
    decimal PerKgRate,
    decimal? FreeAbove,
    decimal CodFee,
    bool IsCodAllowed,
    int EtaMinDays,
    int EtaMaxDays);

/// <summary>Adds a rule to the rate card.</summary>
/// <param name="ZoneId">The zone it prices.</param>
/// <param name="Method">Standard or express.</param>
/// <param name="VendorId">The seller it overrides for, or null for the platform's card.</param>
/// <param name="Terms">What it charges and promises.</param>
internal sealed record CreateRateCommand(
    Guid ZoneId,
    string Method,
    Guid? VendorId,
    RateTerms Terms) : ICommand<ShippingRateResponse>;

/// <summary>Changes a rule.</summary>
/// <param name="RateId">The rule.</param>
/// <param name="Terms">What it charges and promises.</param>
/// <param name="IsActive">Whether it is used.</param>
internal sealed record UpdateRateCommand(Guid RateId, RateTerms Terms, bool IsActive)
    : ICommand<ShippingRateResponse>;

/// <summary>Validates a zone.</summary>
internal sealed class CreateZoneValidator : AbstractValidator<CreateZoneCommand>
{
    public CreateZoneValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32).Matches("^[a-z0-9-]+$");
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Priority).InclusiveBetween(0, 1000);
    }
}

/// <summary>Validates a redraw.</summary>
internal sealed class UpdateZoneValidator : AbstractValidator<UpdateZoneCommand>
{
    public UpdateZoneValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Priority).InclusiveBetween(0, 1000);
    }
}

/// <summary>Validates a rate rule.</summary>
internal sealed class CreateRateValidator : AbstractValidator<CreateRateCommand>
{
    public CreateRateValidator()
    {
        RuleFor(command => command.ZoneId).NotEmpty();
        RuleFor(command => command.Terms).NotNull().SetValidator(new RateTermsValidator());
    }
}

/// <summary>Validates a change to a rate rule.</summary>
internal sealed class UpdateRateValidator : AbstractValidator<UpdateRateCommand>
{
    public UpdateRateValidator()
        => RuleFor(command => command.Terms).NotNull().SetValidator(new RateTermsValidator());
}

/// <summary>
/// Validates what a rule charges and promises.
/// </summary>
/// <remarks>
/// The band checks are here as well as on the table. A validator gives the operator a sentence about
/// the field they got wrong; the check constraint is what stops anything that never went through a
/// handler, and neither replaces the other.
/// </remarks>
internal sealed class RateTermsValidator : AbstractValidator<RateTerms>
{
    public RateTermsValidator()
    {
        RuleFor(terms => terms.MinWeightGrams).GreaterThanOrEqualTo(0);
        RuleFor(terms => terms.MaxWeightGrams).GreaterThanOrEqualTo(terms => terms.MinWeightGrams);
        RuleFor(terms => terms.MinOrderValue).GreaterThanOrEqualTo(0m);
        RuleFor(terms => terms.BaseRate).GreaterThanOrEqualTo(0m);
        RuleFor(terms => terms.PerKgRate).GreaterThanOrEqualTo(0m);
        RuleFor(terms => terms.CodFee).GreaterThanOrEqualTo(0m);
        RuleFor(terms => terms.EtaMinDays).InclusiveBetween(0, 365);
        RuleFor(terms => terms.EtaMaxDays).GreaterThanOrEqualTo(terms => terms.EtaMinDays);
    }
}

/// <summary>Reads the delivery map.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class ListZonesQueryHandler(ShippingDbContext context)
    : IQueryHandler<ListZonesQuery, IReadOnlyList<ShippingZoneResponse>>
{
    public async Task<Result<IReadOnlyList<ShippingZoneResponse>>> HandleAsync(
        ListZonesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Zones.AsNoTracking().AsQueryable();

        if (!query.IncludeInactive)
        {
            rows = rows.Where(zone => zone.IsActive);
        }

        // Priority order, because that is the order the resolver walks them in and an operator
        // reading the map has to see the same precedence the engine applies.
        var zones = await rows
            .OrderBy(zone => zone.Priority)
            .ThenBy(zone => zone.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ShippingZoneResponse>>(
            [.. zones.Select(ShippingProjection.ToZone)]);
    }
}

/// <summary>Opens a zone.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class CreateZoneCommandHandler(ShippingDbContext context)
    : ICommandHandler<CreateZoneCommand, ShippingZoneResponse>
{
    public async Task<Result<ShippingZoneResponse>> HandleAsync(
        CreateZoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code.Trim().ToLowerInvariant();

        var taken = await context.Zones
            .AnyAsync(zone => zone.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (taken)
        {
            return Result.Failure<ShippingZoneResponse>(ShippingErrors.DuplicateZone(code));
        }

        var invalid = Invalid(command.PincodeRanges);

        if (invalid is not null)
        {
            return Result.Failure<ShippingZoneResponse>(invalid);
        }

        var zone = ShippingZone.Create(code, command.Name.Trim(), command.Priority);
        zone.Redefine(command.States, ToRanges(command.PincodeRanges));

        context.Zones.Add(zone);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToZone(zone));
    }

    /// <summary>The first range that would never match anything, or null when they are all sound.</summary>
    /// <remarks>
    /// An inverted range is the mistake this catches: <c>560100</c> to <c>560001</c> silently covers
    /// nothing, and a zone that covers nothing is a rate card with an invisible hole in it.
    /// </remarks>
    internal static Error? Invalid(IReadOnlyList<PincodeRangeModel> ranges)
    {
        foreach (var range in ranges)
        {
            if (range.From.Length != 6 || range.To.Length != 6)
            {
                return ShippingErrors.InvalidRule(
                    $"PIN codes must be six digits: '{range.From}' to '{range.To}'.");
            }

            if (string.CompareOrdinal(range.From, range.To) > 0)
            {
                return ShippingErrors.InvalidRule(
                    $"The range {range.From}–{range.To} runs backwards and would cover nothing.");
            }
        }

        return null;
    }

    internal static IReadOnlyList<PincodeRange> ToRanges(IReadOnlyList<PincodeRangeModel> ranges)
        => [.. ranges.Select(range => new PincodeRange(range.From.Trim(), range.To.Trim()))];
}

/// <summary>Redraws a zone.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class UpdateZoneCommandHandler(ShippingDbContext context)
    : ICommandHandler<UpdateZoneCommand, ShippingZoneResponse>
{
    public async Task<Result<ShippingZoneResponse>> HandleAsync(
        UpdateZoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var zone = await context.Zones
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ZoneId, cancellationToken)
            .ConfigureAwait(false);

        if (zone is null)
        {
            return Result.Failure<ShippingZoneResponse>(ShippingErrors.NotFound("shipping zone"));
        }

        var invalid = CreateZoneCommandHandler.Invalid(command.PincodeRanges);

        if (invalid is not null)
        {
            return Result.Failure<ShippingZoneResponse>(invalid);
        }

        zone.Update(command.Name.Trim(), command.Priority);
        zone.Redefine(command.States, CreateZoneCommandHandler.ToRanges(command.PincodeRanges));
        zone.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToZone(zone));
    }
}

/// <summary>Reads the rate card.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="scope">Confines a seller to their own overrides and the platform's card.</param>
internal sealed class ListRatesQueryHandler(ShippingDbContext context, ShippingScope scope)
    : IQueryHandler<ListRatesQuery, IReadOnlyList<ShippingRateResponse>>
{
    public async Task<Result<IReadOnlyList<ShippingRateResponse>>> HandleAsync(
        ListRatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Rates.AsNoTracking().AsQueryable();

        if (!query.IncludeInactive)
        {
            rows = rows.Where(rate => rate.IsActive);
        }

        if (query.ZoneId is { } zoneId)
        {
            rows = rows.Where(rate => rate.ZoneId == zoneId);
        }

        // A seller sees the platform's card and their own overrides, which is exactly what decides
        // what their parcels cost. The vendor query filter already admits both.
        if (!scope.IsVendor && query.VendorId is { } vendorId)
        {
            rows = rows.Where(rate => rate.VendorId == vendorId);
        }

        if (Enum.TryParse<ShippingMethod>(query.Method, ignoreCase: true, out var method))
        {
            rows = rows.Where(rate => rate.Method == method);
        }

        var rates = await rows
            .OrderBy(rate => rate.ZoneId)
            .ThenBy(rate => rate.Method)
            .ThenBy(rate => rate.MinWeightGrams)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ShippingRateResponse>>(
            [.. rates.Select(ShippingProjection.ToRate)]);
    }
}

/// <summary>Adds a rule to the rate card.</summary>
/// <param name="context">The Shipping data context.</param>
/// <param name="scope">A seller may only write their own override.</param>
internal sealed class CreateRateCommandHandler(ShippingDbContext context, ShippingScope scope)
    : ICommandHandler<CreateRateCommand, ShippingRateResponse>
{
    public async Task<Result<ShippingRateResponse>> HandleAsync(
        CreateRateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var zone = await context.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ZoneId, cancellationToken)
            .ConfigureAwait(false);

        if (zone is null)
        {
            return Result.Failure<ShippingRateResponse>(ShippingErrors.NotFound("shipping zone"));
        }

        // A seller's own id, never the one in the body. A vendor caller writing a platform-wide rate
        // would be repricing delivery for every other seller on the marketplace.
        var vendorId = scope.IsVendor ? scope.VendorId : command.VendorId;

        var rate = ShippingRate.Create(
            command.ZoneId,
            Enum.TryParse<ShippingMethod>(command.Method, ignoreCase: true, out var method)
                ? method
                : ShippingMethod.Standard,
            vendorId);

        Apply(rate, command.Terms);

        context.Rates.Add(rate);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToRate(rate));
    }

    internal static void Apply(ShippingRate rate, RateTerms terms)
    {
        rate.Band(terms.MinWeightGrams, terms.MaxWeightGrams, terms.MinOrderValue, terms.MaxOrderValue);
        rate.Price(terms.BaseRate, terms.PerKgRate, terms.FreeAbove, terms.CodFee, terms.IsCodAllowed);
        rate.Promise(terms.EtaMinDays, terms.EtaMaxDays);
    }
}

/// <summary>Changes a rule.</summary>
/// <param name="context">The Shipping data context.</param>
internal sealed class UpdateRateCommandHandler(ShippingDbContext context)
    : ICommandHandler<UpdateRateCommand, ShippingRateResponse>
{
    public async Task<Result<ShippingRateResponse>> HandleAsync(
        UpdateRateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The vendor query filter is what stops a seller editing the platform's card or another
        // seller's: a rule that is not theirs simply does not resolve.
        var rate = await context.Rates
            .FirstOrDefaultAsync(candidate => candidate.Id == command.RateId, cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return Result.Failure<ShippingRateResponse>(ShippingErrors.NotFound("shipping rate"));
        }

        CreateRateCommandHandler.Apply(rate, command.Terms);
        rate.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ShippingProjection.ToRate(rate));
    }
}

/// <summary>
/// The rate card every deployment starts with.
/// </summary>
/// <remarks>
/// <para>
/// One zone and three weight bands, because the alternative is a store whose checkout offers no
/// delivery at all until somebody fills in a form. The zone is the catch-all — it names no states
/// and no PIN ranges, so it covers everywhere — and the bands are ordinary Indian surface-mail
/// rates: under half a kilo, half to two, and everything heavier.
/// </para>
/// <para>
/// Metro and remote zones are deliberately <b>not</b> seeded. They are lists of PIN ranges that
/// differ per deployment, and a guessed map is worse than no map: it prices parcels wrongly and
/// looks configured. What is seeded is a tariff that works and four rows an operator can see.
/// </para>
/// <para>
/// Upserted by zone code, per the seeder contract: a redeploy neither duplicates the zone nor
/// overwrites the rates an operator has edited. Only a missing zone is created, and only its own
/// rates are added with it — which is what lets a store change every figure here and keep them.
/// </para>
/// </remarks>
/// <param name="context">The Shipping data context.</param>
internal sealed class ShippingRateCardSeeder(ShippingDbContext context) : IDataSeeder
{
    /// <summary>The code the seeded catch-all zone is created under.</summary>
    public const string DefaultZoneCode = "rest-of-india";

    /// <inheritdoc />
    public string Name => "Shipping rate card";

    /// <summary>After the states the zones could name, and after nothing else.</summary>
    public int Order => 300;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Zones
            .IgnoreQueryFilters()
            .AnyAsync(zone => zone.Code == DefaultZoneCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing)
        {
            return;
        }

        // Priority 900, so every zone an operator adds later beats it without their having to think
        // about numbering. A catch-all that outranked a metro map would be a tariff nobody could fix.
        var zone = ShippingZone.Create(DefaultZoneCode, "Rest of India", priority: 900);

        context.Zones.Add(zone);

        // Three bands, and the base rate of each buys its own floor weight. A half-kilo parcel costs
        // the first band's base; a three-kilo one costs the third band's base plus a kilogram.
        var bands = new[]
        {
            (Min: 0, Max: 500, Base: 49m, PerKg: 0m),
            (Min: 501, Max: 2_000, Base: 69m, PerKg: 30m),
            (Min: 2_001, Max: 500_000, Base: 99m, PerKg: 45m),
        };

        foreach (var band in bands)
        {
            var rate = ShippingRate.Create(zone.Id, ShippingMethod.Standard, vendorId: null);

            rate.Band(band.Min, band.Max, minOrderValue: 0m, maxOrderValue: null);
            rate.Price(band.Base, band.PerKg, freeAbove: 999m, codFee: 39m, isCodAllowed: true);
            rate.Promise(minDays: 4, maxDays: 8);

            context.Rates.Add(rate);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
