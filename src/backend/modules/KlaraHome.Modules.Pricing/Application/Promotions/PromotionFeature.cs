using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Application.Promotions;

/// <summary>What a promotion applies to, as the API states it.</summary>
/// <param name="CategoryIds">Categories it covers, including everything beneath them.</param>
/// <param name="BrandIds">Brands it covers.</param>
/// <param name="VendorIds">Sellers it covers.</param>
/// <param name="ListingIds">Individual offers it covers.</param>
/// <param name="ExcludedListingIds">Offers it explicitly does not cover, whatever else matches.</param>
/// <param name="Segments">Customer segments it is limited to.</param>
internal sealed record PromotionScopePayload(
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? BrandIds,
    IReadOnlyList<Guid>? VendorIds,
    IReadOnlyList<Guid>? ListingIds,
    IReadOnlyList<Guid>? ExcludedListingIds,
    IReadOnlyList<string>? Segments);

/// <summary>One step of a tiered promotion, as the API states it.</summary>
/// <param name="MinAmount">The basket value at or above which this step applies.</param>
/// <param name="Value">What the step is worth.</param>
internal sealed record PromotionTierPayload(decimal MinAmount, decimal Value);

/// <summary>What a basket must satisfy, as the API states it.</summary>
/// <param name="MinQuantity">The fewest matching units a basket must hold.</param>
/// <param name="FirstOrderOnly">Whether only a shopper's first order qualifies.</param>
/// <param name="PaymentMethods">Payment methods it is limited to. Empty means any.</param>
/// <param name="MaxQuantityPerOrder">The most matching units one order may be discounted on.</param>
/// <param name="BuyQuantity">Buy-X-get-Y: how many units must be bought.</param>
/// <param name="GetQuantity">Buy-X-get-Y: how many units are earned per set.</param>
/// <param name="GetDiscountPercent">Buy-X-get-Y: how much comes off each earned unit.</param>
/// <param name="BundleListingIds">The offers a bundle is made of.</param>
/// <param name="BundlePrice">What the bundle sells for.</param>
/// <param name="Tiers">The steps of a tiered promotion.</param>
/// <param name="TiersArePercentage">Whether a step's value is a percentage rather than an amount.</param>
internal sealed record PromotionConditionsPayload(
    int? MinQuantity,
    bool? FirstOrderOnly,
    IReadOnlyList<string>? PaymentMethods,
    int? MaxQuantityPerOrder,
    int? BuyQuantity,
    int? GetQuantity,
    decimal? GetDiscountPercent,
    IReadOnlyList<Guid>? BundleListingIds,
    decimal? BundlePrice,
    IReadOnlyList<PromotionTierPayload>? Tiers,
    bool? TiersArePercentage);

/// <summary>A promotion, as the API states it.</summary>
/// <param name="Id">The promotion.</param>
/// <param name="Code">The code a shopper types, or null for an automatic rule.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">The customer-facing terms.</param>
/// <param name="Type">How it computes what it takes off.</param>
/// <param name="AppliesTo">What it reduces.</param>
/// <param name="Value">The percentage or amount.</param>
/// <param name="Scope">What it applies to.</param>
/// <param name="Conditions">What a basket must satisfy.</param>
/// <param name="Stacking">Whether it tolerates company.</param>
/// <param name="Priority">Evaluation order. Lower goes first.</param>
/// <param name="StartsAt">When it opens.</param>
/// <param name="EndsAt">When it closes.</param>
/// <param name="UsageLimitTotal">The most times it may ever be redeemed.</param>
/// <param name="UsageLimitPerCustomer">The most times one shopper may redeem it.</param>
/// <param name="UsageCount">How many times it has been.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxDiscount">The most it will ever take off.</param>
/// <param name="IsActive">Whether it is considered at all.</param>
/// <param name="CreatedAt">When it was drafted.</param>
internal sealed record PromotionResponse(
    Guid Id,
    string? Code,
    string Name,
    string? Description,
    string Type,
    string AppliesTo,
    decimal Value,
    PromotionScopePayload Scope,
    PromotionConditionsPayload Conditions,
    string Stacking,
    int Priority,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerCustomer,
    int UsageCount,
    decimal MinOrderValue,
    decimal? MaxDiscount,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>One use of a promotion, as the API states it.</summary>
/// <param name="Id">The redemption.</param>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Code">Its code at the time.</param>
/// <param name="CustomerId">The shopper.</param>
/// <param name="OrderId">The order.</param>
/// <param name="DiscountAmount">What it took off.</param>
/// <param name="Status">Whether it still stands.</param>
/// <param name="RedeemedAt">When it was used.</param>
/// <param name="ReversedAt">When the use was given back.</param>
internal sealed record PromotionRedemptionResponse(
    Guid Id,
    Guid PromotionId,
    string? Code,
    Guid? CustomerId,
    Guid OrderId,
    decimal DiscountAmount,
    string Status,
    DateTimeOffset RedeemedAt,
    DateTimeOffset? ReversedAt);

/// <summary>Lists promotions.</summary>
/// <param name="Type">Restrict to one kind.</param>
/// <param name="Code">Find the one with this code.</param>
/// <param name="ActiveOnly">Hide the ones that are switched off.</param>
/// <param name="Search">A fragment of the name or the code.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListPromotionsQuery(
    string? Type,
    string? Code,
    bool? ActiveOnly,
    string? Search,
    string? Cursor,
    int? Size) : IQuery<PagedResult<PromotionResponse>>;

/// <summary>Reads one promotion.</summary>
/// <param name="PromotionId">The promotion.</param>
internal sealed record GetPromotionQuery(Guid PromotionId) : IQuery<PromotionResponse>;

/// <summary>Lists a promotion's uses.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="CustomerId">Restrict to one shopper.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListRedemptionsQuery(Guid PromotionId, Guid? CustomerId, string? Cursor, int? Size)
    : IQuery<PagedResult<PromotionRedemptionResponse>>;

/// <summary>Drafts a promotion. It is inactive until somebody activates it.</summary>
/// <param name="Code">The code a shopper types, or null for an automatic rule.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">The customer-facing terms.</param>
/// <param name="Type">How it computes what it takes off.</param>
/// <param name="AppliesTo">What it reduces.</param>
/// <param name="Value">The percentage or amount.</param>
/// <param name="Scope">What it applies to.</param>
/// <param name="Conditions">What a basket must satisfy.</param>
/// <param name="Stacking">Whether it tolerates company.</param>
/// <param name="Priority">Evaluation order.</param>
/// <param name="StartsAt">When it opens.</param>
/// <param name="EndsAt">When it closes.</param>
/// <param name="UsageLimitTotal">The most times it may ever be redeemed.</param>
/// <param name="UsageLimitPerCustomer">The most times one shopper may redeem it.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxDiscount">The most it will ever take off.</param>
internal sealed record CreatePromotionCommand(
    string? Code,
    string Name,
    string? Description,
    string Type,
    string AppliesTo,
    decimal Value,
    PromotionScopePayload? Scope,
    PromotionConditionsPayload? Conditions,
    string Stacking,
    int Priority,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerCustomer,
    decimal MinOrderValue,
    decimal? MaxDiscount) : ICommand<PromotionResponse>;

/// <summary>Restates a promotion.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">The customer-facing terms.</param>
/// <param name="Type">How it computes what it takes off.</param>
/// <param name="AppliesTo">What it reduces.</param>
/// <param name="Value">The percentage or amount.</param>
/// <param name="Scope">What it applies to.</param>
/// <param name="Conditions">What a basket must satisfy.</param>
/// <param name="Stacking">Whether it tolerates company.</param>
/// <param name="Priority">Evaluation order.</param>
/// <param name="StartsAt">When it opens.</param>
/// <param name="EndsAt">When it closes.</param>
/// <param name="UsageLimitTotal">The most times it may ever be redeemed.</param>
/// <param name="UsageLimitPerCustomer">The most times one shopper may redeem it.</param>
/// <param name="MinOrderValue">The smallest basket it applies to.</param>
/// <param name="MaxDiscount">The most it will ever take off.</param>
internal sealed record UpdatePromotionCommand(
    Guid PromotionId,
    string Name,
    string? Description,
    string Type,
    string AppliesTo,
    decimal Value,
    PromotionScopePayload? Scope,
    PromotionConditionsPayload? Conditions,
    string Stacking,
    int Priority,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    int? UsageLimitTotal,
    int? UsageLimitPerCustomer,
    decimal MinOrderValue,
    decimal? MaxDiscount) : ICommand<PromotionResponse>;

/// <summary>Turns a promotion on or off.</summary>
/// <param name="PromotionId">The promotion.</param>
/// <param name="IsActive">Whether it is considered.</param>
internal sealed record SetPromotionActiveCommand(Guid PromotionId, bool IsActive) : ICommand<PromotionResponse>;

/// <summary>Removes a promotion that has never been used.</summary>
/// <param name="PromotionId">The promotion.</param>
internal sealed record DeletePromotionCommand(Guid PromotionId) : ICommand;

/// <summary>The enum names the promotion API accepts.</summary>
internal static class PromotionEnums
{
    /// <summary>Whether a type name is one this platform understands.</summary>
    /// <param name="value">What the caller sent.</param>
    public static bool IsKnownType(string? value) => Enum.TryParse<PromotionType>(value, ignoreCase: true, out _);

    /// <summary>Whether an application name is one this platform understands.</summary>
    /// <param name="value">What the caller sent.</param>
    public static bool IsKnownApplication(string? value)
        => Enum.TryParse<PromotionApplication>(value, ignoreCase: true, out _);

    /// <summary>Whether a stacking name is one this platform understands.</summary>
    /// <param name="value">What the caller sent.</param>
    public static bool IsKnownStacking(string? value) => Enum.TryParse<StackingMode>(value, ignoreCase: true, out _);

    /// <summary>Parses a type, defaulting to a percentage.</summary>
    /// <param name="value">What the caller sent.</param>
    public static PromotionType ParseType(string? value)
        => Enum.TryParse<PromotionType>(value, ignoreCase: true, out var parsed) ? parsed : PromotionType.Percentage;

    /// <summary>Parses an application, defaulting to the order.</summary>
    /// <param name="value">What the caller sent.</param>
    public static PromotionApplication ParseApplication(string? value)
        => Enum.TryParse<PromotionApplication>(value, ignoreCase: true, out var parsed)
            ? parsed
            : PromotionApplication.Order;

    /// <summary>
    /// Parses a stacking mode, defaulting to exclusive.
    /// </summary>
    /// <remarks>
    /// Exclusive is the safe default and the deliberate one: a promotion that stacks by accident
    /// costs money on every order it touches, and a promotion that fails to stack costs a support
    /// conversation.
    /// </remarks>
    /// <param name="value">What the caller sent.</param>
    public static StackingMode ParseStacking(string? value)
        => Enum.TryParse<StackingMode>(value, ignoreCase: true, out var parsed) ? parsed : StackingMode.Exclusive;

    /// <summary>The validation message for a type.</summary>
    public static string TypeMessage { get; } =
        "Type must be one of: " + string.Join(", ", Enum.GetNames<PromotionType>());

    /// <summary>The validation message for an application.</summary>
    public static string ApplicationMessage { get; } =
        "AppliesTo must be one of: " + string.Join(", ", Enum.GetNames<PromotionApplication>());

    /// <summary>The validation message for a stacking mode.</summary>
    public static string StackingMessage { get; } =
        "Stacking must be one of: " + string.Join(", ", Enum.GetNames<StackingMode>());
}

/// <summary>Rejects a promotion that could never work.</summary>
/// <remarks>
/// The type-specific rules matter more here than the length limits. A percentage promotion with a
/// value of 150 and a bundle with no members are both rows the engine would happily store and then
/// silently do nothing with, and "the coupon does nothing and nobody knows why" is the single most
/// expensive kind of merchandising bug.
/// </remarks>
internal sealed class CreatePromotionValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionValidator()
    {
        RuleFor(command => command.Code).MaximumLength(48).Matches("^[A-Za-z0-9][A-Za-z0-9-]*$")
            .When(command => !string.IsNullOrWhiteSpace(command.Code));
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(1000);
        RuleFor(command => command.Type).NotEmpty().Must(PromotionEnums.IsKnownType)
            .WithMessage(PromotionEnums.TypeMessage);
        RuleFor(command => command.AppliesTo).NotEmpty().Must(PromotionEnums.IsKnownApplication)
            .WithMessage(PromotionEnums.ApplicationMessage);
        RuleFor(command => command.Stacking).NotEmpty().Must(PromotionEnums.IsKnownStacking)
            .WithMessage(PromotionEnums.StackingMessage);
        RuleFor(command => command.Priority).InclusiveBetween(0, Promotion.MaxPriority);
        RuleFor(command => command.Value).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.MinOrderValue).GreaterThanOrEqualTo(0m);

        RuleFor(command => command.Value).LessThanOrEqualTo(100m)
            .When(command => string.Equals(command.Type, nameof(PromotionType.Percentage), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A percentage promotion cannot take off more than 100%.");

        RuleFor(command => command.EndsAt).GreaterThan(command => command.StartsAt)
            .When(command => command.EndsAt is not null)
            .WithMessage("A promotion cannot close before it opens.");

        RuleFor(command => command.Conditions!.BundleListingIds).NotEmpty()
            .When(command => string.Equals(command.Type, nameof(PromotionType.Bundle), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A bundle needs the offers it is made of.");

        RuleFor(command => command.Conditions!.Tiers).NotEmpty()
            .When(command => string.Equals(command.Type, nameof(PromotionType.Tiered), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A tiered promotion needs at least one step.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdatePromotionValidator : AbstractValidator<UpdatePromotionCommand>
{
    public UpdatePromotionValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(1000);
        RuleFor(command => command.Type).NotEmpty().Must(PromotionEnums.IsKnownType)
            .WithMessage(PromotionEnums.TypeMessage);
        RuleFor(command => command.AppliesTo).NotEmpty().Must(PromotionEnums.IsKnownApplication)
            .WithMessage(PromotionEnums.ApplicationMessage);
        RuleFor(command => command.Stacking).NotEmpty().Must(PromotionEnums.IsKnownStacking)
            .WithMessage(PromotionEnums.StackingMessage);
        RuleFor(command => command.Priority).InclusiveBetween(0, Promotion.MaxPriority);
        RuleFor(command => command.Value).GreaterThanOrEqualTo(0m);
        RuleFor(command => command.MinOrderValue).GreaterThanOrEqualTo(0m);

        RuleFor(command => command.Value).LessThanOrEqualTo(100m)
            .When(command => string.Equals(command.Type, nameof(PromotionType.Percentage), StringComparison.OrdinalIgnoreCase))
            .WithMessage("A percentage promotion cannot take off more than 100%.");

        RuleFor(command => command.EndsAt).GreaterThan(command => command.StartsAt)
            .When(command => command.EndsAt is not null)
            .WithMessage("A promotion cannot close before it opens.");
    }
}

/// <summary>Lists promotions.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListPromotionsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListPromotionsQuery, PagedResult<PromotionResponse>>
{
    public async Task<Result<PagedResult<PromotionResponse>>> HandleAsync(
        ListPromotionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Promotions.AsNoTracking();

        if (PromotionEnums.IsKnownType(query.Type))
        {
            var type = PromotionEnums.ParseType(query.Type);
            rows = rows.Where(promotion => promotion.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Code))
        {
            var code = Promotion.NormalizeCode(query.Code);
            rows = rows.Where(promotion => promotion.Code == code);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(promotion => promotion.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{PricingQueries.EscapeLike(query.Search)}%";

            rows = rows.Where(promotion =>
                EF.Functions.ILike(promotion.Name, pattern, "\\")
                || (promotion.Code != null && EF.Functions.ILike(promotion.Code, pattern, "\\")));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(promotion => promotion.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(promotion => promotion.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<PromotionResponse>(
            [.. page.Select(PromotionProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one promotion.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class GetPromotionQueryHandler(PricingDbContext context)
    : IQueryHandler<GetPromotionQuery, PromotionResponse>
{
    public async Task<Result<PromotionResponse>> HandleAsync(
        GetPromotionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var promotion = await context.Promotions
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.PromotionId, cancellationToken)
            .ConfigureAwait(false);

        return promotion is null
            ? PricingErrors.NotFound("promotion")
            : Result.Success(PromotionProjection.ToResponse(promotion));
    }
}

/// <summary>Lists a promotion's uses.</summary>
/// <param name="context">The Pricing data context.</param>
internal sealed class ListRedemptionsQueryHandler(PricingDbContext context)
    : IQueryHandler<ListRedemptionsQuery, PagedResult<PromotionRedemptionResponse>>
{
    public async Task<Result<PagedResult<PromotionRedemptionResponse>>> HandleAsync(
        ListRedemptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);

        var rows = context.PromotionRedemptions
            .AsNoTracking()
            .Where(redemption => redemption.PromotionId == query.PromotionId);

        if (query.CustomerId is { } customerId)
        {
            rows = rows.Where(redemption => redemption.CustomerId == customerId);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(redemption => redemption.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(redemption => redemption.Id)
            .Take(size + 1)
            .Select(redemption => new PromotionRedemptionResponse(
                redemption.Id,
                redemption.PromotionId,
                redemption.Code,
                redemption.CustomerId,
                redemption.OrderId,
                redemption.DiscountAmount,
                redemption.Status.ToString(),
                redemption.RedeemedAt,
                redemption.ReversedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<PromotionRedemptionResponse>(
            page,
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Drafts a promotion.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the addition.</param>
internal sealed class CreatePromotionCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<CreatePromotionCommand, PromotionResponse>
{
    /// <summary>The audited action for a new promotion.</summary>
    public const string AuditAction = "pricing.promotion.created";

    /// <summary>The entity type recorded against every promotion action.</summary>
    public const string AuditEntityType = "Promotion";

    public async Task<Result<PromotionResponse>> HandleAsync(
        CreatePromotionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = Promotion.NormalizeCode(command.Code);

        if (code is not null
            && await context.Promotions
                .AnyAsync(candidate => candidate.Code == code, cancellationToken)
                .ConfigureAwait(false))
        {
            return PricingErrors.Duplicate("coupon code");
        }

        var promotion = Promotion.Create(
            code,
            command.Name,
            PromotionEnums.ParseType(command.Type),
            PromotionEnums.ParseApplication(command.AppliesTo));

        Apply(promotion, command);

        context.Promotions.Add(promotion);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = promotion.Id.ToString(),
                After = new
                {
                    promotion.Code,
                    promotion.Name,
                    Type = promotion.Type.ToString(),
                    promotion.Value,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PromotionProjection.ToResponse(promotion));
    }

    /// <summary>Writes the whole editable surface onto a promotion.</summary>
    /// <param name="promotion">The promotion.</param>
    /// <param name="command">What the caller sent.</param>
    internal static void Apply(Promotion promotion, CreatePromotionCommand command)
        => promotion.Update(
            command.Name,
            command.Description,
            PromotionEnums.ParseType(command.Type),
            PromotionEnums.ParseApplication(command.AppliesTo),
            command.Value,
            PromotionProjection.ToScope(command.Scope),
            PromotionProjection.ToConditions(command.Conditions),
            PromotionEnums.ParseStacking(command.Stacking),
            command.Priority,
            command.StartsAt,
            command.EndsAt,
            command.UsageLimitTotal,
            command.UsageLimitPerCustomer,
            command.MinOrderValue,
            command.MaxDiscount);
}

/// <summary>Restates a promotion.</summary>
/// <remarks>
/// The code is deliberately not editable. It has been printed on a campaign and typed by shoppers,
/// and renaming it would silently break every place it has already appeared while leaving the
/// redemption rows pointing at a code that no longer exists.
/// </remarks>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdatePromotionCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdatePromotionCommand, PromotionResponse>
{
    /// <summary>The audited action for a change to a promotion.</summary>
    public const string AuditAction = "pricing.promotion.updated";

    public async Task<Result<PromotionResponse>> HandleAsync(
        UpdatePromotionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var promotion = await context.Promotions
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PromotionId, cancellationToken)
            .ConfigureAwait(false);

        if (promotion is null)
        {
            return PricingErrors.NotFound("promotion");
        }

        var before = new
        {
            promotion.Name,
            Type = promotion.Type.ToString(),
            promotion.Value,
            promotion.Priority,
            Stacking = promotion.Stacking.ToString(),
        };

        promotion.Update(
            command.Name,
            command.Description,
            PromotionEnums.ParseType(command.Type),
            PromotionEnums.ParseApplication(command.AppliesTo),
            command.Value,
            PromotionProjection.ToScope(command.Scope),
            PromotionProjection.ToConditions(command.Conditions),
            PromotionEnums.ParseStacking(command.Stacking),
            command.Priority,
            command.StartsAt,
            command.EndsAt,
            command.UsageLimitTotal,
            command.UsageLimitPerCustomer,
            command.MinOrderValue,
            command.MaxDiscount);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePromotionCommandHandler.AuditEntityType,
                EntityId = promotion.Id.ToString(),
                Before = before,
                After = new
                {
                    promotion.Name,
                    Type = promotion.Type.ToString(),
                    promotion.Value,
                    promotion.Priority,
                    Stacking = promotion.Stacking.ToString(),
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PromotionProjection.ToResponse(promotion));
    }
}

/// <summary>Turns a promotion on or off.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the switch.</param>
internal sealed class SetPromotionActiveCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<SetPromotionActiveCommand, PromotionResponse>
{
    /// <summary>The audited action for switching a promotion.</summary>
    /// <remarks>
    /// Audited with the actor, because "who turned the 50%-off coupon back on" is a question that
    /// gets asked exactly once per marketplace and needs an answer when it does.
    /// </remarks>
    public const string AuditAction = "pricing.promotion.activation-changed";

    public async Task<Result<PromotionResponse>> HandleAsync(
        SetPromotionActiveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var promotion = await context.Promotions
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PromotionId, cancellationToken)
            .ConfigureAwait(false);

        if (promotion is null)
        {
            return PricingErrors.NotFound("promotion");
        }

        if (promotion.IsActive == command.IsActive)
        {
            return Result.Success(PromotionProjection.ToResponse(promotion));
        }

        promotion.SetActive(command.IsActive);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePromotionCommandHandler.AuditEntityType,
                EntityId = promotion.Id.ToString(),
                Before = new { IsActive = !command.IsActive },
                After = new { command.IsActive, promotion.Code, promotion.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(PromotionProjection.ToResponse(promotion));
    }
}

/// <summary>Removes a promotion that has never been used.</summary>
/// <param name="context">The Pricing data context.</param>
/// <param name="audit">Records the removal.</param>
internal sealed class DeletePromotionCommandHandler(PricingDbContext context, IAuditLogger audit)
    : ICommandHandler<DeletePromotionCommand>
{
    /// <summary>The audited action for a removed promotion.</summary>
    public const string AuditAction = "pricing.promotion.deleted";

    public async Task<Result> HandleAsync(DeletePromotionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var promotion = await context.Promotions
            .FirstOrDefaultAsync(candidate => candidate.Id == command.PromotionId, cancellationToken)
            .ConfigureAwait(false);

        if (promotion is null)
        {
            return Result.Failure(PricingErrors.NotFound("promotion"));
        }

        // A promotion that has been redeemed is history: its rows explain discounts on real orders
        // and a settlement reads them. Deactivating one is how it is retired, and the refusal says
        // so rather than leaving the operator to guess.
        if (await context.PromotionRedemptions
                .AnyAsync(redemption => redemption.PromotionId == promotion.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict(
                "PRICING_IN_USE",
                "That promotion has been used on an order. Deactivate it instead of removing it."));
        }

        context.Promotions.Remove(promotion);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreatePromotionCommandHandler.AuditEntityType,
                EntityId = promotion.Id.ToString(),
                Before = new { promotion.Code, promotion.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Turns promotions into responses, and payloads into domain shapes.</summary>
internal static class PromotionProjection
{
    /// <summary>States a promotion.</summary>
    /// <param name="promotion">The promotion.</param>
    public static PromotionResponse ToResponse(Promotion promotion)
    {
        ArgumentNullException.ThrowIfNull(promotion);

        return new PromotionResponse(
            promotion.Id,
            promotion.Code,
            promotion.Name,
            promotion.Description,
            promotion.Type.ToString(),
            promotion.AppliesTo.ToString(),
            promotion.Value,
            new PromotionScopePayload(
                promotion.Scope.CategoryIds,
                promotion.Scope.BrandIds,
                promotion.Scope.VendorIds,
                promotion.Scope.ListingIds,
                promotion.Scope.ExcludedListingIds,
                promotion.Scope.Segments),
            new PromotionConditionsPayload(
                promotion.Conditions.MinQuantity,
                promotion.Conditions.FirstOrderOnly,
                promotion.Conditions.PaymentMethods,
                promotion.Conditions.MaxQuantityPerOrder,
                promotion.Conditions.BuyQuantity,
                promotion.Conditions.GetQuantity,
                promotion.Conditions.GetDiscountPercent,
                promotion.Conditions.BundleListingIds,
                promotion.Conditions.BundlePrice,
                [.. promotion.Conditions.Tiers.Select(tier => new PromotionTierPayload(tier.MinAmount, tier.Value))],
                promotion.Conditions.TiersArePercentage),
            promotion.Stacking.ToString(),
            promotion.Priority,
            promotion.StartsAt,
            promotion.EndsAt,
            promotion.UsageLimitTotal,
            promotion.UsageLimitPerCustomer,
            promotion.UsageCount,
            promotion.MinOrderValue,
            promotion.MaxDiscount,
            promotion.IsActive,
            promotion.CreatedAt);
    }

    /// <summary>Reads a scope payload, treating an absent list as "no restriction".</summary>
    /// <param name="payload">What the caller sent.</param>
    public static PromotionScope ToScope(PromotionScopePayload? payload)
        => new()
        {
            CategoryIds = [.. payload?.CategoryIds ?? []],
            BrandIds = [.. payload?.BrandIds ?? []],
            VendorIds = [.. payload?.VendorIds ?? []],
            ListingIds = [.. payload?.ListingIds ?? []],
            ExcludedListingIds = [.. payload?.ExcludedListingIds ?? []],
            Segments = [.. payload?.Segments ?? []],
        };

    /// <summary>Reads a conditions payload, filling in the defaults an omitted field means.</summary>
    /// <param name="payload">What the caller sent.</param>
    public static PromotionConditions ToConditions(PromotionConditionsPayload? payload)
        => new()
        {
            MinQuantity = Math.Max(1, payload?.MinQuantity ?? 1),
            FirstOrderOnly = payload?.FirstOrderOnly ?? false,
            PaymentMethods = [.. payload?.PaymentMethods ?? []],
            MaxQuantityPerOrder = payload?.MaxQuantityPerOrder is > 0 ? payload.MaxQuantityPerOrder : null,
            BuyQuantity = Math.Max(1, payload?.BuyQuantity ?? 1),
            GetQuantity = Math.Max(1, payload?.GetQuantity ?? 1),
            GetDiscountPercent = Math.Clamp(payload?.GetDiscountPercent ?? 100m, 0m, 100m),
            BundleListingIds = [.. payload?.BundleListingIds ?? []],
            BundlePrice = Math.Max(0m, payload?.BundlePrice ?? 0m),
            Tiers = [.. (payload?.Tiers ?? []).Select(tier => new PromotionTier
            {
                MinAmount = Math.Max(0m, tier.MinAmount),
                Value = Math.Max(0m, tier.Value),
            })],
            TiersArePercentage = payload?.TiersArePercentage ?? true,
        };
}
