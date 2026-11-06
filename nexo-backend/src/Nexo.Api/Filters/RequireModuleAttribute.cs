using Microsoft.AspNetCore.Mvc;

namespace Nexo.Api.Filters;

/// <summary>
/// Aplica o filtro RequireModuleFilter ao controller ou action anotado.
/// Módulo vencido fica somente leitura e módulo que o tenant nunca teve responde 403.
///
/// Uso:
///   [RequireModule("varejo")]
///   public class PurchasesController : ControllerBase { ... }
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public class RequireModuleAttribute : TypeFilterAttribute
{
    /// <summary>Antes da validação automática do corpo (-2000): quem não pode escrever ouve isso, não que o corpo está errado.</summary>
    private const int BeforeModelValidation = -3000;

    public RequireModuleAttribute(string moduleKey)
        : base(typeof(RequireModuleFilter))
    {
        Arguments = [moduleKey];
        Order = BeforeModelValidation;
    }
}
