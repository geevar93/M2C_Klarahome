using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace KlaraHome.Infrastructure.Security;

/// <summary>
/// Encrypts a single database column whose plaintext the application must be able to read again —
/// the "Sensitive" class of docs/07-security-compliance.md §5.
/// </summary>
/// <remarks>
/// <para>
/// A bank account number is the first of these that is not a credential: it cannot be hashed,
/// because a payout has to be made to it, and it cannot be stored in the clear, because a database
/// dump would then be a list of every seller's account. Authenticated encryption at the column is
/// the control §5 asks for.
/// </para>
/// <para>
/// Deliberately not a general-purpose crypto service. It protects short strings, one at a time,
/// under a key ring supplied as configuration; anything larger or streamed belongs to
/// storage-level encryption, not here.
/// </para>
/// </remarks>
public interface IFieldProtector
{
    /// <summary>Whether a usable key is configured. False means nothing can be protected here.</summary>
    bool IsConfigured { get; }

    /// <summary>Encrypts a value under the current key.</summary>
    /// <param name="plaintext">The value to protect.</param>
    /// <exception cref="InvalidOperationException">No usable current key is configured.</exception>
    string Protect(string plaintext);

    /// <summary>Decrypts a value, or returns null if no key this deployment holds can read it.</summary>
    /// <param name="protectedValue">The envelope produced by <see cref="Protect"/>.</param>
    string? Unprotect(string? protectedValue);
}

/// <summary>
/// The column-encryption key ring, bound from the <c>Encryption</c> configuration section and
/// supplied as a secret rather than as a setting.
/// </summary>
/// <remarks>
/// Every key this deployment can still <em>read</em> is listed; exactly one is the key new values
/// are <em>written</em> under. That is what makes rotation an overlapping window rather than a
/// migration: change <see cref="CurrentKeyId"/>, keep the old key in <see cref="Keys"/>, and rows
/// written under it keep decrypting until they are next rewritten.
/// </remarks>
public sealed class FieldProtectionOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Encryption";

    /// <summary>The number of bytes an AES-256 key has.</summary>
    public const int KeyBytes = 32;

    /// <summary>Which key new values are written under.</summary>
    public string CurrentKeyId { get; set; } = string.Empty;

    /// <summary>Every key this deployment can still read, by id. Base64 of 32 bytes.</summary>
    public IDictionary<string, string> Keys { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether a usable current key is configured.</summary>
    public bool IsConfigured => TryGetKeyBytes(CurrentKeyId, out _);

    /// <summary>The current key's bytes.</summary>
    /// <exception cref="InvalidOperationException">No usable current key is configured.</exception>
    public byte[] CurrentKeyBytes()
        => TryGetKeyBytes(CurrentKeyId, out var key)
            ? key
            : throw new InvalidOperationException(
                $"Encryption:CurrentKeyId is '{CurrentKeyId}', which is not a 32-byte base64 key in "
                + "Encryption:Keys. Without it no protected column can be written, so a seller's bank "
                + "account could not be stored at all.");

    /// <summary>Looks a key up by id, returning false rather than throwing for an unknown one.</summary>
    /// <param name="keyId">The key id from a stored envelope.</param>
    /// <param name="key">The 32 key bytes.</param>
    public bool TryGetKeyBytes(string keyId, out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(keyId) || !Keys.TryGetValue(keyId, out var encoded))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[KeyBytes];

        if (!Convert.TryFromBase64String(encoded ?? string.Empty, bytes, out var written) || written != KeyBytes)
        {
            return false;
        }

        key = bytes.ToArray();
        return true;
    }
}

/// <summary>
/// AES-256-GCM, with the key id carried in the envelope so keys can be rotated with an overlapping
/// window.
/// </summary>
/// <remarks>
/// Authenticated encryption rather than plain AES: a tampered ciphertext fails to decrypt instead
/// of yielding a plausible-looking account number that money would then be sent to.
/// </remarks>
/// <param name="options">Supplies the key ring.</param>
internal sealed class AesGcmFieldProtector(IOptions<FieldProtectionOptions> options) : IFieldProtector
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <inheritdoc />
    public bool IsConfigured => options.Value.IsConfigured;

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var keys = options.Value;
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

    /// <inheritdoc />
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        var parts = protectedValue.Split('.');

        if (parts.Length != 4 || !options.Value.TryGetKeyBytes(parts[0], out var key))
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
            // A value that will not decrypt was tampered with, or was written under a key this
            // deployment no longer holds. Both mean "there is no readable secret here", which the
            // caller handles; neither is worth taking a request down for.
            return null;
        }
    }
}

/// <summary>Registration for column-level encryption.</summary>
public static class FieldProtectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFieldProtector"/> and its key ring. Safe to call from more than one
    /// module: the registration is idempotent, and the key ring is one per deployment by design.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Supplies the <c>Encryption</c> section.</param>
    public static IServiceCollection AddKlaraHomeFieldProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FieldProtectionOptions>(configuration.GetSection(FieldProtectionOptions.SectionName));
        services.TryAddSingleton<IFieldProtector, AesGcmFieldProtector>();

        return services;
    }
}
