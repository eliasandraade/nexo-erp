using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.Build;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Orken;

/// <summary>Uma contagem por chave (status, provedor). Chaves de status em UPPER_SNAKE.</summary>
public sealed record OrkenCount(string Key, int Count);

/// <summary>
/// Contrato orken.erp.operations.v1 (docs/engineering/orken-cockpit-contract.md): só agregados, lidos pela Platform com um
/// token de serviço do Orken ID. Nenhum nome, identificador de tenant, e-mail, CNPJ ou outro dado pessoal sai daqui.
/// </summary>
public sealed record OrkenOperationsDto(
    string Contract, DateTimeOffset GeneratedAt, OrkenTenantsDto Tenants, IReadOnlyList<OrkenModuleDto> Modules, OrkenBuildDto Build,
    OrkenInterpreterDto Interpreter, OrkenCommercialDto Commercial);

/// <summary>Tenants do ERP: total, por status e avaliações (TrialEndsAt) que terminam nos próximos 7 dias.</summary>
public sealed record OrkenTenantsDto(int Total, IReadOnlyList<OrkenCount> ByStatus, int TrialsEndingIn7Days);

/// <summary>Tenants distintos com acesso ao módulo agora (mesma regra de acesso dos gates do ERP).</summary>
public sealed record OrkenModuleDto(string Key, int ActiveTenants);

/// <summary>Orken Build: obras por status, portais publicados e jobs de processamento com atividade nas últimas 24 h, pelo estado atual.</summary>
public sealed record OrkenBuildDto(int Projects, IReadOnlyList<OrkenCount> ProjectsByStatus, int PublishedPortals, IReadOnlyList<OrkenCount> JobsLast24Hours);

/// <summary>Interpretador: execuções registradas na telemetria, falhas, execuções por provedor e custo estimado.</summary>
public sealed record OrkenInterpreterDto(
    int RunsLast24Hours, int RunsLast7Days, int FailuresLast24Hours, IReadOnlyList<OrkenCount> RunsByProviderLast7Days,
    OrkenEstimateDto EstimatedCostLast7Days);

/// <summary>Valor estimado (kind = "estimate"), nunca fatura; micros = milionésimos da moeda.</summary>
public sealed record OrkenEstimateDto(string Kind, string Currency, long Micros);

public sealed record OrkenCommercialDto(OrkenEstimatedMrrDto EstimatedMrr);

/// <summary>MRR estimado a partir do preço de tabela dos módulos; <see cref="Method"/> descreve a conta.</summary>
public sealed record OrkenEstimatedMrrDto(string Kind, string Currency, decimal Amount, string Method, int ActiveSubscriptions, int PayingSubscriptions);

/// <summary>
/// Monta o contrato de operações da Orken com consultas agregadas sobre todos os tenants (ignora o filtro de tenant de
/// propósito, como os serviços do painel da plataforma): um número fixo de consultas, qualquer que seja o número de
/// empresas, e só COUNT/SUM/GROUP BY, nunca linhas.
/// </summary>
public sealed class OrkenOperationsQuery
{
    public const string Contract = "orken.erp.operations.v1";
    public const string EstimateKind = "estimate";

    /// <summary>Preços de tabela dos módulos (ModuleDefinition) são em reais.</summary>
    public const string MrrCurrency = "BRL";

    /// <summary>A telemetria do interpretador guarda o custo em milionésimos de dólar (preço por milhão de tokens do provedor).</summary>
    public const string InterpreterCostCurrency = "USD";

    public const string MrrMethod =
        "Soma, por assinatura de módulo com status Active e período vigente (fim nulo ou futuro), do preço de tabela do módulo " +
        "em valor mensal: mensal; trimestral/3; semestral/6; anual/12. Vitalício, cortesia e avaliação contam zero. " +
        "Estimativa a partir da tabela, não é faturamento nem receita recebida.";

    private const int MaxKeyLength = 60;

    private readonly NexoDbContext _db;

    public OrkenOperationsQuery(NexoDbContext db) => _db = db;

    public async Task<OrkenOperationsDto> SnapshotAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var since24h = now.AddHours(-24);
        var since7d = now.AddDays(-7);

        // ── Tenants ───────────────────────────────────────────────────────────
        var tenantsByStatus = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var trialsEnding = await _db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.Status == TenantStatus.Active && t.TrialEndsAt != null && t.TrialEndsAt >= now && t.TrialEndsAt < now.AddDays(7), ct);

        // ── Módulos: tenants distintos com acesso agora ──────────────────────
        var modules = await _db.ModuleSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(ModuleSubscription.HasAccessAt(now))
            .GroupBy(s => s.ModuleKey)
            .Select(g => new { g.Key, Tenants = g.Select(s => s.TenantId).Distinct().Count() })
            .ToListAsync(ct);

        // ── Orken Build ───────────────────────────────────────────────────────
        var projectsByStatus = await _db.BldProjects.IgnoreQueryFilters().AsNoTracking()
            .GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var publishedPortals = await _db.BldProjectPortals.IgnoreQueryFilters().AsNoTracking().CountAsync(p => p.IsPublished, ct);
        var jobs = await _db.BldModelJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(j => j.UpdatedAt >= since24h)
            .GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // ── Interpretador (telemetria, sem filtro de tenant) ─────────────────
        var telemetry = _db.InterpreterTelemetry.AsNoTracking().Where(t => t.CreatedAt >= since7d);
        var runs7d = await telemetry.CountAsync(ct);
        var runs24h = await telemetry.CountAsync(t => t.CreatedAt >= since24h, ct);
        var failures24h = await telemetry.CountAsync(t => t.CreatedAt >= since24h && !t.Success, ct);
        var byProvider = await telemetry.GroupBy(t => t.Provider).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var cost7d = await telemetry.SumAsync(t => t.EstimatedCostMicros, ct);

        // ── Comercial: MRR estimado (mesma conta de GET /api/platform/mrr) ──
        var activeSubscriptions = await _db.ModuleSubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && (s.CurrentPeriodEnd == null || s.CurrentPeriodEnd > now))
            .Select(s => new { s.ModuleKey, s.PlanType })
            .ToListAsync(ct);
        var definitions = await _db.ModuleDefinitions.AsNoTracking().ToDictionaryAsync(d => d.Key, ct);
        var mrr = activeSubscriptions.Sum(s => definitions.TryGetValue(s.ModuleKey, out var d) ? d.MonthlyReferencePrice(s.PlanType) : 0m);
        var paying = activeSubscriptions.Count(s => s.PlanType is not (PlanType.AdminGrant or PlanType.Trial or PlanType.Lifetime));

        return new OrkenOperationsDto(
            Contract,
            new DateTimeOffset(now, TimeSpan.Zero),
            new OrkenTenantsDto(tenantsByStatus.Values.Sum(), AllOf(tenantsByStatus), trialsEnding),
            modules.Select(m => new OrkenModuleDto(Key(m.Key), m.Tenants)).OrderBy(m => m.Key, StringComparer.Ordinal).ToList(),
            new OrkenBuildDto(projectsByStatus.Values.Sum(), AllOf(projectsByStatus), publishedPortals, AllOf(jobs)),
            new OrkenInterpreterDto(runs24h, runs7d, failures24h,
                byProvider.GroupBy(p => Key(p.Key)).Select(g => new OrkenCount(g.Key, g.Sum(p => p.Count)))
                    .OrderByDescending(p => p.Count).ThenBy(p => p.Key, StringComparer.Ordinal).ToList(),
                new OrkenEstimateDto(EstimateKind, InterpreterCostCurrency, cost7d)),
            new OrkenCommercialDto(new OrkenEstimatedMrrDto(EstimateKind, MrrCurrency, Math.Round(mrr, 2), MrrMethod,
                activeSubscriptions.Count, paying)));
    }

    /// <summary>Todos os valores do enum, mesmo os zerados, para o formato não mudar conforme os dados.</summary>
    private static List<OrkenCount> AllOf<TEnum>(Dictionary<TEnum, int> counts) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(v => new OrkenCount(UpperSnake(v.ToString()), counts.GetValueOrDefault(v))).ToList();

    public static string UpperSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }

    /// <summary>Chaves livres (provedor, módulo) só com letras, dígitos, '_', '.' e '-', até 60 caracteres.</summary>
    public static string Key(string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        var chars = raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-' ? c : '_').Take(MaxKeyLength).ToArray();
        return new string(chars);
    }
}
