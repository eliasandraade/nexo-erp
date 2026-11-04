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
    public void A_code_read_before_the_step_boundary_signs_in_once_and_a_recomputed_one_is_a_new_code()
    {
        // Mesma sequência de MfaTests.Sign_in_takes_a_code_once_the_second_step_is_on (issue #82), com relógio fixo:
        // o código do próximo passo é lido no último milissegundo de um passo de 30 s e conferido já no passo seguinte.
        var lastInstant = new DateTime(2026, 10, 30, 12, 0, 29, 999, DateTimeKind.Utc);
        var step = Totp.StepAt(lastInstant);
        Totp.StepAt(lastInstant.AddMilliseconds(1)).Should().Be(step + 1, "a fronteira do passo cai entre as duas leituras do relógio");

        var enabledAt = step - 1;                                     // passo consumido ao ligar a verificação
        var code = Totp.Code(RfcSecret, step + 1);                    // CodeAt(secret, 1), lido antes da fronteira
        var signIn = lastInstant.AddSeconds(1);                       // o servidor confere no passo seguinte

        var used = Totp.Verify(RfcSecret, code, signIn, enabledAt);
        used.Should().Be(step + 1, "o código continua dentro da janela de um passo");
        Totp.Verify(RfcSecret, code, signIn.AddSeconds(2), used).Should().BeNull("o mesmo código só vale uma vez");

        // O teste antigo recalculava CodeAt(secret, 1) depois da fronteira. O resultado é o código de outro passo,
        // novo e válido, e não uma reutilização: por isso o login voltava 200 em vez de 401.
        var recomputed = Totp.Code(RfcSecret, Totp.StepAt(signIn) + 1);
        recomputed.Should().NotBe(code);
        Totp.Verify(RfcSecret, recomputed, signIn.AddSeconds(2), used).Should().Be(step + 2);
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

    [Theory]
    [InlineData("123456", true)]
    [InlineData("123 456", true)]
    [InlineData("123-456", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("2a345-67b89", false)]   // a recovery code with exactly six digits is still a recovery code
    [InlineData("abcde-fghjk", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void An_authenticator_code_is_six_digits_and_nothing_else(string? input, bool expected)
        => Totp.LooksLikeCode(input).Should().Be(expected);
}
