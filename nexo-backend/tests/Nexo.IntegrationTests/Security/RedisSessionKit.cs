using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>O que os testes de sessão com Redis de verdade fazem o tempo todo, sempre pelo HTTP real.</summary>
internal sealed class RedisSessionKit
{
    /// <summary>Rota da empresa sem exigência de módulo nem de papel.</summary>
    public const string CompanyRoute = "/api/categories";
    /// <summary>Rota só da gerência (AppRoles.Management).</summary>
    public const string UsersRoute = "/api/users";
    public const string HubNegotiation = "/hubs/restaurant/negotiate?negotiateVersion=1";
    private const string AnyModule = "varejo";

    private readonly RedisWebApplicationFactory _factory;

    public RedisSessionKit(RedisWebApplicationFactory factory) => _factory = factory;

    /// <summary>Uma empresa nova, a Diretoria já logada e uma pessoa com o papel pedido.</summary>
    public async Task<(HttpClient Director, SyntheticAccount DirectorAccount, SyntheticAccount Target)> NewCompanyAsync(UserRole targetRole)
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var target = await SyntheticTenant.AddUserAsync(_factory, director, targetRole);
        return (await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password), director, target);
    }

    public Task<(HttpClient Client, LoginResponse Body)> SignInAsync(SyntheticAccount account)
        => AuthClientFactory.LoginWithBodyAsync(_factory, account.Login, account.Password);

    public static object UpdateBody(SyntheticAccount user, string role, string status = "active", string? notes = null, string? fullName = null)
        => new { fullName = fullName ?? user.FullName, email = user.Email, role, phone = (string?)null, notes, status };

    public Task ChangeRoleAsync(HttpClient director, SyntheticAccount target, string role)
        => director.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, role)).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

    public HttpClient ClientWith(string token)
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(refreshToken));

    public async Task<string> SecurityStampOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();
    }

    /// <summary>O token morreu: 401 <c>session_revoked</c> em qualquer rota, e a resposta diz por quê.</summary>
    public async Task ShouldBeRevokedAsync(HttpClient client, string route = CompanyRoute)
    {
        using var refused = await client.GetAsync(route);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await _factory.ServerLog.DescribeAsync(refused));
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("session_revoked");
    }

    /// <summary>
    /// Sem o cache, quem barra uma conta bloqueada pode ser a checagem da situação, que vem antes da do carimbo e responde
    /// 401 sem código. O que importa durante a queda é o token não passar.
    /// </summary>
    public async Task ShouldBeRevokedOrRefusedAsync(HttpClient client, string route = CompanyRoute)
    {
        using var refused = await client.GetAsync(route);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await _factory.ServerLog.DescribeAsync(refused));
    }

    /// <summary>Um administrador da plataforma só deste teste.</summary>
    public async Task<HttpClient> PlatformClientAsync()
    {
        var email = $"redis-{Guid.NewGuid():N}"[..17] + "@platform.test";
        const string password = TestCredentials.PlatformPassword;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        return ClientWith((await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!);
    }

    /// <summary>O identificador de um token, para conferir a entrada dele no Redis.</summary>
    public static string JtiOf(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=')));
        return JsonDocument.Parse(payload).RootElement.GetProperty("jti").GetString()!;
    }
}
