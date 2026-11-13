using System.Net;
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

/// <summary>What the second independent review proved on the renewal, in the paths that need no real Redis.</summary>
[Collection("Integration")]
public class RenewalSecondReviewTests
{
    private readonly TestWebApplicationFactory _factory;

    public RenewalSecondReviewTests(TestWebApplicationFactory factory) => _factory = factory;

    private HttpClient Client(WebApplicationFactory<Program>? host = null)
        => (host ?? _factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? address = null, WebApplicationFactory<Program>? host = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body);
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

    private async Task<(string Email, string Refresh)> SignInPlatformAsync()
    {
        var email = $"rv-{Guid.NewGuid():N}"[..14] + "@platform.test";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(TestCredentials.PlatformPassword), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await PostAsync("/api/platform/auth/login", new { email, password = TestCredentials.PlatformPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return (email, (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!);
    }

    private string FamilyOf(string token)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(token)!.Family;
    }

    private string JtiOf(string token)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(token)!.Jti;
    }

    private async Task<int> AuditRowsOfFamilyAsync(string family)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return (await db.AuditRecords.IgnoreQueryFilters().AsNoTracking().Select(a => a.MetadataJson).ToListAsync())
            .Count(m => m != null && m.Contains(family));
    }

    private RouteEndpoint PostEndpoint(string route)
        => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("POST") == true);

    // ── A body cap on the renewal ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_renewal_of_5_MB_that_says_its_size_is_refused_before_anything_reads_it(string route)
    {
        var huge = "p." + new string('x', 5 * 1024 * 1024);

        var response = await PostAsync(route, new StringContent(JsonSerializer.Serialize(new { refreshToken = huge }), Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Theory]
    [InlineData("api/auth/refresh")]
    [InlineData("api/platform/auth/refresh")]
    public void Kestrel_is_told_the_size_of_a_renewal(string route)
    {
        PostEndpoint(route).Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize.Should().Be(16 * 1024);
        PostEndpoint(route).Metadata.GetMetadata<IRequestSizeLimitMetadata>().Should().NotBeNull();
    }

    // ── A 409 says when to come back ──────────────────────────────────────────────────────────────────────────────────

    private static void ShouldSayTheGraceLeft(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var left = response.Headers.RetryAfter?.Delta;
        left.Should().NotBeNull("the client has to know when the server will decide");
        left!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        left.Value.Should().BeLessThanOrEqualTo(AuthService.RotationGrace);
    }

    [Fact]
    public async Task A_409_of_a_company_session_says_how_much_of_the_grace_is_left()
    {
        var signedIn = await SignInAsync();
        (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldSayTheGraceLeft(await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken)));
    }

    [Theory]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/platform/auth/refresh")]
    public async Task A_409_of_a_platform_session_says_it_too(string route)
    {
        var (_, refresh) = await SignInPlatformAsync();
        (await PostAsync(route, TestCredentials.RefreshPayload(refresh))).StatusCode.Should().Be(HttpStatusCode.OK);

        ShouldSayTheGraceLeft(await PostAsync(route, TestCredentials.RefreshPayload(refresh)));
    }

    // ── How much one spent token can write ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task One_spent_token_writes_one_row_however_many_times_it_comes_back()
    {
        var signedIn = await SignInAsync();
        (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var call = 0; call < 25; call++)
            (await PostAsync("/api/auth/logout", new { refreshToken = signedIn.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AuditRowsOfFamilyAsync(FamilyOf(signedIn.RefreshToken))).Should().Be(1, "after the first one the family has already ended");
    }

    // ── The row follows the token ─────────────────────────────────────────────────────────────────────────────────────

    private async Task<UserSession> RowOfAsync(string token)
    {
        using var scope = _factory.Services.CreateScope();
        var jti = JtiOf(token);
        return await scope.ServiceProvider.GetRequiredService<NexoDbContext>().UserSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.RefreshJti == jti);
    }

    [Fact]
    public async Task A_rotation_moves_the_expiry_of_the_row_with_the_token()
    {
        var signedIn = await SignInAsync();
        var first = await RowOfAsync(signedIn.RefreshToken);
        await Task.Delay(1100);

        var rotated = (await (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken))).Content.ReadFromJsonAsync<RefreshResponse>())!;

        var row = await RowOfAsync(rotated.RefreshToken);
        row.ExpiresAt.Should().BeAfter(first.ExpiresAt);
        row.ExpiresAt.Should().BeCloseTo(rotated.RefreshTokenExpiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task A_terminal_past_the_first_expiry_of_its_row_stays_on_the_list_of_devices()
    {
        var signedIn = await SignInAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var jti = JtiOf(signedIn.RefreshToken);
            await db.UserSessions.IgnoreQueryFilters().Where(s => s.RefreshJti == jti).ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }

        var rotated = (await (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(signedIn.RefreshToken))).Content.ReadFromJsonAsync<RefreshResponse>())!;

        using var read = _factory.Services.CreateScope();
        var devices = await read.ServiceProvider.GetRequiredService<Nexo.Infrastructure.Auth.AccountSecurityService>().ListSessionsAsync(Guid.Parse(signedIn.Session.UserId));
        devices.Should().ContainSingle();
        (await RowOfAsync(rotated.RefreshToken)).ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }

    // ── A sign-out of the platform with a spent token ─────────────────────────────────────────────────────────────────

    private async Task<List<(string Action, string Severity, string? Metadata)>> AuditOfAsync(string email)
    {
        using var read = _factory.Services.CreateScope();
        var rows = await read.ServiceProvider.GetRequiredService<NexoDbContext>().AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActorName == email).OrderBy(a => a.CreatedAt).Select(a => new { a.ActionType, a.Severity, a.MetadataJson }).ToListAsync();
        return rows.Select(r => (r.ActionType, r.Severity, r.MetadataJson)).ToList();
    }

    [Fact]
    public async Task A_sign_out_of_the_platform_with_a_spent_token_leaves_the_warning_the_documents_promise()
    {
        var (email, spent) = await SignInPlatformAsync();
        (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(spent))).StatusCode.Should().Be(HttpStatusCode.OK, "someone else rotated it");

        (await PostAsync("/api/auth/logout", new { refreshToken = spent })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await AuditOfAsync(email);
        rows.Should().ContainSingle(r => r.Severity == AuditSeverity.Warning && r.Metadata != null && r.Metadata.Contains("logout_with_spent_token"));
        rows.Should().NotContain(r => r.Action == AuditActions.PlatformLoggedOut, "this was no ordinary sign-out");
        rows.Should().NotContain(r => r.Severity == AuditSeverity.Critical);
    }

    [Fact]
    public async Task A_sign_out_of_the_platform_with_the_current_token_stays_an_ordinary_one()
    {
        var (email, current) = await SignInPlatformAsync();

        (await PostAsync("/api/auth/logout", new { refreshToken = current })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await AuditOfAsync(email);
        rows.Should().ContainSingle(r => r.Action == AuditActions.PlatformLoggedOut && r.Severity == AuditSeverity.Info);
        rows.Should().NotContain(r => r.Metadata != null && r.Metadata.Contains("logout_with_spent_token"));
    }

    [Fact]
    public async Task A_spent_platform_token_coming_back_writes_one_row()
    {
        var (email, spent) = await SignInPlatformAsync();
        (await PostAsync("/api/auth/refresh", TestCredentials.RefreshPayload(spent))).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var call = 0; call < 5; call++)
            (await PostAsync("/api/auth/logout", new { refreshToken = spent })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await AuditOfAsync(email)).Count(r => r.Metadata != null && r.Metadata.Contains("logout_with_spent_token")).Should().Be(1);
    }

    // ── A forwarded value that is no address ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The default source is not in question: the edge overwrites this header. What reaches the limiter and Redis has to be an address, though: anything else falls
    /// back to the connection, and buys no partition of its own.
    /// </summary>
    [Theory]
    [InlineData("nem-um-endereco")]
    [InlineData("::1%aaaaaaaa")]
    [InlineData("203.0.113.7:8080x")]
    public async Task A_forwarded_value_that_is_no_address_buys_no_new_ceiling(string value)
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "2");
            b.UseSetting("RateLimiting:AuthRefresh:FailureBudget", "100000");
        });
        for (var call = 0; call < 2; call++)
            (await PostAsync("/api/auth/refresh", new { refreshToken = "lixo" }, host: host)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var forged = await PostAsync("/api/auth/refresh", new { refreshToken = "lixo" }, value, host);

        forged.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "it counts as the connection, whose ceiling is spent");
    }

    [Fact]
    public async Task A_forwarded_value_of_16_KB_buys_no_new_ceiling_either()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "2");
            b.UseSetting("RateLimiting:AuthRefresh:FailureBudget", "100000");
        });
        for (var call = 0; call < 2; call++)
            (await PostAsync("/api/auth/refresh", new { refreshToken = "lixo" }, host: host)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var forged = await PostAsync("/api/auth/refresh", new { refreshToken = "lixo" }, "::1%" + new string('a', 16 * 1024), host);

        forged.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Two_ways_of_writing_one_address_are_one_ceiling()
    {
        await using var host = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:AuthRefresh:Enabled", "true");
            b.UseSetting("RateLimiting:AuthRefresh:PermitLimit", "2");
            b.UseSetting("RateLimiting:AuthRefresh:FailureBudget", "100000");
        });
        var codes = new List<HttpStatusCode>();
        foreach (var spelling in new[] { "2001:db8::7", "2001:DB8:0:0:0:0:0:7", "2001:0db8::0007" })
            codes.Add((await PostAsync("/api/auth/refresh", new { refreshToken = "lixo" }, spelling, host)).StatusCode);

        codes.Should().Equal(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests);
    }
}
