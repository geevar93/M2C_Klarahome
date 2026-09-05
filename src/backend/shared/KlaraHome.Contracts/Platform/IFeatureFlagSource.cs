namespace KlaraHome.Contracts.Platform;

/// <summary>One flag a module declares, before an operator has changed anything.</summary>
/// <param name="Key">The dotted key, prefixed with the declaring module's name.</param>
/// <param name="Enabled">Whether it ships on.</param>
/// <param name="Description">What it controls, shown in the admin UI.</param>
public sealed record FeatureFlagDeclaration(string Key, bool Enabled, string Description);

/// <summary>
/// Lets a module declare the feature flags it reads, without writing to the <c>platform</c> schema
/// that stores them.
/// </summary>
/// <remarks>
/// <para>
/// A flag is declared in code and seeded into <c>platform.feature_flags</c>; an operator then
/// toggles the row. Declaring them is what makes the admin UI list every switch that exists rather
/// than only the ones somebody has already touched — and a flag nobody can find is a flag nobody
/// turns off in the incident it was added for.
/// </para>
/// <para>
/// The Platform module owns the table and collects every registered source at seed time. That is
/// the whole reason this contract exists: the Identity module needs four flags of its own, and no
/// module may write to another's schema (docs/01-architecture.md §2.1).
/// </para>
/// </remarks>
public interface IFeatureFlagSource
{
    /// <summary>The declaring module, for diagnostics and for grouping in the admin UI.</summary>
    string Module { get; }

    /// <summary>The flags this module reads. Seeded on every deploy, upserted by key.</summary>
    IReadOnlyList<FeatureFlagDeclaration> Flags { get; }
}
