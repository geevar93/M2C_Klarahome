namespace KlaraHome.Infrastructure.Observability;

/// <summary>
/// Marks a property as personal data. Anything so marked is masked before it reaches a log sink,
/// by policy rather than by the caller remembering (docs/09-nfr-testing-observability.md §3.1).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PiiAttribute : Attribute
{
    /// <summary>How much of the value survives masking.</summary>
    public PiiMask Mask { get; init; } = PiiMask.Full;
}

/// <summary>Masking strategies. Partial masking keeps just enough to correlate a support call.</summary>
public enum PiiMask
{
    /// <summary>Replace the whole value.</summary>
    Full = 0,

    /// <summary>Keep the last four characters, e.g. a mobile number.</summary>
    LastFour = 1,

    /// <summary>Keep the first character and the domain, e.g. an email address.</summary>
    Email = 2,
}
