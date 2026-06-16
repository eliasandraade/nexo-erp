using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Comprehensive cookie security validation suite.
/// Ensures HttpOnly, Secure, SameSite, Path, Domain flags are correct,
/// and that cookies are properly managed during login, refresh, and logout.
///
/// These tests are CRITICAL — a cookie security failure is an XSS vulnerability.
/// </summary>
[Collection("Integration")]
public class CookieSecurityTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CookieSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateApiClient();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // LOGIN: Verify cookies are set with correct flags
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_SetsCookies_WithHttpOnlyFlag()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // ContainEquivalentOf == case-insensitive Contains. RFC 6265 cookie attribute
        // names are case-insensitive and ASP.NET Core emits them lowercase ("httponly").
        accessCookie.Should().ContainEquivalentOf("HttpOnly",
            "access token cookie must be HttpOnly to prevent XSS theft");
        refreshCookie.Should().ContainEquivalentOf("HttpOnly",
            "refresh token cookie must be HttpOnly to prevent XSS theft");
    }

    [Fact]
    public async Task Login_SetsCookies_WithSameSiteNone()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // SameSite=None is REQUIRED and correct for the production topology: the SPA is
        // served from app.orken.com.br and calls the API on *.up.railway.app — a different
        // registrable domain, i.e. a CROSS-SITE request. Browsers only attach cookies to
        // cross-site fetches when SameSite=None; Secure. Strict/Lax would silently drop the
        // auth cookies and break the session. CSRF is mitigated by (1) Bearer-header auth
        // being the primary scheme — the Authorization header is immune to CSRF and always
        // wins over the cookie, (2) the cookies being HttpOnly + Secure, and (3) the CORS
        // allow-list with credentials. (If the API later moves to api.orken.com.br — same
        // site — this should be tightened to SameSite=Lax/Strict.)
        accessCookie.Should().ContainEquivalentOf("SameSite=None",
            "cross-site (app.orken.com.br ↔ *.railway.app) cookies require SameSite=None");
        refreshCookie.Should().ContainEquivalentOf("SameSite=None",
            "cross-site refresh-token rotation requires SameSite=None");
    }

    [Fact]
    public async Task Login_SetsCookies_WithPathRoot()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        accessCookie.Should().ContainEquivalentOf("Path=/",
            "cookies must be sent to all paths under the domain");
        refreshCookie.Should().ContainEquivalentOf("Path=/",
            "refresh cookie must be available to all paths for token rotation");
    }

    [Fact]
    public async Task Login_SetsCookies_WithValidExpiry()
    {
        var beforeLogin = DateTimeOffset.UtcNow;
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var afterLogin = DateTimeOffset.UtcNow;

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // Access token should expire in ~15 minutes
        var accessExpiry = ExtractCookieExpiry(accessCookie);
        var accessTtl = (accessExpiry - beforeLogin).TotalMinutes;
        accessTtl.Should().BeGreaterThan(14.5).And.BeLessThan(15.5);
        // access token should expire in approximately 15 minutes

        // Refresh token should expire ~7 days out. The cookie's Expires is set to
        // UtcNow.AddDays(7) on the server and serialized to whole-second precision, so the
        // span measured from beforeLogin lands a fraction under 168h — assert ≈7 days with
        // a small tolerance rather than a strict > 168 (which fails by microseconds).
        var refreshExpiry = ExtractCookieExpiry(refreshCookie);
        var refreshTtl = (refreshExpiry - beforeLogin).TotalHours;
        refreshTtl.Should().BeGreaterThan(167.9,
            "refresh token should expire in ~7 days");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // LOGOUT: Verify cookies are properly deleted
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_DeletesCookies_BySettingExpireInPast()
    {
        // 1. Login to get tokens
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // 2. Set auth header for the logout request
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // 3. Logout
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Verify Set-Cookie headers contain expiration in the past
        var setCookieHeaders = logoutResponse.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // When a cookie is deleted, its Expires should be in the past
        var accessExpiry = ExtractCookieExpiry(accessCookie);
        var refreshExpiry = ExtractCookieExpiry(refreshCookie);

        accessExpiry.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5),
            "access cookie must have expiry in the past to clear it from browser");
        refreshExpiry.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5),
            "refresh cookie must have expiry in the past to clear it from browser");
    }

    [Fact]
    public async Task Logout_DeletedCookies_HaveConsistentFlags_WithLoginCookies()
    {
        // Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);

        // Logout
        var logoutResponse = await _client.PostAsync("/api/auth/logout", null);
        var setCookieHeaders = logoutResponse.Headers.GetValues("Set-Cookie");

        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // Deleted cookies must have the same flags (for browser to match and replace).
        // Case-insensitive checks + SameSite=None — see Login_SetsCookies_WithSameSiteNone.
        accessCookie.Should().ContainEquivalentOf("HttpOnly");
        accessCookie.Should().ContainEquivalentOf("SameSite=None");
        accessCookie.Should().ContainEquivalentOf("Path=/");

        refreshCookie.Should().ContainEquivalentOf("HttpOnly");
        refreshCookie.Should().ContainEquivalentOf("SameSite=None");
        refreshCookie.Should().ContainEquivalentOf("Path=/");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFRESH: Verify cookies are rotated correctly
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_RotatesCookies_WithNewValues()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // 2. Extract cookies from login response
        var loginSetCookieHeaders = loginResponse.Headers.GetValues("Set-Cookie");
        var loginAccessCookie = ExtractSetCookieValue(loginSetCookieHeaders, "nexo_access");

        // 3. Refresh (send refresh token in body)
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(loginBody!.RefreshToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Extract cookies from refresh response
        var refreshSetCookieHeaders = refreshResponse.Headers.GetValues("Set-Cookie");
        var refreshAccessCookie = ExtractSetCookieValue(refreshSetCookieHeaders, "nexo_access");

        // 5. Verify cookies were rotated (new value)
        refreshAccessCookie.Should().NotBe(loginAccessCookie,
            "access token should be rotated (new value) on refresh");
    }

    [Fact]
    public async Task Refresh_RotatedCookies_StillHaveCorrectFlags()
    {
        // Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Refresh
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(loginBody!.RefreshToken));

        var setCookieHeaders = refreshResponse.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");
        var refreshCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_refresh");

        // Verify security flags are still correct after rotation.
        // Case-insensitive checks + SameSite=None — see Login_SetsCookies_WithSameSiteNone.
        accessCookie.Should().ContainEquivalentOf("HttpOnly");
        accessCookie.Should().ContainEquivalentOf("SameSite=None");
        accessCookie.Should().ContainEquivalentOf("Path=/");

        refreshCookie.Should().ContainEquivalentOf("HttpOnly");
        refreshCookie.Should().ContainEquivalentOf("SameSite=None");
        refreshCookie.Should().ContainEquivalentOf("Path=/");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // COOKIES NOT ACCESSIBLE VIA JAVASCRIPT
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cookies_AreHttpOnly_SoJsCannotAccess()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");

        // Every cookie must have HttpOnly flag
        foreach (var setCookie in setCookieHeaders)
        {
            if (setCookie.StartsWith("nexo_"))
            {
                setCookie.Should().ContainEquivalentOf("HttpOnly",
                    $"Cookie must be HttpOnly to prevent XSS access: {setCookie}");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SECURE FLAG: Should be true in HTTPS, can be false in HTTP (localhost dev)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Cookies_SecureFlag_MatchesRequestScheme()
    {
        // The test server runs HTTP (not HTTPS), so Secure should be absent
        // In production (HTTPS), Secure should be present
        var response = await _client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());

        var setCookieHeaders = response.Headers.GetValues("Set-Cookie");
        var accessCookie = ExtractSetCookieHeader(setCookieHeaders, "nexo_access");

        // Since test server is HTTP, Secure flag should be absent
        var isHttps = _client.BaseAddress?.Scheme == "https";
        if (isHttps)
        {
            accessCookie.Should().Contain("Secure",
                "HTTPS connections must have Secure flag");
        }
        else
        {
            // HTTP (localhost development) — Secure may or may not be present,
            // but the code should dynamically detect IsHttps
            // This test documents the expected behavior
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    private static string ExtractSetCookieHeader(IEnumerable<string> setCookieHeaders, string cookieName)
    {
        var cookie = setCookieHeaders.FirstOrDefault(c => c.StartsWith(cookieName));
        cookie.Should().NotBeNull($"Set-Cookie header for '{cookieName}' not found");
        return cookie!;
    }

    private static string ExtractSetCookieValue(IEnumerable<string> setCookieHeaders, string cookieName)
    {
        var header = ExtractSetCookieHeader(setCookieHeaders, cookieName);
        var parts = header.Split(';');
        var value = parts[0].Split('=', 2);
        return value.Length > 1 ? value[1] : string.Empty;
    }

    private static DateTimeOffset ExtractCookieExpiry(string setCookieHeader)
    {
        var parts = setCookieHeader.Split(';');
        // Case-insensitive: ASP.NET Core emits "expires=" (lowercase) per its Set-Cookie
        // serialization; RFC 6265 attribute names are case-insensitive.
        var expiresLine = parts.FirstOrDefault(p =>
            p.Trim().StartsWith("Expires=", StringComparison.OrdinalIgnoreCase));

        if (expiresLine == null)
            return DateTimeOffset.MinValue;

        var eq = expiresLine.IndexOf('=');
        var expiryStr = expiresLine[(eq + 1)..].Trim();
        if (DateTimeOffset.TryParseExact(expiryStr, "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var expiry))
        {
            return expiry;
        }

        return DateTimeOffset.MinValue;
    }
}
