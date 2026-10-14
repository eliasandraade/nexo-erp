using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexo.Api.Controllers;
using Xunit;

namespace Nexo.IntegrationTests.Security;

/// <summary>
/// Structural guard for the RBAC matrix (docs/security/rbac-matrix.md): every API action must
/// either be anonymous on purpose, carry a role/policy requirement, or be listed below as open to
/// any authenticated user of the tenant. A new endpoint that forgets its role fails this test
/// instead of silently shipping open to Vendedor/Estoquista/Cozinha tokens.
/// </summary>
public class RbacCoverageTests
{
    /// <summary>
    /// Actions any signed-in tenant user may call: their own session/profile, catalog reads the
    /// operational screens (PDV, salão, cozinha) need, and harmless lookups. Keep it short.
    /// </summary>
    private static readonly HashSet<string> OpenToAnyTenantUser = new(StringComparer.Ordinal)
    {
        // Own session / profile
        "Auth.*",
        // Own account only — enforced in the action (id must be the caller, or caller is management)
        "Users.GetById", "Users.ChangePassword",
        // A Vendedor asks a manager to type their credentials (rate-limited)
        "Users.ValidateManager",
        "Store.*", "Tenants.*", "Features.*", "Settings.Get",
        // Billing status is shown to everyone (trial banner); changes are role-gated
        "Billing.*",
        // Catalog reads used by PDV / salão / cozinha
        "Products.GetAll", "Products.GetPaged", "Products.GetById", "Products.GetProductSheet",
        "Categories.GetAll", "Categories.GetById",
        "Customers.GetAll", "Customers.GetPaged", "Customers.GetById",
        // Lookups with no tenant data
        "Barcode.*", "Lookup.*", "Weather.*",
        // Service: preset/settings reads drive navigation for every role (decided in #38)
        "Service.*",
    };

    /// <summary>Controllers that are anonymous by design (public menu, public booking, health…).</summary>
    private static readonly HashSet<string> PublicControllers = new(StringComparer.Ordinal)
    {
        "PublicOrdersController",   // public menu / delivery checkout (rate-limited, slug-scoped)
        "PlatformAuthController",   // platform admin login
    };

    [Fact]
    public void Every_action_declares_who_may_call_it()
    {
        var unprotected = ApiActions()
            .Where(a => !IsAnonymous(a) && !HasRoleOrPolicy(a) && !IsAllowListed(a))
            .Select(Name)
            .OrderBy(n => n)
            .ToList();

        string.Join(", ", unprotected).Should().BeEmpty(
            "every endpoint needs [Authorize(Roles = AppRoles.X)] / a policy, or an explicit entry in " +
            "OpenToAnyTenantUser after a deliberate decision");
    }

    [Fact]
    public void Allow_list_has_no_stale_entries()
    {
        var names = ApiActions().Select(Name).ToHashSet();
        var controllers = names.Select(n => n.Split('.')[0]).ToHashSet();
        var stale = OpenToAnyTenantUser.Where(e => e.EndsWith(".*")
            ? !controllers.Contains(e[..^2])
            : !names.Contains(e));
        string.Join(", ", stale).Should().BeEmpty("allow-list entries must point at real actions");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<MethodInfo> ApiActions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    private static string Name(MethodInfo m) =>
        $"{m.DeclaringType!.Name.Replace("Controller", "")}.{m.Name.Replace("Async", "")}";

    private static bool IsAnonymous(MethodInfo m) =>
        m.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || m.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || PublicControllers.Contains(m.DeclaringType!.Name);

    private static bool HasRoleOrPolicy(MethodInfo m) =>
        m.GetCustomAttributes<AuthorizeAttribute>().Concat(m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>())
            .Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.Policy));

    private static bool IsAllowListed(MethodInfo m)
    {
        var name = Name(m);
        var controller = name.Split('.')[0];
        return OpenToAnyTenantUser.Contains(name) || OpenToAnyTenantUser.Contains($"{controller}.*");
    }
}
