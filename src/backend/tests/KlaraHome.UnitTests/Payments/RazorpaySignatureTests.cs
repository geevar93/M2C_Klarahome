using KlaraHome.Modules.Payments.Infrastructure.Gateway.Razorpay;

namespace KlaraHome.UnitTests.Payments;

/// <summary>
/// The two things this platform gets wrong exactly once if it gets them wrong: the paise
/// conversion, and the HMAC over a webhook body.
/// </summary>
/// <remarks>
/// Tested while writing them under the build sprint's rule 1. Both are pure, both have a right
/// answer independent of everything else, and both fail silently — a rounding rule that is out by
/// one paise charges a customer the wrong amount without erroring, and a signature check with an
/// off-by-one comparison accepts a forged webhook without erroring either.
/// </remarks>
public sealed class RazorpaySignatureTests
{
    private const string Secret = "webhook-secret";

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(1, 100L)]
    [InlineData(2499.00, 249900L)]
    [InlineData(0.01, 1L)]
    [InlineData(1234.5678, 123457L)]
    public void Rupees_become_integer_paise(decimal rupees, long expected)
        => Assert.Equal(expected, RazorpaySignature.ToMinorUnits(rupees));

    /// <summary>
    /// Half away from zero, not banker's rounding.
    /// </summary>
    /// <remarks>
    /// .NET rounds midpoints to even by default, which is right for a long series of independent
    /// figures and wrong for a single amount a customer has agreed to: 1.005 must become 101 paise,
    /// because 1.01 is what the shopper was shown on the review screen.
    /// </remarks>
    [Theory]
    [InlineData(1.005, 101L)]
    [InlineData(1.015, 102L)]
    [InlineData(2.005, 201L)]
    public void Midpoints_round_away_from_zero_not_to_even(decimal rupees, long expected)
        => Assert.Equal(expected, RazorpaySignature.ToMinorUnits(rupees));

    [Fact]
    public void Paise_come_back_as_rupees()
    {
        Assert.Equal(24.99m, RazorpaySignature.FromMinorUnits(2499L));
        Assert.Equal(0m, RazorpaySignature.FromMinorUnits(0L));
        Assert.Equal(1m, RazorpaySignature.FromMinorUnits(100L));
    }

    [Fact]
    public void A_whole_rupee_amount_survives_a_round_trip()
    {
        for (var paise = 0L; paise < 1000L; paise++)
        {
            Assert.Equal(paise, RazorpaySignature.ToMinorUnits(RazorpaySignature.FromMinorUnits(paise)));
        }
    }

    [Fact]
    public void A_signature_computed_over_the_body_verifies()
    {
        const string body = """{"event":"payment.captured","payload":{}}""";

        Assert.True(RazorpaySignature.Verify(body, RazorpaySignature.Compute(body, Secret), Secret));
    }

    [Fact]
    public void A_signature_over_a_different_body_does_not_verify()
    {
        var signature = RazorpaySignature.Compute("""{"event":"payment.captured"}""", Secret);

        Assert.False(RazorpaySignature.Verify("""{"event":"payment.failed"}""", signature, Secret));
    }

    /// <summary>
    /// A single altered byte fails, which is the property the whole webhook contract rests on.
    /// </summary>
    [Fact]
    public void One_altered_character_fails_verification()
    {
        const string body = """{"event":"payment.captured","payload":{"amount":100}}""";
        var signature = RazorpaySignature.Compute(body, Secret);
        var tampered = body.Replace("100", "999", StringComparison.Ordinal);

        Assert.False(RazorpaySignature.Verify(tampered, signature, Secret));
    }

    [Fact]
    public void A_signature_under_a_different_secret_does_not_verify()
    {
        const string body = """{"event":"order.paid"}""";

        Assert.False(RazorpaySignature.Verify(body, RazorpaySignature.Compute(body, "other"), Secret));
    }

    /// <summary>
    /// No secret verifies nothing, ever.
    /// </summary>
    /// <remarks>
    /// The failure mode this guards against is the worst one available: a deployment with no webhook
    /// secret configured accepting every webhook it is sent, because there was nothing to compare
    /// against. An unconfigured deployment must be the <em>most</em> suspicious, not the least.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unconfigured_secret_verifies_nothing(string secret)
    {
        const string body = """{"event":"payment.captured"}""";

        Assert.False(RazorpaySignature.Verify(body, RazorpaySignature.Compute(body, secret), secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-signature")]
    public void A_missing_or_malformed_signature_does_not_verify(string? signature)
        => Assert.False(RazorpaySignature.Verify("""{"event":"payment.captured"}""", signature, Secret));

    [Fact]
    public void The_digest_is_lowercase_hex_of_the_expected_length()
    {
        var digest = RazorpaySignature.Compute("payload", Secret);

        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToLowerInvariant(), digest);
        Assert.All(digest, character => Assert.Contains(character, "0123456789abcdef"));
    }
}
