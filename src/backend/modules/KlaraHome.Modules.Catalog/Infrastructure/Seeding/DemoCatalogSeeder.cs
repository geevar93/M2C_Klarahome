using KlaraHome.Contracts.Vendors;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Modules.Catalog.Infrastructure.Seeding;

/// <summary>One demonstration product, as a flat description the seeder expands into rows.</summary>
/// <param name="Sku">The variant's SKU. Every one is prefixed <c>DEMO-</c>.</param>
/// <param name="Name">The product title.</param>
/// <param name="Slug">Its URL segment, prefixed <c>demo-</c>.</param>
/// <param name="CategorySlug">Which leaf category it files under.</param>
/// <param name="BrandSlug">Which brand it carries.</param>
/// <param name="ShortDescription">The one-liner a card and a search result show.</param>
/// <param name="Description">The long copy on the product page.</param>
/// <param name="HsnCode">The HSN code the GST rate is resolved from.</param>
/// <param name="GstRate">The GST percentage. Prices are shown inclusive of it.</param>
/// <param name="Mrp">The statutory ceiling printed on the pack.</param>
/// <param name="SellingPrice">What the demo seller is asking. Below the MRP, always.</param>
/// <param name="NetQuantity">The declared net quantity.</param>
/// <param name="WeightGrams">Dead weight, which the courier is priced from.</param>
/// <param name="Dimensions">Packed length, width and height in millimetres.</param>
/// <param name="Specifications">The specification table rows.</param>
internal sealed record DemoProduct(
    string Sku,
    string Name,
    string Slug,
    string CategorySlug,
    string BrandSlug,
    string ShortDescription,
    string Description,
    string HsnCode,
    decimal GstRate,
    decimal Mrp,
    decimal SellingPrice,
    string NetQuantity,
    int WeightGrams,
    (int Length, int Width, int Height) Dimensions,
    IReadOnlyList<(string Label, string Value)> Specifications);

/// <summary>
/// A small, complete demonstration catalogue: two category branches, three brands and ten products,
/// each with a live offer from the demo seller.
/// </summary>
/// <remarks>
/// <para>
/// **Complete is the point.** A product row on its own renders nothing a person can look at — the
/// storefront needs a published product, an active variant, and an active listing whose vendor the
/// directory says may trade, or the page shows a title above an empty buy box. So this seeder writes
/// all four, and it checks the seller with <see cref="IVendorDirectory"/> before it writes a single
/// offer rather than producing a catalogue that looks right in the database and broken on screen.
/// </para>
/// <para>
/// **What it deliberately does not do.** It writes no stock: the buy box ranks an offer of unknown
/// stock as "do not discriminate" rather than as unavailable, so the demonstration works without
/// reaching across into the Inventory module's schema, which this module may not touch
/// (docs/01-architecture.md §2.1). It writes no media either — every product renders through the
/// placeholder treatment, which is the correct behaviour until real photography is supplied and is
/// itself worth seeing.
/// </para>
/// <para>
/// **Prices, HSN codes and GST rates are plausible rather than invented.** 6304 is the HSN heading
/// for furnishing articles and 6912 for ceramic tableware, both at 12%. A demonstration that showed
/// 18% on a cushion cover would teach a reviewer something false about the product.
/// </para>
/// <para>
/// Idempotent on the <c>demo-</c> slug and the <c>DEMO-</c> SKU prefix: re-running adds what is
/// missing and leaves everything else alone. It never updates a row it did not create, so a
/// designer who edits a demo product's copy in the admin UI does not lose it on the next deploy.
/// </para>
/// <para>
/// See <see cref="DemoDataOptions"/> for why a demo seeder exists at all and what fences it.
/// </para>
/// </remarks>
/// <param name="context">The Catalog data context.</param>
/// <param name="vendors">Checks the demo seller exists and may trade before any offer is written.</param>
/// <param name="environment">Refuses to run in Production whatever configuration says.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="logger">Reports what was created, so a deploy log answers "why is the shop empty".</param>
internal sealed partial class DemoCatalogSeeder(
    CatalogDbContext context,
    IVendorDirectory vendors,
    IHostEnvironment environment,
    IClock clock,
    ILogger<DemoCatalogSeeder> logger) : IDataSeeder
{
    /// <summary>The code of the seller every demonstration offer belongs to.</summary>
    private const string DemoVendorCode = "DEMO-ATELIER";

    /// <inheritdoc />
    public string Name => "Catalog.DemoCatalogue";

    /// <summary>After the demo seller, which every listing here points at.</summary>
    public int Order => 910;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // The second fence. See DemoVendorSeeder for why registration alone is not enough.
        if (environment.IsProduction())
        {
            return;
        }

        var vendorId = await ResolveDemoVendorAsync(cancellationToken).ConfigureAwait(false);

        if (vendorId is null)
        {
            // Not an exception. The demo vendor seeder runs first and in the same pass, but a
            // deployment that enabled this module's demo data and not the Vendors module's would
            // otherwise fail a deploy over sample data, which is the wrong trade.
            DemoVendorMissing(logger, DemoVendorCode);
            return;
        }

        var categories = await EnsureCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var brands = await EnsureBrandsAsync(cancellationToken).ConfigureAwait(false);

        var created = 0;

        foreach (var demo in Catalogue)
        {
            if (await context.Products
                    .AnyAsync(product => product.Slug == demo.Slug, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            if (!categories.TryGetValue(demo.CategorySlug, out var categoryId)
                || !brands.TryGetValue(demo.BrandSlug, out var brandId))
            {
                continue;
            }

            WriteProduct(demo, categoryId, brandId, vendorId.Value);
            created++;
        }

        if (created == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        DemoCatalogueSeeded(logger, created);
    }

    /// <summary>
    /// The demo seller's id, or null when there is no such seller or they cannot trade.
    /// </summary>
    /// <remarks>
    /// Read through the published contract rather than by joining to the Vendors schema, which this
    /// module may not do. The directory is also the same thing the storefront's buy box consults, so
    /// a seller this returns is a seller whose offers will actually render.
    /// </remarks>
    private async Task<Guid?> ResolveDemoVendorAsync(CancellationToken cancellationToken)
    {
        var vendor = await vendors.FindByCodeAsync(DemoVendorCode, cancellationToken).ConfigureAwait(false);

        return vendor is { IsActive: true } ? vendor.Id : null;
    }

    /// <summary>Creates the two category branches if they are not already there, and returns the leaves.</summary>
    private async Task<Dictionary<string, Guid>> EnsureCategoriesAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Categories
            .Where(category => category.Slug.StartsWith("demo-"))
            .ToDictionaryAsync(category => category.Slug, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var leaves = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var (rootSlug, rootName, children) in Taxonomy)
        {
            if (!existing.TryGetValue(rootSlug, out var root))
            {
                root = Category.Create(rootName, rootSlug, parent: null);
                context.Categories.Add(root);
                existing[rootSlug] = root;
            }

            var position = 0;

            foreach (var (leafSlug, leafName, blurb) in children)
            {
                if (!existing.TryGetValue(leafSlug, out var leaf))
                {
                    leaf = Category.Create(leafName, leafSlug, root);
                    leaf.Describe(leafName, leafSlug, blurb, null, null, new SeoMetadata());
                    leaf.MoveTo(position);
                    context.Categories.Add(leaf);
                    existing[leafSlug] = leaf;
                }

                leaves[leafSlug] = leaf.Id;
                position++;
            }
        }

        // Saved on their own so the products written next have category ids that exist, and so a
        // failure in the product loop leaves a usable taxonomy rather than nothing.
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return leaves;
    }

    /// <summary>Creates the demonstration brands if they are not already there.</summary>
    private async Task<Dictionary<string, Guid>> EnsureBrandsAsync(CancellationToken cancellationToken)
    {
        var existing = await context.Brands
            .Where(brand => brand.Slug.StartsWith("demo-"))
            .ToDictionaryAsync(brand => brand.Slug, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var (slug, name, blurb) in Brands)
        {
            if (!existing.TryGetValue(slug, out var brand))
            {
                brand = Brand.Create(name, slug);
                brand.Describe(name, slug, blurb, null, new SeoMetadata());
                context.Brands.Add(brand);
                existing[slug] = brand;
            }

            result[slug] = brand.Id;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Expands one description into a published product, an active variant and a live offer.</summary>
    private void WriteProduct(DemoProduct demo, Guid categoryId, Guid brandId, Guid vendorId)
    {
        var now = clock.UtcNow;

        var product = Product.Draft(demo.Name, demo.Slug, categoryId, vendorId);

        product.Describe(
            demo.Name,
            demo.Slug,
            categoryId,
            brandId,
            demo.ShortDescription,
            demo.Description,
            [.. demo.Specifications.Select(row => new Specification(row.Label, row.Value))],
            new SeoMetadata
            {
                MetaTitle = $"{demo.Name} — Klara Home",
                MetaDescription = demo.ShortDescription,

                // Demonstration data must never be indexed. It will sit on a staging host with a
                // public URL more often than anybody intends.
                NoIndex = true,
            });

        // Filled in properly rather than left blank: these are the fields that decide whether the
        // product page renders its statutory disclosures, and a demo product with an empty
        // compliance block does not demonstrate the page that was actually built.
        product.DeclareCompliance(
            demo.HsnCode,
            demo.GstRate,
            Product.India,
            new PartyDetails
            {
                Name = "Klara Atelier Home Furnishings Private Limited",
                Address = "Plot 14, Sitapura Industrial Area, Jaipur, Rajasthan 302022",
                Contact = "care@klara-atelier.example",
            },
            new PartyDetails
            {
                Name = "Klara Atelier Home Furnishings Private Limited",
                Address = "Plot 14, Sitapura Industrial Area, Jaipur, Rajasthan 302022",
            },
            // Made in India, so there is no importer to declare. An empty party is the correct
            // value here, not a placeholder one.
            new PartyDetails());

        product.SetAfterSalesTerms(isReturnable: true, returnWindowDays: 7, warranty: null);
        product.TransitionTo(ProductStatus.Active, now);

        var variant = Variant.Create(product.Id, demo.Sku);
        variant.Describe(demo.Sku, null, null, position: 0);
        variant.SetDimensions(demo.WeightGrams, demo.Dimensions.Length, demo.Dimensions.Width, demo.Dimensions.Height);
        variant.DeclarePack(Money.Rupees(demo.Mrp), demo.NetQuantity, null, null);
        variant.SetDefault(true);
        variant.TransitionTo(VariantStatus.Active);

        var listing = Listing.Open(vendorId, variant.Id, product.Id);

        listing.SetTerms(
            Money.Rupees(demo.Mrp),
            Money.Rupees(demo.SellingPrice),
            vendorSku: demo.Sku,
            handlingTimeHours: Listing.DefaultHandlingTimeHours,
            isCodAllowed: true,
            maxOrderQuantity: 5);

        listing.TransitionTo(ListingStatus.Active, now);

        context.Products.Add(product);
        context.Variants.Add(variant);
        context.Listings.Add(listing);
    }

    /// <summary>Two branches, each with two leaves. Enough for a breadcrumb and a category page to mean something.</summary>
    private static IReadOnlyList<(string Slug, string Name, IReadOnlyList<(string Slug, string Name, string Blurb)> Children)> Taxonomy =>
    [
        ("demo-soft-furnishings", "Soft Furnishings",
        [
            ("demo-cushion-covers", "Cushion Covers",
                "Block-printed and handwoven covers in cotton, linen and raw silk."),
            ("demo-throws-and-quilts", "Throws & Quilts",
                "Lightweight cotton dohars and hand-quilted razais for every season."),
        ]),
        ("demo-tableware", "Tableware",
        [
            ("demo-stoneware", "Stoneware",
                "Wheel-thrown, kiln-fired and food-safe. Every piece varies a little."),
            ("demo-serveware", "Serveware",
                "Mango wood, brass and marble pieces for the middle of the table."),
        ]),
    ];

    /// <summary>Three brands, so a brand filter on the category page has something to filter.</summary>
    private static IReadOnlyList<(string Slug, string Name, string Blurb)> Brands =>
    [
        ("demo-terra-and-thread", "Terra & Thread", "Naturally dyed cotton and linen from Jaipur."),
        ("demo-nilgiri-clay", "Nilgiri Clay", "Small-batch stoneware from the hills above Coonoor."),
        ("demo-channapatna-co", "Channapatna & Co.", "Lacquered wood and brass, turned by hand."),
    ];

    /// <summary>The ten products. Prices in rupees, inclusive of GST, as the storefront shows them.</summary>
    private static IReadOnlyList<DemoProduct> Catalogue =>
    [
        new("DEMO-CC-0001", "Jaipur Block-Print Cushion Cover 40×40", "demo-jaipur-block-print-cushion-cover",
            "demo-cushion-covers", "demo-terra-and-thread",
            "Hand block-printed cotton, in indigo on natural.",
            "Printed by hand with carved teak blocks in Sanganer, on 200-thread-count cotton that "
            + "softens with every wash. Concealed zip, and cut to take a 45cm insert so the corners "
            + "stay full. Sold as a cover only.",
            "630222", 12m, 1299m, 899m, "1 N", 220, (280, 220, 30),
            [("Material", "100% cotton"), ("Size", "40 × 40 cm"), ("Closure", "Concealed zip"), ("Care", "Machine wash cold")]),

        new("DEMO-CC-0002", "Handwoven Linen Cushion Cover 45×45", "demo-handwoven-linen-cushion-cover",
            "demo-cushion-covers", "demo-terra-and-thread",
            "Slubbed linen with a hand-knotted edge.",
            "Woven on a pit loom in Bhagalpur from flax spun to a deliberately uneven count, which is "
            + "what gives the cloth its texture. The edge is knotted rather than hemmed. Insert not included.",
            "630222", 12m, 1899m, 1449m, "1 N", 260, (300, 240, 30),
            [("Material", "100% linen"), ("Size", "45 × 45 cm"), ("Weave", "Pit loom"), ("Care", "Gentle wash, line dry")]),

        new("DEMO-CC-0003", "Raw Silk Cushion Cover 30×50", "demo-raw-silk-bolster-cover",
            "demo-cushion-covers", "demo-terra-and-thread",
            "A bolster cover in matka silk, with a piped seam.",
            "Matka silk, spun from the shorter fibres left after reeling, so it takes dye deeply and "
            + "keeps a matte finish. Piped along the long seams to hold the bolster's shape.",
            "630222", 12m, 2499m, 1999m, "1 N", 190, (320, 180, 30),
            [("Material", "Matka silk"), ("Size", "30 × 50 cm"), ("Finish", "Piped seam"), ("Care", "Dry clean only")]),

        new("DEMO-TQ-0001", "Cotton Dohar Throw, Double", "demo-cotton-dohar-throw-double",
            "demo-throws-and-quilts", "demo-terra-and-thread",
            "Three layers of voile, quilted flat. For a fan and a ceiling.",
            "A dohar is three layers of cotton voile quilted together — warm enough for an air-conditioned "
            + "room and light enough for April. Printed on both faces, so it reverses.",
            "630492", 12m, 3499m, 2649m, "1 N", 1400, (420, 320, 120),
            [("Material", "3-layer cotton voile"), ("Size", "220 × 240 cm"), ("Reversible", "Yes"), ("Care", "Machine wash cold")]),

        new("DEMO-TQ-0002", "Hand-Quilted Razai, Single", "demo-hand-quilted-razai-single",
            "demo-throws-and-quilts", "demo-terra-and-thread",
            "Hand-teased cotton filling, quilted in running stitch.",
            "Filled with hand-teased cotton and closed with a running stitch worked across the whole "
            + "quilt, which is what stops the filling migrating. Heavier than a dohar and warmer than it looks.",
            "630492", 12m, 4299m, 3399m, "1 N", 2200, (450, 350, 180),
            [("Material", "Cotton shell, cotton filling"), ("Size", "150 × 220 cm"), ("Quilting", "Hand running stitch"), ("Care", "Dry clean")]),

        new("DEMO-SW-0001", "Stoneware Dinner Plate, 26cm", "demo-stoneware-dinner-plate",
            "demo-stoneware", "demo-nilgiri-clay",
            "Wheel-thrown, glazed in a matte oatmeal.",
            "Thrown one at a time and fired to 1240°C, which is what makes it chip-resistant rather "
            + "than merely hard. The glaze pools a little at the rim; no two are quite the same. "
            + "Dishwasher and microwave safe.",
            "691200", 12m, 899m, 699m, "1 N", 780, (280, 280, 40),
            [("Material", "Stoneware"), ("Diameter", "26 cm"), ("Glaze", "Matte oatmeal"), ("Dishwasher safe", "Yes")]),

        new("DEMO-SW-0002", "Stoneware Serving Bowl, 22cm", "demo-stoneware-serving-bowl",
            "demo-stoneware", "demo-nilgiri-clay",
            "A deep bowl for the middle of the table.",
            "Deep enough for a full portion of rice and wide enough to serve from, with a foot ring "
            + "turned on the wheel so it sits flat on a cloth. Fired alongside the dinner plate and "
            + "glazed to match.",
            "691200", 12m, 1249m, 999m, "1 N", 950, (240, 240, 110),
            [("Material", "Stoneware"), ("Diameter", "22 cm"), ("Capacity", "1.4 L"), ("Dishwasher safe", "Yes")]),

        new("DEMO-SW-0003", "Stoneware Mug, 300ml — Set of 2", "demo-stoneware-mug-set-of-two",
            "demo-stoneware", "demo-nilgiri-clay",
            "A pair of mugs with a pulled handle.",
            "The handle is pulled by hand rather than cast, which is why it sits in the palm instead "
            + "of against it. 300ml to the brim, so about a filter coffee and a half.",
            "691200", 12m, 1099m, 849m, "2 N", 720, (240, 130, 110),
            [("Material", "Stoneware"), ("Capacity", "300 ml each"), ("Pieces", "2"), ("Microwave safe", "Yes")]),

        new("DEMO-SV-0001", "Mango Wood Serving Board, 45cm", "demo-mango-wood-serving-board",
            "demo-serveware", "demo-channapatna-co",
            "Turned from a single plank, finished in food-safe oil.",
            "Cut from a single plank of mango wood so the grain runs the length of the board, and "
            + "finished with food-safe oil rather than lacquer — it can be re-oiled instead of replaced. "
            + "Not for the dishwasher.",
            "441900", 12m, 2199m, 1699m, "1 N", 1100, (470, 200, 25),
            [("Material", "Mango wood"), ("Length", "45 cm"), ("Finish", "Food-safe oil"), ("Dishwasher safe", "No")]),

        new("DEMO-SV-0002", "Brass Serving Spoons, Set of 3", "demo-brass-serving-spoons",
            "demo-serveware", "demo-channapatna-co",
            "Hand-beaten brass, lacquered against tarnish.",
            "Beaten rather than cast, which leaves the faint facets across the bowl of each spoon. "
            + "Lacquered so they keep their colour; the lacquer will wear at the handle over a few "
            + "years, and re-lacquering is a twenty-minute job at any brass shop.",
            "741820", 12m, 1799m, 1349m, "3 N", 480, (300, 120, 40),
            [("Material", "Brass"), ("Pieces", "3"), ("Finish", "Lacquered"), ("Care", "Hand wash, dry immediately")]),
    ];

    [LoggerMessage(
        EventId = 9101,
        Level = LogLevel.Information,
        Message = "Seeded {Count} demonstration products. This database is not production data.")]
    private static partial void DemoCatalogueSeeded(ILogger logger, int count);

    [LoggerMessage(
        EventId = 9102,
        Level = LogLevel.Warning,
        Message = "Demonstration catalogue skipped: no active seller with code {Code}. "
                  + "Enable the Vendors demo seeder, or the storefront will have nothing to show.")]
    private static partial void DemoVendorMissing(ILogger logger, string code);
}
