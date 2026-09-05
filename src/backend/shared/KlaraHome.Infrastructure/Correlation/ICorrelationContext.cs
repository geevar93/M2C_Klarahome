namespace KlaraHome.Infrastructure.Correlation;

/// <summary>
/// The correlation id for the request being handled. Scoped, so any service can take a
/// dependency on it without reaching for <c>IHttpContextAccessor</c>.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>Never null once the middleware has run; a fresh id outside a request.</summary>
    string CorrelationId { get; }
}
