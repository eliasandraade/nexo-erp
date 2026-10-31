using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Build;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;

namespace Nexo.Infrastructure.Modules.Build;

public sealed partial class BuildAiService
{
    public const int MaxReportLength = 8000;
    private const int MaxDailyLogNotes = 5000;

    // ── 1. Daily log copilot ──────────────────────────────────────────────────

    public async Task<BuildAiSuggestionDto> DailyLogCopilotAsync(Guid projectId, BuildDailyLogCopilotRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        var text = r.Text?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException("Escreva o relato do dia.", "ai_input_required");
        if (text.Length > MaxReportLength) throw new DomainException($"O relato pode ter até {MaxReportLength} caracteres.", "ai_input_too_long");
        var today = BusinessClock.Today();
        var date = r.Date ?? today;
        if (date > today) throw new DomainException("A data do diário não pode ser futura.", "invalid_date");

        var (photos, photoIds) = await PhotosAsync(projectId, r.PhotoIds, ct);
        var stages = await StagesAsync(projectId, ct);
        var project = await _db.BldProjects.AsNoTracking().Where(p => p.TenantId == Tid && p.Id == projectId).Select(p => p.Name).FirstAsync(ct);

        var llm = await CallAsync(BuildAiCapability.DailyLogCopilot,
            new
            {
                obra = project,
                data = date.ToString("yyyy-MM-dd"),
                etapas = stages.Select(s => new { id = s.Id, nome = s.Name, percentualAtual = s.Percent }),
                fotos = photoIds,
                relato = text,
            },
            "Ligue cada atividade a uma etapa pelo id exato da lista 'etapas' (ou null se não houver). progressHint é opcional (0–100).",
            BuildAiContracts.DailyLogSchema, 2048, photos, fallbackAllowed: true, ct);

        var draft = llm is null
            ? BuildAiRules.DailyLogFallback(text, date, stages)
            : BuildAiContracts.ParseDailyLog(llm.Output, date, stages);
        var refs = new { date = date.ToString("yyyy-MM-dd"), stageIds = stages.Select(s => s.Id), photoIds, textLength = text.Length };
        return await StoreAsync(projectId, BuildAiCapability.DailyLogCopilot, llm, null, refs, draft, draft.Confidence, DefaultTtl, ct);
    }

    /// <summary>Creates the day's log (or appends to it). The client summary is filled, but the log stays hidden from the client.</summary>
    private async Task<(object, bool)> ApplyDailyLogAsync(BuildAiSuggestion s, CancellationToken ct)
    {
        var draft = BuildAiContracts.Deserialize<BuildDailyLogDraft>(s.OutputJson);
        var notes = ComposeNotes(draft);
        if (notes.Length == 0) throw new DomainException("A sugestão não tem conteúdo para o diário.", "ai_empty_output");
        var log = await _db.BldDailyLogs.FirstOrDefaultAsync(l => l.TenantId == Tid && l.ProjectId == s.ProjectId && l.Date == draft.Date, ct);
        var created = log is null;
        if (log is null)
        {
            log = BuildDailyLog.Create(Tid, s.ProjectId, draft.Date, _audit.UserId, Cap(notes, MaxDailyLogNotes), draft.WeatherNote);
            _db.BldDailyLogs.Add(log);
        }
        else
        {
            log.Update(Cap(log.Notes + "\n\n" + notes, MaxDailyLogNotes), log.WeatherSummary ?? draft.WeatherNote);
        }
        // the company still decides what the client sees: never visible from here; a published summary is kept
        if (!log.ClientVisible && draft.ClientSummary is { } summary)
            log.SetClientVisibility(false, Cap(summary, 2000));
        return (new { dailyLogId = log.Id, created, date = draft.Date.ToString("yyyy-MM-dd") }, false);
    }

    public static string ComposeNotes(BuildDailyLogDraft d)
    {
        var lines = new List<string>();
        if (d.Activities.Count > 0)
        {
            lines.Add("Atividades:");
            lines.AddRange(d.Activities.Select(a => "- " + (a.StageName is { } st ? $"[{st}] " : "") + a.Description
                                                    + (a.ProgressHint is { } h ? $" (~{h}%)" : "")));
        }
        if (d.Workforce.Count > 0) lines.Add("Equipe: " + string.Join(", ", d.Workforce.Select(w => $"{w.Role} ({w.Count})")));
        if (d.Equipment.Count > 0) lines.Add("Equipamentos: " + string.Join(", ", d.Equipment));
        if (d.Issues.Count > 0)
        {
            lines.Add("Ocorrências:");
            lines.AddRange(d.Issues.Select(i => "- " + i));
        }
        return string.Join("\n", lines).Trim();
    }

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;

    // ── 2. Progress interpreter ───────────────────────────────────────────────

    public async Task<BuildAiSuggestionDto> ProgressInterpreterAsync(Guid projectId, BuildProgressInterpreterRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        var days = Math.Clamp(r.LookbackDays ?? 14, 1, 60);
        var today = BusinessClock.Today();
        var since = today.AddDays(-days);
        var (photos, photoIds) = await PhotosAsync(projectId, r.PhotoIds, ct);

        var logs = await _db.BldDailyLogs.AsNoTracking()
            .Where(l => l.TenantId == Tid && l.ProjectId == projectId && l.Date >= since && l.Date <= today)
            .OrderByDescending(l => l.Date).Take(30)
            .Select(l => new { l.Id, l.Date, l.Notes, l.WeatherSummary })
            .ToListAsync(ct);
        if (logs.Count == 0 && photos.Count == 0)
            throw new DomainException($"Não há registros do diário nos últimos {days} dias para interpretar.", "ai_no_evidence");
        var stages = await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name).ToListAsync(ct);
        var refs = stages.Select(s => new BuildAiStageRef(s.Id, s.Name, s.Order, s.ProgressPercent)).ToList();

        // no deterministic fallback: interpreting free text into percentages is exactly what rules can't do honestly
        var llm = (await CallAsync(BuildAiCapability.ProgressInterpreter,
            new
            {
                hoje = today.ToString("yyyy-MM-dd"),
                etapas = stages.Select(s => new
                {
                    id = s.Id, nome = s.Name, percentualAtual = s.ProgressPercent,
                    inicioPlanejado = s.PlannedStartDate, fimPlanejado = s.PlannedEndDate,
                }),
                diario = logs.Select(l => new { id = l.Id, data = l.Date.ToString("yyyy-MM-dd"), texto = Cap(l.Notes, 3000), clima = l.WeatherSummary }),
                fotos = photoIds,
            },
            "Cite em evidenceRefs só ids do 'diario' e de 'fotos'. suggestedPercent é o acumulado da etapa (nunca abaixo do atual).",
            BuildAiContracts.ProgressSchema, 3000, photos, fallbackAllowed: false, ct))!;

        var evidence = logs.Select(l => l.Id.ToString()).Concat(photoIds.Select(p => p.ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parsed = BuildAiContracts.ParseProgress(llm.Output, refs, evidence);
        var confidence = parsed.Items.Count == 0 ? 0m : Math.Round(parsed.Items.Average(i => i.Confidence), 3);
        var inputRefs = new { lookbackDays = days, dailyLogIds = logs.Select(l => l.Id), photoIds };
        return await StoreAsync(projectId, BuildAiCapability.ProgressInterpreter, llm, null, inputRefs, parsed, confidence, DefaultTtl, ct);
    }

    /// <summary>Per selected stage, through the shared progress path (source AiConfirmed, reference = the suggestion).</summary>
    private async Task<(object, bool)> ApplyProgressAsync(BuildAiSuggestion s, HashSet<string>? selected, CancellationToken ct)
    {
        var parsed = BuildAiContracts.Deserialize<BuildProgressSuggestion>(s.OutputJson);
        var items = parsed.Items.Where(i => selected is null || selected.Contains(i.Id)).ToList();
        if (selected is not null && items.Count != selected.Count)
            throw new DomainException("Um dos itens escolhidos não faz parte desta sugestão.", "ai_item_not_found");
        if (items.Count == 0) throw new DomainException("Esta sugestão não tem itens para aplicar.", "ai_empty_output");

        var ids = items.Select(i => i.StageId).ToList();
        var stages = await _db.BldStages.Where(st => st.TenantId == Tid && st.ProjectId == s.ProjectId && ids.Contains(st.Id)).ToDictionaryAsync(st => st.Id, ct);
        var today = BusinessClock.Today();
        var applied = new List<object>();
        foreach (var item in items)
        {
            if (!stages.TryGetValue(item.StageId, out var stage))
                throw new DomainException("Uma etapa desta sugestão não existe mais.", "stage_not_of_project");
            if (item.SuggestedPercent <= stage.ProgressPercent) continue;   // moved meanwhile: never backwards
            var previous = stage.ProgressPercent;
            var e = await BuildStageProgressApplier.ApplyAsync(_progress, stage, item.SuggestedPercent, today, BuildProgressSource.AiConfirmed,
                _audit.UserId, _audit.UserName, Cap($"IA confirmada: {item.Rationale}", 1000), s.Id, ct);
            applied.Add(new { stageId = stage.Id, previousPercent = previous, percent = stage.ProgressPercent, eventId = e?.Id });
        }
        return (new { stages = applied }, items.Count < parsed.Items.Count);
    }

    // ── 3. Model mapper ───────────────────────────────────────────────────────

    public async Task<BuildAiSuggestionDto> ModelMapperAsync(Guid projectId, BuildModelMapperRequest r, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        var version = r.ModelVersionId is { } vid
            ? await _db.BldModelVersions.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == Tid && v.Id == vid && v.ProjectId == projectId, ct)
              ?? throw new NotFoundException("BuildModelVersion", vid)
            : await _twin.PrimaryVersionAsync(Tid, projectId, bypass: false, ct)
              ?? throw new DomainException("A obra não tem modelo processado para mapear.", "model_not_ready");
        if (version.Status != BuildModelVersionStatus.Ready)
            throw new DomainException("A versão do modelo ainda não está pronta.", "model_not_ready");

        var categories = await _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == version.Id)
            .GroupBy(e => e.Category).Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(60).ToListAsync(ct);
        var zoneCounts = await _db.BldModelElements.AsNoTracking().Where(e => e.TenantId == Tid && e.ModelVersionId == version.Id && e.ZoneId != null)
            .GroupBy(e => e.ZoneId).Select(g => new { ZoneId = g.Key!.Value, Count = g.Count() }).ToDictionaryAsync(x => x.ZoneId, x => x.Count, ct);
        var zones = await _db.BldModelZones.AsNoTracking()
            .Where(z => z.TenantId == Tid && z.ProjectId == projectId && (z.ModelVersionId == version.Id || z.ModelVersionId == null))
            .OrderBy(z => z.Order).ThenBy(z => z.Name).Take(100)
            .Select(z => new { z.Id, z.Name, z.Kind }).ToListAsync(ct);
        var stages = await StagesAsync(projectId, ct);
        if (stages.Count == 0) throw new DomainException("Cadastre as etapas da obra antes de mapear o modelo.", "ai_no_stages");

        var llm = await CallAsync(BuildAiCapability.ModelMapper,
            new
            {
                etapas = stages.Select(s => new { id = s.Id, nome = s.Name }),
                categorias = categories.Select(c => new { categoria = c.Category, elementos = c.Count }),
                zonas = zones.Select(z => new { id = z.Id, nome = z.Name, tipo = z.Kind.ToString(), elementos = zoneCounts.GetValueOrDefault(z.Id) }),
            },
            "scope = Category (use 'categoria' exatamente como recebido) ou Zone (use o id da zona).",
            BuildAiContracts.MapperSchema, 3000, null, fallbackAllowed: true, ct);

        var categorySet = categories.Select(c => c.Category).ToHashSet(StringComparer.Ordinal);
        var parsed = llm is null
            ? BuildAiRules.MapperFallback(version.Id, categorySet, stages)
            : BuildAiContracts.ParseMapper(llm.Output, version.Id, stages, categorySet, zones.ToDictionary(z => z.Id, z => z.Name));
        var confidence = parsed.Bindings.Count == 0 ? 0m : Math.Round(parsed.Bindings.Average(b => b.Confidence), 3);
        var inputRefs = new { modelVersionId = version.Id, categories = categories.Count, zoneIds = zones.Select(z => z.Id), stageIds = stages.Select(s => s.Id) };
        return await StoreAsync(projectId, BuildAiCapability.ModelMapper, llm, version.Id, inputRefs, parsed, confidence, LongTtl, ct);
    }

    private async Task<(object, bool)> ApplyBindingsAsync(BuildAiSuggestion s, HashSet<string>? selected, CancellationToken ct)
    {
        var parsed = BuildAiContracts.Deserialize<BuildMapperSuggestion>(s.OutputJson);
        var items = parsed.Bindings.Where(b => selected is null || selected.Contains(b.Id)).ToList();
        if (selected is not null && items.Count != selected.Count)
            throw new DomainException("Um dos itens escolhidos não faz parte desta sugestão.", "ai_item_not_found");
        if (items.Count == 0) throw new DomainException("Esta sugestão não tem vínculos para aplicar.", "ai_empty_output");

        var stageIds = items.Select(b => b.StageId).Distinct().ToList();
        var validStages = await _db.BldStages.AsNoTracking().Where(st => st.TenantId == Tid && st.ProjectId == s.ProjectId && stageIds.Contains(st.Id))
            .Select(st => st.Id).ToListAsync(ct);
        var zoneIds = items.Where(b => b.ZoneId is not null).Select(b => b.ZoneId!.Value).Distinct().ToList();
        var zones = await _db.BldModelZones.AsNoTracking().Where(z => z.TenantId == Tid && z.ProjectId == s.ProjectId && zoneIds.Contains(z.Id))
            .ToDictionaryAsync(z => z.Id, z => z.ModelVersionId, ct);
        var existing = await _db.BldModelBindings.AsNoTracking().Where(b => b.TenantId == Tid && b.ProjectId == s.ProjectId)
            .Select(b => new { b.StageId, b.Scope, b.Category, b.ZoneId, b.ModelVersionId }).ToListAsync(ct);
        var source = s.Source == BuildAiSource.Rules ? BuildBindingSource.Rules : BuildBindingSource.AiConfirmed;

        var created = new List<Guid>();
        var skipped = 0;
        foreach (var b in items)
        {
            if (!validStages.Contains(b.StageId)) throw new DomainException("Uma etapa desta sugestão não existe mais.", "stage_not_of_project");
            BuildModelStageBinding binding;
            if (b.Scope == "Zone")
            {
                if (b.ZoneId is not { } z || !zones.TryGetValue(z, out var zoneVersion))
                    throw new DomainException("Uma zona desta sugestão não existe mais.", "invalid_zone");
                if (existing.Any(x => x.StageId == b.StageId && x.Scope == BuildBindingScope.Zone && x.ZoneId == z)) { skipped++; continue; }
                binding = BuildModelStageBinding.Create(Tid, s.ProjectId, b.StageId, BuildBindingScope.Zone, zoneVersion, null, z, null, source, _audit.UserId);
            }
            else
            {
                if (existing.Any(x => x.StageId == b.StageId && x.Scope == BuildBindingScope.Category && x.Category == b.Category
                                      && (x.ModelVersionId == null || x.ModelVersionId == parsed.ModelVersionId))) { skipped++; continue; }
                binding = BuildModelStageBinding.Create(Tid, s.ProjectId, b.StageId, BuildBindingScope.Category, parsed.ModelVersionId, null, null,
                    b.Category, source, _audit.UserId);
            }
            _db.BldModelBindings.Add(binding);
            created.Add(binding.Id);
        }
        _audit.Record(AuditActions.BuildModelBindingSaved, "ModelBinding", s.Id, $"{created.Count} vínculo(s) ao modelo criados por sugestão ({source}).",
            new { s.ProjectId, suggestionId = s.Id, created = created.Count, skipped });
        return (new { bindingIds = created, skipped, source = source.ToString() }, items.Count < parsed.Bindings.Count);
    }

    // ── 4/8. Client update and weekly digest ──────────────────────────────────

    public Task<BuildAiSuggestionDto> ClientUpdateAsync(Guid projectId, BuildClientUpdateAiRequest r, CancellationToken ct)
        => ClientTextAsync(projectId, BuildAiCapability.ClientUpdate, r.From, r.To, ct);

    public Task<BuildAiSuggestionDto> WeeklyDigestAsync(Guid projectId, CancellationToken ct)
        => ClientTextAsync(projectId, BuildAiCapability.WeeklyDigest, null, null, ct);

    private async Task<BuildAiSuggestionDto> ClientTextAsync(Guid projectId, BuildAiCapability capability, DateOnly? from, DateOnly? to,
        CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        var today = BusinessClock.Today();
        var end = to ?? today;
        var start = capability == BuildAiCapability.WeeklyDigest ? end.AddDays(-6) : from ?? end.AddDays(-6);
        if (end > today || start > end || end.DayNumber - start.DayNumber > 92)
            throw new DomainException("Período inválido (até hoje, no máximo 93 dias).", "invalid_period");

        var (facts, visibility, refs) = await ClientFactsAsync(projectId, start, end, ct);
        var kind = capability == BuildAiCapability.WeeklyDigest ? "WeeklyDigest" : "Update";
        var llm = await CallAsync(capability,
            new
            {
                obra = facts.ProjectName,
                periodo = new { de = start.ToString("yyyy-MM-dd"), ate = end.ToString("yyyy-MM-dd") },
                avancoAtual = facts.ActualPercent,
                avancoNoInicioDoPeriodo = facts.ActualPercentAtStart,
                etapasConcluidas = facts.CompletedStages,
                registrosPublicados = facts.VisibleLogSummaries.Select(l => new { data = l.Date.ToString("yyyy-MM-dd"), resumo = l.Summary }),
                aditivosAprovados = facts.ApprovedChangeOrders.Select(c => new { numero = c.Number, titulo = c.Title, impactoPrazoDias = c.ScheduleImpactDays, valor = c.CostImpact }),
                medicoesAprovadas = facts.ApprovedMeasurements.Select(m => new { numero = m.Number, ate = m.PeriodEnd.ToString("yyyy-MM-dd"), valor = m.Amount }),
                proximasEtapas = facts.UpcomingMilestones.Select(m => new { etapa = m.Stage, fimPrevisto = m.PlannedEnd.ToString("yyyy-MM-dd") }),
                previsaoEntrega = facts.ForecastDelivery?.ToString("yyyy-MM-dd"),
            },
            $"Valores financeiros {(visibility.FinancialSummary ? "podem" : "NÃO podem")} ser citados. "
            + $"Datas de entrega {(visibility.ForecastDelivery ? "só a 'previsaoEntrega' informada" : "NÃO podem ser citadas")}. "
            + "Campos nulos ou listas vazias significam que o cliente não vê aquela informação: não mencione.",
            BuildAiContracts.ClientUpdateSchema, 2048, null, fallbackAllowed: true, ct);

        var draft = llm is null
            ? BuildAiRules.ClientUpdateFallback(facts, kind)
            : BuildAiContracts.ParseClientUpdate(llm.Output, kind, start, end);
        return await StoreAsync(projectId, capability, llm, null, refs, draft, llm is null ? 1m : 0.8m, LongTtl, ct);
    }

    private (object, bool) ApplyClientUpdate(BuildAiSuggestion s)
    {
        var draft = BuildAiContracts.Deserialize<BuildClientUpdateDraft>(s.OutputJson);
        var u = BuildClientUpdate.Create(Tid, s.ProjectId,
            draft.Kind == "WeeklyDigest" ? BuildClientUpdateKind.WeeklyDigest : BuildClientUpdateKind.Update,
            draft.Title, draft.Body, s.Source == BuildAiSource.Rules ? BuildClientUpdateSource.Rules : BuildClientUpdateSource.Ai,
            _audit.UserId, _audit.UserName);
        _db.BldClientUpdates.Add(u);
        return (new { clientUpdateId = u.Id, status = u.Status.ToString() }, false);
    }

    /// <summary>
    /// What the client may already know: only what the portal visibility allows (defaults when the project has no portal),
    /// client-visible log summaries, approved change orders and measurements; money only with financialSummary.
    /// </summary>
    internal async Task<(BuildClientFacts Facts, BuildPortalVisibility Visibility, object Refs)> ClientFactsAsync(Guid projectId, DateOnly from,
        DateOnly to, CancellationToken ct)
    {
        var project = await _db.BldProjects.AsNoTracking().FirstAsync(p => p.TenantId == Tid && p.Id == projectId, ct);
        var portal = await _db.BldProjectPortals.AsNoTracking().FirstOrDefaultAsync(p => p.TenantId == Tid && p.ProjectId == projectId, ct);
        var vis = portal?.Visibility ?? BuildPortalVisibility.Defaults;
        var today = BusinessClock.Today();

        decimal? actual = null, atStart = null;
        DateOnly? forecast = null;
        if (vis.OverallProgress || vis.ForecastDelivery)
        {
            var c = await _engine.ComputeAsync(Tid, projectId, today, bypassFilters: false, ct);
            if (vis.OverallProgress && c is not null)
            {
                actual = c.Result.ActualPercent;
                atStart = await _db.BldProgressSnapshots.AsNoTracking()
                    .Where(x => x.TenantId == Tid && x.ProjectId == projectId && x.Date <= from)
                    .OrderByDescending(x => x.Date).Select(x => (decimal?)x.ActualPercent).FirstOrDefaultAsync(ct);
            }
            if (vis.ForecastDelivery) forecast = c?.Result.ForecastDelivery;
        }

        var completed = new List<string>();
        if (vis.StageProgress)
            completed = await (from e in _db.BldStageProgressEvents.AsNoTracking()
                               join st in _db.BldStages.AsNoTracking() on e.StageId equals st.Id
                               where e.TenantId == Tid && e.ProjectId == projectId && e.Percent == 100 && e.PreviousPercent < 100
                                     && e.EffectiveDate >= @from && e.EffectiveDate <= to
                               select st.Name).Distinct().ToListAsync(ct);

        var logs = await _db.BldDailyLogs.AsNoTracking()
            .Where(l => l.TenantId == Tid && l.ProjectId == projectId && l.ClientVisible && l.ClientSummary != null && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date).Select(l => new { l.Id, l.Date, l.ClientSummary }).Take(31).ToListAsync(ct);

        var fromUtc = BusinessClock.TodayStartUtc(from.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var toUtc = BusinessClock.TodayStartUtc(to.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)).AddDays(1);
        var changeOrders = vis.ChangeOrders
            ? await _db.BldChangeOrders.AsNoTracking()
                .Where(c => c.TenantId == Tid && c.ProjectId == projectId && c.Status == BuildChangeOrderStatus.Approved
                            && c.DecidedAt >= fromUtc && c.DecidedAt < toUtc)
                .OrderBy(c => c.Number).Select(c => new { c.Id, c.Number, c.Title, c.ScheduleImpactDays, c.CostImpact }).ToListAsync(ct)
            : [];
        var measurements = vis.Measurements
            ? await _db.BldMeasurements.AsNoTracking()
                .Where(m => m.TenantId == Tid && m.ProjectId == projectId && m.Status == BuildMeasurementStatus.Approved
                            && m.ApprovedAt >= fromUtc && m.ApprovedAt < toUtc)
                .OrderBy(m => m.Number)
                .Select(m => new
                {
                    m.Id, m.Number, m.PeriodEnd,
                    Amount = _db.BldMeasurementItems.Where(i => i.TenantId == Tid && i.MeasurementId == m.Id).Sum(i => i.Amount),
                }).ToListAsync(ct)
            : [];
        var upcoming = vis.Schedule
            ? await _db.BldStages.AsNoTracking()
                .Where(s => s.TenantId == Tid && s.ProjectId == projectId && s.ProgressPercent < 100 && s.PlannedEndDate > to && s.PlannedEndDate <= to.AddDays(30))
                .OrderBy(s => s.PlannedEndDate).Select(s => new { s.Name, End = s.PlannedEndDate!.Value }).Take(10).ToListAsync(ct)
            : [];

        var money = vis.FinancialSummary;
        var facts = new BuildClientFacts(project.Name, from, to, actual, atStart, completed,
            logs.Select(l => (l.Date, l.ClientSummary!)).ToList(),
            changeOrders.Select(c => (c.Number, c.Title, c.ScheduleImpactDays, money ? (decimal?)c.CostImpact : null)).ToList(),
            measurements.Select(m => (m.Number, m.PeriodEnd, money ? m.Amount : null)).ToList(),
            upcoming.Select(u => (u.Name, u.End)).ToList(), forecast);
        var refs = new
        {
            from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), portalVisibility = portal is not null,
            dailyLogIds = logs.Select(l => l.Id), changeOrderIds = changeOrders.Select(c => c.Id), measurementIds = measurements.Select(m => m.Id),
        };
        return (facts, vis, refs);
    }

    // ── 5/6. Risk explanation ─────────────────────────────────────────────────

    /// <summary>The LLM explains the deterministic analysis (it may only refer to its item keys). Needs a provider: no fallback.</summary>
    public async Task<BuildAiSuggestionDto> ExplainRiskAsync(Guid projectId, BuildRiskExplainRequest r, CancellationToken ct)
    {
        var kind = (r.Kind ?? "").Trim().ToLowerInvariant();
        if (kind is not ("delay" or "cost")) throw new DomainException("Tipo de risco inválido: use delay ou cost.", "invalid_value");
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        var analysis = kind == "delay" ? await _risk.DelayAsync(projectId, ct) : await _risk.CostAsync(projectId, ct);
        var capability = kind == "delay" ? BuildAiCapability.DelayRisk : BuildAiCapability.CostRisk;
        var llm = (await CallAsync(capability, new { analise = analysis },
            "Explique só os itens de 'analise.items' pela chave 'key'. Não calcule nem cite números que não estejam na análise.",
            BuildAiContracts.RiskExplainSchema, 2048, null, fallbackAllowed: false, ct))!;
        var parsed = BuildAiContracts.ParseRiskExplanation(llm.Output, analysis);
        var refs = new { kind, analysis.Methodology, itemKeys = analysis.Items.Select(i => i.Key) };
        return await StoreAsync(projectId, capability, llm, null, refs, parsed, analysis.Items.Count == 0 ? 0.5m : 0.7m, DefaultTtl, ct);
    }

    // ── 7. Document intelligence ──────────────────────────────────────────────

    /// <summary>Sends the document (image or PDF) to the model — needs BuildAI and BuildVision. No deterministic extraction.</summary>
    public async Task<BuildAiSuggestionDto> AnalyzeDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await _db.BldDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == Tid && d.Id == documentId, ct)
                  ?? throw new NotFoundException("BuildDocument", documentId);
        await _settings.EnsureBuildAiAsync(Tid, ct);
        await _settings.EnsureBuildVisionAsync(Tid, ct);
        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");
        var bytes = await ReadAsync(doc.FileKey, BuildDocument.MaxBytes, "file_too_large", ct);
        var media = doc.ContentType == AiAttachmentPdf ? AiAttachmentPdf : ImageMediaType(bytes)
                    ?? throw new DomainException("A IA lê PDF, JPEG, PNG ou WebP.", "invalid_file_content");
        if (bytes.Length > Nexo.Infrastructure.Modules.Interpreter.AiStructuredCompletion.MaxAttachmentBytes)
            throw new DomainException("Documento grande demais para a IA (até 5 MB).", "file_too_large");

        var llm = (await CallAsync(BuildAiCapability.DocumentIntelligence,
            new { documento = new { id = doc.Id, titulo = doc.Title, categoriaInformada = doc.Category.ToString(), arquivo = doc.FileName } },
            "Datas no formato AAAA-MM-DD; valores em reais com ponto decimal. documentType deve ser um dos valores permitidos.",
            BuildAiContracts.DocumentSchema, 3000, [new Nexo.Application.Modules.Interpreter.Interfaces.AiAttachment(media, bytes, doc.Title)],
            fallbackAllowed: false, ct))!;
        var parsed = BuildAiContracts.ParseDocument(llm.Output);
        var refs = new { documentId = doc.Id, doc.Sha256, doc.ContentType };
        return await StoreAsync(doc.ProjectId, BuildAiCapability.DocumentIntelligence, llm, doc.Id, refs, parsed, parsed.Confidence, LongTtl, ct);
    }

    private const string AiAttachmentPdf = Nexo.Application.Modules.Interpreter.Interfaces.AiAttachment.Pdf;

    private async Task<(object, bool)> ApplyDocumentAsync(BuildAiSuggestion s, CancellationToken ct)
    {
        var doc = await _db.BldDocuments.FirstOrDefaultAsync(d => d.TenantId == Tid && d.Id == s.SubjectId, ct)
                  ?? throw new DomainException("O documento desta sugestão não existe mais.", "document_not_found");
        var x = BuildAiContracts.Deserialize<BuildDocumentExtraction>(s.OutputJson);
        BuildDocumentCategory? category = x.DocumentType is { } t && Enum.TryParse<BuildDocumentCategory>(t, out var c) ? c : null;
        doc.ApplyExtraction(category, x.ValidUntil, s.OutputJson, s.Id);
        _audit.Record(AuditActions.BuildDocumentSaved, "Document", doc.Id, $"Dados do documento confirmados a partir da IA: {doc.Title}.",
            new { suggestionId = s.Id, category = category?.ToString(), x.ValidUntil });
        // A draft financial movement from an invoice is not created here (see docs/build/ai.md).
        return (new { documentId = doc.Id, category = doc.Category.ToString(), validUntil = doc.ValidUntil }, false);
    }
}
