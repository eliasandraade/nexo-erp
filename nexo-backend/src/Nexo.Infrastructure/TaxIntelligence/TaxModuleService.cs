using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Modules.TaxIntelligence;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.TaxIntelligence;
using Nexo.Infrastructure.TaxIntelligence.Files;

namespace Nexo.Infrastructure.TaxIntelligence;

/// <summary>
/// Platform administration of the module's runtime state: what the configuration allows, the checklist of a mode,
/// activation (explicit phrase, shadow-mode acknowledgement for the pilot, preconditions enforced on the server),
/// suspension (immediate, the first step of any incident) and deactivation — all audited with the authenticated actor.
/// It also builds the administrative readiness view. It never changes the configuration and never approves a rule.
/// </summary>
public sealed class TaxModuleService
{
    public const string PilotPhrase = "ATIVAR PILOTO CONTROLADO";
    public const string FinalPhrase = "ATIVAR MODO DEFINITIVO";
    public const string SuspendPhrase = "SUSPENDER";
    public const string DeactivatePhrase = "DESATIVAR";
    private const string AuditEntity = "Module";

    private readonly TaxContext _ctx;
    private readonly TaxModuleState _state;
    private readonly TaxEngineRegistry _engines;
    private readonly TaxFileStore _files;
    private readonly ILogger<TaxModuleService> _log;

    public TaxModuleService(TaxContext ctx, TaxModuleState state, TaxEngineRegistry engines, TaxFileStore files, ILogger<TaxModuleService> log)
    {
        _ctx = ctx;
        _state = state;
        _engines = engines;
        _files = files;
        _log = log;
    }

    private static DateOnly Today => DateOnly.FromDateTime(BusinessClock.ToBrazil(DateTime.UtcNow));

    public async Task<TaxModuleStateDto> GetStateAsync(CancellationToken ct)
    {
        var history = await _ctx.Db.TaxModuleActivations.AsNoTracking().OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(50).ToListAsync(ct);
        var latest = history.FirstOrDefault();
        var policy = _state.Policy;
        var problems = _state.ConfigurationProblems;
        var status = latest?.Status ?? TaxModuleStatus.Inactive;
        var runs = policy.Enabled && problems.Count == 0 && status == TaxModuleStatus.Active && latest!.Mode == policy.Mode
                   && policy.Mode != TaxApprovalMode.Disabled;
        return new TaxModuleStateDto(policy.Enabled, policy.Mode, problems, status, latest?.Status == TaxModuleStatus.Active ? latest.Mode : null,
            latest is null ? null : Map(latest), runs, Summary(policy, problems, latest), history.Select(Map).ToList());
    }

    private static string Summary(TaxIntelligencePolicyOptions policy, IReadOnlyList<string> problems, TaxModuleActivation? latest)
    {
        if (!policy.Enabled) return "Desligado pela configuração da instalação (kill switch). Nada novo roda.";
        if (problems.Count > 0) return "Configuração inválida: o módulo fica fechado até a correção.";
        if (latest?.Status == TaxModuleStatus.Suspended) return "Suspenso pela plataforma. Nada novo roda; o histórico continua disponível.";
        if (policy.Mode == TaxApprovalMode.Disabled) return "A instalação não configura nenhum modo de aprovação. Nada novo roda.";
        if (latest is not { Status: TaxModuleStatus.Active } || latest.Mode != policy.Mode)
            return $"A instalação permite o modo {policy.Mode}, mas a plataforma ainda não o ativou. Nada novo roda.";
        return policy.Mode == TaxApprovalMode.ControlledPilot
            ? "Piloto controlado ativo (modo sombra): só participantes do escopo, só regras aprovadas internamente."
            : "Modo definitivo ativo: só regras com revisão profissional aprovada.";
    }

    /// <summary>Preconditions of activating a mode today, each with its level; blocking items are enforced again on activation.</summary>
    public async Task<TaxActivationChecklistDto> ChecklistAsync(TaxApprovalMode mode, CancellationToken ct)
    {
        if (mode == TaxApprovalMode.Disabled) throw new DomainException("Escolha o piloto controlado ou o modo definitivo.", "tax_module_mode");
        var items = new List<TaxReadinessItemDto>();
        var policy = _state.Policy;
        var today = Today;
        items.Add(policy.Enabled
            ? Ok("config.enabled", "Configuração", "Kill switch da instalação", "Ligado (TaxIntelligence:Policy:Enabled = true).")
            : Block("config.enabled", "Configuração", "Kill switch da instalação",
                "Desligado (TaxIntelligence:Policy:Enabled = false). Só quem opera a implantação liga; este painel não altera a configuração."));
        items.Add(policy.Mode == mode
            ? Ok("config.mode", "Configuração", "Modo configurado", $"A instalação está configurada para {mode}.")
            : Block("config.mode", "Configuração", "Modo configurado",
                $"A instalação está configurada para {policy.Mode} (TaxIntelligence:Policy:Mode); para ativar {mode} a configuração precisa ser {mode}."));
        var problems = _state.ConfigurationProblems;
        items.Add(problems.Count == 0
            ? Ok("config.valid", "Configuração", "Configuração válida", "Nenhum problema encontrado.")
            : Block("config.valid", "Configuração", "Configuração válida", string.Join(" ", problems)));

        var rules = await ActiveRulesAsync(today, ct);
        var missing = mode == TaxApprovalMode.ControlledPilot
            ? rules.Where(r => !r.Version.IsApprovedForPilotOn(today)).Select(r => r.Label).ToList()
            : rules.Where(r => !r.Version.IsProfessionallyApprovedOn(today)).Select(r => r.Label).ToList();
        items.Add(rules.Count == 0
            ? Block("rules.active", "Regras", "Regras ativas hoje", "Nenhuma versão de regra está ativa para hoje.")
            : missing.Count == 0
                ? Ok("rules.approved", "Regras", mode == TaxApprovalMode.ControlledPilot ? "Aprovação interna" : "Revisão profissional",
                    $"As {rules.Count} versões ativas hoje estão aprovadas para o modo.")
                : Block("rules.approved", "Regras", mode == TaxApprovalMode.ControlledPilot ? "Aprovação interna" : "Revisão profissional",
                    $"{missing.Count} de {rules.Count} versões ativas hoje sem a aprovação que o modo exige: {string.Join(", ", missing)}."));

        if (mode == TaxApprovalMode.ControlledPilot) await PilotScopeItemsAsync(items, today, ct);
        else
            items.Add(Info("pilot.notUsed", "Escopo", "Escopo do piloto",
                "O modo definitivo não usa o escopo do piloto: todas as empresas com o módulo passam a ter cálculos novos."));

        var codes = await _ctx.Db.TaxClassificationCodes.AsNoTracking()
            .Where(c => c.Kind == TaxClassificationKind.CClassTrib && c.Status == TaxClassificationStatus.Active
                        && c.ValidFrom <= today && (c.ValidTo == null || c.ValidTo >= today))
            .Select(c => c.ProfessionalReview).ToListAsync(ct);
        var usable = mode == TaxApprovalMode.ProfessionalFinal
            ? codes.Count(s => s is ProfessionalReviewStatus.Approved or ProfessionalReviewStatus.ApprovedWithCaveats)
            : codes.Count;
        items.Add(usable > 0
            ? Ok("classification", "Classificação", "Catálogo cClassTrib", $"{usable} código(s) utilizáveis hoje.")
            : Block("classification", "Classificação", "Catálogo cClassTrib", mode == TaxApprovalMode.ProfessionalFinal
                ? "Nenhum cClassTrib ativo com revisão profissional aprovada: nenhuma operação seria calculada."
                : "Nenhum cClassTrib ativo no catálogo: nenhuma operação seria calculada. Importe e ative os códigos a partir de fonte documentada."));

        items.Add(_files.State == TaxFileStorageState.Available
            ? Ok("evidence.storage", "Evidências", "Armazenamento privado", "Disponível.")
            : Warn("evidence.storage", "Evidências", "Armazenamento privado", TaxFileStore.UnavailableMessage(_files.State)
                + " Evidências só por referência até ser configurado."));
        var pendingReview = rules.Count(r => !r.Version.IsProfessionallyApprovedOn(today));
        if (mode == TaxApprovalMode.ControlledPilot)
        {
            items.Add(Info("review.pending", "Revisão profissional", "Revisão profissional",
                pendingReview == 0 ? "Todas as versões ativas têm revisão profissional." :
                $"{pendingReview} versão(ões) com revisão profissional pendente — esperado no piloto; a aprovação interna não a substitui."));
            items.Add(Info("shadow", "Modo sombra", "Modo sombra",
                "O piloto não emite documento, não altera venda nem preço, não lança obrigação, não usa crédito, não recolhe e não liquida split."));
        }

        var caveats = mode == TaxApprovalMode.ControlledPilot
            ? rules.Where(r => r.Version.InternalApprovalCaveats is not null).Select(r => $"{r.Label}: {r.Version.InternalApprovalCaveats}").ToList()
            : rules.Where(r => r.Version.ProfessionalReviewCaveats is not null).Select(r => $"{r.Label}: {r.Version.ProfessionalReviewCaveats}").ToList();
        var can = items.All(i => i.Level != TaxReadinessLevel.Blocking);
        return new TaxActivationChecklistDto(mode, can, mode == TaxApprovalMode.ControlledPilot ? PilotPhrase : FinalPhrase, items, missing, caveats);
    }

    private async Task PilotScopeItemsAsync(List<TaxReadinessItemDto> items, DateOnly today, CancellationToken ct)
    {
        var scope = await _ctx.Db.TaxPilotScopes.AsNoTracking().Include(s => s.Participants).OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        if (scope is null || !scope.Enabled)
            items.Add(Block("pilot.scope", "Escopo", "Janela do piloto", "O escopo do piloto não foi aberto (janela e referência da decisão)."));
        else if (today < scope.ValidFrom || (scope.ValidTo is not null && today > scope.ValidTo))
            items.Add(Block("pilot.scope", "Escopo", "Janela do piloto",
                $"Hoje está fora da janela do piloto ({scope.ValidFrom:dd/MM/yyyy} a {(scope.ValidTo is null ? "sem fim" : scope.ValidTo.Value.ToString("dd/MM/yyyy"))})."));
        else
            items.Add(Ok("pilot.scope", "Escopo", "Janela do piloto", $"Aberta; decisão {scope.DecisionReference}."));
        var active = scope?.Participants.Count(p => p.IsActive) ?? 0;
        items.Add(active > 0
            ? Ok("pilot.participants", "Escopo", "Participantes", $"{active} participante(s) ativo(s).")
            : Block("pilot.participants", "Escopo", "Participantes", "Nenhuma empresa participa: inclua explicitamente o tenant (e as lojas)."));
    }

    private sealed record ActiveRule(string Label, TaxRuleVersion Version);

    private async Task<List<ActiveRule>> ActiveRulesAsync(DateOnly today, CancellationToken ct)
        => (await _ctx.Db.TaxRuleVersions.AsNoTracking()
                .Where(v => v.Status == TaxRuleStatus.Active && v.ValidFrom <= today && (v.ValidTo == null || v.ValidTo >= today))
                .Join(_ctx.Db.TaxRules.AsNoTracking(), v => v.RuleId, r => r.Id, (v, r) => new { v, r.Key })
                .ToListAsync(ct))
            .OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.v.Version)
            .Select(x => new ActiveRule($"{x.Key} v{x.v.Version}", x.v)).ToList();

    /// <summary>
    /// Activates the configured mode once every blocking precondition holds, with the exact phrase typed and, for the pilot,
    /// the shadow mode acknowledged. Audited with the authenticated actor and the decision reference.
    /// </summary>
    public async Task<TaxModuleStateDto> ActivateAsync(ActivateTaxModuleRequest r, CancellationToken ct)
    {
        var checklist = await ChecklistAsync(r.Mode, ct);
        if (!string.Equals(r.Confirmation?.Trim(), checklist.ConfirmationPhrase, StringComparison.Ordinal))
            throw new DomainException($"Para ativar, digite exatamente \"{checklist.ConfirmationPhrase}\".", "tax_module_confirmation");
        if (r.Mode == TaxApprovalMode.ControlledPilot && !r.AcknowledgeShadowMode)
            throw new DomainException("Confirme que o piloto roda em modo sombra, sem efeito fiscal, contábil ou financeiro.", "tax_module_shadow");
        var blocking = checklist.Items.Where(i => i.Level == TaxReadinessLevel.Blocking).ToList();
        if (blocking.Count > 0)
            throw new DomainException("Pré-condições não atendidas para ativar o modo.", "tax_module_preconditions",
                blocking.Select(b => $"{b.Label}: {b.Detail}").ToList());

        await LockAsync(ct);
        var a = TaxModuleActivation.Activate(r.Mode, r.DecisionReference, r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, _ctx.Audit.UserRole);
        _ctx.Db.TaxModuleActivations.Add(a);
        _ctx.Audit.RecordPlatform(TaxAuditActions.ModuleActivated, AuditEntity, a.Id,
            $"Inteligência tributária ativada em {r.Mode} por {_ctx.Audit.UserName} ({r.DecisionReference.Trim()}).",
            new { mode = r.Mode.ToString(), r.DecisionReference, r.Reason, actorRole = _ctx.Audit.UserRole,
                checklist = checklist.Items.Select(i => new { i.Key, level = i.Level.ToString(), i.Detail }) });
        await SaveAsync(ct);
        _ctx.Metrics.Approved("module-activation");
        _log.LogWarning("Tax Intelligence activated in {Mode} by {Actor}", r.Mode, _ctx.Audit.UserName);
        return await GetStateAsync(ct);
    }

    /// <summary>Suspends at once (incident, suspension criterion). No precondition: stopping is always allowed.</summary>
    public async Task<TaxModuleStateDto> SuspendAsync(SuspendTaxModuleRequest r, CancellationToken ct)
    {
        Confirm(r.Confirmation, SuspendPhrase);
        await LockAsync(ct);
        var a = TaxModuleActivation.Suspend(_state.Policy.Mode, r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, _ctx.Audit.UserRole);
        _ctx.Db.TaxModuleActivations.Add(a);
        _ctx.Audit.RecordPlatform(TaxAuditActions.ModuleSuspended, AuditEntity, a.Id,
            $"Inteligência tributária SUSPENSA por {_ctx.Audit.UserName}: {r.Reason.Trim()}", new { r.Reason, actorRole = _ctx.Audit.UserRole });
        await SaveAsync(ct);
        _log.LogWarning("Tax Intelligence suspended by {Actor}", _ctx.Audit.UserName);
        return await GetStateAsync(ct);
    }

    public async Task<TaxModuleStateDto> DeactivateAsync(DeactivateTaxModuleRequest r, CancellationToken ct)
    {
        Confirm(r.Confirmation, DeactivatePhrase);
        await LockAsync(ct);
        var a = TaxModuleActivation.Deactivate(_state.Policy.Mode, r.Reason, _ctx.Audit.UserId, _ctx.Audit.UserName, _ctx.Audit.UserRole);
        _ctx.Db.TaxModuleActivations.Add(a);
        _ctx.Audit.RecordPlatform(TaxAuditActions.ModuleDeactivated, AuditEntity, a.Id,
            $"Inteligência tributária desativada por {_ctx.Audit.UserName}: {r.Reason.Trim()}", new { r.Reason, actorRole = _ctx.Audit.UserRole });
        await SaveAsync(ct);
        _log.LogWarning("Tax Intelligence deactivated by {Actor}", _ctx.Audit.UserName);
        return await GetStateAsync(ct);
    }

    private static void Confirm(string? typed, string phrase)
    {
        if (!string.Equals(typed?.Trim(), phrase, StringComparison.Ordinal))
            throw new DomainException($"Para confirmar, digite exatamente \"{phrase}\".", "tax_module_confirmation");
    }

    /// <summary>Serialises state changes: two operators never interleave an activation and a suspension.</summary>
    private async Task LockAsync(CancellationToken ct)
    {
        if (_ctx.Db.Database.CurrentTransaction is null) await _ctx.Db.Database.BeginTransactionAsync(ct);
        await _ctx.Db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7240011)", ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _ctx.Db.SaveChangesAsync(ct);
        if (_ctx.Db.Database.CurrentTransaction is not null) await _ctx.Db.Database.CommitTransactionAsync(ct);
        _state.Invalidate();
    }

    // ── Readiness ───────────────────────────────────────────────────────────────

    /// <summary>The administrative diagnosis: configuration, state, engine, catalogue, approvals, scope, storage, policies, migrations.</summary>
    public async Task<TaxReadinessDto> ReadinessAsync(CancellationToken ct)
    {
        var today = Today;
        var module = await GetStateAsync(ct);
        var items = new List<TaxReadinessItemDto>();
        items.Add(module.NewWorkRuns
            ? Ok("module", "Módulo", "Trabalho novo", module.Summary)
            : Info("module", "Módulo", "Trabalho novo", module.Summary));
        if (module.ConfigurationProblems.Count > 0)
            items.Add(Block("config.valid", "Configuração", "Configuração válida", string.Join(" ", module.ConfigurationProblems)));

        var pendingMigrations = (await _ctx.Db.Database.GetPendingMigrationsAsync(ct)).ToList();
        items.Add(pendingMigrations.Count == 0
            ? Ok("migrations", "Banco", "Migrations", "Todas aplicadas.")
            : Block("migrations", "Banco", "Migrations", $"Pendentes: {string.Join(", ", pendingMigrations)}."));

        var versions = await _ctx.Db.TaxRuleVersions.AsNoTracking()
            .Join(_ctx.Db.TaxRules.AsNoTracking(), v => v.RuleId, r => r.Id, (v, r) => new { v, r.Key }).ToListAsync(ct);
        var byStatus = versions.GroupBy(x => x.v.Status).ToDictionary(g => g.Key, g => g.Count());
        items.Add(Info("catalog", "Catálogo", "Versões de regra",
            string.Join(", ", byStatus.OrderBy(k => k.Key).Select(k => $"{k.Key}: {k.Value}")) + $" (total {versions.Count})."));
        var activeToday = versions.Where(x => x.v.Status == TaxRuleStatus.Active && x.v.Covers(today)).ToList();
        var professionallyApproved = activeToday.Count(x => x.v.IsProfessionallyApprovedOn(today));
        items.Add(professionallyApproved == activeToday.Count && activeToday.Count > 0
            ? Ok("review", "Revisão profissional", "Revisão profissional", $"Todas as {activeToday.Count} versões ativas hoje aprovadas.")
            : Warn("review", "Revisão profissional", "Revisão profissional",
                $"{activeToday.Count - professionallyApproved} de {activeToday.Count} versões ativas hoje sem revisão profissional aprovada — o modo definitivo fica bloqueado."));
        var pilotApproved = activeToday.Count(x => x.v.IsApprovedForPilotOn(today));
        items.Add(Info("internal", "Aprovação interna", "Aprovação interna (piloto)", $"{pilotApproved} de {activeToday.Count} versões ativas hoje aprovadas para o piloto."));

        var sources = await _ctx.Db.TaxLegalSources.AsNoTracking().Select(s => s.TextVerification).ToListAsync(ct);
        var unverified = sources.Count(s => s != TextVerificationStatus.Verified);
        items.Add(unverified == 0
            ? Ok("sources", "Fontes", "Fontes jurídicas", $"{sources.Count} fonte(s), texto conferido em todas.")
            : Warn("sources", "Fontes", "Fontes jurídicas", $"{unverified} de {sources.Count} fonte(s) sem texto conferido (não sustentam ativação de regra)."));

        await PilotScopeItemsAsync(items, today, ct);
        items.Add(_files.State == TaxFileStorageState.Available
            ? Ok("evidence.storage", "Evidências", "Armazenamento privado",
                $"Disponível; limite {_files.MaxBytes / (1024 * 1024)} MB; verificação de conteúdo {(_files.ScannerConfigured ? "configurada" : "não configurada (arquivos ficam como não verificados)")}.")
            : Warn("evidence.storage", "Evidências", "Armazenamento privado", TaxFileStore.UnavailableMessage(_files.State)));

        var materialityTenants = await _ctx.Db.TaxMaterialityPolicies.IgnoreQueryFilters().AsNoTracking().Select(m => m.TenantId).Distinct().CountAsync(ct);
        items.Add(Info("materiality", "Políticas", "Materialidade",
            $"Padrão da instalação: {(_state.Policy.MaterialityThreshold is null ? "nenhum" : _state.Policy.MaterialityThreshold.Value.ToString("N2"))}; {materialityTenants} empresa(s) com política própria."));
        var retention = await _ctx.Db.TaxRetentionPolicies.AsNoTracking().Where(p => p.SupersededAt == null).ToListAsync(ct);
        items.Add(retention.Count == 0
            ? Info("retention", "Políticas", "Retenção", "Nenhuma política: nada é descartado (padrão conservador). O descarte automático não existe.")
            : Info("retention", "Políticas", "Retenção",
                $"{retention.Count(p => p.TenantId == null)} política(s) padrão e {retention.Count(p => p.TenantId != null)} de empresas; descarte automático desligado."));

        var codes = await _ctx.Db.TaxClassificationCodes.AsNoTracking().Where(c => c.Kind == TaxClassificationKind.CClassTrib)
            .Select(c => new { c.Status, c.ProfessionalReview, c.ValidFrom, c.ValidTo }).ToListAsync(ct);
        var activeCodes = codes.Count(c => c.Status == TaxClassificationStatus.Active && c.ValidFrom <= today && (c.ValidTo == null || c.ValidTo >= today));
        items.Add(activeCodes > 0
            ? Ok("classification", "Classificação", "Catálogo cClassTrib", $"{activeCodes} ativo(s) hoje de {codes.Count} cadastrado(s).")
            : Warn("classification", "Classificação", "Catálogo cClassTrib", $"Nenhum código ativo hoje ({codes.Count} cadastrado(s)): nenhuma operação é calculada."));
        var obligations = await _ctx.Db.TaxObligationDefinitions.AsNoTracking().CountAsync(o => o.Active, ct);
        items.Add(Info("obligations", "Obrigações", "Catálogo de obrigações acessórias",
            obligations == 0 ? "Vazio: avaliações verificadas exigem obrigações cadastradas." : $"{obligations} obrigação(ões) ativa(s)."));

        var last = await _ctx.Db.AuditRecords.AsNoTracking().Where(a => a.ActionType.StartsWith("tax_"))
            .OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var engine = _engines.Current;
        return new TaxReadinessDto(DateTime.UtcNow, engine.Version, engine.Canonical.Id, TaxCoveragePolicyV1.Version, module, items,
            activeToday.Where(x => x.v.IsApprovedForPilotOn(today) || x.v.IsProfessionallyApprovedOn(today)).Select(x => $"{x.Key} v{x.v.Version}").Order().ToList(),
            activeToday.Where(x => !x.v.IsApprovedForPilotOn(today) && !x.v.IsProfessionallyApprovedOn(today)).Select(x => $"{x.Key} v{x.v.Version}").Order().ToList(),
            versions.Where(x => x.v.Status == TaxRuleStatus.PendingRegulation).Select(x => $"{x.Key} v{x.v.Version}").Order().ToList(),
            last is null ? null : MapAudit(last));
    }

    /// <summary>The latest refusals of new work across tenants (platform view).</summary>
    public async Task<IReadOnlyList<TaxRefusalDto>> RecentRefusalsAsync(int take, CancellationToken ct)
        => (await _ctx.Db.AuditRecords.AsNoTracking().Where(a => TaxAuditActions.Refusals.Contains(a.ActionType))
                .OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct))
            .Select(a => new TaxRefusalDto(a.ActionType, RefusalCode(a.Description), a.Description, a.ActorName ?? "?", a.CreatedAt, a.TenantId)).ToList();

    /// <summary>Tax audit events across tenants, newest first (platform view).</summary>
    public async Task<TaxPage<TaxAuditEventDto>> AuditAsync(string? action, Guid? tenantId, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);
        var q = _ctx.Db.AuditRecords.AsNoTracking().Where(a => a.ActionType.StartsWith("tax_"));
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.ActionType == action);
        if (tenantId is not null) q = q.Where(a => a.TenantId == tenantId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return new TaxPage<TaxAuditEventDto>(rows.Select(MapAudit).ToList(), total, skip, take);
    }

    /// <summary>The code between the first parentheses of a refusal description ("… recusado (code): …").</summary>
    public static string? RefusalCode(string description)
    {
        var open = description.IndexOf('(');
        var close = open < 0 ? -1 : description.IndexOf(')', open + 1);
        return close > open + 1 ? description[(open + 1)..close] : null;
    }

    public static TaxAuditEventDto MapAudit(Nexo.Domain.Entities.AuditRecord a)
        => new(a.Id, a.ActionType, a.Severity, a.EntityType, a.EntityId, a.Description, a.ActorName ?? "?", a.CreatedAt, a.TenantId);

    private static TaxModuleActivationDto Map(TaxModuleActivation a)
        => new(a.Id, a.Status, a.Mode, a.DecisionReference, a.Reason, a.ActorName, a.ActorRole, a.CreatedAt);

    private static TaxReadinessItemDto Ok(string key, string area, string label, string detail) => new(key, area, label, TaxReadinessLevel.Ok, detail);
    private static TaxReadinessItemDto Info(string key, string area, string label, string detail) => new(key, area, label, TaxReadinessLevel.Info, detail);
    private static TaxReadinessItemDto Warn(string key, string area, string label, string detail) => new(key, area, label, TaxReadinessLevel.Warning, detail);
    private static TaxReadinessItemDto Block(string key, string area, string label, string detail) => new(key, area, label, TaxReadinessLevel.Blocking, detail);
}
