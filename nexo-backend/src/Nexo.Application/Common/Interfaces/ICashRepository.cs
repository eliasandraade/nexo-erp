using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ICashRepository
{
    Task<CashSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the open session for a specific user (1 per tenant+user rule).</summary>
    Task<CashSession?> GetOpenSessionByUserAsync(Guid userId, CancellationToken ct = default);
    Task<CashSession?> GetOpenSessionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CashSession>> GetAllAsync(CancellationToken ct = default);
    Task AddSessionAsync(CashSession session, CancellationToken ct = default);
    Task AddMovementAsync(CashMovement movement, CancellationToken ct = default);
    Task<IReadOnlyList<CashMovement>> GetMovementsBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
