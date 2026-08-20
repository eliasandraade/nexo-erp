using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>
/// Repository for the commission ledger (entries + payouts). Tenant + store isolation via the EF
/// global query filter.
/// </summary>
public interface ISvcCommissionRepository
{
    // ── Entries ──────────────────────────────────────────────────────────────
    Task<IReadOnlyList<SvcCommissionEntry>> GetEntriesAsync(
        Guid? professionalId, DateTime? from, DateTime? to, bool? settled,
        CancellationToken ct = default);

    /// <summary>
    /// Entries still open for a professional inside a period — the set a payout freezes.
    /// Ordered by recognition so the payout total is reproducible.
    /// </summary>
    Task<IReadOnlyList<SvcCommissionEntry>> GetOpenEntriesForPayoutAsync(
        Guid professionalId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default);

    /// <summary>True when this exact source already produced an entry — the anti-double-count guard.</summary>
    Task<bool> EntryExistsForSourceAsync(
        SvcCommissionSource source, Guid sourceId, CancellationToken ct = default);

    Task AddEntryAsync(SvcCommissionEntry entry, CancellationToken ct = default);

    // ── Payouts ──────────────────────────────────────────────────────────────
    Task<SvcCommissionPayout?> GetPayoutByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcCommissionPayout>> GetPayoutsAsync(
        Guid? professionalId, SvcCommissionPayoutStatus? status, CancellationToken ct = default);
    Task AddPayoutAsync(SvcCommissionPayout payout, CancellationToken ct = default);
    void UpdatePayout(SvcCommissionPayout payout);

    Task SaveChangesAsync(CancellationToken ct = default);
}
