using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Nexo.Domain.Enums;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;
using static Nexo.IntegrationTests.Security.RedisSessionKit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Redis fora do ar enquanto a sessão de alguém é encerrada. Durante a queda o aviso para o cache se perde; quando o Redis
/// volta, as chaves de antes da queda voltam junto (o snapshot, ou o processo que só estava congelado), com o tempo de vida
/// que ainda tinham. Nada disso pode liberar o token antigo. Só um Redis de verdade mostra este caso.
/// </summary>
[Collection(RedisRealCollection.Name)]
public sealed class SessionRevocationRedisOutageTests : IClassFixture<RedisWebApplicationFactory>
{
    private readonly RedisWebApplicationFactory _factory;
    private readonly RedisSessionKit _kit;

    public SessionRevocationRedisOutageTests(RedisWebApplicationFactory factory)
    {
        _factory = factory;
        _kit = new RedisSessionKit(factory);
    }

    public enum Outage
    {
        /// <summary>O contêiner parado: conexão recusada, como num redeploy do serviço.</summary>
        Stopped,
        /// <summary>O processo congelado: a conexão fica aberta e as chamadas estouram o tempo, o que abre o disjuntor.</summary>
        Frozen,
    }

    private Task BeginAsync(Outage outage) => outage == Outage.Stopped ? _factory.StopRedisAsync() : _factory.FreezeRedisAsync();

    private Task EndAsync(Outage outage) => outage == Outage.Stopped ? _factory.StartRedisAsync() : _factory.UnfreezeRedisAsync();

    [Theory]
    [InlineData(Outage.Stopped, "vendedor", "active")]
    [InlineData(Outage.Stopped, "gerente", "blocked")]
    [InlineData(Outage.Frozen, "vendedor", "active")]
    [InlineData(Outage.Frozen, "gerente", "blocked")]
    public async Task The_old_token_stays_dead_during_the_outage_and_after_redis_comes_back(Outage outage, string role, string status)
    {
        var (director, directorAccount, target) = await _kit.NewCompanyAsync(UserRole.Gerente);
        var bystander = await SyntheticTenant.AddUserAsync(_factory, directorAccount, UserRole.Gerente);
        var (bystanderClient, _) = await _kit.SignInAsync(bystander);
        var (client, login) = await _kit.SignInAsync(target);
        // os caches da sessão em uso ficam preenchidos, como no dia a dia
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await bystanderClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await BeginAsync(outage);
        try
        {
            await director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, role, status))
                .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory, "a mudança não depende do cache");

            // sem cache, a validação vai ao banco: o token antigo cai e o de quem não mudou continua valendo
            await _kit.ShouldBeRevokedOrRefusedAsync(client);
            await bystanderClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
            await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        }
        finally
        {
            await EndAsync(outage);
        }

        await _kit.ShouldBeRevokedAsync(client, UsersRoute);
        await _kit.RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await bystanderClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        if (status == "active")
        {
            var (renewed, relogin) = await _kit.SignInAsync(target);
            relogin.Session.Role.Should().Be(role);
            await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        }
    }

    [Fact]
    public async Task A_login_during_the_outage_already_has_the_new_role()
    {
        var (director, _, target) = await _kit.NewCompanyAsync(UserRole.Gerente);

        await _factory.StopRedisAsync();
        try
        {
            await _kit.ChangeRoleAsync(director, target, "vendedor");

            var (renewed, relogin) = await _kit.SignInAsync(target);
            relogin.Session.Role.Should().Be("vendedor");
            await renewed.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
            await renewed.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        }
        finally
        {
            await _factory.StartRedisAsync();
        }
    }
}
