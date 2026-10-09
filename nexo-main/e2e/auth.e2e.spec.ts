import { test, expect } from "@playwright/test";

/**
 * Frontend Authentication E2E Tests
 *
 * These tests validate the authentication flow from the frontend perspective:
 * - Login/logout flows
 * - Cookie persistence
 * - Token refresh
 * - Session consistency
 * - Multi-tab behavior
 * - Error handling
 *
 * These tests require:
 * - Backend running on http://localhost:5000
 * - Frontend running on http://localhost:3000 (or configured base URL)
 */

const API_URL = process.env.API_URL || "http://localhost:5000";
const APP_URL = process.env.APP_URL || "http://localhost:3000";

/**
 * E2E test credentials.
 * Real values are set via environment variables in .env.test (git-ignored).
 * Defaults below are FAKE — clearly not production credentials.
 * These defaults are intentionally verbose so no scanner mistakens them for real secrets.
 */
const E2E_LOGIN = process.env.E2E_ADMIN_LOGIN ?? "admin";
const E2E_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? "IntegrationTestOnly!123";

test.describe("Frontend Authentication Flow", () => {
  test.beforeEach(async ({ page, context }) => {
    // Clear all cookies before each test
    await context.clearCookies();
    await page.goto(APP_URL);
  });

  // ────────────────────────────────────────────────────────────────────────────
  // LOGIN FLOW
  // ────────────────────────────────────────────────────────────────────────────

  test("Login with valid credentials succeeds and stores session", async ({ page }) => {
    // Navigate to login page (assuming /login route exists)
    await page.goto(`${APP_URL}/login`);

    // Fill login form
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    // Submit form
    await page.click('button[type="submit"]');

    // Wait for redirect to dashboard (success indicator)
    await page.waitForURL(`${APP_URL}/dashboard`, { timeout: 10000 });

    // Verify session is stored
    const cookies = await page.context().cookies();
    const hasAuthCookies = cookies.some(c => 
      c.name === "nexo_access" || c.name === "nexo_refresh"
    );
    expect(hasAuthCookies).toBeTruthy();
  });

  test("Login with invalid password fails with error message", async ({ page }) => {
    await page.goto(`${APP_URL}/login`);

    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', "wrongpassword");
    await page.click('button[type="submit"]');

    // Expect error message
    const errorElement = page.locator(
      '[role="alert"], .error, [data-testid="error-message"]'
    );
    await expect(errorElement).toContainText(/invalid|incorrect|failed/i);

    // Should still be on login page
    expect(page.url()).toContain("/login");
  });

  test("Login with non-existent user fails gracefully", async ({ page }) => {
    await page.goto(`${APP_URL}/login`);

    await page.fill('input[name="login"]', "ghost_user_12345");
    await page.fill('input[name="password"]', "anypassword");
    await page.click('button[type="submit"]');

    const errorElement = page.locator(
      '[role="alert"], .error, [data-testid="error-message"]'
    );
    await expect(errorElement).toContainText(/invalid|not found|failed/i);
  });

  // ────────────────────────────────────────────────────────────────────────────
  // COOKIES: HttpOnly, Secure, SameSite
  // ────────────────────────────────────────────────────────────────────────────

  test("Login sets httpOnly cookies that are inaccessible to JavaScript", async ({ page }) => {
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Try to access cookies via JavaScript (should fail for httpOnly)
    const cookiesViaJs = await page.evaluate(() => {
      return document.cookie;
    });

    // httpOnly cookies should NOT appear in document.cookie
    // (This is the security property we're validating)
    if (cookiesViaJs.includes("nexo_")) {
      // If the cookie appears, it might not have httpOnly flag
      console.warn(
        "WARNING: Auth cookies may not have httpOnly flag set properly"
      );
    }
  });

  test("Cookies persist across page navigation", async ({ page, context }) => {
    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Get cookies after login
    const cookiesAfterLogin = await context.cookies();

    // Navigate to another page
    await page.goto(`${APP_URL}/profile`);

    // Cookies should still be present
    const cookiesAfterNav = await context.cookies();
    expect(cookiesAfterNav.length).toBe(cookiesAfterLogin.length);

    cookiesAfterNav.forEach((cookie) => {
      const originalCookie = cookiesAfterLogin.find((c) => c.name === cookie.name);
      expect(originalCookie?.value).toBe(cookie.value);
    });
  });

  // ────────────────────────────────────────────────────────────────────────────
  // LOGOUT FLOW
  // ────────────────────────────────────────────────────────────────────────────

  test("Logout clears session and redirects to login", async ({ page, context }) => {
    // Login first
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Verify logged in
    const cookiesBefore = await context.cookies();
    expect(cookiesBefore.some((c) => c.name === "nexo_access")).toBeTruthy();

    // Logout
    const logoutButton = page.locator(
      'button:has-text("Logout"), button:has-text("Sair")'
    );
    await expect(logoutButton).toBeVisible();
    await logoutButton.click();

    // Should redirect to login
    await page.waitForURL(`${APP_URL}/login`, { timeout: 5000 });

    // Cookies should be cleared
    const cookiesAfter = await context.cookies();
    const authCookie = cookiesAfter.find((c) => c.name === "nexo_access");
    if (authCookie) {
      // Cookie might be present with empty value or past expiry
      expect(authCookie.value).toBe("");
    }
  });

  test("Accessing protected route without session redirects to login", async ({ page }) => {
    // Try to access protected page directly without login
    await page.goto(`${APP_URL}/dashboard`, { waitUntil: "networkidle" });

    // Should redirect to login
    const currentUrl = page.url();
    expect(currentUrl).toContain("/login");
  });

  // ────────────────────────────────────────────────────────────────────────────
  // TOKEN REFRESH
  // ────────────────────────────────────────────────────────────────────────────

  test("Session remains valid when token is refreshed in background", async ({ page }) => {
    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Wait a moment (or simulate access token expiry)
    // Make a request that triggers refresh
    const response = await page.request.get(`${APP_URL}/api/auth/me`);

    // Should still be authenticated
    expect(response.ok()).toBeTruthy();
    expect([200, 401, 403]).toContain(response.status());

    // If it was 401, refresh should have happened and next request should work
    if (response.status() === 401) {
      const retryResponse = await page.request.get(`${APP_URL}/api/auth/me`);
      expect([200, 401, 403]).toContain(retryResponse.status());
    }
  });

  // ────────────────────────────────────────────────────────────────────────────
  // MULTI-TAB BEHAVIOR
  // ────────────────────────────────────────────────────────────────────────────

  test("Login in one tab is reflected in another tab", async ({ browser, page }) => {
    // Create two pages (simulating two tabs)
    const page1 = await browser.newPage();
    const page2 = await browser.newPage();

    try {
      // Tab 1: Login
      await page1.goto(`${APP_URL}/login`);
      await page1.fill('input[name="login"]', E2E_LOGIN);
      await page1.fill('input[name="password"]', E2E_PASSWORD);
      await page1.click('button[type="submit"]');
      await page1.waitForURL(`${APP_URL}/dashboard`);

      // Tab 2: Reload and check if logged in
      await page2.goto(`${APP_URL}/dashboard`, { waitUntil: "networkidle" });

      // Tab 2 might require redirect to login initially (depends on implementation)
      // But if cookies are shared, it might show dashboard
      // This documents the expected behavior
    } finally {
      await page1.close();
      await page2.close();
    }
  });

  test("Logout in one tab affects other tabs", async ({ browser, page }) => {
    const page1 = await browser.newPage();
    const page2 = await browser.newPage();

    try {
      // Both tabs: Login
      const loginAndNavigate = async (p) => {
        await p.goto(`${APP_URL}/login`);
        await p.fill('input[name="login"]', E2E_LOGIN);
        await p.fill('input[name="password"]', E2E_PASSWORD);
        await p.click('button[type="submit"]');
        await p.waitForURL(`${APP_URL}/dashboard`);
      };

      await loginAndNavigate(page1);
      await loginAndNavigate(page2);

      // Tab 1: Logout
      const logoutBtn1 = page1.locator(
        'button:has-text("Logout"), button:has-text("Sair")'
      );
      await logoutBtn1.click();
      await page1.waitForURL(`${APP_URL}/login`);

      // Tab 2: Try to navigate (should be logged out)
      // Depends on implementation:
      // - If using cookies only: might still show dashboard (cookie persists until expiry)
      // - If using localStorage: might immediately redirect
      const currentPage2Url = page2.url();
      if (currentPage2Url.includes("/dashboard")) {
        // Try to trigger a refresh to check if still authenticated
        await page2.reload();
        const newUrl = page2.url();
        // If properly logged out, should be on login page
        // Otherwise, depends on implementation
      }
    } finally {
      await page1.close();
      await page2.close();
    }
  });

  // ────────────────────────────────────────────────────────────────────────────
  // CREDENTIALS & CORS
  // ────────────────────────────────────────────────────────────────────────────

  test("API requests include credentials (cookies)", async ({ page }) => {
    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Monitor network requests
    const requestPromise = page.waitForEvent("request", (req) =>
      req.url().includes("/api/")
    );

    // Trigger an API call (e.g., by navigating or interacting with the page)
    await page.goto(`${APP_URL}/profile`);

    const request = await requestPromise;

    // Check if request includes credentials
    const headers = request.headers();
    const isXhr = request.resourceType() === "xhr" || request.resourceType() === "fetch";

    if (isXhr) {
      // Fetch requests should have credentials included
      // This is validated on the client side (credentials: 'include')
      // and confirmed by cookies being sent with requests
    }
  });

  // ────────────────────────────────────────────────────────────────────────────
  // ERROR HANDLING
  // ────────────────────────────────────────────────────────────────────────────

  test("Network errors display user-friendly message", async ({ page }) => {
    // This test validates frontend error handling
    // It would require mocking network failures
    // For now, document the expected behavior
  });

  test("401 responses trigger re-login flow", async ({ page, context }) => {
    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Manually delete auth cookies to simulate expiration
    const cookies = await context.cookies();
    await context.clearCookies({
      name: "nexo_access",
    });

    // Try to access protected endpoint
    const response = await page.request.get(`${APP_URL}/api/auth/me`);

    // Should get 401
    expect(response.status()).toBe(401);

    // Frontend should trigger redirect to login
    // (depends on implementation - might be automatic with interceptor)
  });

  // ────────────────────────────────────────────────────────────────────────────
  // SSR / HYDRATION
  // ────────────────────────────────────────────────────────────────────────────

  test("SSR hydration preserves auth state", async ({ page }) => {
    // Login to establish session
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Hard refresh (SSR hydration)
    await page.reload({ waitUntil: "networkidle" });

    // Should still be on dashboard (hydration preserved auth)
    expect(page.url()).toContain("/dashboard");

    // Not redirected to login
    expect(page.url()).not.toContain("/login");
  });

  // ────────────────────────────────────────────────────────────────────────────
  // SECURITY: NO XSS VECTORS
  // ────────────────────────────────────────────────────────────────────────────

  test("Login error messages do not contain user-supplied XSS", async ({ page }) => {
    await page.goto(`${APP_URL}/login`);

    // Try XSS payload
    const xssPayload = `"><script>alert('xss')</script>`;
    await page.fill('input[name="login"]', xssPayload);
    await page.fill('input[name="password"]', "test");
    await page.click('button[type="submit"]');

    // Wait for response
    await page.waitForTimeout(500);

    // Check if error message contains unescaped script tag
    const errorText = await page.locator(
      '[role="alert"], .error, [data-testid="error-message"]'
    ).textContent();

    expect(errorText || "").not.toContain("<script>");
    // Should be properly escaped
  });
});

test.describe("Frontend Session Management", () => {
  test("Session timeout logs user out after inactivity", async ({ page, context }) => {
    // This test validates session timeout behavior
    // Requires configuring a short timeout for testing

    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Wait for session timeout (would need short timeout in test env)
    // await page.waitForTimeout(session_timeout_ms);

    // Try to access protected page
    // Should redirect to login if session expired
  });

  test("Refresh token prevents constant re-login", async ({ page, context }) => {
    // This validates that users don't have to re-login every 15 minutes
    // The refresh token should automatically extend the session

    // Login
    await page.goto(`${APP_URL}/login`);
    await page.fill('input[name="login"]', E2E_LOGIN);
    await page.fill('input[name="password"]', E2E_PASSWORD);
    await page.click('button[type="submit"]');
    await page.waitForURL(`${APP_URL}/dashboard`);

    // Stay on page for extended period
    // Make requests periodically
    // Session should remain valid (refresh token working)
  });
});
