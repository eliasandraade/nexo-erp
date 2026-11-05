using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// The tax classification catalogue and how a company classifies its products. Codes are imported from a
/// documented source as Draft and activated explicitly by the platform (audited); the coverage matrix only accepts
/// a cClassTrib active on the operation's date. The software never invents or interprets a code.
/// </summary>
public sealed class TaxClassificationService
{
    private const int MaxPage = 200;
    private const string AuditEntity = "Classification";

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly TaxAuditTrail _audit;

    public TaxClassificationService(NexoDbContext db, ICurrentTenant tenant, TaxAuditTrail audit)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
    }

    /// <summary>The cClassTrib codes of an operation as the catalogue holds them on a date (the newest validity covering it).</summary>
    public async Task<IReadOnlyDictionary<string, TaxClassificationEntry>> CatalogueForAsync(
        IEnumerable<string?> codes, DateOnly date, CancellationToken ct)
    {
        var wanted = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim().ToUpperInvariant()).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<string, TaxClassificationEntry>();
        var rows = await _db.TaxClassificationCodes.AsNoTracking()
            .Where(c => c.Kind == TaxClassificationKind.CClassTrib && wanted.Contains(c.Code)
                        && c.ValidFrom <= date && (c.ValidTo == null || c.ValidTo >= date))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.Code)
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .ToDictionary(r => r.Code, r => new TaxClassificationEntry(r.Code, r.Status == TaxClassificationStatus.Active, r.IsProfessionallyApproved));
    }

    // ── Platform catalogue ──────────────────────────────────────────────────────

    public async Task<TaxPage<TaxClassificationCodeDto>> ListAsync(TaxClassificationKind? kind, TaxClassificationStatus? status, string? search,
        int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxPage);
        skip = Math.Max(0, skip);
        var q = _db.TaxClassificationCodes.AsNoTracking();
        if (kind is not null) q = q.Where(c => c.Kind == kind);
        if (status is not null) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            q = q.Where(c => c.Code.Contains(s) || EF.Functions.ILike(c.Description, $"%{search.Trim()}%"));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(c => c.Kind).ThenBy(c => c.Code).ThenByDescending(c => c.ValidFrom).Skip(skip).Take(take).ToListAsync(ct);
        return new TaxPage<TaxClassificationCodeDto>(rows.Select(Map).ToList(), total, skip, take);
    }

    /// <summary>
    /// Imports codes from a documented source. Each enters as Draft; a code already present with the same validity start is
    /// skipped and reported. Nothing becomes usable until it is activated.
    /// </summary>
    public async Task<TaxClassificationImportResultDto> ImportAsync(ImportClassificationCodesRequest r, CancellationToken ct)
    {
        if (r.Items.Count is 0 or > 5000) throw new DomainException("Importe de 1 a 5.000 códigos por vez.", "tax_classification_import_size");
        if (r.SourceDocumentId is { } docId && !await _db.TaxPlatformDocuments.AnyAsync(d => d.Id == docId, ct))
            throw new NotFoundException("Documento", docId);
        var batch = Guid.NewGuid();
        var normalized = r.Items.Select(i => (Item: i, Code: TaxClassificationCode.Normalize(r.Kind, i.Code))).ToList();
        var codes = normalized.Select(x => x.Code).Distinct().ToList();
        var existing = (await _db.TaxClassificationCodes.AsNoTracking().Where(c => c.Kind == r.Kind && codes.Contains(c.Code))
            .Select(c => new { c.Code, c.ValidFrom }).ToListAsync(ct)).Select(x => (x.Code, x.ValidFrom)).ToHashSet();
        var skipped = new List<string>();
        var imported = 0;
        foreach (var (item, code) in normalized)
        {
            if (!existing.Add((code, item.ValidFrom)))
            {
                skipped.Add($"{code} (desde {item.ValidFrom:dd/MM/yyyy}) já existe");
                continue;
            }
            _db.TaxClassificationCodes.Add(TaxClassificationCode.Import(r.Kind, code, item.Description, r.Source, r.SourceVersion,
                item.ValidFrom, item.ValidTo, item.Notes, batch, r.SourceDocumentId, _audit.UserName));
            imported++;
        }
        _audit.RecordPlatform(TaxAuditActions.ClassificationImported, AuditEntity, batch,
            $"Catálogo {r.Kind}: {imported} código(s) importado(s) como rascunho de \"{r.Source.Trim()}\".",
            new { kind = r.Kind.ToString(), r.Source, r.SourceVersion, r.SourceDocumentId, imported, skipped = skipped.Count });
        await _db.SaveChangesAsync(ct);
        return new TaxClassificationImportResultDto(batch, imported, skipped);
    }

    public async Task<TaxClassificationCodeDto> ChangeStatusAsync(Guid id, ChangeClassificationStatusRequest r, CancellationToken ct)
    {
        var code = await _db.TaxClassificationCodes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Código", id);
        if (string.IsNullOrWhiteSpace(r.Reason)) throw new DomainException("Diga por que o código muda de estado.", "tax_classification_reason");
        var before = code.Status;
        code.ChangeStatus(r.Status, _audit.UserName, DateTime.UtcNow);
        _audit.RecordPlatform(TaxAuditActions.ClassificationStatusChanged, AuditEntity, code.Id,
            $"{code.Kind} {code.Code}: {before} → {r.Status}. {r.Reason.Trim()}",
            new { code.Kind, code.Code, before = before.ToString(), after = r.Status.ToString(), reason = r.Reason.Trim() });
        await _db.SaveChangesAsync(ct);
        return Map(code);
    }

    public async Task<TaxClassificationCodeDto> RecordReviewAsync(Guid id, RecordClassificationReviewRequest r, CancellationToken ct)
    {
        var code = await _db.TaxClassificationCodes.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Código", id);
        var before = code.ProfessionalReview;
        code.RecordProfessionalReview(r.Status, r.Notes);
        _audit.RecordPlatform(TaxAuditActions.ClassificationReviewed, AuditEntity, code.Id,
            $"Revisão profissional de {code.Kind} {code.Code}: {before} → {r.Status} (registrada por {_audit.UserName}).",
            new { code.Kind, code.Code, before = before.ToString(), after = r.Status.ToString(), notes = r.Notes.Trim(), recordedBy = _audit.UserName });
        await _db.SaveChangesAsync(ct);
        return Map(code);
    }

    // ── Company: product classification ─────────────────────────────────────────

    /// <summary>Products of the company with the classification set for each (or none yet), with the catalogue status of the code.</summary>
    public async Task<TaxPage<TaxItemClassificationDto>> ListItemsAsync(string? search, bool onlyMissing, int skip, int take, DateOnly today, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxPage);
        skip = Math.Max(0, skip);
        var products = _db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) products = products.Where(p => EF.Functions.ILike(p.Name, $"%{search.Trim()}%"));
        var q = from p in products
                join c in _db.TaxItemClassifications.AsNoTracking() on p.Id equals c.ProductId into cs
                from c in cs.DefaultIfEmpty()
                select new { p.Id, p.Name, p.Ncm, c };
        if (onlyMissing) q = q.Where(x => x.c == null);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.Name).Skip(skip).Take(take).ToListAsync(ct);
        var catalogue = await CatalogueForAsync(rows.Select(r => r.c?.CClassTrib), today, ct);
        var statusByCode = await _db.TaxClassificationCodes.AsNoTracking()
            .Where(x => x.Kind == TaxClassificationKind.CClassTrib && catalogue.Keys.Contains(x.Code))
            .GroupBy(x => x.Code).Select(g => new { g.Key, Status = g.OrderByDescending(x => x.ValidFrom).Select(x => x.Status).First() })
            .ToDictionaryAsync(x => x.Key, x => x.Status, ct);
        return new TaxPage<TaxItemClassificationDto>(rows.Select(r => new TaxItemClassificationDto(r.c?.Id, r.Id, r.Name, r.Ncm,
            r.c?.ItemKind, r.c?.Ncm, r.c?.Nbs, r.c?.CClassTrib,
            CatalogStatusOf(r.c, statusByCode),
            r.c?.Notes, r.c?.SetByName, r.c?.UpdatedAt)).ToList(), total, skip, take);
    }

    public async Task<TaxItemClassificationDto> SetItemAsync(Guid productId, SetItemClassificationRequest r, DateOnly today, CancellationToken ct)
    {
        var product = await _db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => new { p.Id, p.Name, p.Ncm }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Produto", productId);
        var item = await _db.TaxItemClassifications.FirstOrDefaultAsync(c => c.ProductId == productId, ct);
        var before = item is null ? null : new { item.ItemKind, item.Ncm, item.Nbs, item.CClassTrib };
        if (item is null)
        {
            item = TaxItemClassification.Create(_tenant.Id, productId, _audit.UserId, _audit.UserName);
            _db.TaxItemClassifications.Add(item);
        }
        item.Set(r.ItemKind, r.Ncm, r.Nbs, r.CClassTrib, r.Notes, _audit.UserId, _audit.UserName);
        _audit.Record(TaxAuditActions.ItemClassificationSet, AuditEntity, item.Id,
            $"Classificação de \"{product.Name}\": {item.ItemKind}, cClassTrib {item.CClassTrib}.",
            new { productId, before, after = new { item.ItemKind, item.Ncm, item.Nbs, item.CClassTrib } });
        await _db.SaveChangesAsync(ct);
        var entry = (await CatalogueForAsync([item.CClassTrib], today, ct)).GetValueOrDefault(item.CClassTrib);
        return new TaxItemClassificationDto(item.Id, product.Id, product.Name, product.Ncm, item.ItemKind, item.Ncm, item.Nbs, item.CClassTrib,
            StatusOf(entry), item.Notes, item.SetByName, item.UpdatedAt);
    }

    private static TaxClassificationStatus? CatalogStatusOf(TaxItemClassification? item, Dictionary<string, TaxClassificationStatus> statusByCode)
        => item is not null && statusByCode.TryGetValue(item.CClassTrib, out var status) ? status : null;

    private static TaxClassificationStatus? StatusOf(TaxClassificationEntry? entry)
    {
        if (entry is null) return null;
        return entry.Active ? TaxClassificationStatus.Active : TaxClassificationStatus.Inactive;
    }

    /// <summary>Classifications of the given products, for the sales reader.</summary>
    public async Task<IReadOnlyDictionary<Guid, TaxItemClassification>> ForProductsAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        return await _db.TaxItemClassifications.AsNoTracking().Where(c => ids.Contains(c.ProductId)).ToDictionaryAsync(c => c.ProductId, ct);
    }

    public static TaxClassificationCodeDto Map(TaxClassificationCode c) => new(
        c.Id, c.Kind, c.Code, c.Description, c.Source, c.SourceVersion, c.ValidFrom, c.ValidTo, c.Status, c.ProfessionalReview,
        c.ProfessionalReviewNotes, c.Notes, c.ImportBatchId, c.SourceDocumentId, c.CreatedByName, c.CreatedAt, c.StatusChangedByName, c.StatusChangedAt);
}
