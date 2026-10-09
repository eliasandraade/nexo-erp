using FluentAssertions;
using Nexo.Infrastructure.Auth;
using Xunit;

namespace Nexo.UnitTests.Auth;

public class JwtSecretPolicyTests
{
    // Built at runtime: a realistic random secret, never a committed literal.
    private static readonly string RandomSecret = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")]
    public void Strict_environments_refuse_the_secrets_committed_to_the_repository(string env)
    {
        foreach (var committed in new[]
                 {
                     "CHANGE_THIS_IN_PRODUCTION_USE_A_LONG_RANDOM_STRING_AT_LEAST_32_CHARS",
                     "dev-secret-key-change-this-in-production-min-32-chars",
                     "test-secret-key-minimum-32-characters-long!",
                 })
            ((Action)(() => JwtSecretPolicy.EnsureSafe(committed, env)))
                .Should().Throw<InvalidOperationException>().WithMessage("Refusing to start*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-but-random-Q7x!")]                                   // < 32 chars
    [InlineData("my-own-placeholder-value-for-the-jwt-signing-key-2026")]   // placeholder marker
    [InlineData("please-CHANGEME-before-going-live-with-this-key-now")]     // placeholder marker
    [InlineData("abababababababababababababababababababab")]                // too little variety
    public void Strict_environments_refuse_missing_short_placeholder_or_low_entropy_secrets(string? secret)
        => ((Action)(() => JwtSecretPolicy.EnsureSafe(secret, "Production")))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void A_random_secret_is_accepted_in_production()
        => ((Action)(() => JwtSecretPolicy.EnsureSafe(RandomSecret, "Production"))).Should().NotThrow();

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Local_environments_keep_the_committed_placeholders_working(string env)
    {
        ((Action)(() => JwtSecretPolicy.EnsureSafe("dev-secret-key-change-this-in-production-min-32-chars", env))).Should().NotThrow();
        ((Action)(() => JwtSecretPolicy.EnsureSafe("test-secret-key-minimum-32-characters-long!", env))).Should().NotThrow();
        // …but even locally a secret must exist and be long enough.
        ((Action)(() => JwtSecretPolicy.EnsureSafe("too-short", env))).Should().Throw<InvalidOperationException>();
        ((Action)(() => JwtSecretPolicy.EnsureSafe(null, env))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_error_message_never_contains_the_secret()
    {
        const string secret = "dev-secret-key-change-this-in-production-min-32-chars";
        var ex = ((Action)(() => JwtSecretPolicy.EnsureSafe(secret, "Production")))
            .Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().NotContain(secret);
    }
}
