using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id password hashing (docs/07-security-compliance.md §1).
/// </summary>
/// <remarks>
/// <para>
/// The stored value is a PHC string — <c>$argon2id$v=19$m=...,t=...,p=...$salt$hash</c> — so the
/// parameters a hash was produced with travel with it. Raising the cost next year then verifies
/// every existing password correctly and re-hashes it on the owner's next sign-in, instead of
/// invalidating every password in the database.
/// </para>
/// <para>
/// Comparison is constant-time. A verify that returned early on the first differing byte would
/// leak the hash a byte at a time to anyone who can measure it.
/// </para>
/// </remarks>
/// <param name="options">The cost parameters, so they can be tuned per deployment.</param>
internal sealed class PasswordHasher(IOptions<AuthOptions> options)
{
    private const string Algorithm = "argon2id";
    private const int Version = 19;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private PasswordOptions Options => options.Value.Password;

    /// <summary>Hashes a password, producing a PHC string that carries its own parameters.</summary>
    /// <param name="password">The plaintext password.</param>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var parameters = new Argon2Parameters(
            Options.MemoryKib,
            Options.Iterations,
            Options.Parallelism);

        var hash = Derive(password, salt, parameters);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"${Algorithm}$v={Version}$m={parameters.MemoryKib},t={parameters.Iterations},"
            + $"p={parameters.Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>
    /// Verifies a password against a stored hash, and says whether the hash should be rewritten
    /// because the configured cost has since moved on.
    /// </summary>
    /// <param name="password">The plaintext offered.</param>
    /// <param name="stored">The stored PHC string.</param>
    /// <param name="needsRehash">True when the stored hash used weaker parameters than are configured now.</param>
    public bool Verify(string password, string? stored, out bool needsRehash)
    {
        needsRehash = false;

        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return false;
        }

        if (!TryParse(stored, out var parameters, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, parameters);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return false;
        }

        needsRehash = parameters.MemoryKib < Options.MemoryKib
                      || parameters.Iterations < Options.Iterations;

        return true;
    }

    private static byte[] Derive(string password, byte[] salt, Argon2Parameters parameters)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = parameters.MemoryKib,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism,
        };

        return argon2.GetBytes(HashBytes);
    }

    /// <summary>
    /// Reads a PHC string. A malformed or unrecognised value is a failed verification, never an
    /// exception: it reaches this method from the database, and a corrupt row must not be able to
    /// take the login endpoint down.
    /// </summary>
    internal static bool TryParse(
        string stored,
        out Argon2Parameters parameters,
        out byte[] salt,
        out byte[] hash)
    {
        parameters = default;
        salt = [];
        hash = [];

        var parts = stored.Split('$');

        // ["", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash]
        if (parts.Length != 6
            || parts[0].Length != 0
            || !string.Equals(parts[1], Algorithm, StringComparison.Ordinal)
            || !string.Equals(parts[2], $"v={Version}", StringComparison.Ordinal))
        {
            return false;
        }

        int memory = 0, iterations = 0, parallelism = 0;

        foreach (var setting in parts[3].Split(','))
        {
            var pair = setting.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            switch (pair[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": parallelism = value; break;
                default: return false;
            }
        }

        if (memory <= 0 || iterations <= 0 || parallelism <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        parameters = new Argon2Parameters(memory, iterations, parallelism);
        return salt.Length > 0 && hash.Length > 0;
    }
}

/// <summary>The Argon2id cost a particular hash was produced with.</summary>
/// <param name="MemoryKib">Memory cost in KiB.</param>
/// <param name="Iterations">Time cost.</param>
/// <param name="Parallelism">Lanes.</param>
internal readonly record struct Argon2Parameters(int MemoryKib, int Iterations, int Parallelism);
