using System.Text.Json;
using KlaraHome.Modules.Content.Application;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using KlaraHome.Modules.Content.Infrastructure.Collections;

namespace KlaraHome.Modules.Content.Infrastructure.Rendering;

/// <summary>
/// One block, as a version snapshot stores it.
/// </summary>
/// <remarks>
/// Its own record rather than the API's <c>BlockResponse</c>, and deliberately so. A snapshot is
/// written once and read back years later, possibly by a build in which the response shape has moved
/// on; giving it a type of its own means changing the API cannot silently change what a rollback
/// restores. The id is kept so that a restored block keeps the identity it had, which is what makes a
/// rollback a restoration rather than a re-creation.
/// </remarks>
/// <param name="Id">The block.</param>
/// <param name="Type">Its type, as a word.</param>
/// <param name="Position">Where it sat.</param>
/// <param name="Config">Its configuration document.</param>
/// <param name="IsVisible">Whether it was rendered.</param>
/// <param name="StartsAt">When it started appearing.</param>
/// <param name="EndsAt">When it stopped.</param>
internal sealed record BlockSnapshot(
    Guid Id,
    string Type,
    int Position,
    JsonElement Config,
    bool IsVisible,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

/// <summary>
/// Turns this module's entities into the shapes the admin screens read, and back again.
/// </summary>
/// <remarks>
/// Static and free of data access on purpose. Everything that needs a round trip — resolving a media
/// id, resolving a product — is done by the caller and handed in, so a projection cannot quietly
/// become a query and a list endpoint cannot quietly become N of them.
/// </remarks>
internal static class ContentProjection
{
    /// <summary>Turns a stored SEO block into the shape a caller reads.</summary>
    /// <param name="seo">The stored block.</param>
    /// <param name="ogImage">Its Open Graph image, already resolved.</param>
    public static SeoResponse ToSeo(SeoMetadata seo, ContentImageResponse? ogImage)
    {
        ArgumentNullException.ThrowIfNull(seo);

        return new SeoResponse(
            seo.MetaTitle,
            seo.MetaDescription,
            seo.MetaKeywords,
            seo.CanonicalUrl,
            seo.OgTitle,
            seo.OgDescription,
            ogImage,
            seo.OgType,
            seo.NoIndex,
            seo.NoFollow,
            seo.SitemapPriority);
    }

    /// <summary>Turns an inbound SEO block into the shape that is stored.</summary>
    /// <param name="body">What the caller sent, or null for a page with no SEO block at all.</param>
    public static SeoMetadata FromSeo(SeoBody? body)
        => body is null
            ? new SeoMetadata()
            : new SeoMetadata
            {
                MetaTitle = Trim(body.MetaTitle),
                MetaDescription = Trim(body.MetaDescription),
                MetaKeywords = Trim(body.MetaKeywords),
                CanonicalUrl = Trim(body.CanonicalUrl),
                OgTitle = Trim(body.OgTitle),
                OgDescription = Trim(body.OgDescription),
                OgImageFileId = body.OgImageFileId,
                OgType = Trim(body.OgType),
                NoIndex = body.NoIndex,
                NoFollow = body.NoFollow,
                SitemapPriority = body.SitemapPriority,
            };

    /// <summary>Turns a block into the shape an editor reads.</summary>
    /// <param name="block">The block.</param>
    public static BlockResponse ToBlock(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return new BlockResponse(
            block.Id,
            block.Type.ToString(),
            block.Position,
            ContentJson.Parse(block.Config),
            block.IsVisible,
            block.StartsAt,
            block.EndsAt);
    }

    /// <summary>Turns a page into the row an editor's list shows.</summary>
    /// <param name="page">The page.</param>
    /// <param name="blockCount">How many blocks it holds, counted by the query rather than loaded.</param>
    public static PageSummaryResponse ToPageSummary(ContentPage page, int blockCount)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PageSummaryResponse(
            page.Id,
            page.Slug,
            page.Type.ToString(),
            page.Title,
            page.Status.ToString(),
            page.PublishedAt,
            page.ScheduledAt,
            page.ContentChangedAt,
            page.Version,
            blockCount,
            page.UpdatedAt);
    }

    /// <summary>Turns a page into the document an editor opens.</summary>
    /// <param name="page">The page.</param>
    /// <param name="seo">Its SEO block, already resolved.</param>
    /// <param name="coverImage">Its cover image, already resolved.</param>
    /// <param name="actor">Who is looking, so the transition list is theirs and not somebody else's.</param>
    public static PageResponse ToPage(
        ContentPage page,
        SeoResponse seo,
        ContentImageResponse? coverImage,
        PageActor actor)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new PageResponse(
            page.Id,
            page.Slug,
            page.Type.ToString(),
            page.Title,
            page.Summary,
            page.Status.ToString(),
            page.PublishedAt,
            page.ScheduledAt,
            page.ContentChangedAt,
            page.Version,
            seo,
            coverImage,
            page.Author,
            page.Tags,
            [.. page.Blocks.OrderBy(block => block.Position).Select(ToBlock)],
            [.. PageLifecycle.NextFrom(page.Status, actor).Select(status => status.ToString())],
            page.CreatedAt,
            page.UpdatedAt);
    }

    /// <summary>Turns a version into the row a history list shows.</summary>
    /// <param name="version">The version.</param>
    public static PageVersionSummaryResponse ToVersionSummary(PageVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new PageVersionSummaryResponse(
            version.Version,
            version.Title,
            version.Note,
            version.RestoredFrom,
            CountBlocks(version.Blocks),
            version.CreatedAt,
            version.CreatedBy);
    }

    /// <summary>Turns a version into the document a preview or a comparison reads.</summary>
    /// <param name="version">The version.</param>
    /// <param name="seo">Its SEO block, already resolved.</param>
    public static PageVersionResponse ToVersion(PageVersion version, SeoResponse seo)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new PageVersionResponse(
            version.Version,
            version.Title,
            seo,
            [.. ReadSnapshot(version.Blocks).Select(snapshot => new BlockResponse(
                snapshot.Id,
                snapshot.Type,
                snapshot.Position,
                snapshot.Config,
                snapshot.IsVisible,
                snapshot.StartsAt,
                snapshot.EndsAt))],
            version.Note,
            version.RestoredFrom,
            version.CreatedAt,
            version.CreatedBy);
    }

    /// <summary>Turns a menu item into the shape an editor reads.</summary>
    /// <param name="item">The item.</param>
    public static MenuItemResponse ToMenuItem(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new MenuItemResponse(
            item.Id,
            item.ParentId,
            item.Label,
            item.LinkType.ToString(),
            item.TargetId,
            item.Url,
            item.Position,
            item.Depth,
            item.IsVisible,
            item.OpensInNewTab,
            item.IconFileId,
            item.Badge);
    }

    /// <summary>Turns a menu into the document an editor opens.</summary>
    /// <param name="menu">The menu.</param>
    public static MenuResponse ToMenu(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        return new MenuResponse(
            menu.Id,
            menu.Code,
            menu.Name,
            menu.Placement,
            menu.IsActive,
            [.. menu.Items
                .OrderBy(item => item.Depth)
                .ThenBy(item => item.Position)
                .Select(ToMenuItem)],
            menu.CreatedAt,
            menu.UpdatedAt);
    }

    /// <summary>Turns a menu into the row an editor's list shows.</summary>
    /// <param name="menu">The menu.</param>
    /// <param name="itemCount">How many items it holds.</param>
    public static MenuSummaryResponse ToMenuSummary(Menu menu, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(menu);

        return new MenuSummaryResponse(
            menu.Id,
            menu.Code,
            menu.Name,
            menu.Placement,
            menu.IsActive,
            itemCount,
            menu.UpdatedAt);
    }

    /// <summary>Turns a banner into the shape an editor reads.</summary>
    /// <param name="banner">The banner.</param>
    /// <param name="image">Its desktop image, already resolved.</param>
    /// <param name="mobileImage">Its mobile image, already resolved.</param>
    /// <param name="now">The instant "is it live" is answered for.</param>
    public static BannerResponse ToBanner(
        Banner banner,
        ContentImageResponse? image,
        ContentImageResponse? mobileImage,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(banner);

        return new BannerResponse(
            banner.Id,
            banner.Name,
            banner.Placement.ToString(),
            image,
            mobileImage,
            banner.Message,
            banner.AltText,
            banner.Link,
            banner.CtaLabel,
            banner.Priority,
            banner.StartsAt,
            banner.EndsAt,
            banner.Audience.ToString(),
            banner.IsActive,
            banner.IsLiveFor(now, BannerAudience.Everyone),
            banner.UpdatedAt);
    }

    /// <summary>Turns a banner into the shape the storefront renders.</summary>
    /// <param name="banner">The banner.</param>
    /// <param name="image">Its desktop image, already resolved.</param>
    /// <param name="mobileImage">Its mobile image, already resolved.</param>
    public static StoreBannerResponse ToStoreBanner(
        Banner banner,
        ContentImageResponse? image,
        ContentImageResponse? mobileImage)
    {
        ArgumentNullException.ThrowIfNull(banner);

        return new StoreBannerResponse(
            banner.Id,
            banner.Placement.ToString(),
            image,
            mobileImage,
            banner.Message,
            banner.AltText,
            banner.Link,
            banner.CtaLabel,
            banner.Priority);
    }

    /// <summary>Turns a collection into the row an editor's list shows.</summary>
    /// <param name="collection">The collection.</param>
    public static CollectionSummaryResponse ToCollectionSummary(ProductCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        return new CollectionSummaryResponse(
            collection.Id,
            collection.Slug,
            collection.Name,
            collection.Kind.ToString(),
            collection.IsActive,
            collection.IsListed,
            collection.ItemCount,
            collection.RefreshedAt,
            collection.UpdatedAt);
    }

    /// <summary>Turns a collection into the document an editor opens.</summary>
    /// <param name="collection">The collection.</param>
    /// <param name="seo">Its SEO block, already resolved.</param>
    /// <param name="heroImage">Its masthead, already resolved.</param>
    public static CollectionResponse ToCollection(
        ProductCollection collection,
        SeoResponse seo,
        ContentImageResponse? heroImage)
    {
        ArgumentNullException.ThrowIfNull(collection);

        var rule = collection.Kind == CollectionKind.Rule
            ? ToRule(CollectionRules.Read(collection.Rules))
            : null;

        return new CollectionResponse(
            collection.Id,
            collection.Slug,
            collection.Name,
            collection.Description,
            collection.Kind.ToString(),
            rule,
            seo,
            heroImage,
            collection.IsActive,
            collection.IsListed,
            collection.ItemCount,
            collection.RefreshedAt,
            collection.CreatedAt,
            collection.UpdatedAt);
    }

    /// <summary>Turns a rule into the shape an editor reads.</summary>
    /// <param name="rule">The rule.</param>
    public static CollectionRuleResponse ToRule(CollectionRuleSet rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new CollectionRuleResponse(
            rule.MatchAll,
            [.. rule.Conditions.Select(condition => new RuleConditionResponse(
                condition.Field.ToString(),
                condition.Key,
                condition.Operator.ToString(),
                condition.Values))],
            rule.Sort.ToString(),
            rule.Limit,
            rule.IncludeOutOfStock);
    }

    /// <summary>Turns a redirect into the shape an editor reads.</summary>
    /// <param name="redirect">The rule.</param>
    public static RedirectResponse ToRedirect(Redirect redirect)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        return new RedirectResponse(
            redirect.Id,
            redirect.FromPath,
            redirect.ToPath,
            (int)redirect.Status,
            redirect.HitCount,
            redirect.LastHitAt,
            redirect.IsActive,
            redirect.Note,
            redirect.CreatedAt);
    }

    /// <summary>Turns a block type's schema into the shape the admin block picker reads.</summary>
    /// <param name="descriptor">The schema.</param>
    public static BlockTypeResponse ToBlockType(BlockDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return new BlockTypeResponse(
            descriptor.Type.ToString(),
            descriptor.Label,
            descriptor.Description,
            [.. descriptor.Fields.Select(ToBlockField)],
            descriptor.Items is null ? null : [.. descriptor.Items.Select(ToBlockField)],
            descriptor.MaxItems,
            descriptor.IsPrivileged);
    }

    /// <summary>Serialises a page's blocks for a version snapshot.</summary>
    /// <param name="blocks">The blocks, in position order.</param>
    public static string WriteSnapshot(IReadOnlyList<ContentBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var snapshots = blocks
            .OrderBy(block => block.Position)
            .Select(block => new BlockSnapshot(
                block.Id,
                block.Type.ToString(),
                block.Position,
                ContentJson.Parse(block.Config),
                block.IsVisible,
                block.StartsAt,
                block.EndsAt))
            .ToList();

        return JsonSerializer.Serialize(snapshots, ContentJson.Options);
    }

    /// <summary>Reads a version snapshot back.</summary>
    /// <remarks>
    /// A snapshot that will not parse comes back empty rather than throwing. The history is read on
    /// a screen an editor opens when something has already gone wrong, and a list that refuses to
    /// render because one row of it is malformed is the worst possible moment to find out.
    /// </remarks>
    /// <param name="json">The stored snapshot.</param>
    public static IReadOnlyList<BlockSnapshot> ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<BlockSnapshot>>(json, ContentJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>How many blocks a snapshot holds, without materialising them.</summary>
    private static int CountBlocks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static BlockFieldResponse ToBlockField(BlockField field)
        => new(
            field.Name,
            field.Kind.ToString(),
            field.IsRequired,
            field.IsList,
            field.MaxLength,
            field.Choices);

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
