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
/// Refresh token security model:
///   - Each token has a unique JTI stored in Redis under "refresh:valid:{jti}".
///   - On rotate: old JTI deleted, new JTI inserted atomically.
///   - An old token presented after rotation is not in Redis → rejected (401).
///
/// Switch-store security gap (documented):
///   - SwitchStore controller extracts the ACCESS token from Authorization header and
///     passes it to SwitchStoreAsync as the "refreshToken" to revoke.
///   - ValidateRefreshToken rejects it (wrong audience) → oldClaims = null → RemoveAsync
///     never called → the original refresh token stays valid in Redis.
///   - After a store switch, the old refresh token still works.
///   - Test <see cref="SwitchStore_OldRefreshToken_RemainsValid_KnownSecurityGap"/>
///     documents this and currently passes (bug confirmed).
///   - Fix: have client send the refresh token in the request body of switch-store,
///     or revoke all sessions for the userId in SwitchStoreAsync.
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

        // Step 3: replay the original (now revoked) token → must be rejected
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

        // Step 3: replay the original refresh token — it must be rejected
        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));

        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "original refresh token must be revoked after switch-store — replay must return 401");
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
