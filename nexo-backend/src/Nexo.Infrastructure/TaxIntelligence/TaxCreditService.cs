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
/// Credit Hunter foundation: a managerial ledger of IBS/CBS credits. Each movement is an appended
/// entry, taken under a row lock on the credit, so concurrent uses cannot spend the same balance; the
/// database also refuses a second use of a credit in the same obligation. A registered credit is only
/// <b>potential</b> — an estimate with no eligibility, appropriation or availability implied; its
/// requirements are confirmed on record before it becomes "to be appropriated", and only then can it
/// be appropriated and used. Nothing here decides eligibility automatically.
/// </summary>
public sealed class TaxCreditService
{
    private const int MaxList = 200;

    private readonly TaxContext _ctx;
    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxCreditService> _log;

    public TaxCreditService(TaxContext ctx, ILogger<TaxCreditService> log)
    {
        _ctx = ctx;
        _db = ctx.Db;
        _tenant = ctx.Tenant;
        _audit = ctx.Audit;
        _log = log;
    }

    /// <summary>Credits with filters and paging, newest acquisition first.</summary>
    public async Task<TaxPage<TaxCreditDto>> ListAsync(CreditTax? tax, TaxCreditStage? stage, bool? expiringOnly, int skip, int take, DateOnly today,
        CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        skip = Math.Max(0, skip);
        var q = _db.TaxCredits.AsNoTracking();
        if (tax is not null) q = q.Where(c => c.Tax == tax);
        if (stage is not null) q = q.Where(c => c.Stage == stage);
        if (expiringOnly == true) q = q.Where(c => c.ExpiresOn != null && c.ExpiresOn <= today.AddDays(60));
        var total = await q.CountAsync(ct);
        var credits = await q.Include(c => c.Entries)
            .OrderByDescending(c => c.AcquiredOn).ThenByDescending(c => c.CreatedAt).Skip(skip).Take(take)
            .AsSplitQuery().ToListAsync(ct);
        return new TaxPage<TaxCreditDto>(credits.Select(Map).ToList(), total, skip, take);
    }

    /// <summary>
    /// Totals per tax, summed in the database: the potential estimate (outside the ledger) apart from every ledger balance. The same
    /// arithmetic as <see cref="TaxCreditBalance"/>, never loading the credits into memory.
    /// </summary>
    public async Task<IReadOnlyList<TaxCreditTotalsDto>> TotalsAsync(CancellationToken ct)
    {
        var potential = await _db.TaxCredits.AsNoTracking()
            .Where(c => c.Stage == TaxCreditStage.PotentialIdentified || c.Stage == TaxCreditStage.RequirementsUnderReview)
            .GroupBy(c => c.Tax).Select(g => new { g.Key, Sum = g.Sum(c => c.PotentialAmount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var sums = await _db.TaxCreditLedgerEntries.AsNoTracking()
            .Join(_db.TaxCredits.AsNoTracking(), e => e.CreditId, c => c.Id, (e, c) => new { c.Tax, e.Type, e.Amount })
            .GroupBy(x => new { x.Tax, x.Type }).Select(g => new { g.Key.Tax, g.Key.Type, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        return potential.Keys.Concat(sums.Select(s => s.Tax)).Distinct().Order().Select(tax =>
        {
            decimal Sum(TaxCreditEntryType type) => sums.Where(s => s.Tax == tax && s.Type == type).Sum(s => s.Sum);
            var appropriatedGross = Sum(TaxCreditEntryType.Appropriated);
            var appropriated = appropriatedGross - Sum(TaxCreditEntryType.Reversed);
            var used = Sum(TaxCreditEntryType.Used);
            var blocked = Sum(TaxCreditEntryType.Blocked) - Sum(TaxCreditEntryType.Unblocked);
            return new TaxCreditTotalsDto(tax, potential.GetValueOrDefault(tax), Sum(TaxCreditEntryType.Recognized) - appropriatedGross,
                appropriated, used, blocked, appropriated - used - blocked - Sum(TaxCreditEntryType.Expired));
        }).ToList();
    }

    public async Task<TaxCreditDto> GetAsync(Guid id, CancellationToken ct)
        => Map(await _db.TaxCredits.AsNoTracking().Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct)
               ?? throw new NotFoundException("Crédito", id));

    public async Task<TaxCreditDto> RegisterAsync(RegisterTaxCreditRequest r, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(r.SourceDocumentKey) ? null : r.SourceDocumentKey.Trim();
        if (key is not null && await _db.TaxCredits.AnyAsync(c => c.Tax == r.Tax && c.SourceDocumentKey == key, ct))
            throw new ConflictException("Já existe um crédito deste tributo para este documento.");
        var credit = TaxCredit.Register(_tenant.Id, r.Tax, r.SourceDocumentType,
            key, r.SupplierDocument,
            r.AcquiredOn, r.ExpiresOn, r.OriginalAmount, r.PotentialAmount, r.PotentialBasis, null);
        _db.TaxCredits.Add(credit);
        _audit.Record(TaxAuditActions.CreditRegistered, "Credit", credit.Id,
            $"Crédito potencial de {r.Tax} registrado: {r.PotentialAmount:N2} estimado de {r.OriginalAmount:N2} ({r.SourceDocumentType}).",
            new { tax = r.Tax.ToString(), r.OriginalAmount, r.PotentialAmount, r.PotentialBasis, hasDocumentKey = key is not null });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Já existe um crédito deste tributo para este documento.");
        }
        return Map(credit);
    }

    public async Task<TaxCreditDto> MoveAsync(Guid id, TaxCreditMovementRequest r, DateOnly today, CancellationToken ct)
    {
        // A use may refer to an earlier obligation date, never a future one; every other movement is dated today.
        var onDate = r.OnDate ?? today;
        if (onDate > today) throw new DomainException("A data do movimento não pode ser futura.", "tax_credit_future_date");
        // A chosen date is never silently replaced by today's.
        if (r.Movement != TaxCreditMovement.Use && onDate != today)
            throw new DomainException("Só o uso do crédito aceita outra data; os demais movimentos são registrados hoje.", "tax_credit_date_only_for_use");
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.tax_credits WHERE id = {id} AND tenant_id = {_tenant.Id} FOR UPDATE", ct);
        var credit = await _db.TaxCredits.Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Crédito", id);
        // A retried request (same key) finds its movement already recorded: nothing is written twice.
        var key = string.IsNullOrWhiteSpace(r.IdempotencyKey) ? null : r.IdempotencyKey.Trim();
        if (key is not null && credit.Entries.Any(e => e.IdempotencyKey == key)) return Map(credit);

        var before = credit.Balance();
        var stageBefore = credit.Stage;
        // Expiry always writes off the whole available balance — a partial amount is never silently ignored.
        if (r.Movement == TaxCreditMovement.Expire && r.Amount is not null)
            throw new DomainException("O vencimento dá baixa em todo o saldo disponível; não informe valor.", "tax_credit_expire_whole_balance");
        var reason = r.Reason?.Trim() ?? string.Empty;
        if (r.Movement is TaxCreditMovement.StartRequirementsReview or TaxCreditMovement.Discard)
        {
            ChangeStage(credit, r.Movement, reason, stageBefore);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(credit);
        }
        var entry = Append(credit, r, r.Amount ?? 0m, reason, onDate, today);
        if (key is not null) entry.KeyedBy(key);
        _db.TaxCreditLedgerEntries.Add(entry);
        var after = credit.Balance();
        _audit.Record(TaxAuditActions.CreditMovement, "Credit", credit.Id,
            $"Crédito de {credit.Tax}: {entry.Type} de {entry.Amount:N2}.",
            new { entryId = entry.Id, type = entry.Type.ToString(), entry.Amount, entry.ObligationReference, before, after,
                stageBefore = stageBefore.ToString(), stageAfter = credit.Stage.ToString() });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw pg.ConstraintName == "ux_tax_credit_ledger_entries_credit_idempotency"
                ? new ConflictException("Este movimento já foi registrado (mesma chave de requisição).", "tax_credit_duplicate_request")
                : new ConflictException("Este crédito já foi usado nesta obrigação.", "tax_credit_obligation_used");
        }
        await tx.CommitAsync(ct);
        _ctx.Metrics.CreditTransition(entry.Type.ToString());
        _log.LogInformation("Tax credit {CreditId} movement {Type}", credit.Id, entry.Type);
        return Map(credit);
    }

    private void ChangeStage(TaxCredit credit, TaxCreditMovement movement, string reason, TaxCreditStage stageBefore)
    {
        if (movement == TaxCreditMovement.StartRequirementsReview) credit.StartRequirementsReview();
        else credit.Discard(reason);
        _ctx.Metrics.CreditTransition(credit.Stage.ToString());
        _audit.Record(TaxAuditActions.CreditStageChanged, "Credit", credit.Id,
            $"Crédito de {credit.Tax}: {stageBefore} → {credit.Stage}.", new { before = stageBefore.ToString(), after = credit.Stage.ToString(), reason });
    }

    /// <summary>The ledger entry of a movement. Only a use may carry an earlier date; the rest are dated today.</summary>
    private TaxCreditLedgerEntry Append(TaxCredit credit, TaxCreditMovementRequest r, decimal amount, string reason, DateOnly onDate, DateOnly today)
        => r.Movement switch
        {
            TaxCreditMovement.ConfirmToBeAppropriated => credit.ConfirmToBeAppropriated(amount, reason, today, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Appropriate => credit.Appropriate(amount, reason.Length > 0 ? reason : "Apropriação.", today, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Use => credit.Use(amount, r.ObligationTax ?? credit.Tax, r.ObligationReference ?? string.Empty,
                onDate, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Block => credit.Block(amount, reason, today, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Unblock => credit.Unblock(amount, reason.Length > 0 ? reason : "Desbloqueio.", today, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Reverse => credit.ReverseAppropriation(amount, reason, today, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Expire => credit.Expire(today, _audit.UserId, _audit.UserName),
            _ => throw new DomainException("Movimento desconhecido."),
        };

    private static TaxCreditDto Map(TaxCredit c) => new(
        c.Id, c.Tax, c.SourceDocumentType, c.SourceDocumentKey, c.SupplierDocument, c.AcquiredOn, c.ExpiresOn, c.OriginalAmount,
        c.PotentialBasis, c.Balance(),
        c.Entries.OrderBy(e => e.CreatedAt).Select(e => new TaxCreditEntryDto(e.Id, e.Type, e.Amount, e.ObligationReference, e.Reason, e.OccurredOn, e.ActorName, e.CreatedAt)).ToList(),
        c.CreatedAt, c.Stage, c.Lifecycle(), c.RecognitionBasis, c.DiscardReason);
}
