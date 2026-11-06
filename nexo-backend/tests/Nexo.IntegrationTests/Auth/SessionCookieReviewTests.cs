using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Auth.BrowserSessionKit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// What the security review of the cookie mode found, each case as it was proven: the name of the cookie read loosely, a CORS entry that also gave authority
/// over the cookie, and answers that a cache could keep.
/// </summary>
[Collection("Integration")]
public class SessionCookieReviewTests
{
    private readonly TestWebApplicationFactory _factory;
    private readonly BrowserSessionKit _kit;

    public SessionCookieReviewTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new BrowserSessionKit(factory);
    }

    private async Task<CookieSession> SignInAsync()
    {
        var account = await _kit.NewAccountAsync();
        return await _kit.SignInAsync(account.Login, account.Password);
    }

    private static async Task<string?> OwnerOfAsync(HttpResponseMessage renewal)
        => ClaimOf((await renewal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!, "userId");

    // ── The exact name of the cookie ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A browser without the case-insensitive check of the prefix takes "__HOST-" with a Domain from any sibling: the name is only ours when it is exact.</summary>
    [Theory]
    [InlineData("__HOST-nexo_rt")]
    [InlineData("__host-nexo_rt")]
    [InlineData("__Host-NEXO_RT")]
    public async Task A_variant_of_the_name_alone_is_no_cookie(string name)
    {
        var attacker = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", rawCookies: $"{name}={attacker.Cookie}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldSetNoCookie(response);
        (await _kit.RenewAsync(attacker.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "and it was not spent");
    }

    [Fact]
    public async Task A_variant_of_the_name_after_the_real_cookie_does_not_win()
    {
        var victim = await SignInAsync();
        var attacker = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", sessionCookie: victim.Cookie, rawCookies: $"__HOST-nexo_rt={attacker.Cookie}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await OwnerOfAsync(response)).Should().Be(ClaimOf(victim.Access, "userId"), "the session of the person, not the one planted");
        (await _kit.RenewAsync(attacker.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "and the planted token was not spent");
    }

    [Fact]
    public async Task A_variant_of_the_name_before_the_real_cookie_does_not_win()
    {
        var victim = await SignInAsync();
        var attacker = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", rawCookies: $"__HOST-nexo_rt={attacker.Cookie}; {CookieName}={victim.Cookie}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await OwnerOfAsync(response)).Should().Be(ClaimOf(victim.Access, "userId"));
    }

    [Fact]
    public async Task A_variant_of_the_name_does_not_end_a_session_either()
    {
        var attacker = await SignInAsync();

        (await _kit.PostAsync("/api/auth/logout", rawCookies: $"__HOST-nexo_rt={attacker.Cookie}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _kit.RenewAsync(attacker.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "nothing was read");
    }

    /// <summary>A blank that only Unicode calls a blank goes in front of the name in a Set-Cookie: trimming it would turn another name into ours.</summary>
    [Theory]
    [InlineData("\u2000")]
    [InlineData("\u00A0")]
    public async Task A_unicode_space_before_the_name_is_part_of_another_name(string blank)
    {
        var attacker = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", rawCookies: $"{blank}{CookieName}={attacker.Cookie}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.RenewAsync(attacker.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The exact name twice means something else wrote one of them (a nameless cookie goes on the wire as "name=value"). Nobody can say which is real, so neither counts.</summary>
    [Fact]
    public async Task The_exact_name_twice_is_no_cookie()
    {
        var victim = await SignInAsync();
        var attacker = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", sessionCookie: victim.Cookie, rawCookies: $"{CookieName}={attacker.Cookie}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.RenewAsync(victim.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK, "neither was spent");
        (await _kit.RenewAsync(attacker.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Other_cookies_around_it_do_not_get_in_the_way()
    {
        var session = await SignInAsync();

        var response = await _kit.PostAsync("/api/auth/refresh", rawCookies: $"a=1; {CookieName}={session.Cookie}; nexo_rt=outro; b");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_platform_token_is_read_the_same_way()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();
        var session = await _kit.SignInAsync(email, password);

        (await _kit.PostAsync("/api/auth/refresh", rawCookies: $"__HOST-nexo_rt={session.Cookie}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── A list of its own for the cookie ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task By_default_only_the_app_of_the_ERP_has_authority_over_the_cookie()
    {
        var session = await SignInAsync();

        await ShouldBeRefusedByOriginAsync(await _kit.RenewAsync(session.Cookie, "https://www.orken.com.br"));

        (await _kit.RenewAsync(session.Cookie, "https://erp.orken.com.br")).StatusCode.Should().Be(HttpStatusCode.OK, "and the token was not spent");
    }

    [Fact]
    public async Task The_public_host_stays_an_origin_of_CORS()
    {
        var preflight = await PreflightAsync(_kit, "https://www.orken.com.br");

        preflight.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("https://www.orken.com.br");
    }

    /// <summary>docs/build/client-portal-contract.md tells a site of a client to add its origin to the CORS list: that must not hand it the session of whoever visits it.</summary>
    [Fact]
    public async Task A_site_of_a_client_on_the_CORS_list_cannot_renew_nor_sign_in_nor_end_a_session()
    {
        const string site = "https://portal.cliente.example";
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Cors:AllowedOrigins:0", site));
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();
        var session = await kit.SignInAsync(account.Login, account.Password);

        var preflight = await PreflightAsync(kit, site);
        preflight.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(site);

        await ShouldBeRefusedByOriginAsync(await kit.RenewAsync(session.Cookie, site));
        await ShouldBeRefusedByOriginAsync(await kit.PostAsync("/api/auth/logout", origin: site, sessionCookie: session.Cookie));
        await ShouldBeRefusedByOriginAsync(await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), site));

        (await kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_list_of_the_session_comes_from_the_configuration_and_replaces_the_default()
    {
        const string staging = "https://erp.staging.example";
        await using var host = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Session:AllowedOrigins:0", staging));
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        var signIn = await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), staging);

        signIn.StatusCode.Should().Be(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
        signIn.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(new[] { staging }, "whoever has the cookie has to be able to call: it is on the list of CORS by itself");
        await ShouldBeRefusedByOriginAsync(await kit.RenewAsync(CookieValue(signIn), "https://erp.orken.com.br"));
    }

    [Fact]
    public void A_wildcard_on_the_list_of_the_session_stops_the_startup()
    {
        var host = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:Session:AllowedOrigins:0", "https://*.orken.com.br"));

        var start = () => host.CreateClient();

        start.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_the_default_is_the_ERP_alone(string environment)
    {
        await using var host = HostIn(environment);
        var kit = _kit.On(host);
        var account = await _kit.NewAccountAsync();

        await ShouldBeRefusedByOriginAsync(await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "https://www.orken.com.br"));
        (await kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "https://erp.orken.com.br")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_the_header_the_public_host_goes_on_as_before()
    {
        var account = await _kit.NewAccountAsync();

        var signIn = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), "https://www.orken.com.br", mode: null);

        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldSetNoCookie(signIn);
    }

    // ── A sign-out is no open door ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The proof of the review: 40 calls went through with the limits at 1.</summary>
    [Fact]
    public async Task Anonymous_sign_outs_in_the_cookie_mode_stop_at_the_ceiling_and_a_real_one_from_another_address_goes_through()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogout:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogout:PermitLimit", "3");
        });
        var kit = _kit.On(host);
        var address = "198.18.0." + Random.Shared.Next(1, 250);
        var codes = new List<HttpStatusCode>();
        for (var call = 0; call < 40; call++)
            codes.Add((await kit.PostAsync("/api/auth/logout", new { refreshToken = "p.lixo" + call }, clientIp: address)).StatusCode);

        codes.Take(3).Should().OnlyContain(c => c == HttpStatusCode.NoContent);
        codes.Skip(3).Should().OnlyContain(c => c == HttpStatusCode.TooManyRequests);

        var account = await _kit.NewAccountAsync();
        var session = await kit.SignInAsync(account.Login, account.Password);
        (await kit.PostAsync("/api/auth/logout", sessionCookie: session.Cookie, clientIp: "198.18.1." + Random.Shared.Next(1, 250))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await kit.RenewAsync(session.Cookie)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_of_5_MB_in_the_cookie_mode_is_not_read()
    {
        var huge = "p." + new string('x', 5 * 1024 * 1024);
        var body = new StringContent(JsonSerializer.Serialize(new { refreshToken = huge }), Encoding.UTF8, "application/json");

        var response = await _kit.PostAsync("/api/auth/logout", body);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    // ── Answers with tokens are not kept ───────────────────────────────────────────────────────────────────────────────

    private static void ShouldNotBeStored(HttpResponseMessage response)
        => (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue($"{response.RequestMessage?.RequestUri} carries or decides a session");

    [Theory]
    [InlineData("cookie")]
    [InlineData(null)]
    public async Task No_answer_of_the_auth_routes_is_stored_in_either_mode(string? mode)
    {
        var account = await _kit.NewAccountAsync();

        var login = await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), mode: mode);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldNotBeStored(login);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = mode is null ? body.GetProperty("refreshToken").GetString()! : CookieValue(login);
        var access = body.GetProperty("accessToken").GetString()!;

        var renewal = mode is null
            ? await _kit.PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token), mode: null)
            : await _kit.RenewAsync(token);
        renewal.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldNotBeStored(renewal);

        var me = await _kit.SendAsync(HttpMethod.Get, "/api/auth/me", bearer: access, mode: null);
        ShouldNotBeStored(me);

        ShouldNotBeStored(await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, "errada"), mode: mode));
        ShouldNotBeStored(await _kit.SendAsync(HttpMethod.Get, "/api/auth/verify-email?token=nenhum", mode: mode));
    }

    [Theory]
    [InlineData("cookie")]
    [InlineData(null)]
    public async Task The_link_of_the_e_mail_carries_tokens_on_a_GET_and_is_not_stored(string? mode)
    {
        var email = $"nc{Guid.NewGuid():N}"[..14] + "@nexo.test";
        (await _kit.Client().PostAsJsonAsync("/api/auth/register", new { name = "Empresa de teste", email, password = "t" + Guid.NewGuid().ToString("N") })).EnsureSuccessStatusCode();
        var link = Guid.NewGuid().ToString("N");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            user.SetVerificationToken(link, DateTime.UtcNow.AddMinutes(30));
            await db.SaveChangesAsync();
        }

        var response = await _kit.SendAsync(HttpMethod.Get, $"/api/auth/verify-email?token={link}", mode: mode);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ShouldNotBeStored(response);
    }

    [Fact]
    public async Task The_routes_of_the_platform_and_the_refusals_are_not_stored_either()
    {
        var (email, password) = await _kit.NewPlatformAdminAsync();

        var login = await _kit.PostAsync("/api/platform/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldNotBeStored(login);
        ShouldNotBeStored(await _kit.PostAsync("/api/platform/auth/refresh", sessionCookie: CookieValue(login)));
        ShouldNotBeStored(await _kit.PostAsync("/api/auth/login", TestCredentials.LoginPayload("x", "y"), "https://evil.example"));
        ShouldNotBeStored(await _kit.PostAsync("/api/auth/logout"));
    }

    [Fact]
    public async Task Other_routes_keep_their_own_rules()
    {
        var response = await _kit.SendAsync(HttpMethod.Get, "/api/categories", mode: null);

        (response.Headers.CacheControl?.NoStore ?? false).Should().BeFalse();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> PreflightAsync(BrowserSessionKit kit, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "content-type");
        return await kit.Client().SendAsync(request);
    }

    private WebApplicationFactory<Program> HostIn(string environment)
        => _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Jwt:Secret", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
            b.ConfigureTestServices(s =>
            {
                foreach (var job in s.Where(d => d.ServiceType == typeof(IHostedService)
                                                 && d.ImplementationType?.Namespace?.StartsWith("Nexo.", StringComparison.Ordinal) == true).ToList())
                    s.Remove(job);
            });
        });
}
