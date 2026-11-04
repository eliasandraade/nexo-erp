using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Application.Features.Auth;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Concurrency and race condition test suite.
/// Validates:
/// - Concurrent refresh requests don't cause inconsistency
/// - Multi-tab auth scenarios (same user, multiple tokens)
/// - Session state remains consistent under load
/// - No race conditions in token rotation
/// - Simultaneous login/logout don't cause crashes
/// </summary>
[Collection("Integration")]
public class ConcurrencyTests
{
    private readonly TestWebApplicationFactory _factory;

    public ConcurrencyTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ────────────────────────────────────────────────────────────────────────────
    // CONCURRENT REFRESH REQUESTS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentRefresh_WithSameToken_ExactlyOneRotates()
    {
        var client = _factory.CreateApiClient();

        // Login
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var refreshToken = loginBody!.RefreshToken;

        // Make 5 concurrent refresh requests with the same token
        var tasks = Enumerable.Range(0, 5)
            .Select(i => RefreshAsync(refreshToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Uso único: exatamente uma requisição troca o token; as demais recebem 409 "refresh_already_rotated"
        // (a outra aba já tem o par novo). Antes, cada requisição sobreposta emitia um refresh token irmão (só um
        // deles seguido pela sessão registrada) e uma que chegasse logo depois recebia 401 — o teste instável no CI.
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "um token gera exatamente um sucessor");
        results.Where(r => r.StatusCode != HttpStatusCode.OK).Should().AllSatisfy(response =>
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, "quem perde a corrida não é deslogado"));

        var winner = await results.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<RefreshResponse>();
        (await RefreshAsync(winner!.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "o sucessor único segue válido");
    }

    [Fact]
    public async Task ConcurrentRefresh_MultipleClients_OnlyTheWinnerGetsAValidToken()
    {
        // 1. Login once
        var mainClient = _factory.CreateApiClient();
        var loginResponse = await mainClient.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // 2. Create multiple "client" instances and refresh concurrently
        var clients = Enumerable.Range(0, 3)
            .Select(_ => _factory.CreateApiClient())
            .ToList();

        var refreshTasks = clients.Select(client =>
            RefreshAndValidateAsync(client, loginBody!.RefreshToken)
        ).ToList();

        var validTokens = await Task.WhenAll(refreshTasks);

        // 3. Só a vencedora recebe tokens; as outras leem o par dela (no navegador, o armazenamento compartilhado)
        validTokens.Count(t => !string.IsNullOrEmpty(t)).Should().Be(1, "nenhum token irmão é emitido");

        // 4. O access token emitido funciona de imediato
        foreach (var token in validTokens.Where(t => !string.IsNullOrEmpty(t)))
        {
            var client = _factory.CreateApiClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var meResponse = await client.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "newly refreshed token should be valid immediately");
        }
    }

    [Fact]
    public async Task ConcurrentLoginLogout_SameUser_ConsistentState()
    {
        // Simulate a user logging in from multiple tabs
        // Each should get a valid session
        var loginTasks = Enumerable.Range(0, 3)
            .Select(_ => LoginAsync(TestCredentials.AdminLogin, TestCredentials.AdminPassword))
            .ToList();

        var loginResults = await Task.WhenAll(loginTasks);

        // All should succeed
        loginResults.Should().AllSatisfy(response =>
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        });

        // Each should have a valid token
        var tokens = await Task.WhenAll(loginResults.Select(async r =>
            (await r.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken
        ));

        tokens.Should().AllSatisfy(token =>
        {
            token.Should().NotBeNullOrEmpty();
        });
    }

    // ────────────────────────────────────────────────────────────────────────────
    // MULTI-TAB SIMULATION
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiTab_EachTabCanRefreshIndependently()
    {
        // Tab 1: Login
        var tab1Client = _factory.CreateApiClient();
        var loginResponse = await tab1Client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Refresh-token rotation is one-time-use for replay protection — see
        // AuthSessionSecurityTests.RefreshToken_AfterRotation_OldTokenIsRejected. Once a
        // token is used it is revoked, so genuine multi-tab clients always refresh against
        // the LATEST token (the shared cookie/store is updated on every rotation). Chain the
        // rotations the way a real client would and assert each tab obtains a usable token.
        var (tab2Token, refresh2) = await RefreshCaptureAsync(
            _factory.CreateApiClient(), loginBody!.RefreshToken);
        var (tab3Token, refresh3) = await RefreshCaptureAsync(
            _factory.CreateApiClient(), refresh2);
        var (tab4Token, _)        = await RefreshCaptureAsync(
            _factory.CreateApiClient(), refresh3);

        // Each tab should have a usable access token
        var allTokens = new[] { loginBody.AccessToken, tab2Token, tab3Token, tab4Token };
        allTokens.Should().AllSatisfy(t => t.Should().NotBeNullOrEmpty());

        // (Token values might be different, but that's implementation detail)
        // Each should work independently
        foreach (var token in allTokens)
        {
            var client = _factory.CreateApiClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            var meResponse = await client.GetAsync("/api/auth/me");
            meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task MultiTab_LoginLogout_DoesNotAffectOtherTab()
    {
        // Tab 1: Login
        var tab1 = _factory.CreateApiClient();
        var login1 = await tab1.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var body1 = await login1.Content.ReadFromJsonAsync<LoginResponse>();
        var token1 = body1!.AccessToken;

        // Tab 2: Also login (gets same user in different session)
        var tab2 = _factory.CreateApiClient();
        var login2 = await tab2.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var body2 = await login2.Content.ReadFromJsonAsync<LoginResponse>();
        var token2 = body2!.AccessToken;

        // Tab 1: Logout
        tab1.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token1);
        await tab1.PostAsync("/api/auth/logout", null);

        // Tab 2: Should still work (logout in Tab 1 shouldn't affect Tab 2)
        tab2.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token2);
        var tab2MeResponse = await tab2.GetAsync("/api/auth/me");

        tab2MeResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "logout in one tab/session should not affect other tabs");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // REFRESH TOKEN REPLAY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshTokenReplay_OldTokenAfterTwoRotations_IsRejected()
    {
        var client = _factory.CreateApiClient();

        // Login
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var originalRefreshToken = loginBody!.RefreshToken;

        // Refresh 1: Get new token
        var refresh1 = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));
        var body1 = await refresh1.Content.ReadFromJsonAsync<RefreshResponse>();
        var newRefreshToken1 = body1!.RefreshToken;

        // Refresh 2: Use new token to get another new token
        var refresh2 = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(newRefreshToken1));
        var body2 = await refresh2.Content.ReadFromJsonAsync<RefreshResponse>();

        // Try to replay the OLD refresh token (before rotation)
        var replayResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(originalRefreshToken));

        // um token já trocado duas vezes não é corrida de aba: é recusado e encerra a família
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(body2!.RefreshToken)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a sessão inteira termina quando um token antigo volta");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SWITCH STORE CONCURRENCY
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentSwitchStore_SameSession_ExactlyOneSwitchWins()
    {
        // Login
        var (mainClient, loginBody) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);

        // Get available stores
        var meResponse = await mainClient.GetAsync("/api/auth/me");
        var session = await meResponse.Content.ReadFromJsonAsync<SessionDto>();
        var storeIds = session!.StoreIds;

        // A troca de loja também gasta o refresh token da sessão (uso único): com o mesmo token, só uma troca
        // concorrente vence; as outras recebem 409 — nunca 500, nunca dois pares vivos para a mesma sessão.
        var switchTasks = Enumerable.Range(0, 3)
            .Select(i =>
            {
                var client = _factory.CreateApiClient();
                return client.WithBearer(loginBody.AccessToken)
                    .PostAsJsonAsync("/api/auth/switch-store",
                        new { storeId = storeIds[i % storeIds.Count], refreshToken = loginBody.RefreshToken });
            })
            .ToList();

        var results = await Task.WhenAll(switchTasks);

        // o código de cada resposta vai na mensagem: uma falha aqui precisa dizer qual ramo recusou
        // (só status e código, nunca o corpo: o da vencedora traz tokens)
        var outcomes = await Task.WhenAll(results.Select(async r => r.IsSuccessStatusCode
            ? $"{(int)r.StatusCode}"
            : $"{(int)r.StatusCode} {(await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString()}"));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", outcomes));
        results.Where(r => r.StatusCode != HttpStatusCode.OK).Should().AllSatisfy(response =>
            response.StatusCode.Should().Be(HttpStatusCode.Conflict,
                "quem perde a corrida, chegando antes ou depois da vencedora concluir, recebe 409 — nunca 401: "
                + string.Join(" | ", outcomes)));

        // e a corrida não encerra a família: a sessão segue com o par da vencedora
        var winner = await results.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<SwitchStoreResponse>();
        (await RefreshAsync(winner!.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // STRESS: HIGH CONCURRENT REQUESTS
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HighConcurrency_50ParallelRequests_AllRespond()
    {
        // Login
        var client = _factory.CreateApiClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.AdminLoginPayload());
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Fire 50 concurrent requests to /me with the same token
        var tasks = Enumerable.Range(0, 50)
            .Select(_ =>
            {
                var c = _factory.CreateApiClient();
                c.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
                return c.GetAsync("/api/auth/me");
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        // All should succeed (no deadlocks, no crashes)
        results.Should().AllSatisfy(response =>
        {
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.OK,
                HttpStatusCode.Unauthorized  // Token might have expired during test
            );
            response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        });
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> LoginAsync(string login, string password)
    {
        var client = _factory.CreateApiClient();
        return await client.PostAsJsonAsync("/api/auth/login",
            TestCredentials.LoginPayload(login, password));
    }

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
    {
        var client = _factory.CreateApiClient();
        return await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(refreshToken));
    }

    private async Task<string> RefreshAndValidateAsync(HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(refreshToken));

        if (response.StatusCode != HttpStatusCode.OK)
            return string.Empty;

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>();
        return body?.AccessToken ?? string.Empty;
    }

    /// <summary>
    /// Refreshes and returns BOTH the new access token and the rotated refresh token,
    /// so callers can chain rotations (one-time-use tokens cannot be replayed).
    /// </summary>
    private async Task<(string AccessToken, string RefreshToken)> RefreshCaptureAsync(
        HttpClient client, string refreshToken)
    {
        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            TestCredentials.RefreshPayload(refreshToken));

        if (response.StatusCode != HttpStatusCode.OK)
            return (string.Empty, string.Empty);

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>();
        return (body?.AccessToken ?? string.Empty, body?.RefreshToken ?? string.Empty);
    }
}
