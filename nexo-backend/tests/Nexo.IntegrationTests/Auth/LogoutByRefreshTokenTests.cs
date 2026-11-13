using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// The way out for someone with no valid access token: a tab that just loaded, one whose access expired, or a sign-out left pending. Holding the refresh token is proof
/// enough to end that session, spent or not. A client never does this by itself after a 409: that would end the new pair of the tab that won the race.
/// </summary>
[Collection("Integration")]
public class LogoutByRefreshTokenTests
{
    private static int s_addresses;

    private readonly TestWebApplicationFactory _factory;

    public LogoutByRefreshTokenTests(TestWebApplicationFactory factory) => _factory = factory;

    private static string NewAddress()
    {
        var value = Interlocked.Increment(ref s_addresses);
        return $"192.0.{100 + value / 250}.{1 + value % 250}";
    }

    private HttpClient Client(WebApplicationFactory<Program>? host = null)
        => (host ?? _factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? bearer = null, string? address = null, WebApplicationFactory<Program>? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (address is not null) request.Headers.TryAddWithoutValidation("X-Forwarded-For", address);
        return await Client(host).SendAsync(request);
    }

    private async Task<LoginResponse> SignInAsync(WebApplicationFactory<Program>? host = null)
    {
        var account = await SyntheticTenant.CreateAsync(_factory, "varejo");
        var response = await PostAsync("/api/auth/login", TestCredentials.LoginPayload(account.Login, account.Password), host: host);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<RefreshResponse> RotateAsync(string token)
    {
        var rotated = await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        return (await rotated.Content.ReadFromJsonAsync<RefreshResponse>())!;
    }

    private Task<HttpResponseMessage> RenewAsync(string token) => PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(token));

    private string FamilyOf(string token)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(token)!.Family;
    }

    private async Task<string> SignInToPlatformAsync()
    {
        var email = $"out-{Guid.NewGuid():N}"[..14] + "@platform.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(TestCredentials.PlatformPassword), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await PostAsync("/api/platform/auth/login", new { email, password = TestCredentials.PlatformPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;
    }

    [Fact]
    public async Task A_token_someone_else_spent_ends_the_family_inside_the_grace_with_no_access_token()
    {
        var victim = await SignInAsync();
        var thief = await RotateAsync(victim.RefreshToken);
        (await RenewAsync(victim.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Conflict, "inside the grace it still looks like a race");

        var signedOut = await PostAsync("/api/auth/logout", new { refreshToken = victim.RefreshToken });

        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedOut.Content.ReadAsStringAsync());
        (await RenewAsync(thief.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the copy goes with the family");
    }

    [Fact]
    public async Task It_leaves_a_warning_in_the_audit_because_it_may_be_a_stolen_token()
    {
        var victim = await SignInAsync();
        await RotateAsync(victim.RefreshToken);

        (await PostAsync("/api/auth/logout", new { refreshToken = victim.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var family = FamilyOf(victim.RefreshToken);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var rows = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == AuditActions.UserSessionRevoked && a.Severity == AuditSeverity.Warning)
            .Select(a => a.MetadataJson).ToListAsync();
        rows.Should().Contain(m => m != null && m.Contains(family) && m.Contains("logout_with_spent_token"));
    }

    [Fact]
    public async Task A_sign_out_with_the_current_token_leaves_no_such_warning()
    {
        var signedIn = await SignInAsync();

        (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var family = FamilyOf(signedIn.RefreshToken);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var rows = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == AuditActions.UserSessionRevoked)
            .Select(a => a.MetadataJson).ToListAsync();
        rows.Should().NotContain(m => m != null && m.Contains(family));
    }

    [Fact]
    public async Task A_platform_token_someone_else_spent_ends_that_session_too()
    {
        var victim = await SignInToPlatformAsync();
        var rotated = await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(victim));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var thief = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        (await PostAsync("/api/auth/logout", new { refreshToken = victim })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RenewAsync(thief)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task With_no_access_token_and_no_refresh_token_it_is_still_a_401()
    {
        (await PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_we_did_not_issue_ends_nothing_and_says_nothing()
    {
        var signedIn = await SignInAsync();

        (await PostAsync("/api/auth/logout", new { refreshToken = "x" + Guid.NewGuid().ToString("N") })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync("/api/auth/logout", new { refreshToken = "p." + Guid.NewGuid().ToString("N") })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RenewAsync(signedIn.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_sign_out_of_an_access_token_goes_on_as_it_was()
    {
        var signedIn = await SignInAsync();

        (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken }, bearer: signedIn.AccessToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RenewAsync(signedIn.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── It answers without a session now, so it has a limit and a size ─────────────────────────────────────────────────

    [Fact]
    public async Task Anonymous_sign_outs_of_one_address_have_a_ceiling_and_another_address_is_not_affected()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogout:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogout:PermitLimit", "2");
        });
        var address = NewAddress();
        var body = new { refreshToken = "p." + Guid.NewGuid().ToString("N") };

        (await PostAsync("/api/auth/logout", body, address: address, host: host)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync("/api/auth/logout", body, address: address, host: host)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var over = await PostAsync("/api/auth/logout", body, address: address, host: host);
        over.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        over.Headers.RetryAfter.Should().NotBeNull();

        (await PostAsync("/api/auth/logout", body, address: NewAddress(), host: host)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_ceiling_of_an_address_does_not_stop_the_real_sign_out_of_someone_with_a_session()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthLogout:Enabled", "true");
            b.UseSetting("RateLimiting:AuthLogout:PermitLimit", "1");
        });
        var signedIn = await SignInAsync(host);
        var address = NewAddress();
        (await PostAsync("/api/auth/logout", new { refreshToken = "x" }, address: address, host: host)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostAsync("/api/auth/logout", new { refreshToken = "x" }, address: address, host: host)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var signedOut = await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken }, bearer: signedIn.AccessToken, address: address, host: host);

        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public void The_limit_is_on_by_default()
    {
        using var scope = _factory.Services.CreateScope();
        var endpoint = Logout();
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()!.PolicyName.Should().Be("auth-logout");
    }

    [Fact]
    public void Kestrel_is_told_the_size_of_the_body()
    {
        Logout().Metadata.GetMetadata<IRequestSizeLimitMetadata>()!.MaxRequestBodySize.Should().Be(16 * 1024);
    }

    [Fact]
    public async Task A_body_of_5_MB_is_refused_before_anything_reads_it()
    {
        var huge = new string('x', 5 * 1024 * 1024);

        var response = await PostAsync("/api/auth/logout", new StringContent(JsonSerializer.Serialize(new { refreshToken = "p." + huge }), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_token_of_a_real_size_fits()
    {
        var signedIn = await SignInAsync();
        Encoding.UTF8.GetByteCount(signedIn.RefreshToken).Should().BeLessThan(16 * 1024);

        (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private RouteEndpoint Logout() => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
        .Single(e => e.RoutePattern.RawText == "api/auth/logout");
}
