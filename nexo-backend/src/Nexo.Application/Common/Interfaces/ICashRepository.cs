using Nexo.Application.Features.Cash;
using Nexo.Domain.Entities;

namespace Nexo.Application.Common.Interfaces;

public interface ICashRepository
{
    Task<CashSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns the open session for a specific user (1 per tenant+user rule).</summary>
    Task<CashSession?> GetOpenSessionByUserAsync(Guid userId, CancellationToken ct = default);
    /// <summary>The store's earliest-opened open session (deterministic fallback drawer).</summary>
    Task<CashSession?> GetOpenSessionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CashMovement>> GetMovementsByReferenceAsync(string referenceType, Guid referenceId, CancellationToken ct = default);
    Task<IReadOnlyList<CashSession>> GetAllAsync(CancellationToken ct = default);
    Task AddSessionAsync(CashSession session, CancellationToken ct = default);
    Task AddMovementAsync(CashMovement movement, CancellationToken ct = default);
    Task<IReadOnlyList<CashMovement>> GetMovementsBySessionAsync(Guid sessionId, CancellationToken ct = default);
    /// <summary>Row lock on the session for the current transaction (close, manual movements).</summary>
    Task LockSessionAsync(Guid sessionId, Guid tenantId, CancellationToken ct = default);
    /// <summary>Transaction-scoped advisory lock per user, so one user can't open two drawers at once.</summary>
    Task LockUserOpeningAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Confirmed/paid sale payments of the session grouped by method and type.</summary>
    Task<IReadOnlyList<CashPaymentTotalDto>> GetPaymentTotalsBySessionAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
