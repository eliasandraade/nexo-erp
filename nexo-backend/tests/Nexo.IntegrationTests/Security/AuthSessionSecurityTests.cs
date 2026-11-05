using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Regression suite for auth session security: refresh token rotation and switch-store
/// session integrity.
///
/// Modelo do refresh token:
///   - cada token tem um jti único em "refresh:valid:{jti}" e pertence a uma família (a sessão de um login);
///   - uso único: a troca reserva o token de forma atômica; o token trocado, reapresentado dentro da carência,
///     recebe 409 sem tokens; depois dela (ou um token mais antigo), a família inteira é encerrada (401);
///   - a troca de loja gasta o refresh token da sessão como uma renovação: sem um refresh token válido da própria
///     sessão, não há par novo — o access token sozinho não abre sessão (antes, abria uma de 7 dias, mesmo após o logout).
/// Detalhes da política em RefreshTokenFamilyTests.
/// </summary>
[Collection("Integration")]
public class AuthSessionSecurityTests
{
    private readonly TestWebApplicationFactory _factory;

    public AuthSessionSecurityTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ── Test 1 ────────────────────────────────────────────────────────────────
    // Refresh token rotation: after a successful rotate, the old token is revoked.
    // Replaying the old token must return 401.

    [Fact]
    public async Task RefreshToken_AfterRotation_OldTokenIsRejected()
    {
        var client = _factory.CreateApiClient();

        // Step 1: login → obtain initial token pair
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var originalRefreshToken = loginBody!.RefreshToken;
        originalRefreshToken.Should().NotBeNullOrEmpty();

        // Step 2: rotate — old JTI deleted from Redis, new JTI inserted
        var firstRefreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));
        firstRefreshResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "first refresh must succeed");
        var firstRefreshBody = await firstRefreshResponse.Content
            .ReadFromJsonAsync<RefreshResponse>();
        firstRefreshBody!.AccessToken.Should().NotBeNullOrEmpty();
        firstRefreshBody.RefreshToken.Should().NotBeNullOrEmpty();
        firstRefreshBody.RefreshToken.Should().NotBe(originalRefreshToken,
            "rotation must issue a new refresh token, not return the same one");

        // Passo 3: o token original reapresentado logo em seguida (carência da corrida entre abas) → 409, sem tokens
        var graceReplay = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));
        graceReplay.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "um token já usado nunca rende outro par, nem dentro da carência");
        (await graceReplay.Content.ReadAsStringAsync()).Should().NotContain("accessToken");

        // Passo 4: o cliente segue com o sucessor; o original deixa de ser o anterior e volta a ser só uma cópia velha
        (await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(firstRefreshBody.RefreshToken))).StatusCode.Should().Be(HttpStatusCode.OK);
        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));

        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a used refresh token must be rejected — replay protection must be active");
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────
    // Refresh token rotation is not a no-op: using the new token must succeed.
    // Guards against an implementation that revokes without issuing a replacement.

    [Fact]
    public async Task RefreshToken_NewTokenAfterRotation_IsAccepted()
    {
        var client = _factory.CreateApiClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(loginBody!.RefreshToken));
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<RefreshResponse>();

        // The new token must also be usable
        var secondRefresh = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(firstBody!.RefreshToken));

        secondRefresh.StatusCode.Should().Be(HttpStatusCode.OK,
            "the newly issued refresh token must be valid for subsequent refreshes");
        var secondBody = await secondRefresh.Content.ReadFromJsonAsync<RefreshResponse>();
        secondBody!.AccessToken.Should().NotBeNullOrEmpty();
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────
    // Refresh with a structurally valid but completely fabricated JWT must be rejected.
    // Guards against missing signature validation or alg:none attacks.

    [Fact]
    public async Task RefreshToken_FabricatedToken_IsRejected()
    {
        var client = _factory.CreateApiClient();

        // A syntactically 3-part token with an invalid signature.
        // Uses TestCredentials.FakeJwtToken — clearly labelled as fake,
        // no real key material, no GitGuardian triggers.
        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(TestCredentials.FakeJwtToken));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a JWT with an invalid signature must always be rejected");
    }

    // ── Test 4 ────────────────────────────────────────────────────────────────
    // After a switch-store, the original refresh token must be revoked.
    // Replaying it must return 401 — not a fresh token pair.
    //
    // This test FAILS until the implementation is fixed.
    // Fix: SwitchStoreRequest must carry the refresh token in the body so
    // SwitchStoreAsync can validate and delete it from Redis.

    [Fact]
    public async Task SwitchStore_OldRefreshToken_IsRejected()
    {
        var storeBId = await GetOrCreateSecondStoreIdAsync();

        var client = _factory.CreateApiClient();

        // Step 1: login → obtain initial token pair
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var originalRefreshToken = loginBody!.RefreshToken;
        var originalAccessToken  = loginBody.AccessToken;

        // Step 2: switch store — send the refresh token in the body so the server can revoke it
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", originalAccessToken);
        var switchResponse = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = storeBId.ToString(), refreshToken = originalRefreshToken });
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "switch-store must succeed as a precondition");

        // Passo 3: o refresh token original foi gasto pela troca de loja — reapresentado, não rende tokens
        // (409 dentro da carência da corrida entre abas)
        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));

        replayResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "original refresh token must be spent by switch-store — replay must not return a token pair");
        (await replayResponse.Content.ReadAsStringAsync()).Should().NotContain("accessToken");
    }

    // ── Teste 5 ───────────────────────────────────────────────────────────────
    // A troca de loja sem o refresh token da sessão não emite par novo: antes, bastava um access token (15 min)
    // para obter um refresh token de 7 dias.

    [Fact]
    public async Task SwitchStore_WithoutRefreshToken_DoesNotIssueATokenPair()
    {
        var storeBId = await GetOrCreateSecondStoreIdAsync();
        var (client, _) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/switch-store", new { storeId = storeBId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "o access token sozinho não pode virar uma sessão nova");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("refreshToken\":");
    }

    // ── Teste 6 ───────────────────────────────────────────────────────────────
    // Depois do logout, o access token ainda vivo (até 15 min) não ressuscita a sessão pela troca de loja.

    [Fact]
    public async Task SwitchStore_AfterLogout_CannotReviveTheSession()
    {
        var storeBId = await GetOrCreateSecondStoreIdAsync();
        var (client, login) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);

        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PostAsJsonAsync("/api/auth/switch-store",
            new { storeId = storeBId.ToString(), refreshToken = login.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "um logout não pode ser desfeito com o access token que ainda não expirou");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a second store ID for the default seeded tenant.
    /// Creates it if absent. Used to make switch-store calls succeed as a precondition.
    /// </summary>
    private async Task<Guid> GetOrCreateSecondStoreIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<NexoDbContext>();

        // CROSS-TENANT: seeder context
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstAsync();

        const string slug = "z-store-b-auth-test";
        var existing = await db.Stores
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id && s.Slug == slug,
                cancellationToken: default);

        if (existing is not null) return existing.Id;

        var store = Store.Create(tenant.Id, "Z-Store-B-AuthTest", slug);
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }
}
