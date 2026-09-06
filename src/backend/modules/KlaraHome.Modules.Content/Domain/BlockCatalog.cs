namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// The kinds of block a page can be composed from (docs/03-database-design.md §4.14).
/// </summary>
/// <remarks>
/// <para>
/// A closed list, and that is the point of it. The whole promise of this module is that a
/// merchandiser can change the storefront without a deployment; the promise only holds if the
/// storefront knows in advance how to render everything it might be handed. A block type is
/// therefore a joint declaration — this enum and a component in the Angular app — and adding one is
/// a release rather than an afternoon.
/// </para>
/// <para>
/// <see cref="CustomHtml"/> is the escape hatch and it is the dangerous one. It exists because a
/// campaign always eventually needs a shape nobody anticipated, and it is gated by its own
/// permission and its own feature flag for the same reason: arbitrary markup on a page a shopper
/// loads is a stored-XSS vector, and the person who may write copy is not automatically the person
/// who may write script tags.
/// </para>
/// </remarks>
internal enum BlockType
{
    /// <summary>A full-width image or video with a headline and a call to action.</summary>
    Hero = 0,

    /// <summary>A grid of linked images: two, three or four across.</summary>
    BannerGrid = 10,

    /// <summary>A horizontally scrolling row of products, from a collection or a fixed list.</summary>
    ProductCarousel = 20,

    /// <summary>A row of category tiles, each an image and a name.</summary>
    CategoryTiles = 30,

    /// <summary>A block of formatted copy.</summary>
    RichText = 40,

    /// <summary>A list of questions and their answers.</summary>
    Faq = 50,

    /// <summary>Quoted customers, with an optional portrait and rating.</summary>
    Testimonial = 60,

    /// <summary>Raw markup. Permissioned and flagged separately from every other block.</summary>
    CustomHtml = 90,
}

/// <summary>What one field of a block's configuration holds.</summary>
/// <remarks>
/// Deliberately small. The kinds exist to be <em>checked</em>, not to describe every shape a
/// designer might want: a kind earns its place only when getting it wrong produces a broken page or
/// an unsafe one. Anything finer than this belongs to the Angular component that renders the block.
/// </remarks>
internal enum BlockFieldKind
{
    /// <summary>A single line of plain text.</summary>
    Text = 0,

    /// <summary>A block of formatted copy. Sanitised on the way in.</summary>
    RichText = 1,

    /// <summary>Raw markup, accepted only on a <see cref="BlockType.CustomHtml"/> block.</summary>
    Html = 2,

    /// <summary>A link target: an absolute URL, or a site-relative path beginning with a slash.</summary>
    Link = 3,

    /// <summary>A whole number, within the field's declared bounds.</summary>
    Integer = 4,

    /// <summary>True or false.</summary>
    Boolean = 5,

    /// <summary>A media file id, resolved through <c>IMediaLibrary</c> at render time.</summary>
    MediaRef = 6,

    /// <summary>A product id, resolved through <c>IProductProjectionSource</c> at render time.</summary>
    ProductRef = 7,

    /// <summary>A category id, resolved through <c>ICatalogTaxonomy</c> at render time.</summary>
    CategoryRef = 8,

    /// <summary>A curated collection's slug, resolved from this module's own tables.</summary>
    CollectionRef = 9,

    /// <summary>One of a fixed set of words, listed on the field.</summary>
    Choice = 10,
}

/// <summary>
/// One field of a block's configuration.
/// </summary>
/// <param name="Name">The JSON property name, camelCase, as the storefront reads it.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="IsRequired">Whether a block without it is refused.</param>
/// <param name="IsList">Whether the value is an array of <paramref name="Kind"/> rather than one.</param>
/// <param name="MaxLength">The longest a text value may be, or the most items a list may hold.</param>
/// <param name="Choices">For <see cref="BlockFieldKind.Choice"/>, the words accepted.</param>
internal sealed record BlockField(
    string Name,
    BlockFieldKind Kind,
    bool IsRequired = false,
    bool IsList = false,
    int MaxLength = 512,
    IReadOnlyList<string>? Choices = null);

/// <summary>
/// A block type's schema: what its <c>config</c> document may contain.
/// </summary>
/// <param name="Type">The block type.</param>
/// <param name="Label">What an editor sees in the block picker.</param>
/// <param name="Description">What it is for, in one line.</param>
/// <param name="Fields">Its fields. Anything not named here is refused rather than ignored.</param>
/// <param name="Items">
/// The fields of a repeated child, when the block has one — an FAQ's questions, a grid's tiles. Null
/// for a block that has no repeated part.
/// </param>
/// <param name="MaxItems">The most children a repeated block may hold.</param>
/// <param name="IsPrivileged">
/// Whether writing this block needs the separate custom-HTML permission. True for exactly one type,
/// and the reason the flag exists at all.
/// </param>
internal sealed record BlockDescriptor(
    BlockType Type,
    string Label,
    string Description,
    IReadOnlyList<BlockField> Fields,
    IReadOnlyList<BlockField>? Items = null,
    int MaxItems = 24,
    bool IsPrivileged = false);

/// <summary>
/// Every block type this platform renders, and the shape each one's configuration must have.
/// </summary>
/// <remarks>
/// <para>
/// This is the "typed block schemas" the step card asks for, and it is deliberately data rather than
/// a set of C# records with a polymorphic serialiser. Three things read it: the handler that
/// validates a block before it is stored, the admin screen that draws the editor for one, and the
/// OpenAPI document that tells the Angular workspace what exists. A discriminated union would serve
/// the first well and the other two not at all.
/// </para>
/// <para>
/// The configuration is stored as <c>jsonb</c> and validated against this table on the way in, which
/// is the arrangement docs/03-database-design.md §1 allows JSON for: an open shape nothing joins to,
/// nothing filters on, and that is written into a component's inputs exactly as it is. Validating on
/// the way in rather than on the way out is what keeps a bad block an editor's error rather than a
/// shopper's blank page.
/// </para>
/// </remarks>
internal static class BlockCatalog
{
    /// <summary>The block types, in the order the picker lists them.</summary>
    public static IReadOnlyList<BlockDescriptor> All { get; } =
    [
        new(
            BlockType.Hero,
            "Hero",
            "A full-width image with a headline and a call to action.",
            [
                new BlockField("headline", BlockFieldKind.Text, IsRequired: true, MaxLength: 160),
                new BlockField("subheadline", BlockFieldKind.Text, MaxLength: 320),
                new BlockField("imageFileId", BlockFieldKind.MediaRef, IsRequired: true),
                // A separate image rather than a crop of the first. A hero cropped for a desktop
                // banner loses its subject on a 360px screen, and this platform is mobile-first.
                new BlockField("mobileImageFileId", BlockFieldKind.MediaRef),
                new BlockField("imageAlt", BlockFieldKind.Text, MaxLength: 240),
                new BlockField("ctaLabel", BlockFieldKind.Text, MaxLength: 64),
                new BlockField("ctaHref", BlockFieldKind.Link),
                new BlockField(
                    "align",
                    BlockFieldKind.Choice,
                    Choices: ["left", "centre", "right"]),
            ]),

        new(
            BlockType.BannerGrid,
            "Banner grid",
            "Two to four linked images across.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
                new BlockField(
                    "columns",
                    BlockFieldKind.Choice,
                    IsRequired: true,
                    Choices: ["2", "3", "4"]),
            ],
            Items:
            [
                new BlockField("imageFileId", BlockFieldKind.MediaRef, IsRequired: true),
                new BlockField("imageAlt", BlockFieldKind.Text, MaxLength: 240),
                new BlockField("caption", BlockFieldKind.Text, MaxLength: 160),
                new BlockField("href", BlockFieldKind.Link),
            ],
            MaxItems: 8),

        new(
            BlockType.ProductCarousel,
            "Product carousel",
            "A scrolling row of products, from a collection or a chosen list.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
                // One of these two, and the handler refuses a block that gives both or neither: a
                // carousel that had a collection and a list would have two answers to what it shows.
                new BlockField("collectionSlug", BlockFieldKind.CollectionRef),
                new BlockField("productIds", BlockFieldKind.ProductRef, IsList: true, MaxLength: 24),
                new BlockField("limit", BlockFieldKind.Integer, MaxLength: 24),
                new BlockField("showPrice", BlockFieldKind.Boolean),
                new BlockField("viewAllHref", BlockFieldKind.Link),
            ]),

        new(
            BlockType.CategoryTiles,
            "Category tiles",
            "A row of category tiles, each an image and a name.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
                new BlockField(
                    "columns",
                    BlockFieldKind.Choice,
                    Choices: ["2", "3", "4", "6"]),
            ],
            Items:
            [
                new BlockField("categoryId", BlockFieldKind.CategoryRef, IsRequired: true),
                // Optional: the category already has an image, and this overrides it for the tile
                // without changing the catalogue.
                new BlockField("imageFileId", BlockFieldKind.MediaRef),
                new BlockField("label", BlockFieldKind.Text, MaxLength: 96),
            ],
            MaxItems: 12),

        new(
            BlockType.RichText,
            "Rich text",
            "A block of formatted copy.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
                new BlockField("body", BlockFieldKind.RichText, IsRequired: true, MaxLength: 20_000),
                new BlockField(
                    "width",
                    BlockFieldKind.Choice,
                    Choices: ["narrow", "wide", "full"]),
            ]),

        new(
            BlockType.Faq,
            "Questions and answers",
            "A list of questions and their answers.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
            ],
            Items:
            [
                new BlockField("question", BlockFieldKind.Text, IsRequired: true, MaxLength: 320),
                new BlockField("answer", BlockFieldKind.RichText, IsRequired: true, MaxLength: 4_000),
            ],
            MaxItems: 40),

        new(
            BlockType.Testimonial,
            "Testimonials",
            "Quoted customers, with an optional portrait and rating.",
            [
                new BlockField("heading", BlockFieldKind.Text, MaxLength: 160),
            ],
            Items:
            [
                new BlockField("quote", BlockFieldKind.Text, IsRequired: true, MaxLength: 1_000),
                new BlockField("author", BlockFieldKind.Text, IsRequired: true, MaxLength: 96),
                new BlockField("location", BlockFieldKind.Text, MaxLength: 96),
                new BlockField("portraitFileId", BlockFieldKind.MediaRef),
                new BlockField("rating", BlockFieldKind.Integer, MaxLength: 5),
            ],
            MaxItems: 12),

        new(
            BlockType.CustomHtml,
            "Custom HTML",
            "Raw markup. Needs the custom-HTML permission and the flag.",
            [
                new BlockField("html", BlockFieldKind.Html, IsRequired: true, MaxLength: 50_000),
            ],
            IsPrivileged: true),
    ];

    private static readonly Dictionary<BlockType, BlockDescriptor> ByType =
        All.ToDictionary(descriptor => descriptor.Type);

    /// <summary>The schema for a block type.</summary>
    /// <param name="type">The block type.</param>
    public static BlockDescriptor Describe(BlockType type) => ByType[type];

    /// <summary>The schema for a block type named as a string, or null when nothing claims it.</summary>
    /// <param name="type">The block type's name, as the API spells it.</param>
    public static BlockDescriptor? Find(string? type)
        => Enum.TryParse<BlockType>(type, ignoreCase: true, out var parsed)
           && ByType.TryGetValue(parsed, out var descriptor)
            ? descriptor
            : null;
}
