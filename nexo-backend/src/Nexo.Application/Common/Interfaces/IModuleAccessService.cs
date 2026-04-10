namespace Nexo.Application.Common.Interfaces;

/// <summary>
/// Verifica se um tenant tem acesso ativo a um módulo específico.
/// Implementação usa cache em memória com TTL de 5 minutos como camada de aceleração.
/// Fonte de verdade: tabela module_subscriptions no banco.
/// </summary>
public interface IModuleAccessService
{
    /// <summary>
    /// Retorna true se o tenant tem uma assinatura ativa para o módulo informado.
    /// Cache key: "mod:{tenantId}:{moduleKey}" — TTL 5 minutos.
    /// </summary>
    Task<bool> HasActiveModuleAsync(Guid tenantId, string moduleKey, CancellationToken ct = default);

    /// <summary>
    /// Invalida o cache para o tenant+módulo informados.
    /// Deve ser chamado ao ativar, cancelar ou alterar uma assinatura.
    /// </summary>
    void InvalidateCache(Guid tenantId, string moduleKey);
}
