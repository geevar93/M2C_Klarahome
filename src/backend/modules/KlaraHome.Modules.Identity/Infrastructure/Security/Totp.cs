using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KlaraHome.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Time-based one-time passwords, RFC 6238 over RFC 4226 (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// <para>
/// Implemented here rather than taken from a package. TOTP is HMAC-SHA1 over a counter plus the
/// dynamic truncation from RFC 4226 §5.3 — it invents no cryptography, it is forty lines, and
/// both RFCs publish test vectors, so it can be proved correct rather than trusted. The
/// alternative was a dependency on a package that would do the same forty lines.
/// </para>
/// <para>
/// SHA-1 is not an oversight either. Every authenticator app an Indian customer or a warehouse
/// supervisor already has installed implements RFC 6238's default, and HMAC-SHA1 is unaffected by
/// the collision attacks that retired SHA-1 for signatures.
/// </para>
/// </remarks>
internal static class Totp
{
    /// <summary>The step length every authenticator app assumes.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    /// <summary>The code length every authenticator app assumes.</summary>
    public const int Digits = 6;

    private const int SecretBytes = 20;

    /// <summary>Mints a shared secret, base32-encoded as an authenticator app expects it.</summary>
    public static string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>Computes the code for one instant.</summary>
    /// <param name="secret">The base32 shared secret.</param>
    /// <param name="at">The instant to compute for.</param>
    /// <param name="digits">Code length; six unless a test vector says otherwise.</param>
    /// <param name="period">Step length; thirty seconds unless a test vector says otherwise.</param>
    public static string Compute(string secret, DateTimeOffset at, int digits = Digits, TimeSpan? period = null)
    {
        var step = (long)(at.ToUnixTimeSeconds() / (period ?? Period).TotalSeconds);
        return ComputeForCounter(Base32.Decode(secret), step, digits);
    }

    /// <summary>
    /// Whether a code is valid for an instant, allowing a window of steps either side.
    /// </summary>
    /// <remarks>
    /// The window exists because the phone's clock and the server's disagree, and because a person
    /// types six digits in the seconds before a step rolls over. One step either side — ninety
    /// seconds of tolerance in total — is the usual compromise; widening it multiplies the codes an
    /// attacker may guess at.
    /// </remarks>
    /// <param name="secret">The base32 shared secret.</param>
    /// <param name="code">The code the user typed.</param>
    /// <param name="at">The current instant.</param>
    /// <param name="stepsOfTolerance">How many steps either side are accepted.</param>
    public static bool Verify(string secret, string code, DateTimeOffset at, int stepsOfTolerance = 1)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var candidate = code.Trim();
        if (candidate.Length != Digits || !candidate.All(char.IsAsciiDigit))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32.Decode(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var step = (long)(at.ToUnixTimeSeconds() / Period.TotalSeconds);
        var offered = Encoding.ASCII.GetBytes(candidate);
        var matched = false;

        // Every candidate step is checked even after a match, so the time this takes does not
        // reveal which step the accepted code belonged to.
        for (var drift = -stepsOfTolerance; drift <= stepsOfTolerance; drift++)
        {
            var expected = Encoding.ASCII.GetBytes(ComputeForCounter(key, step + drift, Digits));
            matched |= CryptographicOperations.FixedTimeEquals(offered, expected);
        }

        return matched;
    }

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator app scans, per the Key Uri Format.
    /// </summary>
    /// <param name="secret">The base32 shared secret.</param>
    /// <param name="issuer">The store's name, so the app shows which account this is.</param>
    /// <param name="account">The user's email address or mobile number.</param>
    public static string ProvisioningUri(string secret, string issuer, string account)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}"
            + $"&algorithm=SHA1&digits={Digits}&period={(int)Period.TotalSeconds}");
    }

    /// <summary>HOTP (RFC 4226 §5.3): HMAC, dynamic truncation, then the low digits.</summary>
    /// <remarks>
    /// The analyzer flags HMAC-SHA1 as a weak algorithm, and for a signature it would be right.
    /// This is not a signature: HMAC-SHA1 is what RFC 6238 specifies as the default and what every
    /// authenticator app implements, and the collision attacks that retired SHA-1 do not apply to
    /// HMAC. Choosing SHA-256 here would produce codes no customer's phone could generate.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 specifies HMAC-SHA1; every authenticator app implements it, and "
                        + "HMAC is unaffected by SHA-1 collision attacks.")]
    internal static string ComputeForCounter(byte[] key, long counter, int digits)
    {
        Span<byte> message = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, counter);

        Span<byte> mac = stackalloc byte[20];
        HMACSHA1.HashData(key, message, mac);

        // The offset is the low nibble of the last byte; the four bytes at that offset, with the
        // sign bit cleared, are the number the code is taken from.
        var offset = mac[^1] & 0x0F;
        var binary = ((mac[offset] & 0x7F) << 24)
                     | (mac[offset + 1] << 16)
                     | (mac[offset + 2] << 8)
                     | mac[offset + 3];

        var modulus = (int)Math.Pow(10, digits);
        return (binary % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }
}

/// <summary>
/// RFC 4648 base32, the alphabet every authenticator app pastes and scans.
/// </summary>
/// <remarks>
/// .NET has base64 and hex conversions built in but no base32, and this is the one place the
/// project needs it: a TOTP secret is shown to a human to type into their phone, and base32
/// avoids the characters that are misread when it is.
/// </remarks>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes, without padding, as an authenticator app expects.</summary>
    /// <param name="data">The bytes.</param>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var builder = new StringBuilder(((data.Length * 8) + 4) / 5);

        var buffer = 0;
        var bitsHeld = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsHeld += 8;

            while (bitsHeld >= 5)
            {
                bitsHeld -= 5;
                builder.Append(Alphabet[(buffer >> bitsHeld) & 0x1F]);
            }
        }

        if (bitsHeld > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsHeld)) & 0x1F]);
        }

        return builder.ToString();
    }

    /// <summary>Decodes a base32 string, tolerating padding, spaces and lower case.</summary>
    /// <param name="encoded">The encoded secret.</param>
    /// <exception cref="FormatException">The string contains a character outside the alphabet.</exception>
    public static byte[] Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var bytes = new List<byte>(encoded.Length * 5 / 8);
        var buffer = 0;
        var bitsHeld = 0;

        foreach (var character in encoded)
        {
            if (character is '=' or ' ' or '-')
            {
                continue;
            }

            var value = Alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"'{character}' is not a base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsHeld += 5;

            if (bitsHeld >= 8)
            {
                bitsHeld -= 8;
                bytes.Add((byte)((buffer >> bitsHeld) & 0xFF));
            }
        }

        return [.. bytes];
    }
}
