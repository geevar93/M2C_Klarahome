using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Pages;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Templates;

/// <summary>Lists the storefront templates.</summary>
internal sealed record ListStorefrontTemplatesQuery : IQuery<IReadOnlyList<StorefrontTemplateResponse>>;

/// <summary>Renders a template, filled from the catalogue, without saving anything.</summary>
/// <param name="Id">The template.</param>
internal sealed record PreviewStorefrontTemplateQuery(string Id) : IQuery<StorePageResponse>;

/// <summary>Puts a template's blocks on the home page's draft.</summary>
/// <param name="Id">The template.</param>
internal sealed record ApplyStorefrontTemplateCommand(string Id) : ICommand<PageResponse>;

/// <summary>Rules a template id has to satisfy.</summary>
internal sealed class PreviewStorefrontTemplateQueryValidator : AbstractValidator<PreviewStorefrontTemplateQuery>
{
    public PreviewStorefrontTemplateQueryValidator() => RuleFor(query => query.Id).NotEmpty().MaximumLength(40);
}

/// <summary>Rules a template id has to satisfy.</summary>
internal sealed class ApplyStorefrontTemplateCommandValidator : AbstractValidator<ApplyStorefrontTemplateCommand>
{
    public ApplyStorefrontTemplateCommandValidator() => RuleFor(command => command.Id).NotEmpty().MaximumLength(40);
}

/// <summary>Lists the storefront templates.</summary>
internal sealed class ListStorefrontTemplatesQueryHandler
    : IQueryHandler<ListStorefrontTemplatesQuery, IReadOnlyList<StorefrontTemplateResponse>>
{
    public Task<Result<IReadOnlyList<StorefrontTemplateResponse>>> HandleAsync(
        ListStorefrontTemplatesQuery query,
        CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<IReadOnlyList<StorefrontTemplateResponse>>(
            [.. StorefrontTemplateCatalog.All.Select(template => new StorefrontTemplateResponse(
                template.Id,
                template.Name,
                template.Suits,
                template.Description,
                template.HeaderStyle,
                template.FooterStyle,
                template.PaletteIds))]));
}

/// <summary>
/// Renders a template as the storefront would, without saving anything.
/// </summary>
/// <remarks>
/// The page is built in memory and never added to the context, so there is nothing for a later
/// <c>SaveChanges</c> in the scope to write. Blocks are bound in draft mode: the hero has no picture
/// yet and an empty catalogue leaves a carousel with nothing in it, and neither is a reason to refuse
/// a preview.
/// </remarks>
/// <param name="filler">Fills the reference fields from the catalogue.</param>
/// <param name="binder">Validates and canonicalises the blocks.</param>
/// <param name="composer">Resolves the blocks as the storefront does.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class PreviewStorefrontTemplateQueryHandler(
    StorefrontTemplateFiller filler,
    PageBlockBinder binder,
    PageComposer composer,
    ContentScope scope,
    IClock clock)
    : IQueryHandler<PreviewStorefrontTemplateQuery, StorePageResponse>
{
    public async Task<Result<StorePageResponse>> HandleAsync(
        PreviewStorefrontTemplateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var template = StorefrontTemplateCatalog.Find(query.Id);

        if (template is null)
        {
            return Result.Failure<StorePageResponse>(ContentErrors.NotFound("storefront template"));
        }

        var bodies = await filler.FillAsync(template, cancellationToken).ConfigureAwait(false);

        var bound = await binder
            .BindAsync(bodies, scope.CanWriteCustomHtml, cancellationToken, allowIncomplete: true)
            .ConfigureAwait(false);

        if (bound.IsFailure)
        {
            return Result.Failure<StorePageResponse>(bound.Error);
        }

        var now = clock.UtcNow;
        var page = ApplyStorefrontTemplateCommandHandler.NewHomePage(now);
        page.SyncBlocks(bound.Value, now);

        var composed = await composer.ComposeAsync(page, now, cancellationToken).ConfigureAwait(false);

        return Result.Success(composed);
    }
}

/// <summary>
/// Puts a template's blocks on the home page's draft.
/// </summary>
/// <remarks>
/// <para>
/// Creates the home page (slug <c>home</c>) when there is none; otherwise replaces the blocks of the
/// existing one. It never transitions the page: a published home page stays published, and the version
/// history is untouched, which is what makes "apply" recoverable through the rollback that already
/// exists.
/// </para>
/// <para>
/// Blocks are bound in draft mode, so the stored hero has no picture until the editor picks one. A
/// plain page save validates strictly and will ask for it.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="filler">Fills the reference fields from the catalogue.</param>
/// <param name="binder">Validates and canonicalises the blocks.</param>
/// <param name="renderer">Resolves images for the response.</param>
/// <param name="scope">Who is asking.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ApplyStorefrontTemplateCommandHandler(
    ContentDbContext context,
    StorefrontTemplateFiller filler,
    PageBlockBinder binder,
    ContentRenderer renderer,
    ContentScope scope,
    IClock clock)
    : ICommandHandler<ApplyStorefrontTemplateCommand, PageResponse>
{
    /// <summary>The address of the home page.</summary>
    internal const string HomeSlug = "home";

    /// <summary>A home page that exists only in memory, for a preview or for a first apply.</summary>
    /// <param name="now">When.</param>
    internal static ContentPage NewHomePage(DateTimeOffset now)
    {
        var page = ContentPage.Create(HomeSlug, PageType.Home, "Home");

        page.Describe(HomeSlug, "Home", null, new SeoMetadata(), null, null, [], now);

        return page;
    }

    public async Task<Result<PageResponse>> HandleAsync(
        ApplyStorefrontTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var template = StorefrontTemplateCatalog.Find(command.Id);

        if (template is null)
        {
            return Result.Failure<PageResponse>(ContentErrors.NotFound("storefront template"));
        }

        var homes = await context.Pages
            .Include(row => row.Blocks)
            .Where(row => row.Type == PageType.Home)
            .OrderByDescending(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var page = homes.FirstOrDefault(row => PageLifecycle.IsEditable(row.Status));

        if (page is null && homes.Count > 0)
        {
            return Result.Failure<PageResponse>(ContentErrors.PageNotEditable(homes[0].Status.ToString()));
        }

        if (page is null
            && await context.Pages.AnyAsync(row => row.Slug == HomeSlug, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PageResponse>(ContentErrors.DuplicateSlug(HomeSlug));
        }

        var bodies = await filler.FillAsync(template, cancellationToken).ConfigureAwait(false);

        var bound = await binder
            .BindAsync(bodies, scope.CanWriteCustomHtml, cancellationToken, allowIncomplete: true)
            .ConfigureAwait(false);

        if (bound.IsFailure)
        {
            return Result.Failure<PageResponse>(bound.Error);
        }

        var now = clock.UtcNow;

        if (page is null)
        {
            page = NewHomePage(now);
            page.SyncBlocks(bound.Value, now);
            context.Pages.Add(page);
        }
        else
        {
            page.SyncBlocks(bound.Value, now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var response = await PageReader
            .ToResponseAsync(page, renderer, scope.Actor, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(response);
    }
}
