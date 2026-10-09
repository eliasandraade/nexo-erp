using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service.Interfaces;

/// <summary>
/// Repository for the commission ledger (entries + payouts). Tenant + store isolation via the EF
/// global query filter — every query and every conditional UPDATE below is filtered by it.
///
/// The two write paths that must survive concurrency (and multiple API replicas) are enforced by
/// the database, not by in-memory checks:
///   - <see cref="TryAddEntryAsync"/> relies on the unique (tenant, source, source_id) index;
///   - <see cref="AttachOpenEntriesToPayoutAsync"/> and <see cref="TryMarkPayoutPaidAsync"/> are
///     conditional UPDATEs whose affected-row count tells the caller whether it won.
/// </summary>
public interface ISvcCommissionRepository
{
    // ── Entries ──────────────────────────────────────────────────────────────
    Task<IReadOnlyList<SvcCommissionEntry>> GetEntriesAsync(
        Guid? professionalId, DateTime? from, DateTime? to, bool? settled, Guid? payoutId, int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Entries still open for a professional inside a period — the set a payout freezes.
    /// Ordered by recognition so the payout total is reproducible.
    /// </summary>
    Task<IReadOnlyList<SvcCommissionEntry>> GetOpenEntriesForPayoutAsync(
        Guid professionalId, DateTime periodStart, DateTime periodEnd, CancellationToken ct = default);

    /// <summary>True when this exact source holds an ACTIVE earning — the anti-double-count guard.</summary>
    Task<bool> EntryExistsForSourceAsync(
        SvcCommissionSource source, Guid sourceId, CancellationToken ct = default);

    /// <summary>Which of <paramref name="sourceIds"/> hold an ACTIVE earning, in one query.</summary>
    Task<IReadOnlySet<Guid>> GetRecognizedSourceIdsAsync(
        SvcCommissionSource source, IReadOnlyCollection<Guid> sourceIds, CancellationToken ct = default);

    /// <summary>True when a package consumption declares it paid for this appointment.</summary>
    Task<bool> AppointmentCoveredByPackageAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>Order items of <paramref name="orderId"/> that a package consumption points at.</summary>
    Task<IReadOnlySet<Guid>> GetOrderItemIdsCoveredByPackageAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the entry and commits it (SaveChanges). Returns false — and detaches the entry —
    /// when the unique (tenant, source, source_id) index says another request already recognised
    /// the same source. Inside a caller transaction EF rolls back to its automatic savepoint, so
    /// the outer transaction stays usable.
    /// </summary>
    Task<bool> TryAddEntryAsync(SvcCommissionEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Row-locks (SELECT … ORDER BY id FOR UPDATE) the ACTIVE earnings of the given sources and
    /// returns them read AFTER the lock — current PayoutId included. Must run inside a transaction.
    /// </summary>
    Task<IReadOnlyList<SvcCommissionEntry>> LockActiveEarningsAsync(
        SvcCommissionSource source, IReadOnlyCollection<Guid> sourceIds, Guid tenantId, CancellationToken ct = default);

    /// <summary>Catalog items that package consumptions linked to this appointment paid for (one per usage).</summary>
    Task<IReadOnlyList<Guid>> GetCatalogItemsPaidByPackageForAppointmentAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Stamps reversed_at on an active earning (UPDATE … WHERE reversed_at IS NULL). True only for
    /// the single caller that actually reversed it.
    /// </summary>
    Task<bool> TryMarkReversedAsync(Guid entryId, DateTime reversedAt, CancellationToken ct = default);

    /// <summary>
    /// Inserts a reversal entry. False — detached — when the earning already has its reversal
    /// (unique reversal_of_entry_id).
    /// </summary>
    Task<bool> TryAddReversalAsync(SvcCommissionEntry reversal, CancellationToken ct = default);

    /// <summary>Open (not yet in a payout, not reversed) commission per professional: (count, net amount).</summary>
    Task<IReadOnlyDictionary<Guid, (int Count, decimal Amount)>> GetOpenTotalsByProfessionalAsync(
        Guid? professionalId, CancellationToken ct = default);

    /// <summary>
    /// Stamps <paramref name="payoutId"/> on the given entries that are STILL open
    /// (UPDATE … WHERE id = ANY(ids) AND payout_id IS NULL). The rows are locked in id order first
    /// (same order as a reversal, so the two cannot deadlock). Returns how many rows it stamped;
    /// fewer than requested means a concurrent closing or reversal got some of them first.
    /// </summary>
    Task<int> AttachOpenEntriesToPayoutAsync(
        Guid payoutId, IReadOnlyCollection<Guid> entryIds, Guid tenantId, CancellationToken ct = default);

    // ── Payouts ──────────────────────────────────────────────────────────────
    Task<SvcCommissionPayout?> GetPayoutByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SvcCommissionPayout>> GetPayoutsAsync(
        Guid? professionalId, SvcCommissionPayoutStatus? status, CancellationToken ct = default);

    /// <summary>Payout totals per professional and status: (count, amount).</summary>
    Task<IReadOnlyDictionary<(Guid ProfessionalId, SvcCommissionPayoutStatus Status), (int Count, decimal Amount)>>
        GetPayoutTotalsAsync(Guid? professionalId, CancellationToken ct = default);

    Task AddPayoutAsync(SvcCommissionPayout payout, CancellationToken ct = default);

    /// <summary>
    /// Pending → Paid as a conditional UPDATE (… WHERE status = 'Pending'). True only for the
    /// single caller that actually flipped it; a repeat or a concurrent loser gets false.
    /// </summary>
    Task<bool> TryMarkPayoutPaidAsync(Guid payoutId, DateTime paidAt, CancellationToken ct = default);

    /// <summary>Re-reads a tracked payout from the database (after another request changed it).</summary>
    Task ReloadAsync(SvcCommissionPayout payout, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
