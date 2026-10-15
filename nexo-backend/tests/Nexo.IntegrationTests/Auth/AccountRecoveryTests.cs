using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using Xunit;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// PR 63 — account recovery and session control: reset by e-mail link (one use, expiring, no
/// account enumeration), every password change ends every session (refresh tokens included),
/// and "sair de todos os dispositivos". Each test uses its own user — never the shared admin.
/// </summary>
[Collection("Integration")]
public class AccountRecoveryTests
{
    private readonly TestWebApplicationFactory _factory;
    public AccountRecoveryTests(TestWebApplicationFactory factory) => _factory = factory;

    private async Task<(Guid Id, string Login, string Email, string Password)> NewUserAsync()
    {
        var admin = await AuthClientFactory.LoginAsAdminAsync(_factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (login, email, password) = ($"rec{tag}", $"rec{tag}@teste.dev", Fresh());
        var r = await admin.PostAsJsonAsync("/api/users", new
        {
            fullName = "Pessoa Teste", email, login, password, role = "vendedor", phone = (string?)null, notes = (string?)null,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return ((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), login, email, password);
    }

    /// <summary>The e-mail carries the token; tests plant a known one (only its hash is stored).</summary>
    private async Task PlantResetTokenAsync(Guid userId, string token, DateTime expiresAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == userId);
        user.SetPasswordResetToken(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), expiresAt);
        await db.SaveChangesAsync();
    }

    /// <summary>A throwaway credential made at run time (no literal that looks like a password).</summary>
    private static string Fresh() => "t" + Guid.NewGuid().ToString("N")[..12];

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    [Fact]
    public async Task Forgot_password_answers_the_same_for_known_and_unknown_addresses()
    {
        var user = await NewUserAsync();
        var anon = _factory.CreateApiClient();

        var known = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email });
        var unknown = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = $"ninguem{Guid.NewGuid():N}@teste.dev" });

        known.StatusCode.Should().Be(HttpStatusCode.Accepted);
        unknown.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await known.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var stored = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == user.Id);
        stored.PasswordResetTokenHash.Should().MatchRegex("^[0-9a-f]{64}$", "only the hash is stored");
        stored.PasswordResetExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Asking_again_right_away_keeps_the_link_already_sent()
    {
        var user = await NewUserAsync();
        var anon = _factory.CreateApiClient();
        (await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        string? first;
        using (var scope = _factory.Services.CreateScope())
            first = (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(u => u.Id == user.Id)).PasswordResetTokenHash;

        (await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email })).StatusCode.Should().Be(HttpStatusCode.Accepted);

        using (var scope = _factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<NexoDbContext>().Users.IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(u => u.Id == user.Id)).PasswordResetTokenHash
                .Should().Be(first, "spamming the form must not invalidate the link the person is about to use");
    }

    [Fact]
    public async Task Reset_link_sets_the_password_once_and_ends_every_session()
    {
        var user = await NewUserAsync();
        var (_, before) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        await PlantResetTokenAsync(user.Id, "token-de-teste-1", DateTime.UtcNow.AddMinutes(30));
        var anon = _factory.CreateApiClient();

        var renewed = Fresh();
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "token-de-teste-1", newPassword = renewed }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(before.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old session is gone");
        (await anon.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(user.Login, user.Password)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, renewed);
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "token-de-teste-1", newPassword = Fresh() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a link works once");
    }

    [Fact]
    public async Task Expired_or_unknown_links_and_short_passwords_are_refused()
    {
        var user = await NewUserAsync();
        await PlantResetTokenAsync(user.Id, "token-vencido", DateTime.UtcNow.AddMinutes(-1));
        var anon = _factory.CreateApiClient();

        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "token-vencido", newPassword = Fresh() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "nao-existe", newPassword = Fresh() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "token-vencido", newPassword = Fresh()[..3] }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_your_own_password_ends_your_other_sessions()
    {
        var user = await NewUserAsync();
        var (client, first) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);
        var (_, second) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);

        (await client.PostAsJsonAsync($"/api/users/{user.Id}/change-password", new { currentPassword = user.Password, newPassword = Fresh() }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sessions_are_listed_and_signing_out_everywhere_kills_tokens_not_tracked_as_sessions()
    {
        var user = await NewUserAsync();
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, user.Login, user.Password);

        var sessions = await client.GetFromJsonAsync<JsonElement[]>("/api/auth/sessions");
        sessions.Should().NotBeEmpty();

        // a refresh token that exists only in the cache (as verify-email and switch-store issue them)
        string untracked;
        using (var scope = _factory.Services.CreateScope())
        {
            var jwt = scope.ServiceProvider.GetRequiredService<Nexo.Application.Common.Interfaces.IJwtTokenService>();
            var cache = scope.ServiceProvider.GetRequiredService<Nexo.Application.Common.Interfaces.ICacheService>();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var u = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == user.Id);
            var pair = jwt.GenerateTokenPair(u, "t", "T", [], Guid.Empty, []);
            var claims = jwt.ValidateRefreshToken(pair.RefreshToken)!;
            await cache.SetAsync($"refresh:valid:{claims.Jti}", new RefreshTokenEntry(u.Id, u.TenantId, null, u.SecurityStamp), TimeSpan.FromHours(1));
            untracked = pair.RefreshToken;
        }

        // a cross-site form post (no JSON) is refused — the CSRF guard
        (await client.PostAsync("/api/auth/sessions/revoke-all",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "1" })))
            .IsSuccessStatusCode.Should().BeFalse();
        (await client.PostAsJsonAsync("/api/auth/sessions/revoke-all", new { confirm = true })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(untracked)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its security stamp is outdated");
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the access token dies with the stamp");
    }
}
