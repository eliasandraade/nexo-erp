using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Npgsql;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>One retention as a payment provider reports it, linked to the calculation and tax it settles.</summary>
public sealed record SplitProviderSettlement(Guid CalculationId, TaxType Tax, decimal Amount, string ExternalReference, DateTime SettledAtUtc);

/// <summary>
/// Source of actual split payment retentions (a PSP or acquirer report). No provider is integrated:
/// the split payment procedures still depend on regulation and on the payment institutions, and this
/// codebase must not pretend otherwise. Actual retentions are recorded manually until a real adapter
/// exists behind this interface — there is deliberately no fake implementation.
/// </summary>
public interface ISplitPaymentProvider
{
    bool IsConfigured { get; }
    string Name { get; }

    /// <summary>Retentions reported for a period. Only a configured, real provider implements it.</summary>
    Task<IReadOnlyList<SplitProviderSettlement>> FetchSettlementsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed class NotConfiguredSplitPaymentProvider : ISplitPaymentProvider
{
    public bool IsConfigured => false;
    public string Name => "none";

    public Task<IReadOnlyList<SplitProviderSettlement>> FetchSettlementsAsync(DateOnly from, DateOnly to, CancellationToken ct)
        => throw new DomainException("Nenhum provedor de split payment está integrado.", "tax_split_provider_missing");
}

/// <summary>Filters of the split list (status is the reconciliation status, computed in the database).</summary>
public sealed record SplitFilter(
    Guid? CalculationId = null, SplitReconciliationStatus? Status = null, TaxType? Tax = null, DateOnly? From = null, DateOnly? To = null,
    int? MinAgeDays = null, int Skip = 0, int Take = 100);

/// <summary>
/// Split payment management: expected retentions derive from an official calculation's liability; actual retentions are recorded as
/// settlements (partial ones add up; a wrong one is reversed by an opposite entry); reconciliation compares both (excess → possible
/// refund, insufficient → still due). An expectation alone never means anything was retained or paid. Credits are not netted here —
/// under the standard procedure the debit is retained in full; netting belongs to the assessment, not to this expectation.
/// </summary>
public sealed class TaxSplitPaymentService
{
    private const int MaxList = 200;
    private const string ExpectationEntity = "SplitExpectation";

    private readonly TaxContext _ctx;
    private readonly ISplitPaymentProvider _provider;
    private readonly ILogger<TaxSplitPaymentService> _log;

    public TaxSplitPaymentService(TaxContext ctx, ISplitPaymentProvider provider, ILogger<TaxSplitPaymentService> log)
    {
        _ctx = ctx;
        _provider = provider;
        _log = log;
    }

    private static DateOnly Today => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

    public SplitProviderStatusDto ProviderStatus() => _provider.IsConfigured
        ? new SplitProviderStatusDto(true, $"Provedor {_provider.Name} configurado.")
        : new SplitProviderStatusDto(false, "Nenhum provedor de pagamento integrado. O split payment ainda depende de regulamentação e das instituições de pagamento; registre as retenções manualmente para conciliar.");

    public async Task<IReadOnlyList<SplitExpectationDto>> CreateExpectationsAsync(Guid calculationId, CreateSplitExpectationsRequest r, CancellationToken ct)
    {
        var db = _ctx.Db;
        var calc = await db.TaxCalculations.AsNoTracking().Include(c => c.Components).FirstOrDefaultAsync(c => c.Id == calculationId, ct)
            ?? throw new NotFoundException("Cálculo", calculationId);
        var direction = await db.TaxTransactions.AsNoTracking().Where(t => t.Id == calc.TransactionId).Select(t => t.Direction).FirstAsync(ct);
        if (direction != TaxDirection.Outbound)
            throw new DomainException("A retenção esperada vale para operações de saída (o estabelecimento é o fornecedor).", "tax_split_direction");
        if (await db.TaxSplitExpectations.AnyAsync(e => e.CalculationId == calculationId, ct))
            return (await ListAsync(new SplitFilter(CalculationId: calculationId, Take: MaxList), ct)).Items;

        var byTax = calc.Components.GroupBy(c => c.Tax).Where(g => g.Sum(c => c.Amount) > 0).ToList();
        if (byTax.Count == 0) throw new DomainException("O cálculo não tem tributo a reter.", "tax_split_nothing");
        foreach (var g in byTax)
            db.TaxSplitExpectations.Add(SplitExpectation.Create(_ctx.Tenant.Id, calculationId, g.Key, r.Procedure, g.Sum(c => c.Amount)));
        _ctx.Audit.Record(TaxAuditActions.SplitExpectationsCreated, "Calculation", calculationId,
            $"Retenções esperadas de split payment: {string.Join(", ", byTax.Select(g => $"{g.Key} {g.Sum(c => c.Amount):N2}"))}. Expectativa gerencial — não comprova retenção.",
            new { procedure = r.Procedure.ToString() });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
        }
        return (await ListAsync(new SplitFilter(CalculationId: calculationId, Take: MaxList), ct)).Items;
    }

    /// <summary>A member-initialised projection: EF composes filters and counts over it in SQL.</summary>
    private sealed class Row
    {
        public SplitExpectation E { get; init; } = null!;
        public DateOnly OperationDate { get; init; }
        public decimal Actual { get; init; }
        public int Live { get; init; }
    }

    /// <summary>Expectations with their reconciliation computed in the database — no page or count loads every row into memory.</summary>
    private IQueryable<Row> Rows()
    {
        var db = _ctx.Db;
        return from e in db.TaxSplitExpectations.AsNoTracking()
               join c in db.TaxCalculations.AsNoTracking() on e.CalculationId equals c.Id
               select new Row
               {
                   E = e,
                   OperationDate = c.ReferenceDate,
                   Actual = db.TaxSplitSettlements.Where(s => s.ExpectationId == e.Id).Sum(s => (decimal?)s.ActualAmount) ?? 0m,
                   Live = db.TaxSplitSettlements.Count(s => s.ExpectationId == e.Id && s.ReversesSettlementId == null
                       && !db.TaxSplitSettlements.Any(r => r.ReversesSettlementId == s.Id)),
               };
    }

    private static IQueryable<Row> WithStatus(IQueryable<Row> q, SplitReconciliationStatus status)
    {
        const decimal tol = SplitReconciliation.Tolerance;
        return status switch
        {
            SplitReconciliationStatus.Pending => q.Where(x => x.Live == 0),
            SplitReconciliationStatus.Matched => q.Where(x => x.Live > 0 && x.Actual - x.E.ExpectedAmount <= tol && x.E.ExpectedAmount - x.Actual <= tol),
            SplitReconciliationStatus.Excess => q.Where(x => x.Live > 0 && x.Actual - x.E.ExpectedAmount > tol),
            _ => q.Where(x => x.Live > 0 && x.E.ExpectedAmount - x.Actual > tol),
        };
    }

    public async Task<TaxPage<SplitExpectationDto>> ListAsync(SplitFilter f, CancellationToken ct)
    {
        var take = Math.Clamp(f.Take, 1, MaxList);
        var skip = Math.Max(0, f.Skip);
        var q = Rows();
        if (f.CalculationId is not null) q = q.Where(x => x.E.CalculationId == f.CalculationId);
        if (f.Tax is not null) q = q.Where(x => x.E.Tax == f.Tax);
        if (f.From is not null) q = q.Where(x => x.OperationDate >= f.From);
        if (f.To is not null) q = q.Where(x => x.OperationDate <= f.To);
        if (f.MinAgeDays is { } age) q = q.Where(x => x.OperationDate <= Today.AddDays(-age));
        if (f.Status is { } status) q = WithStatus(q, status);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.E.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        var ids = rows.Select(x => x.E.Id).ToList();
        var settlements = (await _ctx.Db.TaxSplitSettlements.AsNoTracking().Where(s => ids.Contains(s.ExpectationId)).ToListAsync(ct))
            .ToLookup(s => s.ExpectationId);
        return new TaxPage<SplitExpectationDto>(rows.Select(x => Map(x.E, settlements[x.E.Id].ToList(), x.OperationDate)).ToList(), total, skip, take);
    }

    /// <summary>Expectations of the store whose retentions do not match — counted in the database over all of them.</summary>
    public async Task<int> CountDivergencesAsync(CancellationToken ct)
        => await WithStatus(Rows(), SplitReconciliationStatus.Excess).CountAsync(ct)
           + await WithStatus(Rows(), SplitReconciliationStatus.Insufficient).CountAsync(ct);

    public async Task<SplitExpectationDto> RecordSettlementAsync(Guid expectationId, RecordSplitSettlementRequest r, CancellationToken ct)
        => await RecordAsync(expectationId, r.ActualAmount, SplitSettlementSource.Manual, r.ExternalReference, r.SettledAt.ToUniversalTime(), ct);

    private async Task<SplitExpectationDto> RecordAsync(Guid expectationId, decimal amount, SplitSettlementSource source, string reference,
        DateTime settledAtUtc, CancellationToken ct)
    {
        var db = _ctx.Db;
        if (settledAtUtc > DateTime.UtcNow.AddMinutes(5))
            throw new DomainException("A data da retenção não pode ser futura.", "tax_split_future_date");
        var expectation = await db.TaxSplitExpectations.AsNoTracking().FirstOrDefaultAsync(e => e.Id == expectationId, ct)
            ?? throw new NotFoundException("Retenção esperada", expectationId);
        var settlement = SplitSettlement.Record(_ctx.Tenant.Id, expectationId, amount, source, reference, settledAtUtc, _ctx.Audit.UserId);
        db.TaxSplitSettlements.Add(settlement);
        var settlements = await db.TaxSplitSettlements.AsNoTracking().Where(s => s.ExpectationId == expectationId).ToListAsync(ct);
        settlements.Add(settlement);
        var operationDate = await db.TaxCalculations.AsNoTracking().Where(c => c.Id == expectation.CalculationId).Select(c => c.ReferenceDate).FirstAsync(ct);
        var dto = Map(expectation, settlements, operationDate);
        var divergent = dto.Status is SplitReconciliationStatus.Excess or SplitReconciliationStatus.Insufficient;
        _ctx.Audit.Record(TaxAuditActions.SplitSettlementRecorded, ExpectationEntity, expectationId,
            $"Retenção de {expectation.Tax} registrada ({source}): {amount:N2} (esperado {expectation.ExpectedAmount:N2}) — {dto.Status}.",
            new { externalReference = reference, amount, expected = expectation.ExpectedAmount, status = dto.Status.ToString(), source = source.ToString() },
            divergent ? AuditSeverity.Warning : AuditSeverity.Info);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Esta referência já foi registrada para esta retenção.", "tax_split_duplicate_reference");
        }
        _ctx.Metrics.Split("settlement");
        if (divergent)
        {
            _ctx.Metrics.Split("divergence");
            _log.LogWarning("Split payment divergence on expectation {ExpectationId}: {Status}", expectationId, dto.Status);
        }
        return dto;
    }

    /// <summary>Cancels a wrong settlement with an opposite entry; the original stays on record.</summary>
    public async Task<SplitExpectationDto> ReverseSettlementAsync(Guid settlementId, ReverseSplitSettlementRequest r, CancellationToken ct)
    {
        var db = _ctx.Db;
        var original = await db.TaxSplitSettlements.AsNoTracking().FirstOrDefaultAsync(s => s.Id == settlementId, ct)
            ?? throw new NotFoundException("Retenção registrada", settlementId);
        var reversal = SplitSettlement.ReversalOf(original, r.Reason, DateTime.UtcNow, _ctx.Audit.UserId);
        db.TaxSplitSettlements.Add(reversal);
        _ctx.Audit.Record(TaxAuditActions.SplitSettlementReversed, ExpectationEntity, original.ExpectationId,
            $"Retenção {original.ExternalReference} ({original.ActualAmount:N2}) estornada: {r.Reason}",
            new { settlementId, reversalId = reversal.Id, original.ActualAmount, r.Reason }, AuditSeverity.Warning);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Esta retenção já foi estornada.", "tax_split_already_reversed");
        }
        _ctx.Metrics.Split("reversal");
        var expectation = await db.TaxSplitExpectations.AsNoTracking().FirstAsync(e => e.Id == original.ExpectationId, ct);
        var settlements = await db.TaxSplitSettlements.AsNoTracking().Where(s => s.ExpectationId == expectation.Id).ToListAsync(ct);
        var operationDate = await db.TaxCalculations.AsNoTracking().Where(c => c.Id == expectation.CalculationId).Select(c => c.ReferenceDate).FirstAsync(ct);
        return Map(expectation, settlements, operationDate);
    }

    /// <summary>
    /// Imports the retentions a configured provider reports for a period (idempotent by reference). With no provider integrated it
    /// refuses: nothing is ever invented as retained.
    /// </summary>
    public async Task<int> ImportFromProviderAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!_provider.IsConfigured)
            throw new DomainException("Nenhum provedor de split payment está integrado: registre as retenções manualmente.", "tax_split_provider_missing");
        var imported = 0;
        foreach (var s in await _provider.FetchSettlementsAsync(from, to, ct))
        {
            var expectationId = await _ctx.Db.TaxSplitExpectations.AsNoTracking()
                .Where(e => e.CalculationId == s.CalculationId && e.Tax == s.Tax).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            if (expectationId is null) continue;
            if (await _ctx.Db.TaxSplitSettlements.AnyAsync(x => x.ExpectationId == expectationId && x.ExternalReference == s.ExternalReference, ct)) continue;
            await RecordAsync(expectationId.Value, s.Amount, SplitSettlementSource.Provider, s.ExternalReference, s.SettledAtUtc, ct);
            imported++;
        }
        return imported;
    }

    /// <summary>The filtered expectations as CSV (status, aging, expected, recorded, difference). Audited.</summary>
    public async Task<(string FileName, byte[] Content)> ExportCsvAsync(SplitFilter f, CancellationToken ct)
    {
        var page = await ListAsync(f with { Skip = 0, Take = MaxList }, ct);
        var csv = new StringBuilder();
        csv.AppendLine("calculo;data_operacao;tributo;procedimento;esperado;registrado;diferenca;status;dias_em_aberto;retencoes");
        foreach (var e in page.Items)
            csv.AppendLine(string.Join(';', e.CalculationId, e.OperationDate?.ToString("yyyy-MM-dd"), e.Tax, e.Procedure,
                e.Expected.ToString(CultureInfo.InvariantCulture), e.Actual.ToString(CultureInfo.InvariantCulture),
                e.Difference.ToString(CultureInfo.InvariantCulture), e.Status, e.AgeDays,
                TaxSimulationService.Csv(string.Join(" | ", e.Settlements.Select(s => $"{s.ExternalReference} {s.ActualAmount.ToString(CultureInfo.InvariantCulture)}")))));
        _ctx.Audit.Record(TaxAuditActions.SplitExported, ExpectationEntity, Guid.Empty, $"Retenções de split exportadas (CSV, {page.Items.Count} linhas).",
            new { f.Status, f.Tax, f.From, f.To, f.MinAgeDays });
        await _ctx.Db.SaveChangesAsync(ct);
        return ($"split-{Today:yyyyMMdd}.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
    }

    private static SplitExpectationDto Map(SplitExpectation e, IReadOnlyList<SplitSettlement> settlements, DateOnly operationDate)
    {
        var reversed = settlements.Where(s => s.ReversesSettlementId is not null).Select(s => s.ReversesSettlementId!.Value).ToHashSet();
        var live = settlements.Count(s => !s.IsReversal && !reversed.Contains(s.Id));
        var rec = SplitReconciliation.Of(e.ExpectedAmount, settlements.Select(s => s.ActualAmount), live);
        int? age = rec.Status is SplitReconciliationStatus.Pending or SplitReconciliationStatus.Insufficient
            ? Math.Max(0, Today.DayNumber - operationDate.DayNumber) : null;
        return new SplitExpectationDto(e.Id, e.CalculationId, e.Tax, e.Procedure, rec.Expected, rec.Actual, rec.Difference, rec.Status,
            settlements.OrderBy(s => s.SettledAt).Select(s => new SplitSettlementDto(s.Id, s.ActualAmount, s.Source, s.ExternalReference, s.SettledAt,
                s.ReversesSettlementId, reversed.Contains(s.Id), s.Reason)).ToList(),
            e.CreatedAt, operationDate, age);
    }
}
