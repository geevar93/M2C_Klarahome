using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Seeding;

namespace KlaraHome.UnitTests.Platform;

/// <summary>
/// Reference data is compiled in and seeded, so a mistake in it is a mistake in every deployment.
/// The GST state code in particular decides whether a sale attracts CGST plus SGST or IGST.
/// </summary>
public sealed class ReferenceDataTests
{
    [Fact]
    public void India_has_twenty_eight_states_and_eight_union_territories()
    {
        var states = IndianJurisdictions.All.Count(entry => entry.Kind == StateKind.State);
        var unionTerritories = IndianJurisdictions.All.Count(entry => entry.Kind == StateKind.UnionTerritory);

        Assert.Equal(28, states);
        Assert.Equal(8, unionTerritories);
    }

    [Fact]
    public void Every_gst_state_code_is_two_digits_and_unique()
    {
        Assert.Distinct(IndianJurisdictions.All.Select(entry => entry.Code));

        Assert.All(IndianJurisdictions.All, entry =>
        {
            Assert.Equal(2, entry.Code.Length);
            Assert.True(entry.Code.All(char.IsAsciiDigit), $"'{entry.Code}' is not a two-digit code.");
            Assert.False(string.IsNullOrWhiteSpace(entry.Name));
        });
    }

    [Fact]
    public void The_codes_of_merged_and_bifurcated_jurisdictions_are_not_offered()
    {
        var codes = IndianJurisdictions.All.Select(entry => entry.Code).ToList();

        // 25 was Daman and Diu, merged into 26; 28 was undivided Andhra Pradesh, now 37. GSTN keeps
        // both so historic returns still parse, but neither is a place a customer lives today.
        Assert.DoesNotContain("25", codes);
        Assert.DoesNotContain("28", codes);
    }

    [Fact]
    public void The_jurisdictions_that_decide_place_of_supply_carry_the_codes_the_law_gives_them()
    {
        // A spot check on the ones a marketplace sees most, and on the two the 2019 reorganisation
        // created. A wrong code here is a wrong tax on every invoice for that state.
        Assert.Equal("27", CodeOf("Maharashtra"));
        Assert.Equal("29", CodeOf("Karnataka"));
        Assert.Equal("33", CodeOf("Tamil Nadu"));
        Assert.Equal("07", CodeOf("Delhi"));
        Assert.Equal("24", CodeOf("Gujarat"));
        Assert.Equal("36", CodeOf("Telangana"));
        Assert.Equal("37", CodeOf("Andhra Pradesh"));
        Assert.Equal("38", CodeOf("Ladakh"));
    }

    [Fact]
    public void Delhi_and_jammu_and_kashmir_are_union_territories()
    {
        // Both were states or are commonly written as states; both are union territories for GST.
        Assert.Equal(StateKind.UnionTerritory, KindOf("Delhi"));
        Assert.Equal(StateKind.UnionTerritory, KindOf("Jammu and Kashmir"));
        Assert.Equal(StateKind.UnionTerritory, KindOf("Ladakh"));
    }

    [Fact]
    public void There_are_ninety_nine_hsn_chapters_numbered_without_a_gap()
    {
        Assert.Equal(99, HsnChapters.All.Count);
        Assert.Distinct(HsnChapters.All.Select(chapter => chapter.Code));

        var expected = Enumerable.Range(1, 99).Select(number => number.ToString("00", null));
        Assert.Equal(expected, HsnChapters.All.Select(chapter => chapter.Code));
    }

    [Fact]
    public void Every_hsn_chapter_is_described()
    {
        Assert.All(HsnChapters.All, chapter =>
        {
            Assert.False(string.IsNullOrWhiteSpace(chapter.Description));
            Assert.True(chapter.Description.Length <= 1000, $"Chapter {chapter.Code} exceeds the column width.");
        });
    }

    [Fact]
    public void A_seeded_hsn_chapter_carries_no_tax_rate()
    {
        // Rates are notified at four, six and eight digits. A plausible-looking chapter rate is
        // exactly the sort of wrong number that reaches an invoice.
        foreach (var (code, description) in HsnChapters.All)
        {
            Assert.Null(HsnCode.Define(code, description).DefaultGstRate);
        }
    }

    [Fact]
    public void A_reference_id_is_the_same_every_time_it_is_derived()
    {
        var first = ReferenceIds.For("state", "27");
        var second = ReferenceIds.For("state", "27");

        // Re-seeding must land on the same row rather than inserting a second Maharashtra.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Reference_ids_from_different_tables_do_not_collide()
    {
        Assert.NotEqual(ReferenceIds.For("state", "27"), ReferenceIds.For("hsn", "27"));
        Assert.NotEqual(ReferenceIds.For("pincode", "400001"), ReferenceIds.For("hsn", "400001"));
    }

    [Fact]
    public void A_reference_id_is_a_valid_rfc_9562_uuid()
    {
        var id = ReferenceIds.For("state", "27");
        Span<byte> bytes = stackalloc byte[16];
        Assert.True(id.TryWriteBytes(bytes, bigEndian: true, out _));

        Assert.Equal(0x80, bytes[6] & 0xF0); // version 8: custom
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC 9562 variant
    }

    private static string CodeOf(string name)
        => IndianJurisdictions.All.Single(entry => entry.Name == name).Code;

    private static StateKind KindOf(string name)
        => IndianJurisdictions.All.Single(entry => entry.Name == name).Kind;
}
