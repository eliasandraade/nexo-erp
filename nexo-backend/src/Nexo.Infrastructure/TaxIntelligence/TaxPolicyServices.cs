using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Microsoft.Extensions.Options;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>The materiality a decision runs under on its date: the company's version in force, else the installation default, else none.</summary>
public sealed record TaxMaterialityInForce(decimal? Threshold, string Source, TaxMaterialityPolicy? Policy)
{
    public const string CompanyPolicy = "company-policy";
    public const string InstallationDefault = "installation-default";
    public const string NotConfigured = "not-configured";
}

/// <summary>
/// The company's materiality threshold, versioned. Each change is a new version from a date; a decision records the version in force
/// on its own date, so a change tomorrow never rewrites a past decision. With nothing configured there is no materiality rule (default).
/// </summary>
public sealed class TaxMaterialityService
{
    private const string AuditEntity = "Materiality";
    private readonly TaxContext _ctx;
    private readonly TaxIntelligencePolicyOptions _policy;

    public TaxMaterialityService(TaxContext ctx, IOptions<TaxIntelligencePolicyOptions> policy)
    {
        _ctx = ctx;
        _policy = policy.Value;
    }

    public async Task<TaxMaterialityInForce> InForceAsync(DateOnly date, CancellationToken ct)
    {
        var versions = await _ctx.Db.TaxMaterialityPolicies.AsNoTracking().ToListAsync(ct);
        var policy = TaxMaterialityPolicy.InForce(versions, date);
        if (policy is not null) return new(policy.Threshold, TaxMaterialityInForce.CompanyPolicy, policy);
        return _policy.MaterialityThreshold is { } d
            ? new(d, TaxMaterialityInForce.InstallationDefault, null)
            : new(null, TaxMaterialityInForce.NotConfigured, null);
    }

    public async Task<TaxMaterialityDto> GetAsync(DateOnly today, CancellationToken ct)
    {
        var versions = await _ctx.Db.TaxMaterialityPolicies.AsNoTracking().OrderByDescending(v => v.Version).ToListAsync(ct);
        var inForce = TaxMaterialityPolicy.InForce(versions, today);
        return new TaxMaterialityDto(inForce is null ? null : Map(inForce), _policy.MaterialityThreshold, versions.Select(Map).ToList());
    }

    public async Task<TaxMaterialityDto> ConfigureAsync(ConfigureMaterialityRequest r, DateOnly today, CancellationToken ct)
    {
        if (r.EffectiveFrom < today)
            throw new DomainException("Uma nova materialidade vale de hoje em diante: decisões passadas ficam com a versão que usaram.", "tax_materiality_retroactive");
        await using var tx = await _ctx.Db.Database.BeginTransactionAsync(ct);
        var key = BitConverter.ToInt64(_ctx.Tenant.Id.ToByteArray(), 0) ^ 0x4D41_5445;
        await _ctx.Db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        var next = (await _ctx.Db.TaxMaterialityPolicies.MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
        var policy = TaxMaterialityPolicy.Create(_ctx.Tenant.Id, next, r.Threshold, r.Currency, r.EffectiveFrom, r.EffectiveTo, r.Rationale,
            r.DecisionReference, _ctx.Audit.UserId, _ctx.Audit.UserName);
        _ctx.Db.TaxMaterialityPolicies.Add(policy);
        _ctx.Audit.Record(TaxAuditActions.MaterialityConfigured, AuditEntity, policy.Id,
            $"Materialidade v{next}: R$ {r.Threshold:N2} a partir de {r.EffectiveFrom:dd/MM/yyyy} ({r.DecisionReference.Trim()}).",
            new { version = next, r.Threshold, r.Currency, r.EffectiveFrom, r.EffectiveTo, r.Rationale, r.DecisionReference }, AuditSeverity.Warning);
        await _ctx.Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(today, ct);
    }

    public static TaxMaterialityPolicyDto Map(TaxMaterialityPolicy p) => new(
        p.Id, p.Version, p.Threshold, p.Currency, p.EffectiveFrom, p.EffectiveTo, p.Rationale, p.DecisionReference, p.ConfiguredByName, p.CreatedAt);
}

/// <summary>
/// Retention policies of tax evidence and the preview of what a valid policy would make eligible for disposal. There is no
/// automatic disposal: the engine only previews, a legal hold always wins, and no policy (or a disabled one) means "keep".
/// A company may set its own period only where the platform default for that type allows it.
/// </summary>
public sealed class TaxRetentionService
{
    private const string AuditEntity = "Retention";
    public const string PreviewNotice =
        "Prévia apenas. Nenhum arquivo é descartado automaticamente; antes de qualquer descarte, exporte o dossiê e confirme que não há retenção legal.";

    private readonly TaxContext _ctx;

    public TaxRetentionService(TaxContext ctx) => _ctx = ctx;

    public async Task<IReadOnlyList<TaxRetentionPolicyDto>> ListAsync(bool platform, CancellationToken ct)
    {
        var tenant = _ctx.Tenant.Id;
        var q = _ctx.Db.TaxRetentionPolicies.AsNoTracking().Where(p => p.TenantId == null || (!platform && p.TenantId == tenant));
        return (await q.OrderBy(p => p.TenantId).ThenBy(p => p.EvidenceType).ThenByDescending(p => p.Version).ToListAsync(ct)).Select(Map).ToList();
    }

    /// <summary>A new version of the policy for a type (platform default when <paramref name="platform"/>, else this company's).</summary>
    public async Task<TaxRetentionPolicyDto> ConfigureAsync(ConfigureRetentionPolicyRequest r, bool platform, CancellationToken ct)
    {
        Guid? tenant = platform ? null : _ctx.Tenant.Id;
        await using var tx = await _ctx.Db.Database.BeginTransactionAsync(ct);
        await _ctx.Db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7240012)", ct);
        if (!platform)
        {
            var defaults = await CurrentAsync(null, r.EvidenceType, ct);
            if (defaults is not null && !defaults.AllowTenantOverride)
                throw new DomainException("A política padrão da plataforma para este tipo não permite que a empresa defina outro prazo.", "tax_retention_override");
        }
        var current = await _ctx.Db.TaxRetentionPolicies
            .Where(p => p.TenantId == tenant && p.EvidenceType == r.EvidenceType && p.SupersededAt == null).ToListAsync(ct);
        var next = (await _ctx.Db.TaxRetentionPolicies.Where(p => p.TenantId == tenant && p.EvidenceType == r.EvidenceType)
            .MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;
        var policy = TaxRetentionPolicy.Create(tenant, next, r.EvidenceType, r.RetentionMonths, r.StartsAt, r.Enabled, platform && r.AllowTenantOverride,
            r.Rationale, r.Reference, _ctx.Audit.UserId, _ctx.Audit.UserName);
        foreach (var c in current) c.Supersede(DateTime.UtcNow);
        _ctx.Db.TaxRetentionPolicies.Add(policy);
        var description = $"Retenção {(platform ? "padrão" : "da empresa")} v{next} para {r.EvidenceType?.ToString() ?? "todos os tipos"}: {r.RetentionMonths} meses ({(r.Enabled ? "ativa" : "inativa")}).";
        var details = new { version = next, evidenceType = r.EvidenceType?.ToString(), r.RetentionMonths, startsAt = r.StartsAt.ToString(), r.Enabled,
            r.AllowTenantOverride, r.Rationale, r.Reference };
        if (platform) _ctx.Audit.RecordPlatform(TaxAuditActions.RetentionConfigured, AuditEntity, policy.Id, description, details);
        else _ctx.Audit.Record(TaxAuditActions.RetentionConfigured, AuditEntity, policy.Id, description, details, AuditSeverity.Warning);
        await _ctx.Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(policy);
    }

    private Task<TaxRetentionPolicy?> CurrentAsync(Guid? tenant, TaxEvidenceType? type, CancellationToken ct)
        => _ctx.Db.TaxRetentionPolicies.AsNoTracking()
            .Where(p => p.TenantId == tenant && p.EvidenceType == type && p.SupersededAt == null).FirstOrDefaultAsync(ct);

    /// <summary>
    /// What a valid policy would make eligible on <paramref name="asOf"/> for this company — files in force, not on legal hold,
    /// past the period of the policy that applies to their type. Read-only: nothing is discarded, ever, by this engine.
    /// </summary>
    public async Task<TaxRetentionPreviewDto> PreviewAsync(DateOnly asOf, int take, CancellationToken ct)
    {
        var policies = await _ctx.Db.TaxRetentionPolicies.AsNoTracking()
            .Where(p => p.SupersededAt == null && (p.TenantId == null || p.TenantId == _ctx.Tenant.Id)).ToListAsync(ct);
        var files = await _ctx.Db.TaxEvidenceFiles.AsNoTracking()
            .Select(f => new { f.Id, f.TenantId, f.OriginalFileName, f.EvidenceType, f.CreatedAt, f.LegalHold, f.Sha256 })
            .ToListAsync(ct);
        var eligible = new List<TaxRetentionCandidateDto>();
        int onHold = 0, withoutPolicy = 0;
        foreach (var f in files)
        {
            if (f.LegalHold) { onHold++; continue; }
            var policy = Effective(policies, f.EvidenceType);
            if (policy is null || !policy.Enabled) { withoutPolicy++; continue; }
            var from = policy.EligibleFrom(DateOnly.FromDateTime(f.CreatedAt));
            if (from <= asOf)
                eligible.Add(new TaxRetentionCandidateDto(f.Id, f.TenantId, f.OriginalFileName, f.EvidenceType, f.CreatedAt, from, policy.Id, policy.Version, f.Sha256));
        }
        return new TaxRetentionPreviewDto(asOf, false, files.Count, onHold, withoutPolicy,
            eligible.OrderBy(e => e.EligibleFrom).Take(Math.Clamp(take, 1, 500)).ToList(), PreviewNotice);
    }

    /// <summary>The policy that applies to a type: the company's (where allowed) over the platform default; a type's own over "every type".</summary>
    public static TaxRetentionPolicy? Effective(IReadOnlyCollection<TaxRetentionPolicy> current, TaxEvidenceType type)
    {
        TaxRetentionPolicy? For(TaxEvidenceType? t)
        {
            var platform = current.FirstOrDefault(p => p.TenantId == null && p.EvidenceType == t);
            var company = current.FirstOrDefault(p => p.TenantId != null && p.EvidenceType == t);
            return company is not null && (platform is null || platform.AllowTenantOverride) ? company : platform;
        }
        return For(type) ?? For(null);
    }

    public static TaxRetentionPolicyDto Map(TaxRetentionPolicy p) => new(
        p.Id, p.TenantId, p.Version, p.EvidenceType, p.RetentionMonths, p.StartsAt, p.Enabled, p.AllowTenantOverride, p.Rationale, p.Reference,
        p.ConfiguredByName, p.CreatedAt, p.SupersededAt);
}
