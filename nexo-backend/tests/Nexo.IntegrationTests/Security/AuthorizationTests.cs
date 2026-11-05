using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Features.Auth;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O que acontece com um token que já estava válido quando a situação muda, e com um token adulterado:
/// módulos (três portões: RequireModuleFilter, RequireModuleAttribute e RequireServiceModuleAttribute), papel,
/// usuário e empresa fora de atividade, security stamp, rotas da plataforma e troca de loja.
/// Cada teste cria a própria empresa (<see cref="SyntheticTenant"/>) e faz a requisição de verdade. Antes de mudar a situação,
/// o token faz uma chamada com sucesso: as entradas de cache de usuário, carimbo e empresa ficam preenchidas, como numa sessão em uso.
/// </summary>
[Collection("Integration")]
public class AuthorizationTests
{
    /// <summary>Rota da empresa sem exigência de módulo nem de papel.</summary>
    private const string CompanyRoute = "/api/categories";
    /// <summary>Rota só da gerência (AppRoles.Management).</summary>
    private const string UsersRoute = "/api/users";
    private const string AnyModule = "varejo";

    private readonly TestWebApplicationFactory _factory;

    public AuthorizationTests(TestWebApplicationFactory factory)
        => _factory = factory;

    // ────────────────────────────────────────────────────────────────────────────
    // MÓDULOS
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("restaurante", "/api/restaurante/areas")]   // RequireModuleFilter
    [InlineData("varejo", "/api/sales")]                    // RequireModuleAttribute
    [InlineData("service", "/api/v1/service/catalog")]      // RequireServiceModuleAttribute
    public async Task ModuleRequirement_ActiveModule_AllowsAccess(string module, string route)
    {
        var director = await SyntheticTenant.CreateAsync(_factory, module);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    [Theory]
    [InlineData("restaurante", "/api/restaurante/areas")]
    [InlineData("varejo", "/api/sales")]
    [InlineData("service", "/api/v1/service/catalog")]
    public async Task ModuleRequirement_InactiveModule_ReturnsInsufficientPermissions(string module, string route)
    {
        // sem o módulo: a rota dele fecha, e o resto da empresa continua aberto
        var withoutIt = await SyntheticTenant.CreateAsync(_factory, "build");
        var client = await AuthClientFactory.LoginAsync(_factory, withoutIt.Login, withoutIt.Password);
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        // a concessão existe, mas venceu. Outra empresa, para a recusa acima não vir do cache.
        var expired = await SyntheticTenant.CreateAsync(_factory, "build");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(expired.TenantId, module, expiresAt: DateTime.UtcNow.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        var expiredClient = await AuthClientFactory.LoginAsync(_factory, expired.Login, expired.Password);
        await expiredClient.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    [Theory]
    [InlineData("restaurante", "/api/restaurante/areas")]
    [InlineData("varejo", "/api/sales")]
    [InlineData("service", "/api/v1/service/catalog")]
    public async Task ModuleActivation_EventuallyAllowsAccess(string module, string route)
    {
        var director = await SyntheticTenant.CreateAsync(_factory);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var platform = await PlatformClientAsync();

        // a recusa fica no cache de 5 minutos dos portões; o mesmo token é usado do começo ao fim
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        await platform.PostAsJsonAsync($"/api/platform/tenants/{director.TenantId}/modules", new { moduleKey = module })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);   // agora o acesso está no cache

        await platform.DeleteAsync($"/api/platform/tenants/{director.TenantId}/modules/{module}")
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await client.GetAsync(route).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PAPÉIS (a matriz completa está em RbacMatrixTests)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminRole_CanAccessAdminEndpoints()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var client = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);

        var session = await client.GetFromJsonAsync<SessionDto>("/api/auth/me");
        session!.Role.Should().Be("diretoria");

        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.PostAsJsonAsync(UsersRoute, NewUserBody()).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);
    }

    [Fact]
    public async Task LowerRole_CannotAccessAdminOnlyEndpoints()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var manager = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Gerente);
        var seller = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Vendedor);
        var managerClient = await AuthClientFactory.LoginAsync(_factory, manager.Login, manager.Password);
        var sellerClient = await AuthClientFactory.LoginAsync(_factory, seller.Login, seller.Password);

        // gerente lista, mas criar e alterar usuários é só da Diretoria
        await managerClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var body = NewUserBody();
        await managerClient.PostAsJsonAsync(UsersRoute, body).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await managerClient.PutAsJsonAsync($"{UsersRoute}/{seller.UserId}", UpdateBody(seller, "gerente", "active"))
            .ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        // vendedor nem lista
        await sellerClient.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await sellerClient.PostAsJsonAsync(UsersRoute, body).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Login == body.Login)).Should().BeFalse("a criação recusada não grava nada");
        (await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == seller.UserId)).Role.Should().Be(UserRole.Vendedor, "a alteração recusada não muda o papel");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // USUÁRIO FORA DE ATIVIDADE, COM UM TOKEN QUE JÁ ESTAVA VÁLIDO
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InactiveUser_CannotAccessProtectedEndpoints()
        => await AssertStatusEndsTheSessionAsync("inactive");

    [Fact]
    public async Task BlockedUser_CannotAccessProtectedEndpoints()
        => await AssertStatusEndsTheSessionAsync("blocked");

    private async Task AssertStatusEndsTheSessionAsync(string status)
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var target = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Vendedor);
        var directorClient = await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, target.Login, target.Password);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await directorClient.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "vendedor", status))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await client.GetAsync("/api/auth/me").ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(target.Login, target.Password))
            .ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        // reativado, entra de novo com um login novo; o token de antes continua morto
        await directorClient.PutAsJsonAsync($"{UsersRoute}/{target.UserId}", UpdateBody(target, "vendedor", "active"))
            .ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        var renewed = await AuthClientFactory.LoginAsync(_factory, target.Login, target.Password);
        await renewed.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // EMPRESA FORA DE ATIVIDADE, COM UM TOKEN QUE JÁ ESTAVA VÁLIDO
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SuspendedTenant_Users_CannotAccessEndpoints()
        => await AssertTenantStatusStopsTheTokenAsync(TenantStatus.Suspended);

    [Fact]
    public async Task InactiveTenant_Users_CannotAccessEndpoints()
        => await AssertTenantStatusStopsTheTokenAsync(TenantStatus.Cancelled);

    private async Task AssertTenantStatusStopsTheTokenAsync(TenantStatus status)
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, director.Login, director.Password);
        var platform = await PlatformClientAsync();
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        await platform.PutAsJsonAsync($"/api/platform/tenants/{director.TenantId}/status", new { status = status.ToString() })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        // sem esperar os 5 minutos do cache da empresa: leitura, escrita e renovação param na hora
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await client.PostAsJsonAsync(CompanyRoute, new { name = "Nao grava" }).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            (await db.Categories.IgnoreQueryFilters().AnyAsync(c => c.TenantId == director.TenantId)).Should().BeFalse("a escrita recusada não grava nada");
        }

        // reativada, o mesmo token volta a valer: a recusa não ficou presa no cache
        await platform.PutAsJsonAsync($"/api/platform/tenants/{director.TenantId}/status", new { status = nameof(TenantStatus.Active) })
            .ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ROTAS DA PLATAFORMA (mais rotas em PlatformAuthorizationTests e PlatformSecurityTests)
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlatformAdmin_HasSeparateAuth()
    {
        const string platformRoute = "/api/platform/tenants";
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, director.Login, director.Password);
        var platform = await PlatformClientAsync();

        await platform.GetAsync(platformRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);   // a rota existe: um 404 abaixo não passaria
        await _factory.CreateApiClient().GetAsync(platformRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        await client.GetAsync(platformRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        // a diretoria da empresa não muda a situação da própria empresa
        await client.PutAsJsonAsync($"/api/platform/tenants/{director.TenantId}/status", new { status = nameof(TenantStatus.Cancelled) })
            .ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        // um token de empresa que se declara da plataforma, sem a chave do servidor
        var payload = ForgedToken.PayloadOf(login.AccessToken);
        payload["type"] = "platform";
        foreach (var (how, forged) in ForgedToken.WithoutTheServerKey(login.AccessToken, payload))
            await ClientWith(forged).GetAsync(platformRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory, how);

        // e o contrário: a sessão da plataforma não entra na área das empresas
        var refused = await platform.GetAsync(CompanyRoute);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("platform_token_scope");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // TOKEN ADULTERADO
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TamperingWithToken_ChangeRole_FailsValidation()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var seller = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Vendedor);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, seller.Login, seller.Password);
        await client.GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        var payload = ForgedToken.PayloadOf(login.AccessToken);
        payload["role"] = new JsonArray("diretoria", "Diretoria");

        // Controle: assinado com a chave do servidor, este payload abre a rota. Os 401 abaixo vêm da assinatura, não de um token malformado.
        var signedByTheServerKey = ForgedToken.Sign(login.AccessToken, payload, ServerKey);
        await ClientWith(signedByTheServerKey).GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);

        foreach (var (how, forged) in ForgedToken.WithoutTheServerKey(login.AccessToken, payload))
            await ClientWith(forged).GetAsync(UsersRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory, how);

        // assinatura removida do token legítimo, sem mudar nada no payload
        var parts = login.AccessToken.Split('.');
        await ClientWith($"{parts[0]}.{parts[1]}.").GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
    }

    [Fact]
    public async Task TenantIdClaim_CannotBeChangedByUser()
    {
        var victim = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var victimClient = await AuthClientFactory.LoginAsync(_factory, victim.Login, victim.Password);
        var secret = $"Categoria da vitima {Guid.NewGuid():N}";
        await victimClient.PostAsJsonAsync(CompanyRoute, new { name = secret }).ShouldHaveStatusAsync(HttpStatusCode.Created, _factory);

        var attacker = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, attacker.Login, attacker.Password);
        var payload = ForgedToken.PayloadOf(login.AccessToken);
        payload["tenantId"] = victim.TenantId.ToString();

        // Mesmo com a chave do servidor (um defeito na emissão, um segredo vazado): o usuário não pertence àquela empresa.
        // Primeira chamada lê o usuário do banco; depois da chamada legítima, lê do cache.
        var signedByTheServerKey = ForgedToken.Sign(login.AccessToken, payload, ServerKey);
        await ClientWith(signedByTheServerKey).GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);
        var own = await client.GetAsync(CompanyRoute);
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.Content.ReadAsStringAsync()).Should().NotContain(secret);
        await ClientWith(signedByTheServerKey).GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        foreach (var (how, forged) in ForgedToken.WithoutTheServerKey(login.AccessToken, payload))
            await ClientWith(forged).GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory, how);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // SECURITY STAMP (o link de "esqueci a senha" está em AccountRecoveryTests)
    // ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("senha redefinida pela diretoria", true)]
    [InlineData("senha trocada pela própria pessoa", true)]
    [InlineData("senha redefinida pela plataforma", true)]
    [InlineData("sessões encerradas pela plataforma", false)]
    public async Task UserSecurityStamp_Changed_InvalidatesToken(string cause, bool passwordChanges)
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var target = await SyntheticTenant.AddUserAsync(_factory, director, UserRole.Vendedor);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, target.Login, target.Password);
        await client.GetAsync(CompanyRoute).ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
        var stampBefore = await SecurityStampOfAsync(target.UserId);

        var newPassword = $"Nova!{Guid.NewGuid():N}";
        var platformPath = $"/api/platform/tenants/{target.TenantId}/users/{target.UserId}";
        var change = cause switch
        {
            "senha redefinida pela diretoria" =>
                (await AuthClientFactory.LoginAsync(_factory, director.Login, director.Password))
                    .PostAsJsonAsync($"{UsersRoute}/{target.UserId}/admin-reset-password", new { newPassword }),
            "senha trocada pela própria pessoa" =>
                client.PostAsJsonAsync($"{UsersRoute}/{target.UserId}/change-password", new { currentPassword = target.Password, newPassword }),
            "senha redefinida pela plataforma" =>
                (await PlatformClientAsync()).PostAsJsonAsync($"{platformPath}/reset-password", new { newPassword }),
            _ => (await PlatformClientAsync()).PostAsync($"{platformPath}/force-logout", null),
        };
        await change.ShouldHaveStatusAsync(HttpStatusCode.NoContent, _factory);

        (await SecurityStampOfAsync(target.UserId)).Should().NotBe(stampBefore);
        var refused = await client.GetAsync(CompanyRoute);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("session_revoked");
        await RefreshAsync(login.RefreshToken).ShouldHaveStatusAsync(HttpStatusCode.Unauthorized, _factory);

        // só a sessão antiga morre: um login novo entra, com a senha que vale agora
        var oldPasswordStatus = passwordChanges ? HttpStatusCode.Unauthorized : HttpStatusCode.OK;
        await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(target.Login, target.Password))
            .ShouldHaveStatusAsync(oldPasswordStatus, _factory);
        var renewed = await AuthClientFactory.LoginAsync(_factory, target.Login, passwordChanges ? newPassword : target.Password);
        await renewed.GetAsync("/api/auth/me").ShouldHaveStatusAsync(HttpStatusCode.OK, _factory);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // LOJAS
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>Não existe vínculo por usuário: as lojas acessíveis são as lojas ativas da empresa do usuário.</summary>
    [Fact]
    public async Task UserCanOnlyAccessAssignedStores()
    {
        var director = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var stranger = await SyntheticTenant.CreateAsync(_factory, AnyModule);
        var (secondStore, inactiveStore) = await AddStoresAsync(director.TenantId);
        var (client, login) = await AuthClientFactory.LoginWithBodyAsync(_factory, director.Login, director.Password);

        foreach (var store in new[] { Guid.NewGuid(), stranger.StoreId, inactiveStore })
            await client.PostAsJsonAsync("/api/auth/switch-store", new { storeId = store.ToString(), refreshToken = login.RefreshToken })
                .ShouldHaveStatusAsync(HttpStatusCode.Forbidden, _factory);

        // a recusa não gastou a sessão, e a loja certa funciona
        var switched = await client.PostAsJsonAsync("/api/auth/switch-store", new { storeId = secondStore.ToString(), refreshToken = login.RefreshToken });
        switched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await switched.Content.ReadFromJsonAsync<SwitchStoreResponse>())!.StoreId.Should().Be(secondStore.ToString());
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string ServerKey => _factory.Services.GetRequiredService<IConfiguration>()["Jwt:Secret"]!;

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => _factory.CreateApiClient().PostAsJsonAsync("/api/auth/refresh", TestCredentials.RefreshPayload(refreshToken));

    private static CreateUserRequest NewUserBody()
    {
        var login = $"novo{Guid.NewGuid():N}"[..20];
        return new CreateUserRequest("Pessoa Nova", $"{login}@{TestCredentials.TestDomain}", login, TestCredentials.SameTenantManagerPassword, "vendedor", null, null, false);
    }

    private static object UpdateBody(SyntheticAccount user, string role, string status)
        => new { fullName = user.FullName, email = user.Email, role, phone = (string?)null, notes = (string?)null, status };

    private async Task<string> SecurityStampOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();
    }

    private async Task<(Guid Active, Guid Inactive)> AddStoresAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tag = Guid.NewGuid().ToString("N")[..12];
        var active = Store.Create(tenantId, $"Segunda {tag}", $"segunda-{tag}");
        var inactive = Store.Create(tenantId, $"Fechada {tag}", $"fechada-{tag}");
        inactive.Deactivate();
        db.Stores.AddRange(active, inactive);
        await db.SaveChangesAsync();
        return (active.Id, inactive.Id);
    }

    /// <summary>Um administrador da plataforma só deste teste.</summary>
    private async Task<HttpClient> PlatformClientAsync()
    {
        var email = $"authz-{Guid.NewGuid():N}"[..17] + "@platform.test";
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

    /// <summary>Monta tokens como um atacante faria: a partir de um token verdadeiro, com o payload alterado.</summary>
    private static class ForgedToken
    {
        public static JsonObject PayloadOf(string token)
        {
            var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            var padded = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
            return JsonNode.Parse(Convert.FromBase64String(padded))!.AsObject();
        }

        /// <summary>O payload alterado com a assinatura de uma chave qualquer.</summary>
        public static string Sign(string original, JsonObject payload, string key)
        {
            var unsigned = $"{original.Split('.')[0]}.{Encode(payload)}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return $"{unsigned}.{Encode(hmac.ComputeHash(Encoding.ASCII.GetBytes(unsigned)))}";
        }

        /// <summary>Todas as formas de apresentar um payload alterado para quem não tem a chave do servidor.</summary>
        public static IEnumerable<(string How, string Token)> WithoutTheServerKey(string original, JsonObject payload)
        {
            var parts = original.Split('.');
            var altered = Encode(payload);
            yield return ("assinado com outra chave", Sign(original, payload, "outra-chave-qualquer-com-mais-de-32-caracteres"));
            yield return ("com a assinatura do token original", $"{parts[0]}.{altered}.{parts[2]}");
            yield return ("com a assinatura removida", $"{parts[0]}.{altered}.");
            yield return ("sem o terceiro segmento", $"{parts[0]}.{altered}");
            yield return ("com alg none", $"{Encode(new JsonObject { ["alg"] = "none", ["typ"] = "JWT" })}.{altered}.");
        }

        private static string Encode(JsonNode node) => Encode(Encoding.UTF8.GetBytes(node.ToJsonString()));

        private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>DTOs for test requests</summary>
public record CreateUserRequest(
    string FullName,
    string Email,
    string Login,
    string Password,
    string Role,
    string? Phone,
    string? Notes,
    bool RequirePasswordChange);

public record UserDto(
    string Id,
    string FullName,
    string Email,
    string Login,
    string Role,
    string? Status = null);

public record SwitchStoreResponse(
    string AccessToken,
    string RefreshToken,
    string StoreId);
