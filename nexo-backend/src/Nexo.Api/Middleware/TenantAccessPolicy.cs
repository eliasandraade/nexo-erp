using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Nexo.Api.Middleware;

/// <summary>
/// What a company in read-only may still do: after the due date it stays read-only, consultations and exports included, and no
/// legitimate contracting or administration is blocked (owner's decision). This is the whole decision, in one place. See
/// docs/engineering/tenant-access-enforcement.md.
///
/// Exemptions compare the route template, never the raw path, so casing or a trailing slash cannot change the answer. A
/// new route is not exempt until it is listed here; TenantAccessEnforcementTests sweeps every write route of the host.
/// </summary>
public static class TenantAccessPolicy
{
    public const string ErrorCode = "subscription_required";
    public const string EnforceSetting = "Billing:EnforceTenantAccess";

    /// <summary>
    /// Everything a session needs to go on existing (leave, renew, change store, password, two-step verification), to pay and
    /// to administer who has access, plus the reads that are sent by POST. Anonymous routes need no entry:
    /// <see cref="IsExempt"/> reads <see cref="IAllowAnonymous"/>.
    /// </summary>
    private static readonly HashSet<string> ExemptRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/auth/logout",
        "api/auth/switch-store",
        "api/auth/impersonation/end",
        "api/auth/sessions/revoke-all",
        "api/auth/mfa/setup",
        "api/auth/mfa/enable",
        "api/auth/mfa/disable",
        "api/users/{id:guid}/change-password",
        "api/users/{id:guid}/admin-reset-password",
        "api/users/{id:guid}/resend-invite",
        "api/users/{id:guid}/mfa/disable",
        // Block someone who left the company. The route is Diretoria only, and creating users stays blocked.
        "api/users/{id:guid}",
        "api/billing/checkout",
        "api/billing/portal",
        "api/billing/subscriptions",
        // Reads sent by POST because the question has a body: they run the rules in memory and record nothing.
        "api/tax/calculations/coverage",
        "api/tax/calculations/{id:guid}/verify",
    };

    /// <summary>Whole areas with an identity of their own: the platform, and the contract read by the Orken Platform.</summary>
    private static readonly string[] ExemptPrefixes = ["api/platform/", "api/orken/v1/"];

    /// <summary>Only what is known not to write passes: an unusual method counts as a write.</summary>
    public static bool IsWrite(string method)
        => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));

    /// <summary>
    /// Whether a write to this endpoint stays open without a subscription. Outside <c>api/</c> there is only the health
    /// checks and the real-time hub, whose POST opens a connection and writes no data. A request that matched no route is
    /// also let through, so it keeps answering 404.
    /// </summary>
    public static bool IsExempt(Endpoint? endpoint)
    {
        if (endpoint is not RouteEndpoint route) return true;
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) return true;

        var template = (route.RoutePattern.RawText ?? string.Empty).TrimStart('/');
        return !template.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
               || ExemptRoutes.Contains(template)
               || ExemptPrefixes.Any(p => template.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>On unless explicitly <c>false</c>: a mistyped value must not turn the rule off.</summary>
    public static bool IsEnforced(IConfiguration configuration)
        => !bool.TryParse(configuration[EnforceSetting], out var enforce) || enforce;
}
