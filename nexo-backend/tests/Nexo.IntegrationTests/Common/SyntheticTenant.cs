using Microsoft.Extensions.DependencyInjection;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Infrastructure.Persistence;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Common;

/// <summary>Um usuário sintético, com a empresa e a loja a que pertence.</summary>
public sealed record SyntheticAccount(
    Guid TenantId,
    Guid StoreId,
    Guid UserId,
    string Login,
    string Email,
    string FullName,
    string Password);

/// <summary>
/// Cria empresa, loja e usuários só para o teste que os pede, sem depender do seed nem do que outro teste deixou no banco.
/// Uma empresa sem nenhum módulo com acesso fica somente para leitura (TenantAccessMiddleware): quem precisa escrever pede um módulo.
/// </summary>
public static class SyntheticTenant
{
    /// <summary>Uma empresa nova, com uma loja, os módulos pedidos (concessão da plataforma) e um usuário da Diretoria.</summary>
    public static async Task<SyntheticAccount> CreateAsync(TestWebApplicationFactory factory, params string[] modules)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var tag = NewTag();
        var taxId = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var tenant = Tenant.Create($"Sintetica {tag}", taxId, $"sintetica-{tag}@{TestCredentials.TestDomain}");
        db.Tenants.Add(tenant);
        var store = Store.Create(tenant.Id, $"Loja {tag}", $"loja-{tag}");
        db.Stores.Add(store);
        foreach (var module in modules)
            db.ModuleSubscriptions.Add(ModuleSubscription.CreateAdminGrant(tenant.Id, module));

        var director = AddUser(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), tenant.Id, store.Id, UserRole.Diretoria, UserStatus.Active);
        await db.SaveChangesAsync();
        return director;
    }

    /// <summary>Mais um usuário na mesma empresa de <paramref name="colleague"/>.</summary>
    public static async Task<SyntheticAccount> AddUserAsync(
        TestWebApplicationFactory factory,
        SyntheticAccount colleague,
        UserRole role,
        UserStatus status = UserStatus.Active)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NexoDbContext>();
        var user = AddUser(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), colleague.TenantId, colleague.StoreId, role, status);
        await db.SaveChangesAsync();
        return user;
    }

    private static SyntheticAccount AddUser(NexoDbContext db, IPasswordHasher hasher, Guid tenantId, Guid storeId, UserRole role, UserStatus status)
    {
        var login = $"sint-{NewTag()}";
        var email = $"{login}@{TestCredentials.TestDomain}";
        const string fullName = "Pessoa Sintetica";
        const string password = TestCredentials.SameTenantManagerPassword;
        var user = User.Create(tenantId: tenantId, fullName: fullName, email: email, login: login, passwordHash: hasher.Hash(password), role: role);
        user.SetStatus(status);
        db.Users.Add(user);
        return new SyntheticAccount(tenantId, storeId, user.Id, login, email, fullName, password);
    }

    private static string NewTag() => Guid.NewGuid().ToString("N")[..12];
}
