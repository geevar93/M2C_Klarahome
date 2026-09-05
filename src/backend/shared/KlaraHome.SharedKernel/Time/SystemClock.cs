namespace KlaraHome.SharedKernel.Time;

/// <summary>The production <see cref="IClock"/>. Tests substitute a fake instead.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
