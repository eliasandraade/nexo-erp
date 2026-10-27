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

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxCreditService> _log;

    public TaxCreditService(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit, ILogger<TaxCreditService> log)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _log = log;
    }

    public async Task<IReadOnlyList<TaxCreditDto>> ListAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxList);
        var credits = await _db.TaxCredits.AsNoTracking().Include(c => c.Entries)
            .OrderByDescending(c => c.AcquiredOn).ThenByDescending(c => c.CreatedAt).Take(take)
            .AsSplitQuery().ToListAsync(ct);
        return credits.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<TaxCreditTotalsDto>> TotalsAsync(CancellationToken ct)
    {
        var credits = await _db.TaxCredits.AsNoTracking().Include(c => c.Entries).AsSplitQuery().ToListAsync(ct);
        return credits.GroupBy(c => c.Tax).OrderBy(g => g.Key).Select(g =>
        {
            var b = g.Select(c => c.Balance()).ToList();
            var live = g.Where(c => c.Stage != TaxCreditStage.Discarded).ToList();
            return new TaxCreditTotalsDto(g.Key,
                live.Where(c => c.Stage is TaxCreditStage.PotentialIdentified or TaxCreditStage.RequirementsUnderReview).Sum(c => c.PotentialAmount),
                b.Sum(x => x.ToBeAppropriated), b.Sum(x => x.Appropriated), b.Sum(x => x.Used), b.Sum(x => x.Blocked), b.Sum(x => x.Available));
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

        var before = credit.Balance();
        var stageBefore = credit.Stage;
        // Expiry always writes off the whole available balance — a partial amount is never silently ignored.
        if (r.Movement == TaxCreditMovement.Expire && r.Amount is not null)
            throw new DomainException("O vencimento dá baixa em todo o saldo disponível; não informe valor.", "tax_credit_expire_whole_balance");
        var amount = r.Amount ?? 0m;
        var reason = r.Reason?.Trim() ?? string.Empty;
        if (r.Movement is TaxCreditMovement.StartRequirementsReview or TaxCreditMovement.Discard)
        {
            if (r.Movement == TaxCreditMovement.StartRequirementsReview) credit.StartRequirementsReview();
            else credit.Discard(reason);
            _audit.Record(TaxAuditActions.CreditStageChanged, "Credit", credit.Id,
                $"Crédito de {credit.Tax}: {stageBefore} → {credit.Stage}.", new { before = stageBefore.ToString(), after = credit.Stage.ToString(), reason });
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(credit);
        }
        var entry = r.Movement switch
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Este crédito já foi usado nesta obrigação.");
        }
        await tx.CommitAsync(ct);
        _log.LogInformation("Tax credit {CreditId} movement {Type}", credit.Id, entry.Type);
        return Map(credit);
    }

    private static TaxCreditDto Map(TaxCredit c) => new(
        c.Id, c.Tax, c.SourceDocumentType, c.SourceDocumentKey, c.SupplierDocument, c.AcquiredOn, c.ExpiresOn, c.OriginalAmount,
        c.PotentialBasis, c.Balance(),
        c.Entries.OrderBy(e => e.CreatedAt).Select(e => new TaxCreditEntryDto(e.Id, e.Type, e.Amount, e.ObligationReference, e.Reason, e.OccurredOn, e.ActorName, e.CreatedAt)).ToList(),
        c.CreatedAt, c.Stage, c.Lifecycle(), c.RecognitionBasis, c.DiscardReason);
}
