using System.Text;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace KlaraHome.UnitTests.Identity;

/// <summary>
/// RFC 4648 base32 and RFC 6238 TOTP, checked against the published vectors.
/// </summary>
/// <remarks>
/// This is why implementing TOTP here rather than taking a dependency was defensible: the
/// specifications publish test vectors, so the implementation is provably the algorithm rather
/// than something that merely produces six digits.
/// </remarks>
public sealed class TotpTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_the_RFC_4648_vectors(string plain, string encoded)
        => Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));

    [Theory]
    [InlineData("MY======", "f")]
    [InlineData("mzxw6ytboi", "foobar")]
    [InlineData("MZXW6 YTB OI", "foobar")]
    public void Base32_decodes_padding_lower_case_and_spacing(string encoded, string plain)
        => Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded)));

    [Fact]
    public void Base32_refuses_a_character_outside_the_alphabet()
        => Assert.Throws<FormatException>(() => Base32.Decode("MZXW6YTB1"));

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Totp_matches_the_RFC_6238_vectors(long unixSeconds, string expected)
    {
        // The RFC's SHA-1 vectors use the twenty-byte seed "12345678901234567890" and eight digits.
        var secret = Base32.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));

        var code = Totp.Compute(
            secret,
            DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
            digits: 8,
            period: TimeSpan.FromSeconds(30));

        Assert.Equal(expected, code);
    }

    [Fact]
    public void A_current_code_verifies_and_a_wrong_one_does_not()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;

        Assert.True(Totp.Verify(secret, Totp.Compute(secret, now), now));
        Assert.False(Totp.Verify(secret, "000000", now.AddYears(1)));
    }

    [Fact]
    public void A_code_from_the_step_either_side_is_accepted_and_one_further_out_is_not()
    {
        var secret = Totp.NewSecret();
        var now = DateTimeOffset.UtcNow;

        // The phone's clock and the server's disagree, and a person takes seconds to type.
        Assert.True(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(-30)), now));
        Assert.True(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(30)), now));

        // Two steps out is a code that is no longer, or not yet, the user's.
        Assert.False(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(-120)), now));
        Assert.False(Totp.Verify(secret, Totp.Compute(secret, now.AddSeconds(120)), now));
    }

    [Fact]
    public void A_malformed_code_or_secret_is_refused_rather_than_throwing()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(Totp.Verify(Totp.NewSecret(), "12345", now));
        Assert.False(Totp.Verify(Totp.NewSecret(), "abcdef", now));
        Assert.False(Totp.Verify("not base32 at all!", "123456", now));
        Assert.False(Totp.Verify(string.Empty, "123456", now));
    }

    [Fact]
    public void The_provisioning_uri_names_the_store_and_the_account()
    {
        var uri = Totp.ProvisioningUri("JBSWY3DPEHPK3PXP", "Klara Home", "founder@example.in");

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=Klara%20Home", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6", uri, StringComparison.Ordinal);
        Assert.Contains("period=30", uri, StringComparison.Ordinal);
    }
}

/// <summary>Argon2id hashing, its PHC encoding, and the cost-upgrade path.</summary>
public sealed class PasswordHasherTests
{
    /// <summary>
    /// The lowest parameters the options allow. The real cost is a deliberate 19 MiB per hash;
    /// paying that in every unit test would make the suite slow for no additional assurance, and
    /// the parameters travel in the hash precisely so they can differ.
    /// </summary>
    private static PasswordHasher Hasher(int memoryKib = 8192, int iterations = 1)
        => new(Options.Create(new AuthOptions
        {
            Password = new PasswordOptions { MemoryKib = memoryKib, Iterations = iterations, Parallelism = 1 },
        }));

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("correct-horse-battery-staple");

        Assert.True(hasher.Verify("correct-horse-battery-staple", hash, out var needsRehash));
        Assert.False(needsRehash);
    }

    [Fact]
    public void A_wrong_password_does_not()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("correct-horse-battery-staple");

        Assert.False(hasher.Verify("correct-horse-battery-stapl", hash, out _));
        Assert.False(hasher.Verify(string.Empty, hash, out _));
    }

    [Fact]
    public void Two_hashes_of_one_password_differ()
    {
        var hasher = Hasher();

        // Different salts. Identical hashes would mean an attacker who saw the table could tell
        // which accounts share a password.
        Assert.NotEqual(hasher.Hash("same-password-twice"), hasher.Hash("same-password-twice"));
    }

    [Fact]
    public void The_hash_is_a_PHC_string_carrying_its_own_parameters()
    {
        var hash = Hasher(memoryKib: 8192, iterations: 3).Hash("whatever");

        Assert.StartsWith("$argon2id$v=19$m=8192,t=3,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(6, hash.Split('$').Length);
    }

    [Fact]
    public void A_hash_made_with_weaker_parameters_verifies_and_asks_to_be_rewritten()
    {
        var weak = Hasher(memoryKib: 8192, iterations: 1).Hash("a-long-enough-password");
        var strong = Hasher(memoryKib: 16384, iterations: 2);

        // Raising the cost must not invalidate everybody's password. It verifies under the
        // parameters it was made with, and is re-hashed while the plaintext is briefly in hand.
        Assert.True(strong.Verify("a-long-enough-password", weak, out var needsRehash));
        Assert.True(needsRehash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2i$v=19$m=8192,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=16$m=8192,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=0,t=1,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=8192,t=1,p=1$not base64!$aGFzaA")]
    public void A_corrupt_stored_hash_is_a_failed_verification_rather_than_an_exception(string stored)
    {
        // It arrives from the database. A malformed row must not be able to take the login
        // endpoint down for everybody.
        Assert.False(Hasher().Verify("anything", stored, out _));
    }

    [Fact]
    public void A_null_stored_hash_is_a_failed_verification()
    {
        // A customer who only ever signs in with an OTP has no password. Offering one must fail,
        // not throw.
        Assert.False(Hasher().Verify("anything", null, out _));
    }
}

/// <summary>The AES-GCM envelope protecting TOTP secrets at rest.</summary>
public sealed class SecretProtectorTests
{
    private const string KeyOne = "a2xhcmFob21lLWludGVncmF0aW9uLXRlc3RzLWtleSE=";
    private const string KeyTwo = "YW5vdGhlci0zMi1ieXRlLWtleS1mb3ItdGVzdHMhISE=";

    private static SecretProtector Protector(string currentKeyId, params (string Id, string Key)[] keys)
        => new(Options.Create(new AuthOptions
        {
            Encryption = new EncryptionOptions
            {
                CurrentKeyId = currentKeyId,
                Keys = keys.ToDictionary(key => key.Id, key => key.Key, StringComparer.Ordinal),
            },
        }));

    [Fact]
    public void A_protected_value_comes_back_unchanged()
    {
        var protector = Protector("k1", ("k1", KeyOne));
        var secret = "JBSWY3DPEHPK3PXP";

        Assert.Equal(secret, protector.Unprotect(protector.Protect(secret)));
    }

    [Fact]
    public void The_envelope_names_its_key_and_does_not_contain_the_plaintext()
    {
        var envelope = Protector("k1", ("k1", KeyOne)).Protect("JBSWY3DPEHPK3PXP");

        Assert.StartsWith("k1.", envelope, StringComparison.Ordinal);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", envelope, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_written_under_the_previous_key_still_decrypts()
    {
        // The overlapping window that makes a key rotation possible without invalidating every
        // enrolled authenticator.
        var before = Protector("k1", ("k1", KeyOne));
        var after = Protector("k2", ("k2", KeyTwo), ("k1", KeyOne));

        var envelope = before.Protect("JBSWY3DPEHPK3PXP");

        Assert.Equal("JBSWY3DPEHPK3PXP", after.Unprotect(envelope));
        Assert.StartsWith("k2.", after.Protect("JBSWY3DPEHPK3PXP"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tampered_envelope_will_not_decrypt()
    {
        var protector = Protector("k1", ("k1", KeyOne));
        var envelope = protector.Protect("JBSWY3DPEHPK3PXP");

        var parts = envelope.Split('.');
        var flipped = Convert.FromBase64String(parts[2]);
        flipped[0] ^= 0xFF;
        parts[2] = Convert.ToBase64String(flipped);

        // Authenticated encryption: a modified ciphertext fails outright rather than producing a
        // plausible secret that would silently reject every code the user types.
        Assert.Null(protector.Unprotect(string.Join('.', parts)));
    }

    [Fact]
    public void A_value_under_a_key_this_deployment_no_longer_holds_reads_as_absent()
    {
        var envelope = Protector("k1", ("k1", KeyOne)).Protect("JBSWY3DPEHPK3PXP");

        Assert.Null(Protector("k2", ("k2", KeyTwo)).Unprotect(envelope));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-envelope")]
    [InlineData("k1.only.three")]
    public void A_malformed_envelope_reads_as_absent(string? envelope)
        => Assert.Null(Protector("k1", ("k1", KeyOne)).Unprotect(envelope));

    [Fact]
    public void A_missing_current_key_fails_loudly_rather_than_writing_something_unreadable()
    {
        var protector = Protector("k9", ("k1", KeyOne));

        var exception = Assert.Throws<InvalidOperationException>(() => protector.Protect("secret"));
        Assert.Contains("Auth:Encryption:CurrentKeyId", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("bm90LWJhc2U2NC1sZW5ndGg=")]
    public void A_key_that_is_not_thirty_two_bytes_is_not_a_key(string encoded)
    {
        var options = new EncryptionOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string>(StringComparer.Ordinal) { ["k1"] = encoded },
        };

        Assert.False(options.TryGetKeyBytes("k1", out _));
    }
}

/// <summary>The salted hashes behind one-time codes and refresh tokens.</summary>
public sealed class SecretHasherTests
{
    [Fact]
    public void The_same_code_to_two_destinations_hashes_differently()
    {
        // Otherwise anyone who could read the table could find every account currently holding a
        // given six-digit code.
        Assert.NotEqual(
            SecretHasher.Hash("123456", "+919876543210"),
            SecretHasher.Hash("123456", "+919000000000"));
    }

    [Fact]
    public void A_code_verifies_against_its_own_destination_only()
    {
        var hash = SecretHasher.Hash("123456", "+919876543210");

        Assert.True(SecretHasher.Verify("123456", "+919876543210", hash));
        Assert.False(SecretHasher.Verify("123456", "+919000000000", hash));
        Assert.False(SecretHasher.Verify("654321", "+919876543210", hash));
    }

    [Fact]
    public void The_salt_and_the_secret_cannot_be_confused_for_one_another()
    {
        // A hash over a plain concatenation would make ("ab", "cd") and ("a", "bcd") collide.
        Assert.NotEqual(SecretHasher.Hash("cd", "ab"), SecretHasher.Hash("bcd", "a"));
    }

    [Fact]
    public void A_numeric_code_is_the_requested_length_and_all_digits()
    {
        for (var digits = 4; digits <= 10; digits++)
        {
            var code = SecretHasher.NewNumericCode(digits);

            Assert.Equal(digits, code.Length);
            Assert.All(code, character => Assert.True(char.IsAsciiDigit(character)));
        }
    }

    [Fact]
    public void An_opaque_token_is_url_safe_and_does_not_repeat()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => SecretHasher.NewOpaqueToken()).ToList();

        Assert.Distinct(tokens);
        Assert.All(tokens, token => Assert.DoesNotContain('+', token));
        Assert.All(tokens, token => Assert.DoesNotContain('/', token));
        Assert.All(tokens, token => Assert.DoesNotContain('=', token));
    }
}
