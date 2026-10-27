using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;
using Npgsql;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Source of actual split payment retentions (a PSP or acquirer report). No provider is integrated:
/// the split payment procedures still depend on regulation and on the payment institutions, and this
/// codebase must not pretend otherwise. Actual retentions are recorded manually until a real adapter
/// exists behind this interface.
/// </summary>
public interface ISplitPaymentProvider
{
    bool IsConfigured { get; }
    string Name { get; }
}

public sealed class NotConfiguredSplitPaymentProvider : ISplitPaymentProvider
{
    public bool IsConfigured => false;
    public string Name => "none";
}

/// <summary>
/// Split payment foundation: expected retentions derive from an official calculation's liability;
/// actual retentions are recorded as settlements; reconciliation compares both (excess → possible
/// refund, insufficient → still due). Credits are not netted here — under the standard procedure the
/// debit is retained in full; netting belongs to the assessment, not to this expectation.
/// </summary>
public sealed class TaxSplitPaymentService
{
    private const int MaxList = 200;

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly ISplitPaymentProvider _provider;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxSplitPaymentService> _log;

    public TaxSplitPaymentService(NexoDbContext db, ICurrentTenant tenant, ISplitPaymentProvider provider, TaxAuditTrail audit,
        ILogger<TaxSplitPaymentService> log)
    {
        _db = db;
        _tenant = tenant;
        _provider = provider;
        _audit = audit;
        _log = log;
    }

    public SplitProviderStatusDto ProviderStatus() => _provider.IsConfigured
        ? new SplitProviderStatusDto(true, $"Provedor {_provider.Name} configurado.")
        : new SplitProviderStatusDto(false, "Nenhum provedor de pagamento integrado. O split payment ainda depende de regulamentação e das instituições de pagamento; registre as retenções manualmente para conciliar.");

    public async Task<IReadOnlyList<SplitExpectationDto>> CreateExpectationsAsync(Guid calculationId, CreateSplitExpectationsRequest r, CancellationToken ct)
    {
        var calc = await _db.TaxCalculations.AsNoTracking().Include(c => c.Components).FirstOrDefaultAsync(c => c.Id == calculationId, ct)
            ?? throw new NotFoundException("Cálculo", calculationId);
        var direction = await _db.TaxTransactions.AsNoTracking().Where(t => t.Id == calc.TransactionId).Select(t => t.Direction).FirstAsync(ct);
        if (direction != TaxDirection.Outbound)
            throw new DomainException("A retenção esperada vale para operações de saída (o estabelecimento é o fornecedor).", "tax_split_direction");
        if (await _db.TaxSplitExpectations.AnyAsync(e => e.CalculationId == calculationId, ct))
            return await ListAsync(calculationId, MaxList, ct);

        var byTax = calc.Components.GroupBy(c => c.Tax).Where(g => g.Sum(c => c.Amount) > 0).ToList();
        if (byTax.Count == 0) throw new DomainException("O cálculo não tem tributo a reter.", "tax_split_nothing");
        foreach (var g in byTax)
            _db.TaxSplitExpectations.Add(SplitExpectation.Create(_tenant.Id, calculationId, g.Key, r.Procedure, g.Sum(c => c.Amount)));
        _audit.Record(TaxAuditActions.SplitExpectationsCreated, "Calculation", calculationId,
            $"Retenções esperadas de split payment: {string.Join(", ", byTax.Select(g => $"{g.Key} {g.Sum(c => c.Amount):N2}"))}.",
            new { procedure = r.Procedure.ToString() });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();
        }
        return await ListAsync(calculationId, MaxList, ct);
    }

    public async Task<IReadOnlyList<SplitExpectationDto>> ListAsync(Guid? calculationId, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        var q = _db.TaxSplitExpectations.AsNoTracking();
        if (calculationId is not null) q = q.Where(e => e.CalculationId == calculationId);
        var expectations = await q.OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(ct);
        var ids = expectations.Select(e => e.Id).ToList();
        var settlements = await _db.TaxSplitSettlements.AsNoTracking().Where(s => ids.Contains(s.ExpectationId)).ToListAsync(ct);
        return expectations.Select(e => Map(e, settlements.Where(s => s.ExpectationId == e.Id).ToList())).ToList();
    }

    /// <summary>Expectations of the store whose retentions do not match — over all of them, not a page.</summary>
    public async Task<int> CountDivergencesAsync(CancellationToken ct)
    {
        var expectations = await _db.TaxSplitExpectations.AsNoTracking().ToListAsync(ct);
        var settlements = (await _db.TaxSplitSettlements.AsNoTracking().ToListAsync(ct)).ToLookup(s => s.ExpectationId);
        return expectations.Select(e => Map(e, settlements[e.Id].ToList()))
            .Count(e => e.Status is SplitReconciliationStatus.Excess or SplitReconciliationStatus.Insufficient);
    }

    public async Task<SplitExpectationDto> RecordSettlementAsync(Guid expectationId, RecordSplitSettlementRequest r, CancellationToken ct)
    {
        var expectation = await _db.TaxSplitExpectations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expectationId, ct)
            ?? throw new NotFoundException("Retenção esperada", expectationId);
        var settlement = SplitSettlement.Record(_tenant.Id, expectationId, r.ActualAmount, SplitSettlementSource.Manual,
            r.ExternalReference, r.SettledAt.ToUniversalTime(), _audit.UserId);
        _db.TaxSplitSettlements.Add(settlement);
        var settlements = await _db.TaxSplitSettlements.AsNoTracking().Where(s => s.ExpectationId == expectationId).ToListAsync(ct);
        settlements.Add(settlement);
        var dto = Map(expectation, settlements);
        _audit.Record(TaxAuditActions.SplitSettlementRecorded, "SplitExpectation", expectationId,
            $"Retenção de {expectation.Tax} registrada: {r.ActualAmount:N2} (esperado {expectation.ExpectedAmount:N2}) — {dto.Status}.",
            new { r.ExternalReference, r.ActualAmount, expected = expectation.ExpectedAmount, status = dto.Status.ToString() },
            dto.Status is SplitReconciliationStatus.Excess or SplitReconciliationStatus.Insufficient ? AuditSeverity.Warning : AuditSeverity.Info);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Esta referência já foi registrada para esta retenção.");
        }
        if (dto.Status is SplitReconciliationStatus.Excess or SplitReconciliationStatus.Insufficient)
            _log.LogWarning("Split payment divergence on expectation {ExpectationId}: {Status}", expectationId, dto.Status);
        return dto;
    }

    /// <summary>Cancels a wrong settlement with an opposite entry; the original stays on record.</summary>
    public async Task<SplitExpectationDto> ReverseSettlementAsync(Guid settlementId, ReverseSplitSettlementRequest r, CancellationToken ct)
    {
        var original = await _db.TaxSplitSettlements.AsNoTracking().FirstOrDefaultAsync(s => s.Id == settlementId, ct)
            ?? throw new NotFoundException("Retenção registrada", settlementId);
        var reversal = SplitSettlement.ReversalOf(original, r.Reason, DateTime.UtcNow, _audit.UserId);
        _db.TaxSplitSettlements.Add(reversal);
        _audit.Record(TaxAuditActions.SplitSettlementReversed, "SplitExpectation", original.ExpectationId,
            $"Retenção {original.ExternalReference} ({original.ActualAmount:N2}) estornada: {r.Reason}",
            new { settlementId, reversalId = reversal.Id, original.ActualAmount, r.Reason }, AuditSeverity.Warning);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Esta retenção já foi estornada.");
        }
        var expectation = await _db.TaxSplitExpectations.AsNoTracking().FirstAsync(e => e.Id == original.ExpectationId, ct);
        var settlements = await _db.TaxSplitSettlements.AsNoTracking().Where(s => s.ExpectationId == expectation.Id).ToListAsync(ct);
        return Map(expectation, settlements);
    }

    private static SplitExpectationDto Map(SplitExpectation e, IReadOnlyList<SplitSettlement> settlements)
    {
        var reversed = settlements.Where(s => s.ReversesSettlementId is not null).Select(s => s.ReversesSettlementId!.Value).ToHashSet();
        var live = settlements.Count(s => !s.IsReversal && !reversed.Contains(s.Id));
        var rec = SplitReconciliation.Of(e.ExpectedAmount, settlements.Select(s => s.ActualAmount), live);
        return new SplitExpectationDto(e.Id, e.CalculationId, e.Tax, e.Procedure, rec.Expected, rec.Actual, rec.Difference, rec.Status,
            settlements.OrderBy(s => s.SettledAt).Select(s => new SplitSettlementDto(s.Id, s.ActualAmount, s.Source, s.ExternalReference, s.SettledAt,
                s.ReversesSettlementId, reversed.Contains(s.Id), s.Reason)).ToList(),
            e.CreatedAt);
    }
}
