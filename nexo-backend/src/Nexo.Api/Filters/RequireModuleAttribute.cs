using Microsoft.AspNetCore.Mvc;

namespace Nexo.Api.Filters;

/// <summary>
/// Aplica o filtro RequireModuleFilter ao controller ou action anotado.
/// Retorna 403 Forbidden se o tenant não tiver assinatura ativa para o módulo.
///
/// Uso:
///   [RequireModule("varejo")]
///   public class PurchasesController : ControllerBase { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequireModuleAttribute : TypeFilterAttribute
{
    public RequireModuleAttribute(string moduleKey)
        : base(typeof(RequireModuleFilter))
    {
        Arguments = [moduleKey];
    }
}
