using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Hashes the high-entropy secrets this module stores: refresh tokens, one-time codes and
/// verification links.
/// </summary>
/// <remarks>
/// SHA-256 rather than Argon2id, and that is a deliberate distinction rather than an oversight.
/// A password is low-entropy and chosen by a human, so it needs a memory-hard KDF to make a
/// dictionary attack expensive. These secrets are generated here, from a CSPRNG, and are checked
/// on nearly every request — a slow hash would buy nothing and cost a great deal. The six-digit
/// codes are the exception that proves the rule: they are trivially enumerable, which is why they
/// are protected by an attempt budget and a five-minute expiry rather than by their hash.
/// </remarks>
internal static class SecretHasher
{
    /// <summary>Hashes a secret, salted with something that identifies who it was issued to.</summary>
    /// <remarks>
    /// A six-digit code is salted with its destination, so two people who happen to be sent the
    /// same code do not produce the same stored hash — which would otherwise let anyone who could
    /// read the table find every account currently holding a given code. A refresh token needs no
    /// salt and passes an empty one: it is looked up <em>by</em> its hash, which a per-row salt
    /// would turn into a scan, and 256 bits of randomness has no rainbow table to defend against.
    /// </remarks>
    /// <param name="secret">The code or token.</param>
    /// <param name="salt">Who it was issued to, or empty for an unsalted lookup hash.</param>
    public static string Hash(string secret, string salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(salt);

        // HMAC rather than a digest over a concatenation, so no choice of separator has to be
        // defended: keyed by the salt, ("ab", "cd") and ("a", "bcd") cannot collide by
        // construction rather than by an argument about which characters an email may contain.
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(secret), digest);

        return Convert.ToBase64String(digest);
    }

    /// <summary>Compares a candidate against a stored hash in constant time.</summary>
    /// <param name="secret">The candidate.</param>
    /// <param name="salt">Who it was issued to.</param>
    /// <param name="storedHash">The stored hash.</param>
    public static bool Verify(string secret, string salt, string storedHash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(secret, salt)),
            Encoding.ASCII.GetBytes(storedHash));
    }

    /// <summary>Mints an opaque 256-bit secret, URL-safe so it survives a cookie and a query string.</summary>
    public static string NewOpaqueToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Mints a numeric one-time code of the given length, uniformly distributed.
    /// </summary>
    /// <remarks>
    /// <see cref="RandomNumberGenerator.GetInt32(int, int)"/> rather than a modulo of random
    /// bytes: the modulo is very slightly biased towards low digits, and an OTP is exactly the
    /// place where "very slightly" compounds over millions of codes.
    /// </remarks>
    /// <param name="digits">How many digits the code has.</param>
    public static string NewNumericCode(int digits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, 10);

        Span<char> code = stackalloc char[digits];

        for (var index = 0; index < digits; index++)
        {
            code[index] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }

        return new string(code);
    }
}

/// <summary>
/// Encrypts the secrets that must be readable again — today, only the TOTP shared secret, which
/// the server needs in the clear to compute the expected code
/// (docs/07-security-compliance.md §5, "Sensitive").
/// </summary>
/// <remarks>
/// <para>
/// AES-256-GCM with a key from configuration, supplied as a Docker secret. Authenticated
/// encryption rather than plain AES, so a tampered ciphertext fails to decrypt instead of
/// producing a plausible-looking secret that silently rejects every code the user types.
/// </para>
/// <para>
/// The key id is carried in the envelope so the key can be rotated with an overlapping window:
/// a value encrypted under the previous key still decrypts while new writes use the current one.
/// </para>
/// </remarks>
/// <param name="options">Supplies the key ring.</param>
internal sealed class SecretProtector(IOptions<AuthOptions> options)
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>Encrypts a value under the current key.</summary>
    /// <param name="plaintext">The value to protect.</param>
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var keys = options.Value.Encryption;
        var key = keys.CurrentKeyBytes();

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(nonce, bytes, ciphertext, tag);
        }

        return string.Join(
            '.',
            keys.CurrentKeyId,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(ciphertext),
            Convert.ToBase64String(tag));
    }

    /// <summary>Decrypts a value, or returns null if it cannot be read under any known key.</summary>
    /// <param name="protectedValue">The envelope produced by <see cref="Protect"/>.</param>
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        var parts = protectedValue.Split('.');
        if (parts.Length != 4)
        {
            return null;
        }

        if (!options.Value.Encryption.TryGetKeyBytes(parts[0], out var key))
        {
            return null;
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var ciphertext = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            // A value that will not decrypt is a value that was tampered with, or that was written
            // under a key this deployment no longer holds. Both are "there is no secret here",
            // which the caller handles; neither is an exception worth taking a request down for.
            return null;
        }
    }
}
