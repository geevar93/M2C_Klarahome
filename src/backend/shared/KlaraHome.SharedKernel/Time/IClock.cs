namespace KlaraHome.SharedKernel.Time;

/// <summary>
/// The only sanctioned source of the current time. <c>DateTime.Now</c> and
/// <c>DateTimeOffset.UtcNow</c> must not appear in application or domain code — see
/// docs/01-architecture.md §6. Everything is UTC; rendering in the store's timezone is a
/// presentation concern.
/// </summary>
public interface IClock
{
    /// <summary>The current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
