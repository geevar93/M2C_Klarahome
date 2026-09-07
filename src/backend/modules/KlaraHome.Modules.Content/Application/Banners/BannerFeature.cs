using FluentValidation;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Banners;

/// <summary>Lists the store's banners.</summary>
/// <param name="Placement">Only the banners in one slot.</param>
/// <param name="ActiveOnly">Only the ones switched on.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListBannersQuery(
    BannerPlacement? Placement,
    bool? ActiveOnly,
    string? Cursor,
    int? Size)
    : IQuery<PagedResult<BannerResponse>>;

/// <summary>Reads one banner.</summary>
/// <param name="Id">The banner.</param>
internal sealed record GetBannerQuery(Guid Id) : IQuery<BannerResponse>;

/// <summary>Opens a banner.</summary>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it appears.</param>
/// <param name="MediaFileId">The desktop image.</param>
/// <param name="MobileMediaFileId">The mobile image.</param>
/// <param name="Message">The words, for an announcement bar.</param>
/// <param name="AltText">Its alt text.</param>
/// <param name="Link">Where clicking it goes.</param>
/// <param name="CtaLabel">The button's wording.</param>
/// <param name="Priority">Which banner wins the placement.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it stops.</param>
/// <param name="Audience">Who sees it.</param>
/// <param name="IsActive">Whether it is switched on.</param>
internal sealed record CreateBannerCommand(
    string? Name,
    BannerPlacement Placement,
    Guid? MediaFileId,
    Guid? MobileMediaFileId,
    string? Message,
    string? AltText,
    string? Link,
    string? CtaLabel,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    BannerAudience? Audience,
    bool IsActive) : ICommand<BannerResponse>;

/// <summary>Rewrites a banner. Its placement is not editable.</summary>
/// <param name="Id">The banner.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="MediaFileId">The desktop image.</param>
/// <param name="MobileMediaFileId">The mobile image.</param>
/// <param name="Message">The words, for an announcement bar.</param>
/// <param name="AltText">Its alt text.</param>
/// <param name="Link">Where clicking it goes.</param>
/// <param name="CtaLabel">The button's wording.</param>
/// <param name="Priority">Which banner wins the placement.</param>
/// <param name="StartsAt">When it starts.</param>
/// <param name="EndsAt">When it stops.</param>
/// <param name="Audience">Who sees it.</param>
/// <param name="IsActive">Whether it is switched on.</param>
internal sealed record UpdateBannerCommand(
    Guid Id,
    string? Name,
    Guid? MediaFileId,
    Guid? MobileMediaFileId,
    string? Message,
    string? AltText,
    string? Link,
    string? CtaLabel,
    int Priority,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    BannerAudience? Audience,
    bool IsActive) : ICommand<BannerResponse>;

/// <summary>Switches a banner on or off without touching its schedule.</summary>
/// <param name="Id">The banner.</param>
/// <param name="IsActive">Whether it is switched on.</param>
internal sealed record SetBannerActiveCommand(Guid Id, bool IsActive) : ICommand<BannerResponse>;

/// <summary>Removes a banner.</summary>
/// <param name="Id">The banner.</param>
internal sealed record DeleteBannerCommand(Guid Id) : ICommand;

/// <summary>Rules a new banner has to satisfy.</summary>
internal sealed class CreateBannerCommandValidator : AbstractValidator<CreateBannerCommand>
{
    public CreateBannerCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Banner.MaxNameLength);
        RuleFor(command => command.Message).MaximumLength(Banner.MaxMessageLength);
        RuleFor(command => command.AltText).MaximumLength(Banner.MaxAltTextLength);
        RuleFor(command => command.CtaLabel).MaximumLength(64);
        RuleFor(command => command.Priority).InclusiveBetween(0, 1_000);

        // A real enum in the contract, so an unknown placement never reaches a validator.
        RuleFor(command => command.Placement).IsInEnum();
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
internal sealed class UpdateBannerCommandValidator : AbstractValidator<UpdateBannerCommand>
{
    public UpdateBannerCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Banner.MaxNameLength);
        RuleFor(command => command.Message).MaximumLength(Banner.MaxMessageLength);
        RuleFor(command => command.AltText).MaximumLength(Banner.MaxAltTextLength);
        RuleFor(command => command.CtaLabel).MaximumLength(64);
        RuleFor(command => command.Priority).InclusiveBetween(0, 1_000);
    }
}

/// <summary>Lists the store's banners, newest first.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images.</param>
/// <param name="clock">The sanctioned clock, for "is it live right now".</param>
internal sealed class ListBannersQueryHandler(ContentDbContext context, ContentRenderer renderer, IClock clock)
    : IQueryHandler<ListBannersQuery, PagedResult<BannerResponse>>
{
    public async Task<Result<PagedResult<BannerResponse>>> HandleAsync(
        ListBannersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Banners.AsNoTracking().AsQueryable();

        if (query.Placement is { } placement)
        {
            rows = rows.Where(banner => banner.Placement == placement);
        }

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(banner => banner.IsActive);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(banner => banner.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(banner => banner.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var banners = page.Take(size).ToList();

        var images = await renderer
            .ResolveImagesAsync(BannerImages.Of(banners), cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;

        var items = banners
            .Select(banner => ContentProjection.ToBanner(
                banner,
                BannerImages.Find(images, banner.MediaFileId, banner.AltText),
                BannerImages.Find(images, banner.MobileMediaFileId, banner.AltText),
                now))
            .ToArray();

        var next = hasMore && items.Length > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<BannerResponse>(items, new PageInfo(size, next)));
    }
}

/// <summary>Reads one banner.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class GetBannerQueryHandler(ContentDbContext context, ContentRenderer renderer, IClock clock)
    : IQueryHandler<GetBannerQuery, BannerResponse>
{
    public async Task<Result<BannerResponse>> HandleAsync(GetBannerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var banner = await context.Banners
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        if (banner is null)
        {
            return Result.Failure<BannerResponse>(ContentErrors.NotFound("banner"));
        }

        var response = await BannerImages
            .ToResponseAsync(banner, renderer, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Opens a banner.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class CreateBannerCommandHandler(ContentDbContext context, ContentRenderer renderer, IClock clock)
    : ICommandHandler<CreateBannerCommand, BannerResponse>
{
    public async Task<Result<BannerResponse>> HandleAsync(
        CreateBannerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var placement = command.Placement;
        var banner = Banner.Create(command.Name!.Trim(), placement);

        var applied = BannerImages.Apply(
            banner,
            placement,
            command.Name,
            command.MediaFileId,
            command.MobileMediaFileId,
            command.Message,
            command.AltText,
            command.Link,
            command.CtaLabel,
            command.Priority,
            command.StartsAt,
            command.EndsAt,
            command.Audience,
            command.IsActive);

        if (applied is not null)
        {
            return Result.Failure<BannerResponse>(applied);
        }

        context.Banners.Add(banner);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await BannerImages
            .ToResponseAsync(banner, renderer, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Rewrites a banner.</summary>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class UpdateBannerCommandHandler(ContentDbContext context, ContentRenderer renderer, IClock clock)
    : ICommandHandler<UpdateBannerCommand, BannerResponse>
{
    public async Task<Result<BannerResponse>> HandleAsync(
        UpdateBannerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var banner = await context.Banners
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (banner is null)
        {
            return Result.Failure<BannerResponse>(ContentErrors.NotFound("banner"));
        }

        var applied = BannerImages.Apply(
            banner,
            banner.Placement,
            command.Name,
            command.MediaFileId,
            command.MobileMediaFileId,
            command.Message,
            command.AltText,
            command.Link,
            command.CtaLabel,
            command.Priority,
            command.StartsAt,
            command.EndsAt,
            command.Audience,
            command.IsActive);

        if (applied is not null)
        {
            return Result.Failure<BannerResponse>(applied);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await BannerImages
            .ToResponseAsync(banner, renderer, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Switches a banner on or off.</summary>
/// <remarks>
/// Its own command rather than a field on the edit, and the reason is the incident. Taking a campaign
/// down in the next ninety seconds — a price that was wrong, a partner who pulled out — should not
/// require sending the whole banner back and passing its validation on the way.
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="renderer">Resolves the images for the response.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class SetBannerActiveCommandHandler(ContentDbContext context, ContentRenderer renderer, IClock clock)
    : ICommandHandler<SetBannerActiveCommand, BannerResponse>
{
    public async Task<Result<BannerResponse>> HandleAsync(
        SetBannerActiveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var banner = await context.Banners
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (banner is null)
        {
            return Result.Failure<BannerResponse>(ContentErrors.NotFound("banner"));
        }

        banner.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await BannerImages
            .ToResponseAsync(banner, renderer, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}

/// <summary>Removes a banner.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class DeleteBannerCommandHandler(ContentDbContext context) : ICommandHandler<DeleteBannerCommand>
{
    public async Task<Result> HandleAsync(DeleteBannerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var banner = await context.Banners
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (banner is null)
        {
            return Result.Failure(ContentErrors.NotFound("banner"));
        }

        context.Banners.Remove(banner);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>The banner rules and image resolution the four handlers share.</summary>
internal static class BannerImages
{
    /// <summary>Applies a banner's fields, refusing the combinations that would never render.</summary>
    /// <returns>The refusal, or null when the banner is sound.</returns>
    public static Error? Apply(
        Banner banner,
        BannerPlacement placement,
        string? name,
        Guid? mediaFileId,
        Guid? mobileMediaFileId,
        string? message,
        string? altText,
        string? link,
        string? ctaLabel,
        int priority,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        BannerAudience? audience,
        bool isActive)
    {
        ArgumentNullException.ThrowIfNull(banner);

        if (startsAt is not null && endsAt is not null && endsAt <= startsAt)
        {
            return ContentErrors.InvalidWindow;
        }

        if (link is { Length: > 0 } && !ContentFormats.IsLinkTarget(link))
        {
            return ContentErrors.MenuLinkIncomplete("banner");
        }

        // The announcement bar is words; every other placement is a picture, and a picture with no
        // alt text is an accessibility failure the store would ship on every page.
        var incomplete = placement == BannerPlacement.AnnouncementBar
            ? string.IsNullOrWhiteSpace(message)
            : mediaFileId is null || string.IsNullOrWhiteSpace(altText);

        if (incomplete)
        {
            return ContentErrors.BannerIncomplete(placement.ToString());
        }

        // Omitted and "None" both mean everybody: an editor who has not chosen an audience has not
        // narrowed one, and the enum's own zero value is the absence rather than a cohort.
        var resolved = audience is { } chosen && chosen != BannerAudience.None ? chosen : BannerAudience.Everyone;

        banner.Describe(
            name!.Trim(),
            placement == BannerPlacement.AnnouncementBar ? null : mediaFileId,
            placement == BannerPlacement.AnnouncementBar ? null : mobileMediaFileId,
            placement == BannerPlacement.AnnouncementBar ? message!.Trim() : null,
            string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
            string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
            string.IsNullOrWhiteSpace(ctaLabel) ? null : ctaLabel.Trim(),
            priority,
            startsAt,
            endsAt,
            resolved,
            isActive);

        return null;
    }

    /// <summary>Every media file a set of banners refers to.</summary>
    public static IReadOnlyList<Guid> Of(IReadOnlyList<Banner> banners)
    {
        ArgumentNullException.ThrowIfNull(banners);

        return
        [
            .. banners
                .SelectMany(banner => new[] { banner.MediaFileId, banner.MobileMediaFileId })
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct(),
        ];
    }

    /// <summary>Finds a resolved image, attaching the banner's own alt text.</summary>
    public static ContentImageResponse? Find(
        IReadOnlyDictionary<Guid, ContentImageResponse> images,
        Guid? fileId,
        string? alt)
    {
        ArgumentNullException.ThrowIfNull(images);

        return fileId is not null && images.TryGetValue(fileId.Value, out var image)
            ? image with { Alt = alt }
            : null;
    }

    /// <summary>Builds one banner's response, resolving its two images.</summary>
    public static async Task<BannerResponse> ToResponseAsync(
        Banner banner,
        ContentRenderer renderer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(banner);
        ArgumentNullException.ThrowIfNull(renderer);

        var images = await renderer.ResolveImagesAsync(Of([banner]), cancellationToken).ConfigureAwait(false);

        return ContentProjection.ToBanner(
            banner,
            Find(images, banner.MediaFileId, banner.AltText),
            Find(images, banner.MobileMediaFileId, banner.AltText),
            now);
    }
}
