using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Calculation;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Application.Tax;

/// <summary>A tax rate, as the API states it.</summary>
/// <param name="Id">The rate.</param>
/// <param name="HsnCode">The HSN code it applies to.</param>
/// <param name="Description">What the code covers.</param>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last day it applies, or null while it is current.</param>
/// <param name="IsActive">Whether it is considered.</param>
/// <param name="CreatedAt">When it was recorded.</param>
internal sealed record TaxRateResponse(
    Guid Id,
    string HsnCode,
    string? Description,
    decimal Rate,
    decimal CessRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>What the resolver decided for one HSN code on one day.</summary>
/// <param name="HsnCode">The code asked about.</param>
/// <param name="AsOf">The date it was resolved for.</param>
/// <param name="Rate">The GST percentage in force.</param>
/// <param name="CessRate">The cess percentage in force.</param>
/// <param name="TaxRateId">
/// The row that decided it, or null when no row covers the code and the product's own rate would
/// have stood.
/// </param>
internal sealed record TaxRateResolutionResponse(
    string HsnCode,
    DateOnly AsOf,
    decimal Rate,
    decimal CessRate,
    Guid? TaxRateId);

/// <summary>Lists tax rates.</summary>
/// <param name="HsnCode">Restrict to one code, to see its whole history.</param>
/// <param name="ActiveOnly">Hide superseded rows.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListTaxRatesQuery(string? HsnCode, bool? ActiveOnly, string? Cursor, int? Size)
    : IQuery<PagedResult<TaxRateResponse>>;

/// <summary>Reads one tax rate.</summary>
/// <param name="TaxRateId">The rate.</param>
internal sealed record GetTaxRateQuery(Guid TaxRateId) : IQuery<TaxRateResponse>;

/// <summary>Resolves the rate in force for a code on a date.</summary>
/// <param name="HsnCode">The code.</param>
/// <param name="AsOf">The date of supply. Defaults to today.</param>
internal sealed record ResolveTaxRateQuery(string HsnCode, DateOnly? AsOf) : IQuery<TaxRateResolutionResponse>;

/// <summary>Records a tax rate.</summary>
/// <param name="HsnCode">The HSN code.</param>
/// <param name="Description">What the code covers.</param>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last day it applies, or null.</param>
internal sealed record CreateTaxRateCommand(
    string HsnCode,
    string? Description,
    decimal Rate,
    decimal CessRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo) : ICommand<TaxRateResponse>;

/// <summary>Restates a tax rate.</summary>
/// <param name="TaxRateId">The rate.</param>
/// <param name="Description">What the code covers.</param>
/// <param name="Rate">The GST percentage.</param>
/// <param name="CessRate">The compensation cess percentage.</param>
/// <param name="EffectiveFrom">The first day it applies.</param>
/// <param name="EffectiveTo">The last day it applies, or null.</param>
/// <param name="IsActive">Whether it is considered.</param>
internal sealed record UpdateTaxRateCommand(
    Guid TaxRateId,
    string? Description,
    decimal Rate,
    decimal CessRate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive) : ICommand<TaxRateResponse>;

/// <summary>Removes a tax rate.</summary>
/// <param name="TaxRateId">The rate.</param>
internal sealed record DeleteTaxRateCommand(Guid TaxRateId) : ICommand;

/// <summary>Rejects a tax rate that could never be stored.</summary>
internal sealed class CreateTaxRateValidator : AbstractValidator<CreateTaxRateCommand>
{
    public CreateTaxRateValidator()
    {
        RuleFor(command => command.HsnCode).NotEmpty().Matches("^[0-9]{4,8}$")
            .WithMessage("An HSN code is four to eight digits.");
        RuleFor(command => command.Description).MaximumLength(300);
        RuleFor(command => command.Rate).InclusiveBetween(0m, TaxRate.MaxRate);
        RuleFor(command => command.CessRate).InclusiveBetween(0m, TaxRate.MaxRate);

        RuleFor(command => command.EffectiveTo)
            .GreaterThanOrEqualTo(command => command.EffectiveFrom)
            .When(command => command.EffectiveTo is not null)
            .WithMessage("A rate cannot end before it starts.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateTaxRateValidator : AbstractValidator<UpdateTaxRateCommand>
{
    public UpdateTaxRateValidator()
    {
        RuleFor(command => command.TaxRateId).NotEmpty();
        RuleFor(command => command.Description).MaximumLength(300);
        RuleFor(command => command.Rate).InclusiveBetween(0m, TaxRate.MaxRate);
        RuleFor(command => command.CessRate).InclusiveBetween(0m, TaxRate.MaxRate);

        RuleFor(command => command.EffectiveTo)
            .GreaterThanOrEqualTo(command => command.EffectiveFrom)
            .When(command => command.EffectiveTo is not null)
            .WithMessage("A rate cannot end before it starts.");
    }
}

/// <summary>Lists tax rates.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListTaxRatesQueryHandler(PricingDbContext context)
    : IQueryHandler<ListTaxRatesQuery, PagedResult<TaxRateResponse>>
{
    public async Task<Result<PagedResult<TaxRateResponse>>> HandleAsync(
        ListTaxRatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.TaxRates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.HsnCode))
        {
            var code = TaxRate.Normalize(query.HsnCode);
            rows = rows.Where(rate => rate.HsnCode == code);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(rate => rate.IsActive);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(rate => rate.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(rate => rate.Id)
            .Take(size + 1)
            .Select(rate => new TaxRateResponse(
                rate.Id,
                rate.HsnCode,
                rate.Description,
                rate.Rate,
                rate.CessRate,
                rate.EffectiveFrom,
                rate.EffectiveTo,
                rate.IsActive,
                rate.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<TaxRateResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one tax rate.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class GetTaxRateQueryHandler(PricingDbContext context)
    : IQueryHandler<GetTaxRateQuery, TaxRateResponse>
{
    public async Task<Result<TaxRateResponse>> HandleAsync(
        GetTaxRateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rate = await context.TaxRates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.TaxRateId, cancellationToken)
            .ConfigureAwait(false);

        return rate is null
            ? PricingErrors.NotFound("tax rate")
            : Result.Success(TaxRateProjection.ToResponse(rate));
    }
}

/// <summary>
/// Resolves the rate in force for a code on a date.
/// </summary>
/// <remarks>
/// The endpoint behind it exists so an operator can answer "why was this invoice taxed at 12%"
/// without reading the table by hand and reconstructing the window logic in their head. It is the
/// same resolver the quote engine uses, so its answer is the answer.
/// </remarks>
/// <param name="resolver">The rate lookup.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ResolveTaxRateQueryHandler(TaxRateResolver resolver, IClock clock)
    : IQueryHandler<ResolveTaxRateQuery, TaxRateResolutionResponse>
{
    public async Task<Result<TaxRateResolutionResponse>> HandleAsync(
        ResolveTaxRateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var code = TaxRate.Normalize(query.HsnCode);
        var asOf = query.AsOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var resolved = await resolver.ResolveAsync([code], asOf, cancellationToken).ConfigureAwait(false);

        return resolved.TryGetValue(code, out var match)
            ? Result.Success(new TaxRateResolutionResponse(code, asOf, match.Rate, match.CessRate, match.Source))
            : PricingErrors.NotFound("tax rate for that code on that date");
    }
}

/// <summary>Records a tax rate.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the addition.</param>
internal sealed class CreateTaxRateCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateTaxRateCommand, TaxRateResponse>
{
    /// <summary>The audited action for a new tax rate.</summary>
    public const string AuditAction = "pricing.tax-rate.created";

    /// <summary>The entity type recorded against every tax-rate action.</summary>
    public const string AuditEntityType = "TaxRate";

    public async Task<Result<TaxRateResponse>> HandleAsync(
        CreateTaxRateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = TaxRate.Normalize(command.HsnCode);

        if (await context.TaxRates
                .AnyAsync(
                    candidate => candidate.HsnCode == code && candidate.EffectiveFrom == command.EffectiveFrom,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return PricingErrors.DuplicateTaxRate;
        }

        var rate = TaxRate.Create(code, command.Rate, command.CessRate, command.EffectiveFrom);

        rate.Update(
            command.Description,
            command.Rate,
            command.CessRate,
            command.EffectiveFrom,
            command.EffectiveTo,
            isActive: true);

        context.TaxRates.Add(rate);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = rate.Id.ToString(),
                After = new { rate.HsnCode, rate.Rate, rate.CessRate, rate.EffectiveFrom },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(TaxRateProjection.ToResponse(rate));
    }
}

/// <summary>Restates a tax rate.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateTaxRateCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateTaxRateCommand, TaxRateResponse>
{
    /// <summary>The audited action for a change to a tax rate.</summary>
    /// <remarks>
    /// Every rate change is audited with its before and after, and that is not routine bookkeeping:
    /// it is the evidence that answers a GST notice about why a supply was taxed the way it was.
    /// </remarks>
    public const string AuditAction = "pricing.tax-rate.updated";

    public async Task<Result<TaxRateResponse>> HandleAsync(
        UpdateTaxRateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rate = await context.TaxRates
            .FirstOrDefaultAsync(candidate => candidate.Id == command.TaxRateId, cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return PricingErrors.NotFound("tax rate");
        }

        if (rate.EffectiveFrom != command.EffectiveFrom
            && await context.TaxRates
                .AnyAsync(
                    candidate => candidate.HsnCode == rate.HsnCode
                                 && candidate.EffectiveFrom == command.EffectiveFrom
                                 && candidate.Id != rate.Id,
                    cancellationToken)
                .ConfigureAwait(false))
        {
            return PricingErrors.DuplicateTaxRate;
        }

        var before = new { rate.Rate, rate.CessRate, rate.EffectiveFrom, rate.EffectiveTo, rate.IsActive };

        rate.Update(
            command.Description,
            command.Rate,
            command.CessRate,
            command.EffectiveFrom,
            command.EffectiveTo,
            command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateTaxRateCommandHandler.AuditEntityType,
                EntityId = rate.Id.ToString(),
                Before = before,
                After = new { rate.Rate, rate.CessRate, rate.EffectiveFrom, rate.EffectiveTo, rate.IsActive },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(TaxRateProjection.ToResponse(rate));
    }
}

/// <summary>Removes a tax rate.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class DeleteTaxRateCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<DeleteTaxRateCommand>
{
    /// <summary>The audited action for a removed tax rate.</summary>
    public const string AuditAction = "pricing.tax-rate.deleted";

    public async Task<Result> HandleAsync(DeleteTaxRateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var rate = await context.TaxRates
            .FirstOrDefaultAsync(candidate => candidate.Id == command.TaxRateId, cancellationToken)
            .ConfigureAwait(false);

        if (rate is null)
        {
            return Result.Failure(PricingErrors.NotFound("tax rate"));
        }

        var removed = new { rate.HsnCode, rate.Rate, rate.CessRate, rate.EffectiveFrom };

        context.TaxRates.Remove(rate);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateTaxRateCommandHandler.AuditEntityType,
                EntityId = rate.Id.ToString(),
                Before = removed,
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Turns tax rates into responses.</summary>
internal static class TaxRateProjection
{
    /// <summary>States a tax rate.</summary>
    /// <param name="rate">The rate.</param>
    public static TaxRateResponse ToResponse(TaxRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);

        return new TaxRateResponse(
            rate.Id,
            rate.HsnCode,
            rate.Description,
            rate.Rate,
            rate.CessRate,
            rate.EffectiveFrom,
            rate.EffectiveTo,
            rate.IsActive,
            rate.CreatedAt);
    }
}
