using KlaraHome.Modules.Catalog.Domain;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// The variant combination hash, which is what enforces "a variant's defining attribute combination
/// is unique within its product" (docs/02-domain-model.md §4.1).
/// </summary>
/// <remarks>
/// Worth testing while writing under the build sprint's rule 1: a unique index is only as good as
/// the value it indexes, and a hash that is order-sensitive or process-dependent would let the same
/// combination be created twice with nothing anywhere reporting a problem.
/// </remarks>
public sealed class VariantCombinationTests
{
    private static readonly Guid Colour = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Size = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Beige = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Grey = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly Guid Small = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    [Fact]
    public void The_same_combination_hashes_the_same_whatever_order_it_arrives_in()
    {
        var one = Variant.HashOf([(Colour, Beige), (Size, Small)]);
        var other = Variant.HashOf([(Size, Small), (Colour, Beige)]);

        Assert.Equal(one, other);
    }

    [Fact]
    public void A_different_combination_hashes_differently()
    {
        var beige = Variant.HashOf([(Colour, Beige), (Size, Small)]);
        var grey = Variant.HashOf([(Colour, Grey), (Size, Small)]);

        Assert.NotEqual(beige, grey);
    }

    [Fact]
    public void Dropping_an_axis_changes_the_hash()
    {
        var both = Variant.HashOf([(Colour, Beige), (Size, Small)]);
        var colourOnly = Variant.HashOf([(Colour, Beige)]);

        Assert.NotEqual(both, colourOnly);
    }

    [Fact]
    public void A_product_with_no_options_still_gets_a_real_hash()
    {
        // Not an empty string: the unique index has to catch the mistake of creating two
        // "no options" variants on one product, and it can only do that if they collide.
        Assert.Equal(64, Variant.EmptyCombinationHash.Length);
        Assert.Equal(Variant.EmptyCombinationHash, Variant.HashOf([]));
    }

    [Fact]
    public void The_hash_is_a_fixed_width_lowercase_hex_string()
    {
        var hash = Variant.HashOf([(Colour, Beige)]);

        // Fixed width because the column is char(64), and stable across processes because it is
        // SHA-256 rather than the runtime's randomised string hash.
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'));
    }

    [Fact]
    public void A_new_variant_starts_with_the_empty_combination_hash()
    {
        var variant = Variant.Create(Guid.CreateVersion7(), "SKU-000001");

        Assert.Equal(Variant.EmptyCombinationHash, variant.AttributeHash);
    }

    [Fact]
    public void Recording_a_combination_replaces_the_hash()
    {
        var variant = Variant.Create(Guid.CreateVersion7(), "SKU-000001");
        variant.SetCombination([(Colour, Beige), (Size, Small)]);

        Assert.Equal(Variant.HashOf([(Colour, Beige), (Size, Small)]), variant.AttributeHash);
    }
}
