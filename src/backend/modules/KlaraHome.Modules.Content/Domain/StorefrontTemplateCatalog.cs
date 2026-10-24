namespace KlaraHome.Modules.Content.Domain;

/// <summary>
/// One block of a storefront template: its type and its default configuration.
/// </summary>
/// <param name="Type">The block type.</param>
/// <param name="ConfigJson">
/// The default configuration as a JSON object, valid against <see cref="BlockCatalog"/> once the
/// reference fields (categories, products, banner links) have been filled in. The hero picture is
/// left empty for the editor's picker, so validation of these is done in draft mode.
/// </param>
/// <param name="Fill">How the filler picks this block's content, or null for the plain default.</param>
internal sealed record StorefrontTemplateBlock(BlockType Type, string ConfigJson, StorefrontBlockFill? Fill = null);

/// <summary>
/// How <c>StorefrontTemplateFiller</c> chooses the content of one banner grid or product carousel.
/// </summary>
/// <param name="Skip">For a banner grid, how many of the best-stocked categories to pass over first.</param>
/// <param name="Take">For a banner grid, the most banners it holds.</param>
/// <param name="MinItems">
/// The fewest items the block needs to be worth showing. Below it the block is dropped from the
/// applied page.
/// </param>
/// <param name="MaxPrice">For a carousel, the highest selling price a product may have, or null for no cap.</param>
internal sealed record StorefrontBlockFill(int Skip = 0, int Take = int.MaxValue, int MinItems = 1, decimal? MaxPrice = null);

/// <summary>
/// A ready-made look for the storefront: a home page, a header treatment and a footer treatment.
/// </summary>
/// <param name="Id">The stable key, also what <c>branding.templateId</c> stores.</param>
/// <param name="Name">What the merchant sees.</param>
/// <param name="Suits">The kind of store it suits, in a few words.</param>
/// <param name="Description">One or two sentences on how it feels.</param>
/// <param name="HeaderStyle">One of <c>inline</c>, <c>centred</c>, <c>search</c>, <c>wordmark</c>.</param>
/// <param name="FooterStyle">One of <c>columns</c>, <c>slim</c>, <c>columnsNewsletter</c>.</param>
/// <param name="PaletteIds">The curated palette presets offered with it.</param>
/// <param name="Blocks">The home page blocks, in order.</param>
internal sealed record StorefrontTemplate(
    string Id,
    string Name,
    string Suits,
    string Description,
    string HeaderStyle,
    string FooterStyle,
    IReadOnlyList<string> PaletteIds,
    IReadOnlyList<StorefrontTemplateBlock> Blocks);

/// <summary>
/// The storefront templates, as static data (docs/design/storefront-designer.md).
/// </summary>
/// <remarks>
/// <para>
/// Static on purpose. A template is a release-time decision joined to the storefront's header and
/// footer components, exactly as a block type is joined to its renderer, so a table in code is the
/// honest shape for it.
/// </para>
/// <para>
/// The hero has no layout field in <see cref="BlockCatalog"/>, only <c>align</c>. The designer's
/// full-width and split looks are therefore carried by it: <c>centre</c> is the full-width hero
/// (image behind, copy centred) and <c>left</c> is the split hero (copy on the left, image beside
/// it). <c>right</c> is not used by any template.
/// </para>
/// <para>
/// Reference fields are left empty here. <c>StorefrontTemplateFiller</c> fills the hero picture,
/// categories, products, banner pictures and links from the live catalogue; the merchant replaces
/// any of them in the editor.
/// </para>
/// </remarks>
internal static class StorefrontTemplateCatalog
{
    /// <summary>The hero alignment that means the full-width look.</summary>
    public const string HeroFullWidthAlign = "centre";

    /// <summary>The hero alignment that means the split look.</summary>
    public const string HeroSplitAlign = "left";

    /// <summary>The header treatments the storefront draws.</summary>
    public static IReadOnlyList<string> HeaderStyles { get; } = ["inline", "centred", "search", "wordmark"];

    /// <summary>The footer treatments the storefront draws.</summary>
    public static IReadOnlyList<string> FooterStyles { get; } = ["columns", "slim", "columnsNewsletter"];

    /// <summary>The templates, in the order the designer lists them.</summary>
    public static IReadOnlyList<StorefrontTemplate> All { get; } =
    [
        new(
            "loom",
            "Loom",
            "Textiles and soft goods",
            "Soft and tactile. A full-width hero, rooms to shop by and a short note on how things are made.",
            "centred",
            "columns",
            ["sand", "terracotta", "berry", "emerald"],
            [
                new(BlockType.Hero, $$"""
                    {
                      "headline": "Handwoven for the home",
                      "subheadline": "Cushion covers, throws and table linen, block-printed and loomed in small batches.",
                      "imageAlt": "Handwoven textiles folded on a wooden bench",
                      "ctaLabel": "Shop the collection",
                      "ctaHref": "/search",
                      "align": "{{HeroFullWidthAlign}}"
                    }
                    """),
                new(BlockType.CategoryTiles, """
                    { "heading": "Shop by room", "columns": "3" }
                    """),
                new(BlockType.ProductCarousel, """
                    { "heading": "New this week", "limit": 6, "showPrice": true, "viewAllHref": "/search" }
                    """),
                new(BlockType.Testimonial, """
                    {
                      "heading": "What people say",
                      "items": [
                        { "quote": "The cushion covers are even softer than they look, and the print has not faded after a dozen washes.", "author": "Meera S.", "location": "Bengaluru", "rating": 5 },
                        { "quote": "Ordered a throw on Monday and it was on my sofa by Thursday. Beautifully packed, too.", "author": "Anand R.", "location": "Hyderabad", "rating": 5 },
                        { "quote": "Honest colours and honest prices. Exactly what the photographs promised.", "author": "Pooja K.", "location": "Pune", "rating": 4 }
                      ]
                    }
                    """),
                new(BlockType.RichText, """
                    {
                      "heading": "Made slowly, sent quickly",
                      "body": "<p>Every piece is woven, printed or stitched by hand by small workshops across India, so no two are exactly alike. We keep stock close to you, so what you order leaves within a day and reaches you in a few.</p>",
                      "width": "narrow",
                      "align": "centre"
                    }
                    """),
            ]),

        new(
            "atelier",
            "Atelier",
            "Furniture, photography-led",
            "Quiet and image-first. A split hero, a banner to the biggest category and a short list of bestsellers.",
            "inline",
            "slim",
            ["slate", "sand", "ocean"],
            [
                new(BlockType.Hero, $$"""
                    {
                      "headline": "Furniture that stays",
                      "subheadline": "Solid wood and honest joinery, made to be handed down rather than replaced.",
                      "imageAlt": "A solid wood dining chair in a bright room",
                      "ctaLabel": "See the range",
                      "ctaHref": "/search",
                      "align": "{{HeroSplitAlign}}"
                    }
                    """),
                // The biggest category on its own, full width.
                new(BlockType.BannerGrid, """
                    { "columns": "2" }
                    """, new StorefrontBlockFill(Skip: 0, Take: 1, MinItems: 1)),
                // Then a half-and-half pair, only when two more categories have a picture.
                new(BlockType.BannerGrid, """
                    { "columns": "2" }
                    """, new StorefrontBlockFill(Skip: 1, Take: 2, MinItems: 2)),
                new(BlockType.ProductCarousel, """
                    { "heading": "Bestsellers", "limit": 4, "showPrice": true, "viewAllHref": "/search" }
                    """),
                new(BlockType.Faq, """
                    {
                      "heading": "Good to know",
                      "items": [
                        { "question": "How long does delivery take?", "answer": "<p>Most pieces reach you within 5 to 7 working days. Larger furniture is delivered on an appointment you choose.</p>" },
                        { "question": "Is assembly included?", "answer": "<p>Yes. Our delivery team assembles furniture in the room you want it, and takes the packaging away.</p>" },
                        { "question": "Can I return something that does not fit?", "answer": "<p>You can return most items within 7 days of delivery if they are unused. Made-to-order pieces are the exception, and the product page says so.</p>" },
                        { "question": "What is the warranty?", "answer": "<p>Furniture carries a one-year warranty against faults in materials and workmanship.</p>" }
                      ]
                    }
                    """),
            ]),

        new(
            "bazaar",
            "Bazaar",
            "Many categories, catalogue-first",
            "Busy and easy to scan. Categories up front, banners, two product rows and a delivery promise.",
            "search",
            "columnsNewsletter",
            ["coral", "indigo", "ocean", "emerald"],
            [
                new(BlockType.CategoryTiles, """
                    { "heading": "Browse the shop", "columns": "6" }
                    """),
                new(BlockType.BannerGrid, """
                    { "columns": "2" }
                    """, new StorefrontBlockFill(Skip: 0, Take: 2, MinItems: 1)),
                new(BlockType.ProductCarousel, """
                    { "heading": "Trending now", "limit": 6, "showPrice": true, "viewAllHref": "/search" }
                    """),
                new(BlockType.ProductCarousel, """
                    { "heading": "Under ₹999", "limit": 6, "showPrice": true, "viewAllHref": "/search" }
                    """, new StorefrontBlockFill(MinItems: 3, MaxPrice: 999m)),
                new(BlockType.RichText, """
                    {
                      "heading": "Free delivery over ₹1,499",
                      "body": "<p>Free delivery on every order above ₹1,499, cash on delivery in most pincodes, and easy returns within 7 days.</p>",
                      "width": "wide",
                      "align": "start"
                    }
                    """),
            ]),

        new(
            "edit",
            "Edit",
            "A small, curated range",
            "Editorial and restrained. A short list, a split hero for the story and the six pieces we stand behind.",
            "wordmark",
            "slim",
            ["slate", "violet", "terracotta"],
            [
                new(BlockType.RichText, """
                    {
                      "heading": "Six things we would buy again",
                      "body": "<p>We keep the range small on purpose. Each piece here has been used at home for months before it earned a place, and the ones that did not hold up are gone.</p>",
                      "width": "narrow",
                      "align": "start"
                    }
                    """),
                new(BlockType.Hero, $$"""
                    {
                      "headline": "The linen edit",
                      "subheadline": "Washed linen for the bed and the table, in colours that look better with age.",
                      "imageAlt": "Washed linen bedding in soft natural tones",
                      "ctaLabel": "Shop linen",
                      "ctaHref": "/search",
                      "align": "{{HeroSplitAlign}}"
                    }
                    """),
                new(BlockType.ProductCarousel, """
                    { "heading": "The six", "limit": 6, "showPrice": true, "viewAllHref": "/search" }
                    """),
                new(BlockType.Testimonial, """
                    {
                      "heading": "From the kitchen table",
                      "items": [
                        { "quote": "I bought the linen napkins on a whim and now every guest asks where they are from.", "author": "Divya M.", "location": "Chennai", "rating": 5 },
                        { "quote": "A small shop that actually answers its messages. The bedding has survived two monsoons.", "author": "Rahul T.", "location": "Mumbai", "rating": 5 },
                        { "quote": "The duvet cover washes beautifully and gets softer every time. I have already ordered a second for the guest room.", "author": "Ishita B.", "location": "Kolkata", "rating": 5 }
                      ]
                    }
                    """),
            ]),
    ];

    /// <summary>The template with this id, or null.</summary>
    /// <param name="id">The id, case-insensitive.</param>
    public static StorefrontTemplate? Find(string? id)
        => All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));
}
