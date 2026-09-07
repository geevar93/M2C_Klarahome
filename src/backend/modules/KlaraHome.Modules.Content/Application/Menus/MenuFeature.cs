using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Infrastructure.Rendering;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Application.Menus;

/// <summary>Lists the store's menus.</summary>
/// <param name="Placement">Only the menus rendered in one place.</param>
internal sealed record ListMenusQuery(string? Placement) : IQuery<IReadOnlyList<MenuSummaryResponse>>;

/// <summary>Reads one menu, items and all.</summary>
/// <param name="Id">The menu.</param>
internal sealed record GetMenuQuery(Guid Id) : IQuery<MenuResponse>;

/// <summary>Opens a menu.</summary>
/// <param name="Code">Its stable key, which the storefront asks for.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where the storefront renders it.</param>
internal sealed record CreateMenuCommand(string? Code, string? Name, string? Placement) : ICommand<MenuResponse>;

/// <summary>Rewrites a menu and every item in it.</summary>
/// <param name="Id">The menu.</param>
/// <param name="Name">What an editor calls it.</param>
/// <param name="Placement">Where it renders.</param>
/// <param name="IsActive">Whether it is served.</param>
/// <param name="Items">
/// The whole tree, parents before their children, or null to leave the items exactly as they are.
/// </param>
internal sealed record UpdateMenuCommand(
    Guid Id,
    string? Name,
    string? Placement,
    bool IsActive,
    IReadOnlyList<MenuItemBody>? Items) : ICommand<MenuResponse>;

/// <summary>Removes a menu.</summary>
/// <param name="Id">The menu.</param>
internal sealed record DeleteMenuCommand(Guid Id) : ICommand;

/// <summary>Rules a new menu has to satisfy.</summary>
internal sealed class CreateMenuCommandValidator : AbstractValidator<CreateMenuCommand>
{
    public CreateMenuCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(Menu.MaxCodeLength);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Menu.MaxNameLength);
        RuleFor(command => command.Placement).MaximumLength(40);
    }
}

/// <summary>Rules an edit has to satisfy. The code is not editable.</summary>
/// <remarks>
/// The code is what the storefront asks for by name, so changing one would be a menu the header
/// stopped finding — a navigation bar that quietly emptied itself on deploy.
/// </remarks>
internal sealed class UpdateMenuCommandValidator : AbstractValidator<UpdateMenuCommand>
{
    public UpdateMenuCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Menu.MaxNameLength);
        RuleFor(command => command.Placement).MaximumLength(40);

        RuleFor(command => command.Items)
            .Must(items => items is null || items.Count <= Menu.MaxItems)
            .WithMessage($"A menu may hold at most {Menu.MaxItems} items.");
    }
}

/// <summary>Lists the store's menus.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class ListMenusQueryHandler(ContentDbContext context)
    : IQueryHandler<ListMenusQuery, IReadOnlyList<MenuSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<MenuSummaryResponse>>> HandleAsync(
        ListMenusQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Menus.AsNoTracking().AsQueryable();

        if (query.Placement is { Length: > 0 } placement)
        {
            rows = rows.Where(menu => menu.Placement == placement);
        }

        // Every menu in the store, unpaged. There are a handful — a header, a footer, perhaps a
        // mobile drawer — and a cursor on a list of four rows is ceremony.
        var menus = await rows
            .OrderBy(menu => menu.Code)
            .Select(menu => new { Menu = menu, Count = menu.Items.Count })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<MenuSummaryResponse>>(
            [.. menus.Select(row => ContentProjection.ToMenuSummary(row.Menu, row.Count))]);
    }
}

/// <summary>Reads one menu in full.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class GetMenuQueryHandler(ContentDbContext context) : IQueryHandler<GetMenuQuery, MenuResponse>
{
    public async Task<Result<MenuResponse>> HandleAsync(GetMenuQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var menu = await context.Menus
            .AsNoTracking()
            .Include(row => row.Items)
            .FirstOrDefaultAsync(row => row.Id == query.Id, cancellationToken)
            .ConfigureAwait(false);

        return menu is null
            ? Result.Failure<MenuResponse>(ContentErrors.NotFound("menu"))
            : Result.Success(ContentProjection.ToMenu(menu));
    }
}

/// <summary>Opens a menu.</summary>
/// <param name="context">The Content data context.</param>
internal sealed class CreateMenuCommandHandler(ContentDbContext context)
    : ICommandHandler<CreateMenuCommand, MenuResponse>
{
    public async Task<Result<MenuResponse>> HandleAsync(
        CreateMenuCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = ContentFormats.ToSlug(command.Code!, Menu.MaxCodeLength);

        if (!ContentFormats.Code().IsMatch(code))
        {
            return Result.Failure<MenuResponse>(ContentErrors.InvalidSlug);
        }

        if (await context.Menus.AnyAsync(menu => menu.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MenuResponse>(ContentErrors.DuplicateCode(code));
        }

        var menu = Menu.Create(code, command.Name!.Trim(), command.Placement?.Trim());

        context.Menus.Add(menu);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ContentProjection.ToMenu(menu));
    }
}

/// <summary>
/// Rewrites a menu and every item in it.
/// </summary>
/// <remarks>
/// <para>
/// The whole tree in one call, because that is how a navigation editor works: somebody drags items
/// between columns and presses save once. The body is a flat list with parents named by id and parents
/// listed before their children, which makes depth, ordering and orphaning all one pass rather than
/// three recursive walks.
/// </para>
/// <para>
/// Items are reconciled by id rather than deleted and re-inserted, for the reason a page's blocks are:
/// deleting and re-adding rows with the same primary key in one transaction is something the change
/// tracker refuses outright.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
internal sealed class UpdateMenuCommandHandler(ContentDbContext context)
    : ICommandHandler<UpdateMenuCommand, MenuResponse>
{
    public async Task<Result<MenuResponse>> HandleAsync(
        UpdateMenuCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var menu = await context.Menus
            .Include(row => row.Items)
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (menu is null)
        {
            return Result.Failure<MenuResponse>(ContentErrors.NotFound("menu"));
        }

        menu.Describe(command.Name!.Trim(), command.Placement?.Trim(), command.IsActive);

        if (command.Items is not null)
        {
            var rebuilt = Rebuild(menu, command.Items);

            if (rebuilt.IsFailure)
            {
                return Result.Failure<MenuResponse>(rebuilt.Error);
            }

            menu.ReplaceItems(rebuilt.Value);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ContentProjection.ToMenu(menu));
    }

    /// <summary>Validates the tree a caller sent and reconciles it against the items that exist.</summary>
    private static Result<List<MenuItem>> Rebuild(Menu menu, IReadOnlyList<MenuItemBody> bodies)
    {
        var existing = menu.Items.ToDictionary(item => item.Id);
        var depths = new Dictionary<Guid, int>();
        var kept = new List<MenuItem>(bodies.Count);
        var positions = new Dictionary<Guid, int>();

        foreach (var body in bodies)
        {
            if (string.IsNullOrWhiteSpace(body.Label))
            {
                return Result.Failure<List<MenuItem>>(
                    ContentErrors.MenuLinkIncomplete(body.LinkType.ToString()));
            }

            // The link type is an enum in the contract, so an unknown word is refused by the model
            // binder before this runs (Step 28B, deliverable 11).
            var linkType = body.LinkType;

            var check = CheckTarget(linkType, body);

            if (check is not null)
            {
                return Result.Failure<List<MenuItem>>(check);
            }

            var depth = 0;

            if (body.ParentId is { } parentId)
            {
                // The parent has to have been seen already. Requiring parents first is what turns a
                // cycle — an item that is its own grandparent — into a refusal rather than an
                // infinite render.
                if (!depths.TryGetValue(parentId, out var parentDepth))
                {
                    return Result.Failure<List<MenuItem>>(ContentErrors.MenuItemOrphaned);
                }

                depth = parentDepth + 1;

                if (depth >= MenuItem.MaxDepth)
                {
                    return Result.Failure<List<MenuItem>>(ContentErrors.MenuTooDeep(MenuItem.MaxDepth));
                }
            }

            var siblingKey = body.ParentId ?? Guid.Empty;
            positions.TryGetValue(siblingKey, out var position);
            positions[siblingKey] = position + 1;

            var item = body.Id is { } id && existing.TryGetValue(id, out var found)
                ? Reuse(found, body, linkType, position, depth)
                : MenuItem.Create(
                    menu.Id,
                    body.ParentId,
                    body.Label.Trim(),
                    linkType,
                    body.TargetId,
                    Normalize(linkType, body.Url),
                    position,
                    depth);

            item.Present(body.IsVisible, body.OpensInNewTab, body.IconFileId, Trim(body.Badge));

            depths[item.Id] = depth;
            kept.Add(item);
        }

        return Result.Success(kept);
    }

    /// <summary>
    /// Keeps an item that already exists, when nothing structural about it changed.
    /// </summary>
    /// <remarks>
    /// An item whose parent, label, link or position moved is rebuilt with a new identity rather than
    /// mutated: <see cref="MenuItem"/> deliberately has no setter for those, because they are what the
    /// item <em>is</em>, and a menu item is cheap enough that recreating one costs nothing. What is
    /// preserved is the common case — an editor toggling visibility or adding a badge — where the row
    /// genuinely is the same item.
    /// </remarks>
    private static MenuItem Reuse(MenuItem found, MenuItemBody body, MenuLinkType linkType, int position, int depth)
        => found.ParentId == body.ParentId
           && found.LinkType == linkType
           && found.TargetId == body.TargetId
           && string.Equals(found.Label, body.Label?.Trim(), StringComparison.Ordinal)
           && string.Equals(found.Url, Normalize(linkType, body.Url), StringComparison.Ordinal)
           && found.Position == position
           && found.Depth == depth
            ? found
            : MenuItem.Create(
                found.MenuId,
                body.ParentId,
                body.Label!.Trim(),
                linkType,
                body.TargetId,
                Normalize(linkType, body.Url),
                position,
                depth);

    /// <summary>Whether an item carries what its link type needs, and nothing it does not.</summary>
    private static Error? CheckTarget(MenuLinkType linkType, MenuItemBody body)
        => linkType switch
        {
            MenuLinkType.None when body.TargetId is not null || !string.IsNullOrWhiteSpace(body.Url) =>
                ContentErrors.MenuLinkIncomplete(linkType.ToString()),

            MenuLinkType.Url when !ContentFormats.IsLinkTarget(body.Url) =>
                ContentErrors.MenuLinkIncomplete(linkType.ToString()),

            MenuLinkType.Page or MenuLinkType.Category or MenuLinkType.Collection
                when body.TargetId is null || body.TargetId == Guid.Empty =>
                ContentErrors.MenuLinkIncomplete(linkType.ToString()),

            _ => null,
        };

    /// <summary>The literal URL, kept only for the link type that has one.</summary>
    private static string? Normalize(MenuLinkType linkType, string? url)
        => linkType == MenuLinkType.Url && !string.IsNullOrWhiteSpace(url) ? url.Trim() : null;

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Removes a menu.</summary>
/// <remarks>
/// A hard delete, unlike a page. A menu is a rule about navigation rather than a record: there is no
/// history to preserve, nothing links to one, and an editor who wants to keep one without rendering
/// it switches it off instead.
/// </remarks>
/// <param name="context">The Content data context.</param>
internal sealed class DeleteMenuCommandHandler(ContentDbContext context) : ICommandHandler<DeleteMenuCommand>
{
    public async Task<Result> HandleAsync(DeleteMenuCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var menu = await context.Menus
            .Include(row => row.Items)
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (menu is null)
        {
            return Result.Failure(ContentErrors.NotFound("menu"));
        }

        context.Menus.Remove(menu);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
