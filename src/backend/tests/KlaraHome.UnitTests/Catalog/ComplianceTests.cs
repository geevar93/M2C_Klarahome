using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The India mandatory-disclosure rules that gate publication
/// (docs/02-domain-model.md §7.4, Legal Metrology + Consumer Protection (E-Commerce) Rules 2020).
/// </summary>
/// <remarks>
/// Tested while writing under the build sprint's rule 1, and for a reason beyond cost: an incorrect
/// rule here does not fail loudly, it publishes goods the operator is legally required not to
/// publish. The importer-for-imports rule in particular has a conditional in it, and a conditional
/// is where a compliance check quietly stops applying.
/// </remarks>
public sealed class ComplianceTests
{
    private static Product Compliant()
    {
        var product = Product.Draft("Cotton Cushion Cover", "cotton-cushion-cover", Guid.CreateVersion7(), null);

        product.DeclareCompliance(
            "6304",
            5m,
            "IN",
            new PartyDetails { Name = "Klara Textiles", Address = "12 Mill Road, Karur, Tamil Nadu 639001" },
            new PartyDetails(),
            new PartyDetails());

        return product;
    }

    private static Variant CompliantVariant()
    {
        var variant = Variant.Create(Guid.CreateVersion7(), "SKU-000001");
        variant.SetDimensions(weightGrams: 220, lengthMm: 400, widthMm: 400, heightMm: 20);
        variant.DeclarePack(Money.Rupees(499m), "1 piece", shelfLifeDays: null, expiresOn: null);

        return variant;
    }

    [Fact]
    public void A_fully_declared_domestic_product_has_no_gaps()
        => Assert.Empty(Compliant().ComplianceGaps());

    [Fact]
    public void A_product_with_no_hsn_code_cannot_be_sold()
    {
        var product = Compliant();

        product.DeclareCompliance(
            null,
            5m,
            "IN",
            new PartyDetails { Name = "Klara Textiles", Address = "12 Mill Road" },
            new PartyDetails(),
            new PartyDetails());

        Assert.Contains(product.ComplianceGaps(), gap => gap.Contains("HSN", StringComparison.Ordinal));
    }

    [Fact]
    public void A_product_with_no_country_of_origin_cannot_be_sold()
    {
        var product = Compliant();

        product.DeclareCompliance(
            "6304",
            5m,
            null,
            new PartyDetails { Name = "Klara Textiles", Address = "12 Mill Road" },
            new PartyDetails(),
            new PartyDetails());

        Assert.Contains(product.ComplianceGaps(), gap => gap.Contains("country of origin", StringComparison.Ordinal));
    }

    [Fact]
    public void A_product_with_no_manufacturer_declared_cannot_be_sold()
    {
        var product = Compliant();

        product.DeclareCompliance("6304", 5m, "IN", new PartyDetails(), new PartyDetails(), new PartyDetails());

        Assert.Contains(product.ComplianceGaps(), gap => gap.Contains("manufacturer", StringComparison.Ordinal));
    }

    [Fact]
    public void Imported_goods_must_declare_an_importer()
    {
        var product = Compliant();

        product.DeclareCompliance(
            "6304",
            5m,
            "CN",
            new PartyDetails { Name = "Some Mill", Address = "Somewhere" },
            new PartyDetails(),
            new PartyDetails());

        Assert.Contains(product.ComplianceGaps(), gap => gap.Contains("importer", StringComparison.Ordinal));
    }

    [Fact]
    public void Domestic_goods_are_not_asked_for_an_importer()
    {
        // The conditional worth pinning: asking an Indian-made cushion cover for an importer is how
        // an operator learns to leave the check switched off.
        Assert.DoesNotContain(Compliant().ComplianceGaps(), gap => gap.Contains("importer", StringComparison.Ordinal));
    }

    [Fact]
    public void The_origin_check_is_not_case_sensitive()
    {
        var product = Compliant();

        product.DeclareCompliance(
            "6304",
            5m,
            "in",
            new PartyDetails { Name = "Klara Textiles", Address = "12 Mill Road" },
            new PartyDetails(),
            new PartyDetails());

        Assert.Empty(product.ComplianceGaps());
    }

    [Fact]
    public void A_fully_declared_variant_has_no_gaps()
        => Assert.Empty(CompliantVariant().ComplianceGaps());

    [Fact]
    public void A_variant_with_no_mrp_cannot_be_sold()
    {
        var variant = CompliantVariant();
        variant.DeclarePack(Money.Rupees(0m), "1 piece", null, null);

        Assert.Contains(variant.ComplianceGaps(), gap => gap.Contains("MRP", StringComparison.Ordinal));
    }

    [Fact]
    public void A_variant_with_no_net_quantity_cannot_be_sold()
    {
        var variant = CompliantVariant();
        variant.DeclarePack(Money.Rupees(499m), null, null, null);

        Assert.Contains(variant.ComplianceGaps(), gap => gap.Contains("net quantity", StringComparison.Ordinal));
    }

    [Fact]
    public void A_variant_with_no_weight_cannot_be_priced_for_a_courier()
    {
        var variant = CompliantVariant();
        variant.SetDimensions(0, 400, 400, 20);

        Assert.Contains(variant.ComplianceGaps(), gap => gap.Contains("weight", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_gap_names_the_sku_so_an_operator_knows_which_variant_to_fix()
    {
        var variant = Variant.Create(Guid.CreateVersion7(), "SKU-000042");

        Assert.NotEmpty(variant.ComplianceGaps());
        Assert.All(variant.ComplianceGaps(), gap => Assert.Contains("SKU-000042", gap, StringComparison.Ordinal));
    }
}
