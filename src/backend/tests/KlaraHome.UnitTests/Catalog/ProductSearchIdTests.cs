using KlaraHome.Modules.Catalog.Application.Products;

namespace KlaraHome.UnitTests.Catalog;

/// <summary>
/// When the admin product search is a list of ids rather than words.
/// </summary>
/// <remarks>
/// The page composer's picker resolves the ids a block already holds back into names by sending
/// them as the search term, and an operator may paste one. Anything that is not wholly ids must
/// stay a name-or-SKU search, or a SKU that happens to contain a GUID-shaped part would stop
/// matching.
/// </remarks>
public sealed class ProductSearchIdTests
{
    private static readonly Guid First = Guid.Parse("01a089c4-5204-73d8-ab46-9339d1e6507c");
    private static readonly Guid Second = Guid.Parse("01a07a30-f386-7906-83e6-03190945bb68");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cushion")]
    [InlineData("01a089c4-5204-73d8-ab46-9339d1e6507c cushion")]
    public void A_term_that_is_not_wholly_ids_is_a_text_search(string? search)
        => Assert.Null(ListProductsQueryHandler.TryParseIds(search));

    [Fact]
    public void One_pasted_id_is_an_id_search()
        => Assert.Equal([First], ListProductsQueryHandler.TryParseIds($"  {First}  "));

    [Fact]
    public void Several_ids_separated_by_spaces_commas_or_lines_are_all_searched()
        => Assert.Equal([First, Second], ListProductsQueryHandler.TryParseIds($"{First},\n{Second}"));
}
