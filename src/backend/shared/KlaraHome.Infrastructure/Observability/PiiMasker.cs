namespace KlaraHome.Infrastructure.Observability;

/// <summary>Applies a <see cref="PiiMask"/> to a value.</summary>
public static class PiiMasker
{
    /// <summary>The token written in place of a redacted value.</summary>
    public const string Redacted = "***";

    /// <summary>
    /// Property names masked wherever they appear, even without a <see cref="PiiAttribute"/>.
    /// This is the safety net for DTOs nobody remembered to annotate.
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysMaskedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "newPassword", "currentPassword", "confirmPassword",
        "otp", "otpCode", "pin", "secret", "clientSecret", "apiKey", "signature",
        "token", "accessToken", "refreshToken", "idToken", "authorization",
        "cardNumber", "cvv", "pan", "upiId",
        "mobile", "mobileNumber", "phone", "phoneNumber", "email", "emailAddress",
        "addressLine1", "addressLine2", "aadhaar", "gstin",
    };

    public static string Apply(string? value, PiiMask mask)
    {
        if (string.IsNullOrEmpty(value))
        {
            return Redacted;
        }

        return mask switch
        {
            PiiMask.LastFour => value.Length <= 4 ? Redacted : Redacted + value[^4..],
            PiiMask.Email => MaskEmail(value),
            _ => Redacted,
        };
    }

    private static string MaskEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Redacted : value[0] + Redacted + value[at..];
    }
}
