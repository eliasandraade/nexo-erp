using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Auth;

/// <summary>
/// Política de rotação do refresh token (mesma regra das sessões da plataforma):
///   - uso único: duas abas renovando com o mesmo token ao mesmo tempo → uma troca (200), a outra recebe 409
///     "refresh_already_rotated" e nenhum token irmão é emitido; a sessão continua com o par da vencedora;
///   - o token trocado reapresentado dentro da carência (30 s) → 409, sem tokens; depois dela, ou um token mais antigo
///     → a família inteira (a sessão daquele login) é encerrada e o caso vai para a auditoria;
///   - logout encerra a família inteira (outras abas, e um par trocado agora mesmo), não os outros logins;
///   - uma família encerrada não renova mais, nem com um token que tenha nascido depois do encerramento.
/// </summary>
[Collection("Integration")]
public class RefreshTokenFamilyTests
{
    private readonly TestWebApplicationFactory _factory;

    public RefreshTokenFamilyTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Two_tabs_refreshing_at_once_one_rotates_the_other_gets_409_and_the_session_survives()
    {
        var login = await LoginAsync();

        var results = await Task.WhenAll(RefreshAsync(login.RefreshToken), RefreshAsync(login.RefreshToken));

        results.Select(r => r.StatusCode).Should().BeEquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            "o token é de uso único: uma aba troca, a outra é avisada de que a troca já aconteceu");
        var conflict = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        conflictBody.GetProperty("code").GetString().Should().Be("refresh_already_rotated");
        conflictBody.TryGetProperty("accessToken", out _).Should().BeFalse("quem perde a corrida não recebe tokens");

        var winner = (await results.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<RefreshResponse>())!;

        // a aba que perdeu volta com o mesmo token logo depois (ainda na carência): 409 de novo, a sessão não cai
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // e segue com o par da vencedora, lido do armazenamento compartilhado
        (await RefreshAsync(winner.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a corrida entre abas não pode derrubar a sessão");
    }

    [Fact]
    public async Task Replaying_a_rotated_token_after_the_grace_window_ends_the_whole_family()
    {
        var login = await LoginAsync();
        var otherDevice = await LoginAsync();

        var rotated = await RefreshOkAsync(login.RefreshToken);
        await AgeLastRotationAsync(rotated.RefreshToken, AuthService.RotationGrace + TimeSpan.FromSeconds(1));

        var replay = await RefreshAsync(login.RefreshToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "fora da carência, um token já usado indica uma cópia");
        (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("session_ended");

        (await RefreshAsync(rotated.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a família inteira termina: quem ficou com o token novo também perde a sessão");

        (await RefreshAsync(otherDevice.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "só aquela sessão termina; os outros logins do usuário seguem");

        var family = Family(login.RefreshToken);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var critical = await db.AuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.ActionType == AuditActions.UserSessionRevoked && a.Severity == AuditSeverity.Critical)
            .Select(a => a.MetadataJson)
            .ToListAsync();
        critical.Should().Contain(m => m != null && m.Contains(family),
            "a reapresentação de um token já usado fica na auditoria como crítica");
    }

    [Fact]
    public async Task Replaying_an_older_token_of_the_family_ends_it_without_waiting_for_the_grace()
    {
        var login = await LoginAsync();
        var first = await RefreshOkAsync(login.RefreshToken);
        var second = await RefreshOkAsync(first.RefreshToken);

        // o login já foi trocado duas vezes: não é a corrida de uma aba, é uma cópia antiga
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ends_the_whole_family_including_a_tab_holding_the_previous_token()
    {
        var login = await LoginAsync();
        var otherDevice = await LoginAsync();
        var rotated = await RefreshOkAsync(login.RefreshToken);   // a aba A trocou; a aba B ainda tem o token do login

        // a aba B sai com o token antigo que tinha
        var tabB = _factory.CreateApiClient().WithBearer(rotated.AccessToken);
        (await tabB.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(rotated.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "o logout encerra a sessão em todas as abas, inclusive o par que a aba A acabou de receber");
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "depois do logout não há mais carência: 401, não 409");
        (await RefreshAsync(otherDevice.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK,
            "o logout de uma sessão não derruba os outros dispositivos");
    }

    [Fact]
    public async Task Logout_with_the_current_token_also_kills_the_token_it_replaced()
    {
        var login = await LoginAsync();
        var rotated = await RefreshOkAsync(login.RefreshToken);

        var client = _factory.CreateApiClient().WithBearer(rotated.AccessToken);
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rotated.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(rotated.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "o token anterior não ressuscita uma sessão encerrada");
    }

    [Fact]
    public async Task A_revoked_family_cannot_refresh_even_with_a_token_issued_after_the_revocation()
    {
        var login = await LoginAsync();
        var client = _factory.CreateApiClient().WithBearer(login.AccessToken);
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = login.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // um par da mesma família emitido depois do logout (o caso de uma renovação concorrente que termina depois dele)
        string late;
        using (var scope = _factory.Services.CreateScope())
        {
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            var claims = jwt.ValidateRefreshToken(login.RefreshToken)!;
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == claims.UserId);
            var pair = jwt.GenerateTokenPair(user, "t", "T", [], Guid.Empty, [], claims.Family);
            var next = jwt.ValidateRefreshToken(pair.RefreshToken)!;
            await cache.SetAsync($"refresh:valid:{next.Jti}", new RefreshTokenEntry(user.Id, user.TenantId, null, user.SecurityStamp),
                TimeSpan.FromHours(1));
            late = pair.RefreshToken;
        }

        (await RefreshAsync(late)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "uma família encerrada não volta, nem por um token que nasceu depois do encerramento");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<LoginResponse> LoginAsync()
    {
        var (_, body) = await AuthClientFactory.LoginAsAdminWithBodyAsync(_factory);
        return body;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(refreshToken));

    private async Task<RefreshResponse> RefreshOkAsync(string refreshToken)
    {
        var response = await RefreshAsync(refreshToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RefreshResponse>())!;
    }

    private string Family(string refreshToken)
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtTokenService>().ValidateRefreshToken(refreshToken)!.Family;
    }

    /// <summary>Recua no tempo a última troca da família — simula a passagem da carência sem esperar por ela.</summary>
    private async Task AgeLastRotationAsync(string anyTokenOfTheFamily, TimeSpan by)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var key = $"refresh:family:{Family(anyTokenOfTheFamily)}";
        var head = await cache.GetAsync<RefreshFamilyHead>(key);
        head.Should().NotBeNull("a troca registra a família");
        await cache.SetAsync(key, head! with { RotatedAt = head.RotatedAt - by }, TimeSpan.FromHours(1));
    }
}
