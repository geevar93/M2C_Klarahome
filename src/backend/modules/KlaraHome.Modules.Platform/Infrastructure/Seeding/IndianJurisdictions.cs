using KlaraHome.Modules.Platform.Domain;

namespace KlaraHome.Modules.Platform.Infrastructure.Seeding;

/// <summary>
/// The 28 states and 8 union territories of India with their GST state codes, as constituted after
/// the 2019–2020 reorganisations.
/// </summary>
/// <remarks>
/// <para>
/// The GST state code is the first two digits of every GSTIN and decides place of supply — whether
/// a sale attracts CGST plus SGST or IGST (docs/02-domain-model.md §7). Getting one wrong is a tax
/// error on every invoice for that state, which is why the list is compiled in and seeded rather
/// than typed into an admin screen.
/// </para>
/// <para>
/// Codes 25 (Daman and Diu) and 28 (undivided Andhra Pradesh) are deliberately absent: both
/// jurisdictions were merged or bifurcated, and their codes are retained by GSTN only so historic
/// returns still parse. Code 97 (Other Territory, for offshore areas) is also absent — it is
/// neither a state nor a union territory, and the Shipping module can add it when an offshore
/// place of supply first has to be modelled.
/// </para>
/// </remarks>
internal static class IndianJurisdictions
{
    /// <summary>Every current state and union territory, ordered by GST code.</summary>
    public static IReadOnlyList<(string Code, string Name, StateKind Kind)> All { get; } =
    [
        ("01", "Jammu and Kashmir", StateKind.UnionTerritory),
        ("02", "Himachal Pradesh", StateKind.State),
        ("03", "Punjab", StateKind.State),
        ("04", "Chandigarh", StateKind.UnionTerritory),
        ("05", "Uttarakhand", StateKind.State),
        ("06", "Haryana", StateKind.State),
        ("07", "Delhi", StateKind.UnionTerritory),
        ("08", "Rajasthan", StateKind.State),
        ("09", "Uttar Pradesh", StateKind.State),
        ("10", "Bihar", StateKind.State),
        ("11", "Sikkim", StateKind.State),
        ("12", "Arunachal Pradesh", StateKind.State),
        ("13", "Nagaland", StateKind.State),
        ("14", "Manipur", StateKind.State),
        ("15", "Mizoram", StateKind.State),
        ("16", "Tripura", StateKind.State),
        ("17", "Meghalaya", StateKind.State),
        ("18", "Assam", StateKind.State),
        ("19", "West Bengal", StateKind.State),
        ("20", "Jharkhand", StateKind.State),
        ("21", "Odisha", StateKind.State),
        ("22", "Chhattisgarh", StateKind.State),
        ("23", "Madhya Pradesh", StateKind.State),
        ("24", "Gujarat", StateKind.State),
        ("26", "Dadra and Nagar Haveli and Daman and Diu", StateKind.UnionTerritory),
        ("27", "Maharashtra", StateKind.State),
        ("29", "Karnataka", StateKind.State),
        ("30", "Goa", StateKind.State),
        ("31", "Lakshadweep", StateKind.UnionTerritory),
        ("32", "Kerala", StateKind.State),
        ("33", "Tamil Nadu", StateKind.State),
        ("34", "Puducherry", StateKind.UnionTerritory),
        ("35", "Andaman and Nicobar Islands", StateKind.UnionTerritory),
        ("36", "Telangana", StateKind.State),
        ("37", "Andhra Pradesh", StateKind.State),
        ("38", "Ladakh", StateKind.UnionTerritory),
    ];
}
