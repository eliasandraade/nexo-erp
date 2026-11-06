using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Security.RedisSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O que acontece com uma sessão quando o papel ou a situação da pessoa muda, com o cache de produção e um Redis de verdade
/// (issue #137). A resposta do <c>PUT</c> é o ponto de corte: nenhuma requisição enviada depois dela passa com o token antigo.
/// Cada teste cria a própria empresa e faz as requisições de verdade.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class SessionRevocationRedisTests : IClassFixture<RedisWebApplicationFactory>, IAsyncLifetime
{
    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _kit;

    public SessionRevocationRedisTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new RedisSessionKit(factory);
    }

    /// <summary>Um estouro de tempo em qualquer teste abre o disjuntor por 10 s, e sem cache a renovação de sessão recusa tudo: cada teste começa com o cache respondendo.</summary>
    public Task InitializeAsync() => _factory.WaitForCacheAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ────────────────────────────────────────────────────────────────────────────
    // MUDANÇA DE PAPEL
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_host_really_uses_redis()
    {
        var (_, _, target) = await _kit.NewCompanyAsync(UserRole.Vendedor);

        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(login.RefreshToken)}")).Should().BeTrue("a renovação depende desta entrada");
        (await _factory.RedisDatabase.KeyExistsAsync($"user:{target.UserId}:info")).Should().BeTrue();
    }

    [Fact]
    public async Task Demotion_kills_the_old_token_at_once_and_the_next_login_has_the_new_role()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.PostAsync(HubNegotiation, null).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await _kit.ChangeRoleAsync(director, target, "vendedor");

        await _kit.ShouldBeRevokedAsync(client, UsersRoute);
        await _kit.ShouldBeRevokedAsync(client, CompanyRoute);
        await _kit.ShouldBeRevokedAsync(client, "/api/auth/me");
        await client.PostAsync(HubNegotiation, null).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory, "sem conexão nova com o token antigo");
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        var (renewed, relogin) = await _kit.SignInAsync(target);
        relogin.Session.Role.Should().Be("vendedor");
        (await renewed.GetFromJsonAsync<SessionDto>("/api/auth/me"))!.Role.Should().Be("vendedor");
        await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory, "vendedor não lista usuários");
        await renewed.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Fact]
    public async Task Promotion_kills_the_old_token_at_once_and_the_next_login_opens_the_new_route()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Vendedor);
        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        await _kit.ChangeRoleAsync(director, target, "gerente");

        await _kit.ShouldBeRevokedAsync(client);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        var (renewed, relogin) = await _kit.SignInAsync(target);
        relogin.Session.Role.Should().Be("gerente");
        await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Fact]
    public async Task Changing_your_own_role_ends_your_own_session()
    {
        var (director, account, _) = await _kit.NewCompanyAsync(UserRole.Vendedor);
        // the company keeps a director: leaving as the last one is refused (LastDirectorTests)
        await SyntheticTenant.AddUserAsync(_factory, account, UserRole.Diretoria);

        await _kit.ChangeRoleAsync(director, account, "gerente");

        await _kit.ShouldBeRevokedAsync(director);
        var (renewed, relogin) = await _kit.SignInAsync(account);
        relogin.Session.Role.Should().Be("gerente");
        await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    /// <summary>
    /// A change refused for being the last director's stops before anything of the session: the stamp, the session rows, the
    /// refresh entry and the cached info are all as they were, and the token goes on working.
    /// </summary>
    [Theory]
    [InlineData("gerente", "active")]
    [InlineData("diretoria", "blocked")]
    public async Task A_refused_change_touches_nothing_of_the_session(string role, string status)
    {
        var (director, account, _) = await _kit.NewCompanyAsync(UserRole.Vendedor);
        var (client, login) = await _kit.SignInAsync(account);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var stampBefore = await _kit.SecurityStampOfAsync(account.UserId);
        var sessionsBefore = (await client.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength();

        using (var refused = await director.PutAsJsonAsync($"{UsersRoute}/{account.UserId}", UpdateBody(account, role, status)))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await _factory.ServerLog.DescribeAsync(refused));
            (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("last_director");
        }

        (await _kit.SecurityStampOfAsync(account.UserId)).Should().Be(stampBefore);
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength().Should().Be(sessionsBefore);
        (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(login.RefreshToken)}")).Should().BeTrue("the refresh entry stays in Redis");
        (await _factory.RedisDatabase.KeyExistsAsync($"user:{account.UserId}:info")).Should().BeTrue();
        await director.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Fact]
    public async Task Every_session_of_the_person_falls_at_once_including_each_refresh_family()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var devices = new[] { await _kit.SignInAsync(target), await _kit.SignInAsync(target), await _kit.SignInAsync(target) };
        foreach (var (client, _) in devices)
            await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await devices[0].Client.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength().Should().Be(3);

        await _kit.ChangeRoleAsync(director, target, "vendedor");

        foreach (var (client, login) in devices)
        {
            await _kit.ShouldBeRevokedAsync(client, UsersRoute);
            (await _factory.RedisDatabase.KeyExistsAsync($"refresh:valid:{JtiOf(login.RefreshToken)}")).Should().BeFalse("a entrada de renovação sai do Redis");
            await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        }

        var (renewed, _) = await _kit.SignInAsync(target);
        (await renewed.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength().Should().Be(1, "só o login novo continua de pé");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // BLOQUEIO E INATIVAÇÃO
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("inactive")]
    [InlineData("blocked")]
    public async Task Blocking_or_deactivating_ends_the_session_and_login_stays_closed_until_reactivation(string status)
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Vendedor);
        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "vendedor", status))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await _kit.ShouldBeRevokedAsync(client);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(target.Login, target.Password))
            .ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        await director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "vendedor"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await _kit.ShouldBeRevokedAsync(client);
        var (renewed, _) = await _kit.SignInAsync(target);
        await renewed.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // EDIÇÃO SEM MUDANÇA DE PAPEL NEM DE SITUAÇÃO
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Editing_without_changing_role_or_status_keeps_the_session()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var stampBefore = await _kit.SecurityStampOfAsync(target.UserId);

        // o mesmo papel escrito de outro jeito também não é uma mudança
        foreach (var role in new[] { "gerente", "Gerente" })
            await director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, role, notes: $"Nota {role}", fullName: "Pessoa Renomeada"))
                .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        (await _kit.SecurityStampOfAsync(target.UserId)).Should().Be(stampBefore);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/sessions")).GetArrayLength().Should().Be(1);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    /// <summary>
    /// A decisão da #137: o carimbo não vai para o cache, nem passa de uma leitura por requisição. O resto do que o
    /// caminho da requisição guarda no Redis continua lá.
    /// </summary>
    [Fact]
    public async Task The_stamp_is_read_from_the_database_once_per_request_and_the_rest_stays_cached()
    {
        var (_, _, target) = await _kit.NewCompanyAsync(UserRole.Vendedor);
        var (client, _) = await _kit.SignInAsync(target);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        var stampReads = _factory.Gate.Count(DbCommandGate.StampReadOf(target.UserId));
        for (var i = 0; i < 5; i++)
            await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        stampReads().Should().Be(5);
        _factory.RedisKeys("user:stamp:*").Should().BeEmpty();
        (await _factory.RedisDatabase.KeyExistsAsync($"user:{target.UserId}:info")).Should().BeTrue();
        (await _factory.RedisDatabase.KeyExistsAsync($"tenant:{target.TenantId}:info")).Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // CONCORRÊNCIA
    // ────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> EveryWayToChangeTheStamp() => new()
    {
        "papel", "bloqueio", "inativacao", "senha pela diretoria", "senha pela propria pessoa", "senha pelo link",
        "sair de todos os dispositivos", "verificacao em duas etapas", "plataforma redefine a senha",
        "plataforma encerra a sessao", "plataforma revoga as sessoes",
    };

    /// <summary>
    /// A corrida, de forma determinística: uma requisição leu o carimbo antigo do banco e ficou parada antes de usar o que
    /// leu; a mudança começa, faz commit e responde; só então a requisição segue. Ela pode até terminar (a checagem dela
    /// aconteceu antes do commit), mas não pode deixar o token antigo valendo para a requisição seguinte.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryWayToChangeTheStamp))]
    public async Task A_late_read_of_the_old_stamp_does_not_bring_the_old_token_back(string cause)
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var (client, _) = await _kit.SignInAsync(target);
        var change = await PrepareChangeAsync(cause, director, client, target);
        var stampBefore = await _kit.SecurityStampOfAsync(target.UserId);

        using var hold = _factory.Gate.HoldNext(DbCommandGate.StampReadOf(target.UserId), DbCommandGate.Moment.AfterRunning);
        var inFlight = client.GetAsync(CompanyRoute);
        await hold.ReachedAsync();

        using (var answered = await change())
            answered.IsSuccessStatusCode.Should().BeTrue(await _factory.ServerLog.DescribeAsync(answered));
        (await _kit.SecurityStampOfAsync(target.UserId)).Should().NotBe(stampBefore);

        hold.Release();
        using (var late = await inFlight)
            late.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);

        await _kit.ShouldBeRevokedAsync(client);
    }

    /// <summary>Tudo o que a mudança precisa fica pronto antes da corrida começar: nenhuma requisição dela pode ler o carimbo antes da hora.</summary>
    private async Task<Func<Task<HttpResponseMessage>>> PrepareChangeAsync(string cause, HttpClient director, HttpClient own, SyntheticAccount target)
    {
        var newPassword = $"Nova!{Guid.NewGuid():N}";
        var userRoute = $"{UsersRoute}/{target.UserId}";
        var platformRoute = $"/api/platform/tenants/{target.TenantId}/users/{target.UserId}";
        switch (cause)
        {
            case "papel":
                return () => director.PutAsJsonAsync(userRoute, UpdateBody(target, "vendedor"));
            case "bloqueio":
                return () => director.PutAsJsonAsync(userRoute, UpdateBody(target, "gerente", "blocked"));
            case "inativacao":
                return () => director.PutAsJsonAsync(userRoute, UpdateBody(target, "gerente", "inactive"));
            case "senha pela diretoria":
                return () => director.PostAsJsonAsync($"{userRoute}/admin-reset-password", new { newPassword });
            case "senha pela propria pessoa":
                return () => own.PostAsJsonAsync($"{userRoute}/change-password", new { currentPassword = target.Password, newPassword });
            case "senha pelo link":
                var token = await PlantResetTokenAsync(target.UserId);
                return () => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword });
            case "sair de todos os dispositivos":
                return () => own.PostAsJsonAsync("/api/auth/sessions/revoke-all", new { confirm = true });
            case "verificacao em duas etapas":
                var secret = await PlantMfaSetupAsync(target.UserId);
                return () => own.PostAsJsonAsync("/api/auth/mfa/enable", new { code = Totp.Code(secret, Totp.StepAt(DateTime.UtcNow)) });
            case "plataforma redefine a senha":
                var resetting = await _kit.PlatformClientAsync();
                return () => resetting.PostAsJsonAsync($"{platformRoute}/reset-password", new { newPassword });
            case "plataforma encerra a sessao":
                var forcing = await _kit.PlatformClientAsync();
                return () => forcing.PostAsync($"{platformRoute}/force-logout", null);
            case "plataforma revoga as sessoes":
                var revoking = await _kit.PlatformClientAsync();
                return () => revoking.DeleteAsync($"{platformRoute}/sessions");
            default:
                throw new ArgumentOutOfRangeException(nameof(cause), cause, null);
        }
    }

    private async Task<string> PlantResetTokenAsync(Guid userId)
    {
        var token = $"token-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        user.SetPasswordResetToken(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(), DateTime.UtcNow.AddMinutes(30));
        await db.SaveChangesAsync();
        return token;
    }

    private async Task<byte[]> PlantMfaSetupAsync(Guid userId)
    {
        var secret = Totp.NewSecret();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
        user.StartMfaSetup(scope.ServiceProvider.GetRequiredService<IMfaSecretProtector>().Protect(secret, userId));
        await db.SaveChangesAsync();
        return secret;
    }

    /// <summary>
    /// Duas sessões da mesma pessoa batendo em paralelo enquanto o papel muda. Cada requisição anota se foi enviada depois da
    /// resposta do <c>PUT</c>; essas têm de cair todas. Quantas passam antes não importa, desde que alguma passe.
    /// </summary>
    [Fact]
    public async Task While_the_role_changes_no_request_sent_after_the_answer_passes_with_the_old_token()
    {
        const int workers = 8;
        const int requestsAfterTheAnswer = 200;
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var sessions = new[] { (await _kit.SignInAsync(target)).Client, (await _kit.SignInAsync(target)).Client };

        var outcomes = new ConcurrentBag<(bool SentAfterTheAnswer, HttpStatusCode Status)>();
        var someRequestPassed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var enough = new CancellationTokenSource();
        var answered = 0;
        var afterTheAnswer = 0;

        async Task HammerAsync(HttpClient client)
        {
            while (!enough.IsCancellationRequested)
            {
                var sentAfterTheAnswer = Volatile.Read(ref answered) == 1;
                using var response = await client.GetAsync(UsersRoute);
                outcomes.Add((sentAfterTheAnswer, response.StatusCode));
                if (response.StatusCode == HttpStatusCode.OK) someRequestPassed.TrySetResult();
                if (sentAfterTheAnswer && Interlocked.Increment(ref afterTheAnswer) >= requestsAfterTheAnswer) await enough.CancelAsync();
            }
        }

        var hammering = Enumerable.Range(0, workers).Select(i => Task.Run(() => HammerAsync(sessions[i % sessions.Length]))).ToArray();
        await someRequestPassed.Task.WaitAsync(TimeSpan.FromSeconds(60));

        await _kit.ChangeRoleAsync(director, target, "vendedor");
        Volatile.Write(ref answered, 1);
        await Task.WhenAll(hammering).WaitAsync(TimeSpan.FromSeconds(120));

        outcomes.Should().Contain(o => !o.SentAfterTheAnswer && o.Status == HttpStatusCode.OK);
        var late = outcomes.Where(o => o.SentAfterTheAnswer).ToList();
        late.Count.Should().BeGreaterThanOrEqualTo(requestsAfterTheAnswer);
        late.Where(o => o.Status != HttpStatusCode.Unauthorized).Should().BeEmpty("o token antigo não passa depois que o PUT respondeu");
    }

    /// <summary>
    /// Renovação em voo: ela leu a pessoa antes do commit (papel antigo e carimbo antigo, do mesmo instante) ou depois dele.
    /// No primeiro caso a resposta pode até trazer um par, mas ele já nasce morto; no segundo, nem isso.
    /// </summary>
    [Theory]
    [InlineData(DbCommandGate.Moment.AfterRunning)]
    [InlineData(DbCommandGate.Moment.BeforeRunning)]
    public async Task A_refresh_in_flight_during_the_change_never_yields_a_usable_pair(DbCommandGate.Moment heldAt)
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var (client, login) = await _kit.SignInAsync(target);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        using var hold = _factory.Gate.HoldNext(DbCommandGate.UserRowOf(target.UserId), heldAt);
        var refreshing = _kit.RefreshAsync(login.RefreshToken);
        await hold.ReachedAsync();

        await _kit.ChangeRoleAsync(director, target, "vendedor");
        hold.Release();

        await AssertNoUsablePairAsync(await refreshing);
        await _kit.ShouldBeRevokedAsync(client, UsersRoute);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(
            heldAt == DbCommandGate.Moment.AfterRunning ? HttpStatusCode.Conflict : HttpStatusCode.Unauthorized, _factory,
            "o token gasto na renovação em voo não abre mais nada");

        (await _kit.SignInAsync(target)).Body.Session.Role.Should().Be("vendedor");
    }

    [Fact]
    public async Task A_login_in_flight_during_the_change_never_yields_a_usable_pair()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);

        using var hold = _factory.Gate.HoldNext(DbCommandGate.UserRowOfLogin(target.Login), DbCommandGate.Moment.AfterRunning);
        var signingIn = _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(target.Login, target.Password));
        await hold.ReachedAsync();

        await _kit.ChangeRoleAsync(director, target, "vendedor");
        hold.Release();

        await AssertNoUsablePairAsync(await signingIn);

        var (renewed, relogin) = await _kit.SignInAsync(target);
        relogin.Session.Role.Should().Be("vendedor", "o login em voo não desfaz a mudança");
        await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    private async Task AssertNoUsablePairAsync(HttpResponseMessage response)
    {
        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await _factory.ServerLog.DescribeAsync(response));
                return;
            }

            var pair = await response.Content.ReadFromJsonAsync<JsonElement>();
            await _kit.ShouldBeRevokedAsync(_kit.ClientWith(pair.GetProperty("accessToken").GetString()!), UsersRoute);
            await _kit.RefreshAsync(pair.GetProperty("refreshToken").GetString()!).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        }
    }
}
