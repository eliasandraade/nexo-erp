using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

public sealed record AiTenantCost(Guid TenantId, string TenantName, long Cost30dMicros, int Calls30d, int LlmCalls30d,
    long MonthSpentMicros, long MonthReservedMicros, long? SoftLimitMicros, long? HardLimitMicros, bool SoftExceeded, bool HardReached);

/// <summary>
/// Prompts and spend limits as the SuperAdmin manages them — each with the effect the runtime gives it: the active prompt is
/// the one the analyzers send (one active per type, enforced by the database), the hard limit blocks paid calls before they
/// happen (<see cref="AiUsageGuard"/>), the soft limit only warns. Every change is audited.
/// </summary>
public sealed class AiGovernanceAdminService
{
    public static readonly string[] PromptTypes = ["extraction", "interpretation", "memory"];
    public const int MaxPromptLength = 16000;

    private readonly NexoDbContext _db;
    private readonly IAuditWriter _audit;

    public AiGovernanceAdminService(NexoDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    // ── Limits ──────────────────────────────────────────────────────────────────

    /// <summary>Monthly limits of a company in USD (null clears one). Soft ≤ hard when both are set.</summary>
    public async Task<AiTenantCost> SetLimitsAsync(Guid tenantId, decimal? softUsd, decimal? hardUsd, PlatformActor actor, CancellationToken ct)
    {
        var tenantName = await _db.Tenants.IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.CompanyName).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Empresa", tenantId);
        var soft = Cents(softUsd);
        var hard = Cents(hardUsd);
        if (soft is not null && hard is not null && soft > hard)
            throw new DomainException("O limite suave não pode passar do limite rígido.", "ai_limit_order");

        var limit = await _db.TenantAiLimits.FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);
        if (limit is null) _db.TenantAiLimits.Add(TenantAiLimit.Create(tenantId, soft, hard));
        else limit.Update(soft, hard);
        _audit.Stage(AuditActions.AiTenantLimitsChanged, AuditSeverity.Warning, "Tenant", tenantId.ToString(),
            $"Limites mensais de IA de '{tenantName}': suave {Usd(soft)}, rígido {Usd(hard)}.", tenantId: tenantId,
            actorId: actor.Id, actorName: actor.Name, actorType: "platform",
            metadata: new { softLimitCents = soft, hardLimitCents = hard, correlationId = actor.RequestId });
        await _db.SaveChangesAsync(ct);
        return (await CostsAsync([tenantId], ct)).Single();
    }

    /// <summary>
    /// Per company: the last 30 days of calls (telemetry) and this calendar month's spend as the guard counts it, against the
    /// limits. Companies with a limit appear even before their first call.
    /// </summary>
    public async Task<IReadOnlyList<AiTenantCost>> CostsAsync(IReadOnlyCollection<Guid>? only, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since = now.AddDays(-30);
        var telemetry = await _db.InterpreterTelemetry.AsNoTracking()
            .Where(t => t.CreatedAt >= since && (only == null || only.Contains(t.TenantId)))
            .GroupBy(t => t.TenantId)
            .Select(g => new { TenantId = g.Key, Micros = g.Sum(t => t.EstimatedCostMicros), Calls = g.Count(), Llm = g.Count(t => t.Provider != "RuleBased") })
            .ToDictionaryAsync(x => x.TenantId, ct);
        var limited = await _db.TenantAiLimits.AsNoTracking().Where(l => only == null || only.Contains(l.TenantId)).Select(l => l.TenantId).ToListAsync(ct);
        var ids = telemetry.Keys.Union(limited).Union(only ?? []).Distinct().ToList();
        var names = await _db.Tenants.IgnoreQueryFilters().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);

        var rows = new List<AiTenantCost>();
        foreach (var id in ids)
        {
            var usage = await AiUsageGuard.TenantUsageAsync(_db, id, now, ct);
            telemetry.TryGetValue(id, out var t);
            rows.Add(new AiTenantCost(id, names.GetValueOrDefault(id, "Empresa removida"), t?.Micros ?? 0, t?.Calls ?? 0, t?.Llm ?? 0,
                usage.SpentMicros, usage.ReservedMicros, usage.SoftLimitMicros, usage.HardLimitMicros, usage.SoftExceeded, usage.HardReached));
        }
        return rows.OrderByDescending(r => r.MonthSpentMicros).ThenByDescending(r => r.Cost30dMicros).ToList();
    }

    // ── Prompts ─────────────────────────────────────────────────────────────────

    /// <summary>A new version of a prompt type, inactive until activated. The hash is computed from the content.</summary>
    public async Task<StoredPromptVersion> CreatePromptAsync(string promptType, string version, string content, string? description,
        PlatformActor actor, CancellationToken ct)
    {
        var type = promptType?.Trim() ?? "";
        if (!PromptTypes.Contains(type, StringComparer.Ordinal))
            throw new DomainException($"Tipo de prompt desconhecido. Use: {string.Join(", ", PromptTypes)}.", "ai_prompt_type");
        var v = version?.Trim() ?? "";
        if (v.Length is 0 or > 20 || !v.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
            throw new DomainException("Versão inválida (ex.: 1.1.0).", "ai_prompt_version");
        var text = content?.Trim() ?? "";
        if (text.Length is 0 or > MaxPromptLength)
            throw new DomainException($"Escreva o prompt (até {MaxPromptLength} caracteres).", "ai_prompt_content");
        if (await _db.StoredPromptVersions.AnyAsync(p => p.PromptType == type && p.Version == v, ct))
            throw new ConflictException($"A versão {v} do prompt {type} já existe.");

        var prompt = StoredPromptVersion.Create(type, v, ActivePromptProvider.HashOf(text), text, description?.Trim() ?? "", actor.Name ?? "plataforma");
        _db.StoredPromptVersions.Add(prompt);
        _audit.Stage(AuditActions.AiPromptCreated, AuditSeverity.Info, "StoredPromptVersion", prompt.Id.ToString(),
            $"Prompt {type} versão {v} criado (hash {prompt.Hash}).", actorId: actor.Id, actorName: actor.Name, actorType: "platform",
            metadata: new { type, version = v, hash = prompt.Hash, correlationId = actor.RequestId });
        await _db.SaveChangesAsync(ct);
        return prompt;
    }

    /// <summary>
    /// Makes a version the active one of its type: the previous one is deactivated first, in the same transaction (a partial
    /// unique index guarantees one active per type even under concurrent activations — the loser gets a 409).
    /// </summary>
    public async Task<StoredPromptVersion> ActivatePromptAsync(Guid id, PlatformActor actor, CancellationToken ct)
    {
        var target = await _db.StoredPromptVersions.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Prompt", id);
        if (target.IsActive) return target;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var previous = await _db.StoredPromptVersions.Where(p => p.PromptType == target.PromptType && p.IsActive).ToListAsync(ct);
        foreach (var p in previous) p.Deactivate();
        await _db.SaveChangesAsync(ct);
        target.Activate();
        _audit.Stage(AuditActions.AiPromptActivated, AuditSeverity.Warning, "StoredPromptVersion", target.Id.ToString(),
            $"Prompt {target.PromptType} versão {target.Version} ativado (o runtime passa a usá-lo).", actorId: actor.Id, actorName: actor.Name,
            actorType: "platform", metadata: new { type = target.PromptType, version = target.Version, previous = previous.Select(p => p.Version), correlationId = actor.RequestId });
        try
        {
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("Outra versão deste prompt acabou de ser ativada. Recarregue a lista.");
        }
        return target;
    }

    private static int? Cents(decimal? usd)
    {
        if (usd is null) return null;
        if (usd < 0 || usd > 1_000_000) throw new DomainException("Limite inválido (de 0 a 1.000.000 USD).", "ai_limit_invalid");
        return (int)Math.Round(usd.Value * 100m);
    }

    private static string Usd(int? cents) => cents is null ? "sem limite" : $"US$ {cents / 100m:0.00}";
}
