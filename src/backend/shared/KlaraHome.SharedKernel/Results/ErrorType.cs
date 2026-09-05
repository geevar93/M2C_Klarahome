namespace KlaraHome.SharedKernel.Results;

/// <summary>
/// The category of a failure. This is the single place where a domain/application failure is
/// classified; the HTTP layer maps the category to a status code
/// (see docs/04-api-specification.md §1.2) so handlers never mention HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>Malformed input that could not even be interpreted. Maps to 400.</summary>
    Malformed = 0,

    /// <summary>No credentials, or credentials that are expired/invalid. Maps to 401.</summary>
    Unauthorized = 1,

    /// <summary>Authenticated but not permitted. Maps to 403.</summary>
    Forbidden = 2,

    /// <summary>Absent, or deliberately invisible to this caller. Maps to 404.</summary>
    NotFound = 3,

    /// <summary>Concurrency clash, duplicate, or an illegal state transition. Maps to 409.</summary>
    Conflict = 4,

    /// <summary>The resource existed but has expired (checkout session, cart). Maps to 410.</summary>
    Gone = 5,

    /// <summary>Field validation or a business rule refusal. Maps to 422.</summary>
    Validation = 6,

    /// <summary>Caller exceeded a rate limit. Maps to 429.</summary>
    RateLimited = 7,

    /// <summary>An unexpected failure. Maps to 500 and never leaks internals.</summary>
    Unexpected = 8,

    /// <summary>A dependency (gateway, courier) is unavailable. Maps to 503.</summary>
    Unavailable = 9,
}
