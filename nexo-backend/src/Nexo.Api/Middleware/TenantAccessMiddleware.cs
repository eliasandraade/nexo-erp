using Nexo.Application.Common.Interfaces;

namespace Nexo.Api.Middleware;

/// <summary>
/// A company with no module with access now (the single rule, ModuleSubscription.HasAccessAt) is read-only: it keeps reading
/// and exporting its data, and writes answer 402 <c>subscription_required</c>. With any module with access, the routes of a
/// module without it are the gates' to answer (<see cref="ExpiredModuleGate"/>). What stays open is in <see cref="TenantAccessPolicy"/>.
///
/// Reads <see cref="ICurrentTenant.ActiveModules"/>, which tenant resolution loads from the tenant cache (invalidated by the
/// Stripe webhook and by platform grants; a Redis failure falls back to the database): no query of its own per request.
/// Sessions without a company (platform, anonymous) never get here resolved, and an impersonation is a company session like
/// any other. Runs after authentication and authorization, so 401 and 403 keep their precedence.
/// </summary>
public class TenantAccessMiddleware
{
    private const string Message =
        "Esta empresa está sem assinatura ativa e ficou em modo somente leitura: os dados podem ser consultados e exportados, " +
        "mas não alterados. Veja as opções na tela Assinatura ou fale com o suporte Orken.";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantAccessMiddleware> _logger;

    public TenantAccessMiddleware(RequestDelegate next, ILogger<TenantAccessMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant, IConfiguration configuration)
    {
        if (!TenantAccessPolicy.IsWrite(context.Request.Method)
            || !currentTenant.IsResolved
            || currentTenant.ActiveModules.Count > 0
            || TenantAccessPolicy.IsExempt(context.GetEndpoint()))
        {
            await _next(context);
            return;
        }

        // The route template, never the raw path: no ids and nothing typed goes to this log. Named RequestPath on purpose,
        // like the request log: the framework adds the raw path under that name to every log line, unless it is already set.
        var route = Nexo.Api.Observability.RequestLogging.RouteOf(context);

        if (!TenantAccessPolicy.IsEnforced(configuration))
        {
            _logger.LogWarning(
                "Tenant write would be blocked, {Setting} is off: no module with access. TenantId={TenantId} Method={Method} Route={RequestPath}",
                TenantAccessPolicy.EnforceSetting, currentTenant.Id, context.Request.Method, route);
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Tenant write blocked: no module with access. TenantId={TenantId} Method={Method} Route={RequestPath}",
            currentTenant.Id, context.Request.Method, route);
        await ExceptionHandlingMiddleware.WriteErrorAsync(context, StatusCodes.Status402PaymentRequired, Message, code: TenantAccessPolicy.ErrorCode);
    }
}
