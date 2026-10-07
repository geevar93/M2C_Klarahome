using System.Text.Json;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Content.Application.Pages;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Content.Infrastructure.Seeding;

/// <summary>
/// The storefront's header and footer navigation for the demonstration catalogue, and the three
/// informational pages the footer links to.
/// </summary>
/// <remarks>
/// <para>
/// **Without these the shell is a bare frame.** The storefront reads two menus by code - <c>header</c>
/// and <c>footer</c> - on every page, and a database with a catalogue and no menus opens on a header
/// with no navigation and a footer with only its always-valid links. The categories exist; nothing
/// on screen leads to them.
/// </para>
/// <para>
/// **A menu is only created when there is none for that placement.** Navigation is the other thing
/// somebody is likely to have hand-built in a shared development database, and a seeder that rewrote
/// it each deploy would discard that work. Same rule, same fence, as <see cref="DemoHomePageSeeder"/>.
/// </para>
/// <para>
/// **No dead links.** Category items target the category by id, so they follow a rename; the three
/// footer pages are written here (only if the slug is free) so that "About us" opens a page rather
/// than a 404. There is deliberately no "Sale" item: the storefront's search page is empty without a
/// query and a sale collection is a rule somebody should author, so any link written here would be a
/// dead end. An item whose target cannot be resolved is left out instead of written dangling - the
/// menu composer would drop it anyway.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Content data context.</param>
/// <param name="taxonomy">Resolves the demonstration categories the items target.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was written, and why nothing was when nothing was.</param>
internal sealed partial class DemoMenuSeeder(
    ContentDbContext context,
    ICatalogTaxonomy taxonomy,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DemoMenuSeeder> logger) : IDataSeeder
{
    private const string HeaderCode = "header";
    private const string FooterCode = "footer";
    private const string PublishNote = "Published with the demonstration data.";

    /// <summary>The footer pages: slug, title, summary, body.</summary>
    private static readonly (string Slug, string Title, string Summary, string Body)[] FooterPages =
    [
        (
            "about-us",
            "About us",
            "Who makes Klara Home, and how.",
            "<p>Klara Home is a small label for hand-finished home textiles and stoneware. Our "
            + "block-printed cottons come from Sanganer, our quilts from Rajasthan and our stoneware "
            + "from a studio above Coonoor. Everything is made in small batches by the people who "
            + "designed it, which is why no two pieces are quite alike.</p>"
            + "<p>This is demonstration content for the development storefront.</p>"),
        (
            "shipping-and-returns",
            "Shipping & returns",
            "How long delivery takes and how to send something back.",
            "<p>Orders ship within two working days and arrive in three to seven, depending on your "
            + "pin code. Delivery is free above the threshold shown at checkout.</p>"
            + "<p>If something is not right, tell us within seven days of delivery and we will arrange "
            + "a pick-up, a replacement or a refund to your original payment method.</p>"
            + "<p>This is demonstration content for the development storefront.</p>"),
        (
            "contact",
            "Contact",
            "How to reach us.",
            "<p>Write to <strong>hello@klarahome.example</strong> and we will reply within one "
            + "working day. For an order, quote the order number from your confirmation email.</p>"
            + "<p>This is demonstration content for the development storefront.</p>"),
    ];

    private static readonly string[] HelpSlugs = ["shipping-and-returns", "contact"];

    private static readonly string[] CompanySlugs = ["about-us"];

    /// <inheritdoc />
    public string Name => "Content.DemoMenus";

    /// <summary>After the demonstration catalogue and its images, before the home page that links the same places.</summary>
    public int Order => 917;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        var categories = (await taxonomy
                .ListCategoriesAsync(activeOnly: true, cancellationToken)
                .ConfigureAwait(false))
            .Where(category => category.Slug.StartsWith("demo-", StringComparison.Ordinal))
            .ToDictionary(category => category.Slug, StringComparer.Ordinal);

        if (categories.Count == 0)
        {
            // Nothing to navigate to. A menu of one dead "Sale" link is worse than the absence.
            return;
        }

        var wroteHeader = await EnsureHeaderAsync(categories, cancellationToken).ConfigureAwait(false);
        var wroteFooter = await EnsureFooterAsync(categories, cancellationToken).ConfigureAwait(false);

        if (wroteHeader || wroteFooter)
        {
            MenusSeeded(logger, wroteHeader, wroteFooter);
        }
    }

    /// <summary>The header: the two demonstration roots with their leaves beneath, and nothing else.</summary>
    private async Task<bool> EnsureHeaderAsync(
        IReadOnlyDictionary<string, CategoryNode> categories,
        CancellationToken cancellationToken)
    {
        if (await MenuExistsAsync(HeaderCode, cancellationToken).ConfigureAwait(false))
        {
            MenuAlreadyAuthored(logger, HeaderCode);
            return false;
        }

        var menu = Menu.Create(HeaderCode, "Header", HeaderCode);
        var items = new List<MenuItem>();

        // Roots and children in the order the taxonomy lists them - the same order the shop shows them.
        var roots = categories.Values
            .Where(category => category.ParentId is null)
            .ToList();

        var position = 0;

        foreach (var root in roots)
        {
            var parent = MenuItem.Create(menu.Id, null, root.Name, MenuLinkType.Category, root.Id, null, position++, 0);
            items.Add(parent);

            var childPosition = 0;

            foreach (var child in categories.Values
                         .Where(category => category.ParentId == root.Id))
            {
                items.Add(MenuItem.Create(
                    menu.Id, parent.Id, child.Name, MenuLinkType.Category, child.Id, null, childPosition++, 1));
            }
        }

        menu.ReplaceItems(items);
        context.Menus.Add(menu);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>The footer: Shop, Help and Company columns.</summary>
    private async Task<bool> EnsureFooterAsync(
        IReadOnlyDictionary<string, CategoryNode> categories,
        CancellationToken cancellationToken)
    {
        if (await MenuExistsAsync(FooterCode, cancellationToken).ConfigureAwait(false))
        {
            MenuAlreadyAuthored(logger, FooterCode);
            return false;
        }

        var pageIds = await EnsureFooterPagesAsync(cancellationToken).ConfigureAwait(false);

        var menu = Menu.Create(FooterCode, "Footer", FooterCode);
        var items = new List<MenuItem>();
        var column = 0;

        // One column per heading; a heading whose links all failed to resolve is left out whole.
        void Add(string heading, IReadOnlyList<(string Label, MenuLinkType Type, Guid TargetId)> links)
        {
            if (links.Count == 0)
            {
                return;
            }

            var parent = MenuItem.Create(menu.Id, null, heading, MenuLinkType.None, null, null, column++, 0);
            items.Add(parent);

            for (var index = 0; index < links.Count; index++)
            {
                var (label, type, targetId) = links[index];
                items.Add(MenuItem.Create(menu.Id, parent.Id, label, type, targetId, null, index, 1));
            }
        }

        Add(
            "Shop",
            [
                .. categories.Values
                    .Where(category => category.ParentId is not null)
                    .Select(category => (category.Name, MenuLinkType.Category, category.Id)),
            ]);

        Add(
            "Help",
            [
                .. HelpSlugs
                    .Where(pageIds.ContainsKey)
                    .Select(slug => (PageTitle(slug), MenuLinkType.Page, pageIds[slug])),
            ]);

        Add(
            "Company",
            [
                .. CompanySlugs
                    .Where(pageIds.ContainsKey)
                    .Select(slug => (PageTitle(slug), MenuLinkType.Page, pageIds[slug])),
            ]);

        menu.ReplaceItems(items);
        context.Menus.Add(menu);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static string PageTitle(string slug) => FooterPages.First(page => page.Slug == slug).Title;

    /// <summary>
    /// Whether a menu already answers to this code or this placement - the storefront asks by code
    /// and falls back to placement, so either is a menu that is already doing the job.
    /// </summary>
    private Task<bool> MenuExistsAsync(string code, CancellationToken cancellationToken)
        => context.Menus.AnyAsync(menu => menu.Code == code || menu.Placement == code, cancellationToken);

    /// <summary>
    /// The footer pages' ids, by slug: the published page already at that slug if there is one, a
    /// newly written one otherwise. A slug held by something unpublished is omitted, because a link
    /// to it would be a 404.
    /// </summary>
    private async Task<Dictionary<string, Guid>> EnsureFooterPagesAsync(CancellationToken cancellationToken)
    {
        var slugs = FooterPages.Select(page => page.Slug).ToList();

        var existing = await context.Pages
            .Where(page => slugs.Contains(page.Slug))
            .Select(page => new { page.Id, page.Slug, page.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = existing
            .Where(page => page.Status == PageStatus.Published)
            .ToDictionary(page => page.Slug, page => page.Id, StringComparer.Ordinal);

        var taken = existing.Select(page => page.Slug).ToHashSet(StringComparer.Ordinal);
        var now = clock.UtcNow;

        foreach (var (slug, title, summary, body) in FooterPages)
        {
            if (taken.Contains(slug))
            {
                continue;
            }

            var page = ContentPage.Create(slug, PageType.Static, title);

            page.Describe(
                slug,
                title,
                summary,
                new SeoMetadata { MetaTitle = title + " — Klara Home", MetaDescription = summary, NoIndex = true },
                coverImageFileId: null,
                author: null,
                tags: [],
                now);

            page.SyncBlocks(
                [
                    new BlockDraft(
                        null,
                        BlockType.RichText,
                        JsonSerializer.Serialize(new { heading = title, body, width = "narrow" }),
                        IsVisible: true,
                        StartsAt: null,
                        EndsAt: null),
                ],
                now);

            // `Publisher`, not `System`: see DemoHomePageSeeder for why, and why the result is checked.
            if (!page.Transition(PageStatus.Published, PageActor.Publisher, now))
            {
                throw new InvalidOperationException(
                    $"The demonstration page '{slug}' could not be published. The page life cycle has "
                    + "changed and this seeder now writes a draft nobody can see.");
            }

            TransitionPageCommandHandler.Snapshot(page, PublishNote, restoredFrom: null, now, actorId: null, context);

            context.Pages.Add(page);
            result[slug] = page.Id;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    [LoggerMessage(
        EventId = 9108,
        Level = LogLevel.Information,
        Message = "Seeded demonstration navigation: header {Header}, footer {Footer}.")]
    private static partial void MenusSeeded(ILogger logger, bool header, bool footer);

    [LoggerMessage(
        EventId = 9109,
        Level = LogLevel.Information,
        Message = "A '{Menu}' menu already exists, so the demonstration one was not written.")]
    private static partial void MenuAlreadyAuthored(ILogger logger, string menu);
}
