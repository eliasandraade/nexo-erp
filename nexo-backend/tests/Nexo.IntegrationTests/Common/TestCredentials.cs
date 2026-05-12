namespace Nexo.IntegrationTests.Common;

/// <summary>
/// Centralized, obviously-fake test credentials for integration tests.
///
/// SECURITY NOTICE:
///   - ALL values in this file are fake and exist ONLY for automated testing.
///   - No production credential should ever appear here.
///   - Domains use the RFC 2606 / IANA reserved TLDs (.test, .local, .example)
///     which are guaranteed to never resolve in public DNS.
///   - Passwords are deliberately verbose and un-guessable in production context.
///
/// These constants are the SINGLE SOURCE OF TRUTH for test credentials.
/// Never inline credentials directly in test files — always use this class.
/// </summary>
public static class TestCredentials
{
    // ── Default tenant admin (seeded by DataSeeder in Testing environment) ──────

    /// <summary>Login for the default seeded admin user.</summary>
    public const string AdminLogin = "admin";

    /// <summary>
    /// Password for the default seeded admin.
    /// Override applied via Seed:AdminPassword in TestWebApplicationFactory.
    /// FAKE — FOR INTEGRATION TESTS ONLY.
    /// </summary>
    public const string AdminPassword = "IntegrationTestOnly!123";

    // ── Platform superuser (seeded by DataSeeder in Testing environment) ────────

    /// <summary>Email for the platform superuser.</summary>
    public const string PlatformEmail = "platform-test@nexo.test";

    /// <summary>
    /// Password for the platform superuser.
    /// Override applied via Seed:PlatformPassword in TestWebApplicationFactory.
    /// FAKE — FOR INTEGRATION TESTS ONLY.
    /// </summary>
    public const string PlatformPassword = "FakePlatformPass!999";

    // ── Cross-tenant test users (seeded on-the-fly in individual tests) ─────────

    /// <summary>Password used when seeding cross-tenant manager users in isolation tests.</summary>
    public const string TenantBManagerPassword = "FakeTenantBManager!456";

    /// <summary>Password used when seeding same-tenant manager users for verify-manager tests.</summary>
    public const string SameTenantManagerPassword = "FakeSameTenantManager!789";

    // ── Email domain constants (IANA-reserved, never resolve in DNS) ─────────────

    /// <summary>Primary test domain. Never resolves in DNS per RFC 2606.</summary>
    public const string TestDomain = "nexo.test";

    /// <summary>Secondary test domain for cross-tenant scenarios.</summary>
    public const string TestLocalDomain = "test.local";

    // ── Fake token fixtures ───────────────────────────────────────────────────────

    /// <summary>
    /// A clearly fake, syntactically 3-part JWT string used to test rejection of fabricated tokens.
    ///
    /// This is NOT a real JWT. It has the 3-segment structure (header.payload.signature)
    /// that the format-validation tests expect, but:
    ///   - The header decodes to a valid alg:HS256 marker (safe to show)
    ///   - The payload contains FAKE_TEST_ONLY making intent obvious to any scanner
    ///   - The signature is INVALID — server will always reject it
    ///
    /// FAKE — FOR INTEGRATION TESTS ONLY. Never generated from a real secret.
    /// </summary>
    public const string FakeJwtToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" +   // {"alg":"HS256","typ":"JWT"}
        ".FAKE_TEST_ONLY_NOT_A_REAL_PAYLOAD_00000" + // clearly not real base64url claims
        ".INVALID_SIGNATURE_WILL_NOT_VERIFY_EVER";   // invalid sig — always rejected

    /// <summary>
    /// A clearly invalid (non-JWT) token string for testing malformed-token rejection.
    /// </summary>
    public const string MalformedToken = "not-a-real-jwt-test-fixture";

    /// <summary>
    /// A plausible-looking but invalid refresh token string.
    /// </summary>
    public const string InvalidRefreshToken = "invalid-refresh-token-test-fixture";

    // ── Payload builder helpers ───────────────────────────────────────────────────

    /// <summary>Returns an anonymous login payload for use with PostAsJsonAsync.</summary>
    public static object LoginPayload(string login, string password)
        => new { login, password };

    /// <summary>Returns the default admin login payload.</summary>
    public static object AdminLoginPayload()
        => LoginPayload(AdminLogin, AdminPassword);

    /// <summary>Returns the platform superuser login payload.</summary>
    public static object PlatformLoginPayload()
        => LoginPayload(PlatformEmail, PlatformPassword);

    /// <summary>Returns a refresh payload for use with PostAsJsonAsync.</summary>
    public static object RefreshPayload(string refreshToken)
        => new { refreshToken };
}
