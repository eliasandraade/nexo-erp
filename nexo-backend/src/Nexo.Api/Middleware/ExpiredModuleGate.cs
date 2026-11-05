using Microsoft.AspNetCore.Mvc;
using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Middleware;

/// <summary>
/// What a module gate does when the tenant has no access to the module now: a module with a subscription row is expired, so it
/// reads and refuses writes with 402 <c>subscription_required</c>; with no row the tenant never had it, and the gate keeps its
/// own refusal. The same on all three gates, with another module active or not.
///
/// Decides with <see cref="ICurrentTenant"/>, loaded from the tenant cache: no query of its own per request. A module still
/// listed as active there is treated as expired too: that cache can lag behind the gate's own for up to its lifetime, and a
/// module that just ended is not a module the tenant never had.
///
/// <see cref="TenantAccessPolicy.IsEnforced"/> does not reach this: the gates refused these writes before that switch existed.
/// See docs/engineering/tenant-access-enforcement.md.
/// </summary>
public static class ExpiredModuleGate
{
    private const string Message =
        "Este módulo está sem assinatura ativa e ficou em modo somente leitura: os dados podem ser consultados e exportados, " +
        "mas não alterados. Veja as opções na tela Assinatura ou fale com o suporte Orken.";

    /// <returns>Null when the request goes on, otherwise the answer that ends it.</returns>
    public static IActionResult? Decide(HttpContext context, ICurrentTenant tenant, Func<string, bool> isModule, IActionResult neverHad)
    {
        if (!tenant.ExpiredModules.Any(isModule) && !tenant.ActiveModules.Any(isModule)) return neverHad;
        if (!TenantAccessPolicy.IsWrite(context.Request.Method) || TenantAccessPolicy.IsExempt(context.GetEndpoint())) return null;

        // The route template, never the raw path: no ids and nothing typed goes to this log (see TenantAccessMiddleware).
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExpiredModuleGate)).LogWarning(
            "Write on an expired module blocked. TenantId={TenantId} Method={Method} Route={RequestPath}",
            tenant.Id, context.Request.Method, Nexo.Api.Observability.RequestLogging.RouteOf(context));

        return new JsonResult(new { error = Message, details = Array.Empty<string>(), requestId = context.TraceIdentifier, code = TenantAccessPolicy.ErrorCode })
        {
            StatusCode = StatusCodes.Status402PaymentRequired,
        };
    }
}
