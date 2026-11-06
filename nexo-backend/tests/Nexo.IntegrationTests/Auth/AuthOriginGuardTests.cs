using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// A page of any site can send a simple POST with no preflight (<c>mode: "no-cors"</c>, <c>text/plain</c>) to these routes. The answer does
/// not matter to it: the limiters count the request before MVC refuses the content type, so it spends the budget of everyone behind the
/// address of the person who opened that page. A browser always says where such a request came from.
/// </summary>
[Collection("Integration")]
public class AuthOriginGuardTests
{
    private const string Foreign = "https://evil.example";
    private const string Listed = "https://erp.orken.com.br";
    private static int s_addresses;

    private readonly TestWebApplicationFactory _factory;

    public AuthOriginGuardTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string NewAddress()
    {
        var value = Interlocked.Increment(ref s_addresses);
        return $"192.0.{2 + value / 250}.{1 + value % 250}";
    }

    private static HttpClient Bare(WebApplicationFactory<Program> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> host, HttpMethod method, string path, HttpContent? content, string? origin, string? address = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (address is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return await Bare(host).SendAsync(request);
    }

    /// <summary>What a page of another site can send without asking anyone first.</summary>
    private static StringContent SimplePost() => new("x", Encoding.UTF8, "text/plain");

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, text);
        JsonDocument.Parse(text).RootElement.GetProperty("code").GetString().Should().Be("origin_not_allowed");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private async Task<SyntheticAccount> NewAccountAsync() => await SyntheticTenant.CreateAsync(_factory, "varejo");

    private async Task<LoginResponse> SignInAsync(SyntheticAccount account, WebApplicationFactory<Program>? host = null, string? address = null)
    {
        var response = await SendAsync(host ?? _factory, HttpMethod.Post, "/api/auth/login", JsonContent.Create(TestCredentials.LoginPayload(account.Login, account.Password)), origin: null, address);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task A_foreign_flood_on_the_renewal_spends_no_permit_and_a_valid_renewal_from_that_address_goes_through()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "5");
        });
        var store = NewAddress();
        var signedIn = await SignInAsync(await NewAccountAsync(), host, NewAddress());

        for (var attempt = 0; attempt < 20; attempt++)
            await ShouldBeRefusedAsync(await SendAsync(host, HttpMethod.Post, "/api/auth/refresh", SimplePost(), Foreign, store));

        var renewal = await SendAsync(host, HttpMethod.Post, "/api/auth/refresh", JsonContent.Create(TestCredentials.RefreshPayload(signedIn.RefreshToken)), Listed, store);
        renewal.StatusCode.Should().Be(HttpStatusCode.OK, await renewal.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_foreign_flood_on_the_sign_in_spends_no_permit_and_a_valid_sign_in_from_that_address_goes_through()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogin:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogin:PermitLimit", "5");
        });
        var account = await NewAccountAsync();
        var store = NewAddress();

        for (var attempt = 0; attempt < 20; attempt++)
            await ShouldBeRefusedAsync(await SendAsync(host, HttpMethod.Post, "/api/auth/login", SimplePost(), Foreign, store));

        var signIn = await SendAsync(host, HttpMethod.Post, "/api/auth/login", JsonContent.Create(TestCredentials.LoginPayload(account.Login, account.Password)), Listed, store);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK, await signIn.Content.ReadAsStringAsync());
    }

    /// <summary>The proof of the limit itself: without an Origin the same request still counts, so the flood above is not passing by luck.</summary>
    [Fact]
    public async Task With_no_origin_nothing_changes_and_the_limiter_counts_as_before()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "2");
        });
        var server = NewAddress();

        (await SendAsync(host, HttpMethod.Post, "/api/auth/refresh", SimplePost(), origin: null, server)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await SendAsync(host, HttpMethod.Post, "/api/auth/refresh", SimplePost(), origin: null, server)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await SendAsync(host, HttpMethod.Post, "/api/auth/refresh", SimplePost(), origin: null, server)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_listed_origin_keeps_the_path_it_had()
    {
        (await SendAsync(_factory, HttpMethod.Post, "/api/auth/refresh", SimplePost(), Listed)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("POST", "/api/auth/login")]
    [InlineData("POST", "/api/auth/refresh")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/auth/mfa/verify")]
    [InlineData("POST", "/api/auth/forgot-password")]
    [InlineData("POST", "/api/auth/reset-password")]
    [InlineData("POST", "/api/auth/register")]
    [InlineData("POST", "/api/auth/resend-verification")]
    [InlineData("GET", "/api/auth/verify-email?token=x")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/platform/auth/login")]
    [InlineData("POST", "/api/platform/auth/refresh")]
    [InlineData("POST", "/api/platform/auth/mfa/verify")]
    [InlineData("GET", "/api/platform/auth/security")]
    [InlineData("POST", "/API/AUTH/REFRESH")]
    public async Task Every_auth_route_refuses_a_foreign_origin(string method, string path)
    {
        await ShouldBeRefusedAsync(await SendAsync(_factory, new HttpMethod(method), path, method == "POST" ? SimplePost() : null, Foreign));
    }

    [Theory]
    [InlineData("https://evil.orken.com.br")]
    [InlineData("https://erp.orken.com.br.evil.example")]
    [InlineData("http://erp.orken.com.br")]
    [InlineData("null")]
    public async Task Anything_that_is_not_exactly_on_the_list_is_foreign(string origin)
    {
        await ShouldBeRefusedAsync(await SendAsync(_factory, HttpMethod.Post, "/api/auth/refresh", SimplePost(), origin));
    }

    [Fact]
    public async Task A_right_password_from_a_foreign_origin_opens_no_session()
    {
        var account = await NewAccountAsync();

        await ShouldBeRefusedAsync(await SendAsync(_factory, HttpMethod.Post, "/api/auth/login",
            JsonContent.Create(TestCredentials.LoginPayload(account.Login, account.Password)), Foreign));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.UserSessions.IgnoreQueryFilters().CountAsync(s => s.UserId == account.UserId)).Should().Be(0);
    }

    /// <summary>A page load renews in the cookie mode: the budget of a session has room for a person working all day, and still stops a loop.</summary>
    [Fact]
    public void The_budget_of_a_session_is_300_by_default()
    {
        new RefreshBudgetOptions().FamilyBudget.Should().Be(300);
        _factory.Services.GetRequiredService<RefreshBudgetOptions>().FamilyBudget.Should().Be(300);
        _factory.Services.GetRequiredService<RefreshBudgetOptions>().FamilyWindow.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Other_routes_are_not_this_guard_s_business()
    {
        (await SendAsync(_factory, HttpMethod.Get, "/api/categories", null, Foreign)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_preflight_from_a_foreign_origin_still_gets_the_answer_of_CORS()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.TryAddWithoutValidation("Origin", Foreign);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");

        var preflight = await Bare(_factory).SendAsync(request);

        preflight.StatusCode.Should().Be(HttpStatusCode.NoContent);
        preflight.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
