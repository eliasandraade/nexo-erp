using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Service.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

/// <summary>
/// Commission ledger use cases (regime de caixa). Three triggers recognise entries, each called by
/// the service that owns the triggering event, inside that event's transaction:
///   - <see cref="RecognizeOrderIfSettledAsync"/> — a payment that settles the comanda in full
///     (SvcPaymentService). One entry per eligible order item;
///   - <see cref="RecognizeAppointmentAsync"/> — an appointment completed WITHOUT a linked order
///     (SvcAppointmentService). With an order, the order is the only source;
///   - <see cref="RecognizePackageUsageAsync"/> — a package consumption (SvcCustomerPackageService),
///     on the prorated value snapshotted on the usage.
///
/// Rate: catalog item → professional default → none (no entry). It is snapshotted on the entry and
/// never recalculated. Idempotency: the unique (tenant, source, source_id) index — a replayed or
/// concurrent trigger finds its entry already there and records nothing.
///
/// Cross-source anti-double-count (one service, one commission):
///   - appointment with any linked order → only the order generates;
///   - appointment completed first and an order opened from it later → the order skips the item
///     that carries the appointment's service (the appointment entry already covers it);
///   - order item consumed from a package → the package usage generates, the order item does not;
///     a usage pointing at an order item that was already commissioned generates nothing;
///   - appointment paid with a package (usage linked to it) → whichever is recognised first holds
///     the commission: completion skips a package-covered appointment, and a usage linked to an
///     appointment that already earned its entry generates nothing.
///
/// Reversal (cash basis works both ways): a voided payment that leaves the order no longer fully
/// paid reverses that order's active earnings — open ones simply stop counting; ones already
/// closed into a payout get a negative entry the next payout discounts. Paying again re-earns.
///
/// Payouts: closing freezes a professional's open entries in a period (conditional UPDATE, safe
/// against concurrent closings); paying writes the settled Payable in the financeiro exactly once.
/// </summary>
public class SvcCommissionService
{
    /// <summary>Upper bound for list endpoints — summaries are aggregated server-side regardless.</summary>
    public const int MaxEntriesPerQuery = 1000;

    private readonly ISvcCommissionRepository       _commissions;
    private readonly ISvcOrderRepository            _orders;
    private readonly ISvcPaymentRepository          _payments;
    private readonly ISvcAppointmentRepository      _appointments;
    private readonly ISvcProfessionalRepository     _professionals;
    private readonly ISvcCatalogItemRepository      _catalog;
    private readonly ServiceFinancialPostingService _posting;
    private readonly IUnitOfWork                    _uow;
    private readonly ICurrentTenant                 _currentTenant;

    public SvcCommissionService(
        ISvcCommissionRepository commissions, ISvcOrderRepository orders, ISvcPaymentRepository payments,
        ISvcAppointmentRepository appointments, ISvcProfessionalRepository professionals,
        ISvcCatalogItemRepository catalog, ServiceFinancialPostingService posting, IUnitOfWork uow,
        ICurrentTenant currentTenant)
    {
        _commissions = commissions; _orders = orders; _payments = payments; _appointments = appointments;
        _professionals = professionals; _catalog = catalog; _posting = posting; _uow = uow;
        _currentTenant = currentTenant;
    }

    // ── Recognition: order ───────────────────────────────────────────────────

    /// <summary>
    /// Recognises one entry per eligible item once the order is paid in full. A partially paid
    /// order recognises nothing; calling it again after the entries exist is a no-op. Must run
    /// after the triggering payment was saved, so the paid total already includes it.
    /// </summary>
    public async Task RecognizeOrderIfSettledAsync(Guid orderId, DateTime recognizedAt, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct);
        if (order is null || order.Status == SvcOrderStatus.Cancelled || order.TotalAmount <= 0m) return;

        var paid = (await _payments.GetByOrderAsync(orderId, ct))
            .Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);
        if (paid < order.TotalAmount) return;                     // regime de caixa: only when fully settled

        var items = order.Items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).ToList();
        if (items.Count == 0) return;

        var alreadyRecognized = await _commissions.GetRecognizedSourceIdsAsync(
            SvcCommissionSource.OrderItem, items.Select(i => i.Id).ToList(), ct);
        var coveredByPackage = await _commissions.GetOrderItemIdsCoveredByPackageAsync(orderId, ct);
        var coveredByAppointment = await ItemCoveredByAppointmentEntryAsync(order, items, ct);

        var professionals = new Dictionary<Guid, SvcProfessional?>();
        foreach (var item in items)
        {
            if (alreadyRecognized.Contains(item.Id) || coveredByPackage.Contains(item.Id)
                || item.Id == coveredByAppointment)
                continue;

            if ((item.ProfessionalId ?? order.ProfessionalId) is not { } professionalId) continue;
            var professional = await GetProfessionalCachedAsync(professionals, professionalId, ct);

            var percent = SvcCommissionPolicy.ResolvePercent(
                item.CommissionPercentSnapshot, professional?.DefaultCommissionPercent);

            await TryRecognizeAsync(professionalId, order.CustomerId, SvcCommissionSource.OrderItem, item.Id,
                item.TotalAmount, percent, recognizedAt, $"{order.Code} · {item.NameSnapshot}", ct);
        }
    }

    /// <summary>
    /// When the order was opened from an appointment that already earned its own entry (completed
    /// before the order existed), the order item carrying the appointment's service is the same
    /// service — return it so it is not commissioned twice. Earliest matching item wins.
    /// </summary>
    private async Task<Guid?> ItemCoveredByAppointmentEntryAsync(
        SvcOrder order, IReadOnlyList<SvcOrderItem> items, CancellationToken ct)
    {
        if (order.AppointmentId is not { } appointmentId) return null;
        if (!await _commissions.EntryExistsForSourceAsync(SvcCommissionSource.Appointment, appointmentId, ct))
            return null;

        var appointment = await _appointments.GetByIdAsync(appointmentId, ct);
        return items.FirstOrDefault(i => i.CatalogItemId == appointment?.CatalogItemId)?.Id;
    }

    // ── Reversal: order ──────────────────────────────────────────────────────

    /// <summary>
    /// Reverses the order's active earnings when the order is no longer fully paid (a payment was
    /// voided). Must run after the void was saved, inside the same transaction, with the order row
    /// locked. Idempotent: each earning is reversed once (conditional UPDATE) and gets at most one
    /// negative entry (unique index).
    /// </summary>
    public async Task ReverseOrderIfUnsettledAsync(Guid orderId, DateTime reversedAt, CancellationToken ct = default)
    {
        var order = await _orders.GetByIdWithItemsAsync(orderId, ct);
        if (order is null) return;

        var paid = (await _payments.GetByOrderAsync(orderId, ct))
            .Where(p => p.Status == SvcPaymentStatus.Paid).Sum(p => p.Amount);
        if (order.TotalAmount > 0m && paid >= order.TotalAmount) return;   // still settled

        var earnings = await _commissions.GetActiveEarningsAsync(
            SvcCommissionSource.OrderItem, order.Items.Select(i => i.Id).ToList(), ct);
        foreach (var earning in earnings)
        {
            if (!await _commissions.TryMarkReversedAsync(earning.Id, reversedAt, ct)) continue;
            if (earning.PayoutId is null) continue;            // never paid out — it simply stops counting

            var reversal = SvcCommissionEntry.CreateReversalOf(
                earning, reversedAt, Truncate($"Estorno · {earning.Notes ?? order.Code}", 500));
            await _commissions.TryAddReversalAsync(reversal, ct);
        }
    }

    // ── Recognition: appointment ─────────────────────────────────────────────

    /// <summary>
    /// Recognises the commission of a completed appointment that has no linked order, on its price
    /// and rate snapshots. With a linked order (in any state) the order is the source — nothing here.
    /// </summary>
    public async Task RecognizeAppointmentAsync(SvcAppointment appointment, CancellationToken ct = default)
    {
        if (appointment.Status != SvcAppointmentStatus.Completed) return;
        if (!SvcCommissionPolicy.Earns(appointment.CommissionPercentSnapshot)) return;
        if (await _orders.ExistsForAppointmentAsync(appointment.Id, ct)) return;
        if (await _commissions.AppointmentCoveredByPackageAsync(appointment.Id, ct)) return;

        var catalog = await _catalog.GetByIdAsync(appointment.CatalogItemId, ct);
        await TryRecognizeAsync(appointment.ProfessionalId, appointment.CustomerId,
            SvcCommissionSource.Appointment, appointment.Id, appointment.PriceSnapshot,
            appointment.CommissionPercentSnapshot, DateTime.UtcNow,
            $"Agendamento · {catalog?.Name ?? "serviço"}", ct);
    }

    // ── Recognition: package usage ───────────────────────────────────────────

    /// <summary>
    /// Recognises the commission of a package consumption from the snapshots stored on the usage.
    /// A usage pointing at an order item that already earned commission generates nothing.
    /// </summary>
    public async Task RecognizePackageUsageAsync(
        SvcPackageUsage usage, Guid customerId, string description, CancellationToken ct = default)
    {
        if (usage.ProfessionalId is not { } professionalId) return;
        if (!SvcCommissionPolicy.Earns(usage.CommissionPercentSnapshot)) return;
        if (usage.OrderItemId is { } orderItemId
            && await _commissions.EntryExistsForSourceAsync(SvcCommissionSource.OrderItem, orderItemId, ct))
            return;
        if (usage.AppointmentId is { } appointmentId
            && await _commissions.EntryExistsForSourceAsync(SvcCommissionSource.Appointment, appointmentId, ct))
            return;

        await TryRecognizeAsync(professionalId, customerId, SvcCommissionSource.PackageUsage, usage.Id,
            usage.BaseAmountSnapshot ?? 0m, usage.CommissionPercentSnapshot, usage.CreatedAt, description, ct);
    }

    private async Task TryRecognizeAsync(
        Guid professionalId, Guid customerId, SvcCommissionSource source, Guid sourceId,
        decimal baseAmount, decimal? percent, DateTime recognizedAt, string description, CancellationToken ct)
    {
        if (!SvcCommissionPolicy.Earns(percent) || baseAmount <= 0m) return;

        var entry = SvcCommissionEntry.Create(
            _currentTenant.Id, professionalId, customerId, source, sourceId,
            baseAmount, percent!.Value, recognizedAt, Truncate(description, 500));
        if (entry.CommissionAmount <= 0m) return;                 // rounds to zero — nothing to pay

        // false = another request already recognised this source; its entry stands.
        await _commissions.TryAddEntryAsync(entry, ct);
    }

    // ── Queries ──────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<SvcCommissionEntryDto>> GetEntriesAsync(
        Guid? professionalId, DateTime? from, DateTime? to, bool? settled, Guid? payoutId,
        CancellationToken ct = default)
        => (await _commissions.GetEntriesAsync(professionalId, from, to, settled, payoutId, MaxEntriesPerQuery, ct))
            .Select(MapEntry).ToList();

    public async Task<IReadOnlyList<SvcCommissionSummaryDto>> GetSummaryAsync(
        Guid? professionalId, CancellationToken ct = default)
    {
        var open    = await _commissions.GetOpenTotalsByProfessionalAsync(professionalId, ct);
        var payouts = await _commissions.GetPayoutTotalsAsync(professionalId, ct);

        var ids = open.Keys.Concat(payouts.Keys.Select(k => k.ProfessionalId)).Distinct();
        if (professionalId is { } only) ids = ids.Append(only).Distinct();

        return ids.Select(id =>
        {
            var o       = open.GetValueOrDefault(id);
            var pending = payouts.GetValueOrDefault((id, SvcCommissionPayoutStatus.Pending));
            var paid    = payouts.GetValueOrDefault((id, SvcCommissionPayoutStatus.Paid));
            return new SvcCommissionSummaryDto(id, o.Count, o.Amount, pending.Count, pending.Amount, paid.Count, paid.Amount);
        }).ToList();
    }

    public async Task<IReadOnlyList<SvcCommissionPayoutDto>> GetPayoutsAsync(
        Guid? professionalId, SvcCommissionPayoutStatus? status, CancellationToken ct = default)
        => (await _commissions.GetPayoutsAsync(professionalId, status, ct)).Select(MapPayout).ToList();

    public async Task<SvcCommissionPayoutDetailDto> GetPayoutByIdAsync(Guid id, CancellationToken ct = default)
    {
        var payout = await _commissions.GetPayoutByIdAsync(id, ct) ?? throw new NotFoundException("SvcCommissionPayout", id);
        var entries = await _commissions.GetEntriesAsync(null, null, null, null, id, MaxEntriesPerQuery, ct);
        return new(MapPayout(payout), entries.Select(MapEntry).ToList());
    }

    // ── Payout: close ────────────────────────────────────────────────────────

    /// <summary>
    /// Freezes every open entry of the professional recognised in the period into a new Pending
    /// payout. The entries are stamped by a conditional UPDATE: if a concurrent closing captured
    /// any of them first, this one rolls back with 409 and nothing is half-closed.
    /// </summary>
    public async Task<SvcCommissionPayoutDetailDto> ClosePayoutAsync(
        CloseSvcCommissionPayoutRequest r, CancellationToken ct = default)
    {
        _ = await _professionals.GetByIdAsync(r.ProfessionalId, ct)
            ?? throw new NotFoundException("SvcProfessional", r.ProfessionalId);

        return await _uow.ExecuteInTransactionAsync(async tct =>
        {
            var open = await _commissions.GetOpenEntriesForPayoutAsync(r.ProfessionalId, r.PeriodStart, r.PeriodEnd, tct);
            if (open.Count == 0)
                throw new DomainException("There are no open commissions for this professional in the period.");

            var total = open.Sum(e => e.CommissionAmount);
            if (total <= 0m)
                throw new DomainException(
                    "The period has more reversals to discount than commission to pay; nothing to close yet.");

            var payout = SvcCommissionPayout.Create(
                _currentTenant.Id, r.ProfessionalId, r.PeriodStart, r.PeriodEnd, total, open.Count, r.Notes);
            await _commissions.AddPayoutAsync(payout, tct);
            await _commissions.SaveChangesAsync(tct);

            var stamped = await _commissions.AttachOpenEntriesToPayoutAsync(payout.Id, open.Select(e => e.Id).ToList(), tct);
            if (stamped != open.Count)
                throw new ConflictException(
                    "Some of these commissions were just closed in another payout. Refresh and try again.");

            var entries = await _commissions.GetEntriesAsync(null, null, null, null, payout.Id, MaxEntriesPerQuery, tct);
            return new SvcCommissionPayoutDetailDto(MapPayout(payout), entries.Select(MapEntry).ToList());
        }, ct);
    }

    // ── Payout: pay ──────────────────────────────────────────────────────────

    /// <summary>
    /// Pending → Paid and, in the same transaction, the settled Payable in the financeiro
    /// (ReferenceType = SvcCommissionPayout). Idempotent: a payout that is already paid — or that a
    /// concurrent request just paid — is returned as is and no second expense is written.
    /// </summary>
    public async Task<SvcCommissionPayoutDto> MarkPayoutPaidAsync(
        Guid id, MarkSvcCommissionPayoutPaidRequest r, CancellationToken ct = default)
    {
        await using var tx = await _uow.BeginTransactionAsync(ct);

        var payout = await _commissions.GetPayoutByIdAsync(id, ct) ?? throw new NotFoundException("SvcCommissionPayout", id);
        if (payout.Status == SvcCommissionPayoutStatus.Paid) return MapPayout(payout);

        var paidAt = r.PaidAt ?? DateTime.UtcNow;
        if (!await _commissions.TryMarkPayoutPaidAsync(id, paidAt, ct))
        {
            // Lost the race to a concurrent request: it already paid and posted the expense.
            await _commissions.ReloadAsync(payout, ct);
            return MapPayout(payout);
        }

        payout.MarkPaid(paidAt);                                  // keep the tracked entity in step with the row
        var professional = await _professionals.GetByIdAsync(payout.ProfessionalId, ct);
        await _posting.PostSettledExpenseAsync(
            payout.TotalAmount, paidAt,
            Truncate($"Orken Service — repasse de comissão · {professional?.Name ?? "profissional"}", 500),
            ServiceFinancialPostingService.CommissionPayoutReference, payout.Id, ct);
        await _commissions.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return MapPayout(payout);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private async Task<SvcProfessional?> GetProfessionalCachedAsync(
        Dictionary<Guid, SvcProfessional?> cache, Guid id, CancellationToken ct)
    {
        if (!cache.TryGetValue(id, out var professional))
            cache[id] = professional = await _professionals.GetByIdAsync(id, ct);
        return professional;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static SvcCommissionEntryDto MapEntry(SvcCommissionEntry e) => new(
        Id: e.Id, StoreId: e.StoreId, ProfessionalId: e.ProfessionalId, CustomerId: e.CustomerId,
        Kind: e.Kind, Source: e.Source, SourceId: e.SourceId, BaseAmount: e.BaseAmount,
        CommissionPercent: e.CommissionPercent, CommissionAmount: e.CommissionAmount,
        RecognizedAt: e.RecognizedAt, PayoutId: e.PayoutId, ReversedAt: e.ReversedAt,
        ReversalOfEntryId: e.ReversalOfEntryId, Description: e.Notes, CreatedAt: e.CreatedAt);

    private static SvcCommissionPayoutDto MapPayout(SvcCommissionPayout p) => new(
        Id: p.Id, StoreId: p.StoreId, ProfessionalId: p.ProfessionalId, PeriodStart: p.PeriodStart,
        PeriodEnd: p.PeriodEnd, TotalAmount: p.TotalAmount, EntryCount: p.EntryCount, Status: p.Status,
        PaidAt: p.PaidAt, Notes: p.Notes, CreatedAt: p.CreatedAt, UpdatedAt: p.UpdatedAt);
}
