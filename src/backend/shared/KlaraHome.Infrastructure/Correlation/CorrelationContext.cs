namespace KlaraHome.Infrastructure.Correlation;

/// <inheritdoc />
internal sealed class CorrelationContext : ICorrelationContext
{
    private string? _correlationId;

    public string CorrelationId
    {
        get => _correlationId ??= NewId();
        private set => _correlationId = value;
    }

    public void Set(string correlationId) => CorrelationId = correlationId;

    /// <summary>UUIDv7 without dashes: sortable, compact enough to paste into a support ticket.</summary>
    public static string NewId() => Guid.CreateVersion7().ToString("n");
}
