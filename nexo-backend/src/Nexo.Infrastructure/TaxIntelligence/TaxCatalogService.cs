using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// The rule catalogue: read by every tenant, written only through the platform admin API. Rule
/// versions are created as drafts with their content fixed and fingerprinted; afterwards only the
/// status moves. Activation needs verified legal sources, and an active version fully covered by a
/// newer active one becomes superseded — it stays in the catalogue, so old calculations can still be
/// rebuilt with it.
/// </summary>
public sealed class TaxCatalogService
{
    private readonly NexoDbContext _db;
    private readonly TaxAuditTrail _audit;
    private readonly ILogger<TaxCatalogService> _log;

    public TaxCatalogService(NexoDbContext db, TaxAuditTrail audit, ILogger<TaxCatalogService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    // ── Engine view ─────────────────────────────────────────────────────────────

    /// <summary>Every version covering the date, whatever its status — the engine applies only active ones and lists the rest.</summary>
    public async Task<IReadOnlyList<TaxEngineRule>> LoadForDateAsync(DateOnly date, CancellationToken ct)
        => await LoadAsync(_db.TaxRuleVersions.Where(v => v.ValidFrom <= date && (v.ValidTo == null || v.ValidTo >= date)), ct);

    /// <summary>Exactly these versions, whatever their status today — used to rebuild a past calculation.</summary>
    public async Task<IReadOnlyList<TaxEngineRule>> LoadByIdsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken ct)
        => await LoadAsync(_db.TaxRuleVersions.Where(v => versionIds.Contains(v.Id)), ct);

    private async Task<IReadOnlyList<TaxEngineRule>> LoadAsync(IQueryable<TaxRuleVersion> versions, CancellationToken ct)
    {
        var rows = await versions.AsNoTracking()
            .Join(_db.TaxRules.AsNoTracking(), v => v.RuleId, r => r.Id, (v, r) => new { v, r.Key })
            .ToListAsync(ct);
        var ids = rows.Select(x => x.v.Id).ToList();
        var codes = await _db.Set<TaxRuleVersionSource>().AsNoTracking()
            .Where(s => ids.Contains(s.RuleVersionId))
            .Select(s => new { s.RuleVersionId, s.LegalSource!.Code })
            .ToListAsync(ct);
        var codesByVersion = codes.GroupBy(c => c.RuleVersionId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(c => c.Code).OrderBy(c => c, StringComparer.Ordinal).ToList());

        return rows.Select(x => new TaxEngineRule(
                x.v.Id, x.Key, x.v.Version, x.v.Status, x.v.ValidFrom, x.v.ValidTo, x.v.Priority,
                TaxCanonical.Deserialize<List<TaxCondition>>(x.v.ConditionsJson),
                TaxCanonical.Deserialize<TaxRuleResult>(x.v.ResultJson),
                x.v.ContentHash,
                codesByVersion.GetValueOrDefault(x.v.Id) ?? []))
            .ToList();
    }

    // ── Reading ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<TaxRuleDto>> GetRulesAsync(CancellationToken ct)
    {
        var rules = await _db.TaxRules.AsNoTracking()
            .Include(r => r.Versions).ThenInclude(v => v.Sources).ThenInclude(s => s.LegalSource)
            .OrderBy(r => r.Key)
            .AsSplitQuery()
            .ToListAsync(ct);
        return rules.Select(r => new TaxRuleDto(r.Id, r.Key, r.Name, r.Description,
            r.Versions.OrderByDescending(v => v.Version).Select(MapVersion).ToList())).ToList();
    }

    public async Task<IReadOnlyList<TaxLegalSourceDto>> GetLegalSourcesAsync(CancellationToken ct)
        => (await _db.TaxLegalSources.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct)).Select(MapSource).ToList();

    // ── Platform administration ─────────────────────────────────────────────────

    public async Task<TaxLegalSourceDto> CreateLegalSourceAsync(CreateLegalSourceRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await _db.TaxLegalSources.AnyAsync(s => s.Code == code, ct))
            throw new ConflictException($"Já existe a fonte {code}.");
        var source = TaxLegalSource.Create(code, r.Kind, r.Title, r.Reference, r.Url, r.Excerpt, r.PublishedOn, r.Notes);
        _db.TaxLegalSources.Add(source);
        _audit.RecordPlatform(TaxAuditActions.LegalSourceCreated, "LegalSource", source.Id, $"Fonte {code} cadastrada ({r.Reference}).");
        await _db.SaveChangesAsync(ct);
        return MapSource(source);
    }

    public async Task<TaxLegalSourceDto> VerifyLegalSourceAsync(string code, VerifyLegalSourceRequest r, CancellationToken ct)
    {
        var source = await _db.TaxLegalSources.FirstOrDefaultAsync(s => s.Code == code.ToUpperInvariant(), ct)
            ?? throw new NotFoundException("Fonte jurídica", code);
        source.MarkVerified(r.VerifiedBy, DateTime.UtcNow);
        _audit.RecordPlatform(TaxAuditActions.LegalSourceVerified, "LegalSource", source.Id, $"Fonte {source.Code} conferida por {r.VerifiedBy}.");
        await _db.SaveChangesAsync(ct);
        return MapSource(source);
    }

    public async Task<TaxRuleDto> CreateRuleAsync(CreateTaxRuleRequest r, CancellationToken ct)
    {
        var key = r.Key.Trim().ToUpperInvariant();
        if (key.StartsWith(TaxSimulator.HypothesisKeyPrefix, StringComparison.Ordinal))
            throw new DomainException("Chaves HYPOTHESIS.* são reservadas às simulações.");
        if (await _db.TaxRules.AnyAsync(x => x.Key == key, ct))
            throw new ConflictException($"Já existe a regra {key}.");
        var rule = TaxRule.Create(key, r.Name, r.Description);
        _db.TaxRules.Add(rule);
        _audit.RecordPlatform(TaxAuditActions.RuleCreated, "Rule", rule.Id, $"Regra {key} criada.");
        await _db.SaveChangesAsync(ct);
        return new TaxRuleDto(rule.Id, rule.Key, rule.Name, rule.Description, []);
    }

    public async Task<TaxRuleVersionDto> CreateVersionAsync(string ruleKey, CreateTaxRuleVersionRequest r, CancellationToken ct)
    {
        var key = ruleKey.Trim().ToUpperInvariant();
        var rule = await _db.TaxRules.FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw new NotFoundException("Regra", key);

        List<TaxCondition> conditions;
        TaxRuleResult result;
        try
        {
            conditions = TaxCanonical.Deserialize<List<TaxCondition>>(r.Conditions.GetRawText());
            result = TaxCanonical.Deserialize<TaxRuleResult>(r.Result.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new DomainException("Condições ou resultado fora do vocabulário da engine.", "tax_rule_content");
        }
        TaxRuleContentValidator.Validate(key, conditions, result);

        var codes = r.LegalSourceCodes.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        var sources = await _db.TaxLegalSources.Where(s => codes.Contains(s.Code)).Select(s => new { s.Id, s.Code }).ToListAsync(ct);
        var missing = codes.Except(sources.Select(s => s.Code)).ToList();
        if (missing.Count > 0) throw new DomainException($"Fontes não cadastradas: {string.Join(", ", missing)}.");

        await LockRuleAsync(rule.Id, ct);
        var next = (await _db.TaxRuleVersions.Where(v => v.RuleId == rule.Id).MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
        var hash = TaxCanonical.RuleContentHash(key, next, r.ValidFrom, r.ValidTo, r.Priority, conditions, result);
        var version = TaxRuleVersion.Create(rule.Id, next, r.ValidFrom, r.ValidTo, r.Priority,
            TaxCanonical.Serialize(conditions), TaxCanonical.Serialize(result), hash, "platform", _audit.UserName, r.Notes,
            sources.Select(s => s.Id));
        _db.TaxRuleVersions.Add(version);
        _audit.RecordPlatform(TaxAuditActions.RuleVersionCreated, "RuleVersion", version.Id,
            $"Regra {key} v{next} criada como rascunho ({r.ValidFrom:yyyy-MM-dd} a {(r.ValidTo is null ? "sem fim" : r.ValidTo.Value.ToString("yyyy-MM-dd"))}).",
            new { key, version = next, hash, conditions = version.ConditionsJson, result = version.ResultJson, sources = codes });
        await _db.SaveChangesAsync(ct);
        await _db.Database.CommitTransactionAsync(ct);
        _log.LogInformation("Tax rule version created: {RuleKey} v{Version} ({ContentHash})", key, next, hash);
        return await GetVersionAsync(version.Id, ct);
    }

    public async Task<TaxRuleVersionDto> ChangeStatusAsync(Guid versionId, ChangeTaxRuleVersionStatusRequest r, CancellationToken ct)
    {
        var version = await _db.TaxRuleVersions.Include(v => v.Sources).ThenInclude(s => s.LegalSource)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct) ?? throw new NotFoundException("Versão de regra", versionId);
        await LockRuleAsync(version.RuleId, ct);

        if (r.Status == TaxRuleStatus.Active)
        {
            if (version.Sources.Count == 0 || version.Sources.Any(s => s.LegalSource is not { IsVerified: true }))
                throw new DomainException("Só uma regra com todas as fontes jurídicas conferidas pode ser ativada.", "tax_rule_unverified_source");
        }

        var before = version.Status;
        var now = DateTime.UtcNow;
        version.ChangeStatus(r.Status, now);

        var superseded = new List<int>();
        if (r.Status == TaxRuleStatus.Active)
        {
            var older = await _db.TaxRuleVersions
                .Where(v => v.RuleId == version.RuleId && v.Id != version.Id && v.Status == TaxRuleStatus.Active && v.Version < version.Version)
                .ToListAsync(ct);
            foreach (var o in older.Where(o => o.ValidFrom >= version.ValidFrom
                         && (version.ValidTo is null || (o.ValidTo is not null && o.ValidTo <= version.ValidTo))))
            {
                o.ChangeStatus(TaxRuleStatus.Superseded, now);
                superseded.Add(o.Version);
            }
        }

        _audit.RecordPlatform(TaxAuditActions.RuleVersionStatusChanged, "RuleVersion", version.Id,
            $"Versão {version.Version} da regra passou de {before} para {r.Status}.",
            new { before = before.ToString(), after = r.Status.ToString(), superseded });
        await _db.SaveChangesAsync(ct);
        await _db.Database.CommitTransactionAsync(ct);
        _log.LogWarning("Tax rule version {VersionId} status {Before} -> {After}; superseded {Superseded}", version.Id, before, r.Status, superseded);
        return await GetVersionAsync(version.Id, ct);
    }

    private async Task<TaxRuleVersionDto> GetVersionAsync(Guid id, CancellationToken ct)
    {
        var v = await _db.TaxRuleVersions.AsNoTracking().Include(x => x.Sources).ThenInclude(s => s.LegalSource)
            .FirstAsync(x => x.Id == id, ct);
        return MapVersion(v);
    }

    /// <summary>Serialises version numbering and status changes of one rule (transaction-scoped advisory lock).</summary>
    private async Task LockRuleAsync(Guid ruleId, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is null) await _db.Database.BeginTransactionAsync(ct);
        var key = BitConverter.ToInt64(ruleId.ToByteArray(), 0);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }

    private static TaxRuleVersionDto MapVersion(TaxRuleVersion v) => new(
        v.Id, v.Version, v.Status, v.ValidFrom, v.ValidTo, v.Priority,
        TaxJson.Element(v.ConditionsJson), TaxJson.Element(v.ResultJson),
        v.ContentHash, v.Origin, v.CreatedBy, v.Notes, v.ActivatedAt,
        v.Sources.Where(s => s.LegalSource is not null).Select(s => s.LegalSource!.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
        v.CreatedAt);

    private static TaxLegalSourceDto MapSource(TaxLegalSource s) => new(
        s.Id, s.Code, s.Kind, s.Title, s.Reference, s.Url, s.Excerpt, s.PublishedOn, s.Verification, s.VerifiedAt, s.VerifiedBy, s.Notes);
}
