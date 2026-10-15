using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Auth;
using Xunit;

namespace Nexo.UnitTests.Auth;

public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]                                   // RFC 6238 appendix B (SHA-1), last 6 digits
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_test_vectors(long unixTime, string expected)
        => Totp.Code(RfcSecret, unixTime / 30).Should().Be(expected);

    [Fact]
    public void Base32_matches_the_rfc_example()
        => Totp.ToBase32(RfcSecret).Should().Be("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");

    [Fact]
    public void A_code_works_within_one_step_of_drift_and_only_once()
    {
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var step = Totp.StepAt(now);
        var code = Totp.Code(RfcSecret, step);

        Totp.Verify(RfcSecret, code, now, null).Should().Be(step);
        Totp.Verify(RfcSecret, code, now.AddSeconds(30), null).Should().Be(step, "the phone may be a step behind");
        Totp.Verify(RfcSecret, code, now.AddSeconds(90), null).Should().BeNull("too old");
        Totp.Verify(RfcSecret, code, now, step).Should().BeNull("already used");
        Totp.Verify(RfcSecret, "12 34 5", now, null).Should().BeNull();
        Totp.Verify(RfcSecret, $"{code[..3]} {code[3..]}", now, null).Should().Be(step, "spaces are ignored");
    }

    [Fact]
    public void Secrets_are_encrypted_and_tamper_evident()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = new string('k', 64) }).Build();
        var protector = new MfaSecretProtector(config);
        var secret = Totp.NewSecret();

        var owner = Guid.NewGuid();
        var stored = protector.Protect(secret, owner);
        stored.Should().StartWith("v1:").And.NotContain(Totp.ToBase32(secret));
        protector.Unprotect(stored, owner).Should().Equal(secret);
        protector.Unprotect(stored, Guid.NewGuid()).Should().BeNull("bound to its user");

        var raw = Convert.FromBase64String(stored[3..]);
        raw[^1] ^= 1;
        protector.Unprotect("v1:" + Convert.ToBase64String(raw), owner).Should().BeNull("tampered");
        protector.HashRecoveryCode("abcdefghjk").Should().HaveLength(64).And.NotBe(protector.HashRecoveryCode("abcdefghjm"));
    }
}
