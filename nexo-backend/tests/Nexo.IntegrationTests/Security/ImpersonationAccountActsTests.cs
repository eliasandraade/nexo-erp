using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// O acesso da plataforma a uma empresa (impersonação) é temporário, auditado e nunca vira uma sessão normal. Por isso,
/// durante ele, nenhum ato que crie ou troque a credencial de um usuário: senão o administrador sairia do acesso com
/// uma senha conhecida (redefinida, ou de um usuário novo) ou com o e-mail de recuperação trocado. Cada teste usa uma
/// empresa própria — o administrador compartilhado da suíte nunca é tocado.
/// </summary>
[Collection("Integration")]
public class ImpersonationAccountActsTests
{
    private readonly TestWebApplicationFactory _factory;

    public ImpersonationAccountActsTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task An_impersonation_cannot_create_or_change_any_user_credential()
    {
        var (tenantId, ownerId, ownerEmail) = await NewTenantAsync();
        var imp = await ImpersonateAsync(tenantId);

        await ExpectForbiddenAsync(imp.PostAsJsonAsync($"/api/users/{ownerId}/admin-reset-password",
            new { newPassword = "n" + Guid.NewGuid().ToString("N")[..12] }));
        await ExpectForbiddenAsync(imp.PostAsJsonAsync("/api/users", new
        {
            fullName = "Conta Nova", email = $"nova-{Guid.NewGuid():N}"[..14] + "@empresa.test",
            login = $"nova{Guid.NewGuid():N}"[..12], password = "n" + Guid.NewGuid().ToString("N")[..12],
            role = "diretoria", phone = (string?)null, notes = (string?)null,
        }));
        await ExpectForbiddenAsync(imp.PutAsJsonAsync($"/api/users/{ownerId}", new
        {
            fullName = "Dono", email = "outro-endereco@empresa.test", role = "diretoria",
            phone = (string?)null, notes = (string?)null, status = "active",
        }));
        await ExpectForbiddenAsync(imp.PostAsJsonAsync($"/api/users/{ownerId}/resend-invite", new { }));
        await ExpectForbiddenAsync(imp.PostAsJsonAsync($"/api/users/{ownerId}/mfa/disable", new { }));

        // nada mudou na conta do dono, e nenhum usuário novo apareceu
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var owner = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == ownerId);
        owner.Email.Should().Be(ownerEmail);
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(TestCredentials.AdminPassword, owner.PasswordHash)
            .Should().BeTrue("a senha do dono continua a dele");
        (await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId)).Should().Be(1);

        // e a leitura segue liberada: o acesso serve para ver e ajudar
        (await imp.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task ExpectForbiddenAsync(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, response.RequestMessage?.RequestUri?.ToString());
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("impersonation_forbidden");
    }

    /// <summary>Uma empresa nova (cadastro) com o dono ativo.</summary>
    private async Task<(Guid TenantId, Guid OwnerId, string OwnerEmail)> NewTenantAsync()
    {
        var email = $"imp-{Guid.NewGuid():N}"[..16] + "@" + TestCredentials.TestDomain;
        var reg = await _factory.CreateApiClient().PostAsJsonAsync("/api/auth/register",
            new { name = "Empresa Acesso Plataforma", email, password = TestCredentials.AdminPassword });
        reg.StatusCode.Should().Be(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Login == email);
        user.Activate();
        await db.SaveChangesAsync();
        return (user.TenantId, user.Id, user.Email);
    }

    private async Task<HttpClient> ImpersonateAsync(Guid tenantId)
    {
        var email = $"ops-{Guid.NewGuid():N}"[..14] + "@platform.test";
        var password = "Px!" + Guid.NewGuid().ToString("N")[..20];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
            db.PlatformUsers.Add(PlatformUser.Create(email, scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password), "super_admin"));
            await db.SaveChangesAsync();
        }
        var login = await _factory.CreateApiClient().PostAsJsonAsync("/api/platform/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var platform = _factory.CreateApiClient();
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

        var granted = await platform.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/impersonate",
            new { reason = "Suporte: conferir cadastro de usuários" });
        granted.StatusCode.Should().Be(HttpStatusCode.OK, await granted.Content.ReadAsStringAsync());
        var imp = _factory.CreateApiClient();
        imp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await granted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());
        return imp;
    }
}
