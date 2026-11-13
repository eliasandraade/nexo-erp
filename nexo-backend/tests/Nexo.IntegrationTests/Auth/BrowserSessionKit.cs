using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// What a browser sends and receives in the cookie mode of the session, written out by hand: no cookie jar, so each test says
/// exactly which cookie, Origin and mode header go in a request, and reads the Set-Cookie exactly as the API wrote it.
/// </summary>
internal sealed class BrowserSessionKit
{
    public const string ModeHeader = "X-Orken-Session";
    public const string CookieName = "__Host-nexo_rt";
    public const string AppOrigin = "https://erp.orken.com.br";

    private readonly TestWebApplicationFactory _factory;
    private readonly WebApplicationFactory<Program> _host;

    public BrowserSessionKit(TestWebApplicationFactory factory, WebApplicationFactory<Program>? host = null)
    {
        _factory = factory;
        _host = host ?? factory;
    }

    public BrowserSessionKit On(WebApplicationFactory<Program> host) => new(_factory, host);

    public HttpClient Client() => _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? origin = AppOrigin,
        string? sessionCookie = null,
        string? bearer = null,
        string? mode = "cookie",
        string? rawCookies = null,
        string? clientIp = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (mode is not null) request.Headers.TryAddWithoutValidation(ModeHeader, mode);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (clientIp is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        var cookies = string.Join("; ", new[] { sessionCookie is null ? null : $"{CookieName}={sessionCookie}", rawCookies }.Where(c => c is not null));
        if (cookies.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookies);
        return await Client().SendAsync(request);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null, string? origin = AppOrigin, string? sessionCookie = null,
        string? bearer = null, string? mode = "cookie", string? rawCookies = null, string? clientIp = null)
        => SendAsync(HttpMethod.Post, path, body, origin, sessionCookie, bearer, mode, rawCookies, clientIp);

    public Task<HttpResponseMessage> RenewAsync(string sessionCookie, string? origin = AppOrigin, string? clientIp = null, string route = "/api/auth/refresh")
        => PostAsync(route, origin: origin, sessionCookie: sessionCookie, clientIp: clientIp);

    /// <summary>A sign-in in the cookie mode: the access token of the body and the value of the cookie.</summary>
    public async Task<CookieSession> SignInAsync(string login, string password, string route = "/api/auth/login")
    {
        var response = await PostAsync(route, TestCredentials.LoginPayload(login, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await SessionFromAsync(response);
    }

    public static async Task<CookieSession> SessionFromAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new CookieSession(body.GetProperty("accessToken").GetString()!, CookieValue(response));
    }

    /// <summary>A new company with its director: nothing else in the suite shares this session.</summary>
    public Task<SyntheticAccount> NewAccountAsync() => SyntheticTenant.CreateAsync(_factory, "varejo");

    /// <summary>A new platform administrator, straight in the database.</summary>
    public async Task<(string Email, string Password)> NewPlatformAdminAsync()
    {
        var email = $"ck-{Guid.NewGuid():N}"[..14] + "@platform.test";
        const string password = TestCredentials.PlatformPassword;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "super_admin"));
        await db.SaveChangesAsync();
        return (email, password);
    }

    // ── What the response says ─────────────────────────────────────────────────────────────────────────────────────────

    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    public static string CookieValue(HttpResponseMessage response)
    {
        var header = SetCookies(response).Single(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));
        return header.Split(';')[0][(CookieName.Length + 1)..];
    }

    /// <summary>The whole cookie, attribute by attribute: anything added to it (a Domain, say) fails here.</summary>
    public static void ShouldSetTheSessionCookie(HttpResponseMessage response, DateTime expiresAt, string sameSite = "lax")
    {
        var cookies = SetCookies(response);
        cookies.Should().ContainSingle("the session cookie is the only one this API writes: {0}", string.Join(" | ", cookies.Select(NameOf)));
        var parts = cookies[0].Split(';').Select(p => p.Trim()).ToList();
        parts[0].Should().StartWith(CookieName + "=");
        parts[0].Length.Should().BeGreaterThan(CookieName.Length + 1);
        var attributes = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        attributes.Where(a => !a.StartsWith("expires=", StringComparison.Ordinal))
            .Should().BeEquivalentTo("path=/", "secure", "httponly", $"samesite={sameSite}");
        ExpiryOf(cookies[0]).Should().BeCloseTo(new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)), TimeSpan.FromSeconds(5));
    }

    /// <summary>Removing a __Host- cookie takes the same attributes: a browser refuses the Set-Cookie otherwise.</summary>
    public static void ShouldExpireTheSessionCookie(HttpResponseMessage response)
    {
        var cookie = SetCookies(response).SingleOrDefault(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));
        cookie.Should().NotBeNull("the session cookie must be removed: {0}", string.Join(" | ", SetCookies(response).Select(NameOf)));
        var parts = cookie!.Split(';').Select(p => p.Trim()).ToList();
        parts[0].Should().Be(CookieName + "=");
        parts.Skip(1).Select(p => p.ToLowerInvariant()).Where(a => !a.StartsWith("expires=", StringComparison.Ordinal))
            .Should().BeEquivalentTo("path=/", "secure", "httponly", "samesite=lax");
        ExpiryOf(cookie).Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));
    }

    public static void ShouldExpireTheOldCookies(HttpResponseMessage response)
    {
        foreach (var name in new[] { "nexo_access", "nexo_refresh" })
        {
            var cookie = SetCookies(response).SingleOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal));
            cookie.Should().NotBeNull($"{name} of an older client still has to go");
            cookie!.Split(';')[0].Should().Be(name + "=");
            ExpiryOf(cookie).Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));
        }
    }

    public static void ShouldSetNoCookie(HttpResponseMessage response)
        => SetCookies(response).Should().BeEmpty("nothing here writes a cookie");

    public static async Task ShouldNotCarryARefreshTokenAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        text.Should().NotContain("refreshToken", "in the cookie mode nothing about the refresh token goes to the page");
        var cookie = SetCookies(response).FirstOrDefault(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));
        if (cookie is not null) text.Should().NotContain(cookie.Split(';')[0][(CookieName.Length + 1)..]);
    }

    public static async Task ShouldBeRefusedByOriginAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, text);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().Should().Be("origin_not_allowed");
        text.Should().NotContain("accessToken");
        ShouldSetNoCookie(response);
    }

    public static async Task<string?> CodeOfAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    public static DateTime TokenExpiry(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt).ValidTo;

    public static string? ClaimOf(string jwt, string type)
        => new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.FirstOrDefault(c => c.Type == type)?.Value;

    private static string NameOf(string setCookie) => setCookie.Split('=')[0];

    private static DateTimeOffset ExpiryOf(string setCookie)
    {
        var expires = setCookie.Split(';').Select(p => p.Trim()).Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return DateTimeOffset.ParseExact(expires[(expires.IndexOf('=') + 1)..], "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
    }
}

internal sealed record CookieSession(string Access, string Cookie);
