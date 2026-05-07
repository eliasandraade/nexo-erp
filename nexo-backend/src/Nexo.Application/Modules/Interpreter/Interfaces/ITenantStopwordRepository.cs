using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface ITenantStopwordRepository
{
    Task<IReadOnlyList<string>> GetWordsByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<TenantStopword?> GetByWordAsync(Guid tenantId, string word, CancellationToken ct = default);
    Task AddAsync(TenantStopword stopword, CancellationToken ct = default);
    Task RemoveAsync(TenantStopword stopword, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
