using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Auth.BrowserSessionKit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// The attributes of the cookies of this API, in every environment. The session cookie and what happens to it are in
/// <see cref="SessionCookieModeTests"/>; here, what does not depend on the flow: a request without the mode header gets no cookie, the
/// cookie of the mode is the same everywhere, and a sign-out removes the cookies an older version may have left in a browser.
/// </summary>
[Collection("Integration")]
public class CookieSecurityTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly BrowserSessionKit _kit;
    private readonly HttpClient _client;

    public CookieSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new BrowserSessionKit(factory);
        _client = factory.CreateApiClient();
    }

    [Fact]
    public async Task Login_without_the_mode_header_sets_no_cookie_at_all()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(response);
    }

    [Fact]
    public async Task Refresh_without_the_mode_header_sets_no_cookie_at_all()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(body!.RefreshToken));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(response);
    }

    [Fact]
    public async Task Switch_store_without_the_mode_header_sets_no_cookie_at_all()
    {
        var (client, login) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = login.Session.StoreIds[0], refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(response);
    }

    [Fact]
    public async Task Logout_removes_the_cookies_of_older_versions_with_the_flags_they_had()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", TestCredentials.AdminLoginPayload());
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var response = await _client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ShouldExpireTheOldCookies(response);
        foreach (var cookie in SetCookies(response))
            Attributes(cookie).Should().Contain(["secure", "httponly", "samesite=lax", "path=/"]);
    }

    [Fact]
    public async Task The_session_cookie_is_secure_over_plain_http()
    {
        var account = await _kit.NewAccountAsync();

        var response = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));

        _kit.Client().BaseAddress!.Scheme.Should().Be("http", "this host stands in for the API behind the proxy, where TLS has ended");
        Attributes(SetCookies(response).Single()).Should().Contain("secure");
    }

    /// <summary>A browser refuses a __Host- cookie that is not Secure, so Development gets the same one.</summary>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public async Task The_session_cookie_is_the_same_in_every_environment(string environment)
    {
        await using var host = HostIn(environment);
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        var response = await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetTheSessionCookie(response, TokenExpiry(CookieValue(response)));
    }

    [Fact]
    public async Task Removing_the_cookie_with_SameSite_None_keeps_SameSite_None()
    {
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Session:CookieSameSite", "None"));
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();
        var session = await kit.SignInAsync(account.Login, account.Password);

        var response = await kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie);

        // on a response from another site, a browser refuses a Set-Cookie with SameSite=Lax: the removal would never happen
        Attributes(SetCookies(response).Single(c => c.StartsWith(CookieName, StringComparison.Ordinal))).Should().Contain("samesite=none");
    }

    /// <summary>The attributes after the value, lower-cased: a JWT value could contain any of these words by chance.</summary>
    private static string[] Attributes(string setCookieHeader)
        => setCookieHeader.Split(';').Skip(1).Select(p => p.Trim().ToLowerInvariant()).ToArray();

    /// <summary>The real startup in another environment: strict ones demand a random signing secret.</summary>
    private WebApplicationFactory<Program> HostIn(string environment)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Jwt:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            // Outside "Testing" the periodic jobs start too: they must not touch the data of other tests in the collection.
            b.ConfigureTestServices(s =>
            {
                foreach (var job in s.Where(d => d.ServiceType == typeof(IHostedService)
                                                 && d.ImplementationType?.Namespace?.StartsWith("Nexo.", StringComparison.Ordinal) == true).ToList())
                    s.Remove(job);
            });
        });
}
