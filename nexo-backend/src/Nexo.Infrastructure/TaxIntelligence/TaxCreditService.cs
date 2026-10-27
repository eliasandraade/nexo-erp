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
/// database also refuses a second use of a credit in the same obligation. Eligibility is recorded as
/// declared — this version does not decide it automatically.
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
            return new TaxCreditTotalsDto(g.Key, b.Sum(x => x.Eligible), b.Sum(x => x.Appropriated), b.Sum(x => x.Used),
                b.Sum(x => x.Blocked), b.Sum(x => x.Available), b.Sum(x => x.NotAppropriated));
        }).ToList();
    }

    public async Task<TaxCreditDto> GetAsync(Guid id, CancellationToken ct)
        => Map(await _db.TaxCredits.AsNoTracking().Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct)
               ?? throw new NotFoundException("Crédito", id));

    public async Task<TaxCreditDto> RegisterAsync(RegisterTaxCreditRequest r, CancellationToken ct)
    {
        var credit = TaxCredit.Register(_tenant.Id, r.Tax, r.SourceDocumentType,
            string.IsNullOrWhiteSpace(r.SourceDocumentKey) ? null : r.SourceDocumentKey.Trim(), r.SupplierDocument,
            r.AcquiredOn, r.ExpiresOn, r.OriginalAmount, r.EligibleAmount, r.EligibilityBasis, null, _audit.UserId, _audit.UserName);
        _db.TaxCredits.Add(credit);
        _audit.Record(TaxAuditActions.CreditRegistered, "Credit", credit.Id,
            $"Crédito de {r.Tax} registrado: {r.EligibleAmount:N2} elegível de {r.OriginalAmount:N2} ({r.SourceDocumentType}).",
            new { tax = r.Tax.ToString(), r.OriginalAmount, r.EligibleAmount, r.EligibilityBasis, hasDocumentKey = r.SourceDocumentKey is not null });
        await _db.SaveChangesAsync(ct);
        return Map(credit);
    }

    public async Task<TaxCreditDto> MoveAsync(Guid id, TaxCreditMovementRequest r, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM nexo.tax_credits WHERE id = {id} AND tenant_id = {_tenant.Id} FOR UPDATE", ct);
        var credit = await _db.TaxCredits.Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Crédito", id);

        var before = credit.Balance();
        var amount = r.Amount ?? 0m;
        var reason = r.Reason?.Trim() ?? string.Empty;
        var entry = r.Movement switch
        {
            TaxCreditMovement.Appropriate => credit.Appropriate(amount, reason.Length > 0 ? reason : "Apropriação.", _audit.UserId, _audit.UserName),
            TaxCreditMovement.Use => credit.Use(amount, r.ObligationTax ?? credit.Tax, r.ObligationReference ?? string.Empty,
                r.OnDate ?? DateOnly.FromDateTime(DateTime.UtcNow), _audit.UserId, _audit.UserName),
            TaxCreditMovement.Block => credit.Block(amount, reason, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Unblock => credit.Unblock(amount, reason.Length > 0 ? reason : "Desbloqueio.", _audit.UserId, _audit.UserName),
            TaxCreditMovement.Reverse => credit.ReverseAppropriation(amount, reason, _audit.UserId, _audit.UserName),
            TaxCreditMovement.Expire => credit.Expire(r.OnDate ?? DateOnly.FromDateTime(DateTime.UtcNow), _audit.UserId, _audit.UserName),
            _ => throw new DomainException("Movimento desconhecido."),
        };
        _db.TaxCreditLedgerEntries.Add(entry);
        var after = credit.Balance();
        _audit.Record(TaxAuditActions.CreditMovement, "Credit", credit.Id,
            $"Crédito de {credit.Tax}: {entry.Type} de {entry.Amount:N2}.",
            new { entryId = entry.Id, type = entry.Type.ToString(), entry.Amount, entry.ObligationReference, before, after });
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
        c.EligibilityBasis, c.Balance(),
        c.Entries.OrderBy(e => e.CreatedAt).Select(e => new TaxCreditEntryDto(e.Id, e.Type, e.Amount, e.ObligationReference, e.Reason, e.ActorName, e.CreatedAt)).ToList(),
        c.CreatedAt);
}
