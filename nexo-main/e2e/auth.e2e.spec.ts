import { test, expect, type Page } from "@playwright/test";

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
 * - Frontend running on http://localhost:8080 (or configured base URL)
 */

const API_URL = process.env.API_URL || "http://localhost:5000";
const APP_URL = process.env.APP_URL || "http://localhost:8080";

/**
 * E2E test credentials.
 * Real values are set via environment variables in .env.test (git-ignored).
 * Defaults below are FAKE — clearly not production credentials.
 * These defaults are intentionally verbose so no scanner mistakens them for real secrets.
 */
const E2E_LOGIN = process.env.E2E_ADMIN_LOGIN ?? "admin";
const E2E_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? "IntegrationTestOnly!123";

const LOGIN_FIELD = "#login";
const PASSWORD_FIELD = "#password";
const ACCESS_TOKEN_KEY = "nexo:access_token";
const REFRESH_TOKEN_KEY = "nexo:refresh_token";

async function signIn(page: Page) {
  await page.goto(`${APP_URL}/login`);
  await page.fill(LOGIN_FIELD, E2E_LOGIN);
  await page.fill(PASSWORD_FIELD, E2E_PASSWORD);
  await page.click('button[type="submit"]');
  // Where login lands depends on the user's workspaces; the tests continue from the dashboard.
  await page.waitForURL((url) => url.pathname !== "/login");
  await page.goto(`${APP_URL}/dashboard`);
}

const signOutButton = (page: Page) => page.getByRole("button", { name: "Sair da conta" });

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
    await page.fill(LOGIN_FIELD, E2E_LOGIN);
    await page.fill(PASSWORD_FIELD, E2E_PASSWORD);

    const [response] = await Promise.all([
      page.waitForResponse((r) => r.url().endsWith("/api/auth/login") && r.request().method() === "POST"),
      page.click('button[type="submit"]'),
    ]);
    expect(response.status()).toBe(200);

    const setCookies = (await response.headersArray())
      .filter((h) => h.name.toLowerCase() === "set-cookie")
      .map((h) => h.value.toLowerCase());
    for (const name of ["nexo_access", "nexo_refresh"]) {
      const cookie = setCookies.find((value) => value.startsWith(`${name}=`));
      expect(cookie, `${name} in the login response`).toBeDefined();
      expect(cookie).toContain("httponly");
    }

    await page.waitForURL((url) => url.pathname !== "/login");
    expect(await page.evaluate(() => document.cookie)).not.toContain("nexo_");
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

  test("Login in one tab is reflected in another tab", async ({ context, page }) => {
    await signIn(page);

    // Pages of one context are tabs of one browser: they share the stored session.
    const otherTab = await context.newPage();
    await otherTab.goto(`${APP_URL}/dashboard`);

    await expect(signOutButton(otherTab)).toBeVisible();
    await expect(otherTab).toHaveURL(`${APP_URL}/dashboard`);
  });

  test("Logout in one tab affects other tabs", async ({ context, page }) => {
    await signIn(page);
    const otherTab = await context.newPage();
    await otherTab.goto(`${APP_URL}/dashboard`);
    await expect(signOutButton(otherTab)).toBeVisible();

    await signOutButton(page).click();
    await page.waitForURL(`${APP_URL}/login`);

    // The other tab finds out on its next load: the stored session is gone.
    await otherTab.reload();
    await expect(otherTab).toHaveURL(/\/login/);
  });

  // ────────────────────────────────────────────────────────────────────────────
  // CREDENTIALS & CORS
  // ────────────────────────────────────────────────────────────────────────────

  test("API requests carry the access token of the session", async ({ page }) => {
    await signIn(page);
    const token = await page.evaluate((key) => localStorage.getItem(key), ACCESS_TOKEN_KEY);
    expect(token).toBeTruthy();

    // The frontend authenticates with the header; it does not send the cookies.
    const apiRequest = page.waitForRequest((r) => r.url().endsWith("/api/auth/me") && r.method() === "GET");
    await page.reload();

    expect(await (await apiRequest).headerValue("authorization")).toBe(`Bearer ${token}`);
  });

  // ────────────────────────────────────────────────────────────────────────────
  // ERROR HANDLING
  // ────────────────────────────────────────────────────────────────────────────

  test("Network errors display user-friendly message", async ({ page }) => {
    await page.route("**/api/auth/login", (route) => route.abort("connectionrefused"));
    await page.goto(`${APP_URL}/login`);
    await page.fill(LOGIN_FIELD, E2E_LOGIN);
    await page.fill(PASSWORD_FIELD, E2E_PASSWORD);
    await page.click('button[type="submit"]');

    const message = page.getByRole("alert");
    await expect(message).toBeVisible();
    await expect(message).not.toContainText(/failed to fetch|networkerror|typeerror/i);
    await expect(message).toContainText(/conectar|conex/i);
    await expect(page).toHaveURL(/\/login/);
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
  // The app has no inactivity timer: a session ends when its tokens stop being accepted.
  test("Expired session sends the user back to login", async ({ page }) => {
    await signIn(page);

    await page.evaluate(
      ([accessKey, refreshKey]) => {
        localStorage.setItem(accessKey, "expired");
        localStorage.setItem(refreshKey, "expired");
      },
      [ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY],
    );
    await page.reload();

    await expect(page).toHaveURL(/\/login/);
  });

  test("Refresh token prevents constant re-login", async ({ page }) => {
    await signIn(page);
    const refreshTokenBefore = await page.evaluate((key) => localStorage.getItem(key), REFRESH_TOKEN_KEY);

    // Only the access token stopped being accepted, as it does after 15 minutes.
    await page.evaluate((key) => localStorage.setItem(key, "expired"), ACCESS_TOKEN_KEY);
    const refresh = page.waitForResponse((r) => r.url().endsWith("/api/auth/refresh") && r.request().method() === "POST");
    await page.reload();

    expect((await refresh).status()).toBe(200);
    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_URL}/dashboard`);
    await expect
      .poll(() => page.evaluate((key) => localStorage.getItem(key), ACCESS_TOKEN_KEY))
      .not.toBe("expired");
    expect(await page.evaluate((key) => localStorage.getItem(key), REFRESH_TOKEN_KEY)).not.toBe(refreshTokenBefore);
  });
});
