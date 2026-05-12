/**
 * Centralized test credential constants for frontend tests.
 *
 * SECURITY NOTICE:
 *   - ALL values here are fake and exist ONLY for automated testing.
 *   - No production credential should ever appear here.
 *   - Domains use the IANA reserved .test TLD (RFC 2606), which
 *     is guaranteed to never resolve in public DNS.
 *   - Passwords are deliberately verbose and un-guessable in production.
 *
 * These constants are the SINGLE SOURCE OF TRUTH for all frontend test credentials.
 * Never inline credentials directly in test files — always import from here.
 */
export const TEST_CREDENTIALS = {
  // Default seeded admin (matches DataSeeder Testing override)
  adminLogin: "admin",
  adminPassword: "IntegrationTestOnly!123",

  // Platform superuser (Testing environment only)
  platformEmail: "platform-test@nexo.test",
  platformPassword: "FakePlatformPass!999",
} as const;

/**
 * A clearly fake, syntactically 3-part JWT string.
 *
 * Used in tests that need a structurally valid JWT format (3 dot-separated segments)
 * but must be rejected by the server (invalid signature / clearly fake payload).
 *
 * NOT a real JWT. FAKE — FOR TESTS ONLY. Never generated from a real secret.
 */
export const FAKE_JWT =
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9" + // header: {"alg":"HS256","typ":"JWT"}
  ".FAKE_TEST_ONLY_NOT_A_REAL_PAYLOAD_00000" + // clearly not real base64url claims
  ".INVALID_SIGNATURE_WILL_NOT_VERIFY_EVER"; // invalid sig — always rejected by server

/**
 * A completely malformed (non-JWT) token string for testing rejection
 * of tokens with wrong structure.
 */
export const MALFORMED_TOKEN = "not-a-real-jwt-test-fixture";
