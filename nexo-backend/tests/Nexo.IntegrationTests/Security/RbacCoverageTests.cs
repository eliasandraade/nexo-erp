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
        // Own session / account (GetById and ChangePassword enforce "self or management" in the action)
        "Auth.Me", "Auth.SwitchStore", "Auth.Logout",
        "Auth.Sessions", "Auth.RevokeAllSessions",   // own sessions only (any role)
        "Users.GetById", "Users.ChangePassword",
        // A Vendedor asks a manager to type their credentials (rate-limited per user)
        "Auth.VerifyManager", "Users.ValidateManager",
        // Shell data every role renders: stores, own tenant, feature flags, settings, billing banner
        "Store.GetMyStores", "Tenants.GetById", "Tenants.GetBySlug", "Features.GetFeatures",
        "Settings.Get", "Billing.GetSubscriptions",
        // Catalog reads used by PDV / salão / cozinha
        "Products.GetAll", "Products.GetPaged", "Products.GetById",
        "Categories.GetAll", "Categories.GetById",
        // Lookups with no tenant data
        "Integrations.Barcode.LookupBarcode", "Integrations.Lookup.LookupCep", "Integrations.Lookup.LookupCnpj",
        "Integrations.Weather.GetCurrent", "Integrations.Weather.GetHistory",
        // Service preset/settings reads drive navigation for every role (decided in #38)
        "Modules.Service.Service.GetPreset", "Modules.Service.Service.GetSettings",
        "Modules.Service.Service.GetPublicBooking",
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
        var stale = OpenToAnyTenantUser.Where(e => !names.Contains(e));
        string.Join(", ", stale).Should().BeEmpty("allow-list entries must point at real actions");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<MethodInfo> ApiActions() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    /// <summary>Namespace-qualified so same-named controllers (two Orders, two Reports) never share an entry.</summary>
    private static string Name(MethodInfo m)
    {
        var type = m.DeclaringType!.FullName!.Replace("Nexo.Api.Controllers.", "");
        return $"{type[..^"Controller".Length]}.{m.Name.Replace("Async", "")}";
    }

    private static bool IsAnonymous(MethodInfo m) =>
        m.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || m.DeclaringType!.GetCustomAttribute<AllowAnonymousAttribute>() is not null
        || PublicControllers.Contains(m.DeclaringType!.Name);

    private static bool HasRoleOrPolicy(MethodInfo m) =>
        m.GetCustomAttributes<AuthorizeAttribute>().Concat(m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>())
            .Any(a => !string.IsNullOrEmpty(a.Roles) || !string.IsNullOrEmpty(a.Policy));

    private static bool IsAllowListed(MethodInfo m) => OpenToAnyTenantUser.Contains(Name(m));
}
