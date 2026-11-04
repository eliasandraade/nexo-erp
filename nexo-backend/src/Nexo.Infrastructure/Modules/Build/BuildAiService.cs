using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.Options;
using Nexo.Application.Modules.Build;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Build;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Build;

/// <summary>
/// Orken Build AI (Wave 5) — company side. Every capability produces a <see cref="BuildAiSuggestion"/> (Pending) through the
/// platform's AI runtime (<see cref="IAiStructuredCompletion"/>): provider and key from the SuperAdmin, versioned prompts,
/// usage limits, telemetry. Nothing is applied until a person accepts it (whole or selected items); acceptance goes through the
/// same paths as the manual actions (progress applier, daily log, bindings, client update drafts, document fields).
/// Gates: <c>BuildAiEnabled</c> (403 build_ai_disabled) and, to send photos or files, <c>BuildVisionEnabled</c>
/// (403 build_vision_disabled). No usable provider (or the monthly limit reached): the capabilities with a deterministic
/// fallback store a Source=Rules suggestion with no generated prose; the others answer 422 ai_provider_unavailable.
/// </summary>
public sealed partial class BuildAiService
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);
    public static readonly TimeSpan LongTtl = TimeSpan.FromDays(14);
    public const int MaxPhotos = 5;
    public const long MaxPhotoBytes = 5L * 1024 * 1024;

    private static readonly JsonSerializerOptions DataJson = new(JsonSerializerDefaults.Web)
    {
        // the data block is plain text for a model, not HTML: keep accents readable (the delimiter is neutralized separately)
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly NexoDbContext _db;
    private readonly ICurrentTenant _tenant;
    private readonly BuildAuditTrail _audit;
    private readonly BuildTenantSettingsService _settings;
    private readonly IAiStructuredCompletion _ai;
    private readonly IStorageProvider _storage;
    private readonly IIntegrationFeatureFlags _flags;
    private readonly IBuildProgressRecorder _progress;
    private readonly BuildProgressEngine _engine;
    private readonly BuildTwinStateReader _twin;
    private readonly BuildRiskService _risk;
    private readonly ILogger<BuildAiService> _logger;

    public BuildAiService(NexoDbContext db, ICurrentTenant tenant, BuildAuditTrail audit, BuildTenantSettingsService settings,
        IAiStructuredCompletion ai, IStorageProvider storage, IIntegrationFeatureFlags flags, IBuildProgressRecorder progress,
        BuildProgressEngine engine, BuildTwinStateReader twin, BuildRiskService risk, ILogger<BuildAiService> logger)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _settings = settings;
        _ai = ai;
        _storage = storage;
        _flags = flags;
        _progress = progress;
        _engine = engine;
        _twin = twin;
        _risk = risk;
        _logger = logger;
    }

    private Guid Tid => _tenant.Id;

    /// <summary>Set by the weekly job: the suggestion is created by the system, not a signed-in user.</summary>
    internal string? SystemActorName { get; set; }

    private string ActorName => SystemActorName ?? _audit.UserName;

    // ── Suggestions ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuildAiSuggestionDto>> ListAsync(Guid projectId, string? capability, string? status, CancellationToken ct)
    {
        await BuildScope.EnsureProjectAsync(_db, Tid, projectId, ct);
        await ExpireDueAsync(projectId, ct);
        var q = _db.BldAiSuggestions.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(capability))
        {
            var c = BuildScope.ParseEnum(capability, BuildAiCapability.DailyLogCopilot, "Capacidade");
            q = q.Where(s => s.Capability == c);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = BuildScope.ParseEnum(status, BuildAiSuggestionStatus.Pending, "Status");
            q = q.Where(s => s.Status == st);
        }
        var rows = await q.OrderByDescending(s => s.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<BuildAiSuggestionDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        if (s.ExpireIfDue(DateTime.UtcNow)) await _db.SaveBuildAsync(ct);
        return Map(s);
    }

    public async Task<BuildAiSuggestionDto> RejectAsync(Guid id, RejectBuildAiSuggestionRequest r, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        var now = DateTime.UtcNow;
        if (s.ExpireIfDue(now)) await _db.SaveBuildAsync(ct);
        s.Reject(_audit.UserId, _audit.UserName, r.Note, now);
        _audit.Record(AuditActions.BuildAiSuggestionRejected, "AiSuggestion", s.Id, $"Sugestão de IA recusada ({s.Capability}).",
            new { s.ProjectId, capability = s.Capability.ToString(), r.Note });
        await _db.SaveBuildAsync(ct);
        return Map(s);
    }

    /// <summary>
    /// Applies the suggestion (or the selected items) through the same paths as the manual actions. Expired → 409; already
    /// decided → 409. A refusal of the underlying path (e.g. an open critical NC blocking 100%) leaves the suggestion Pending.
    /// </summary>
    public async Task<BuildAiSuggestionDto> AcceptAsync(Guid id, AcceptBuildAiSuggestionRequest r, CancellationToken ct)
    {
        var s = await LoadAsync(id, ct);
        var now = DateTime.UtcNow;
        if (s.ExpireIfDue(now))
        {
            await _db.SaveBuildAsync(ct);
            throw new ConflictException("Esta sugestão expirou. Gere uma nova.");
        }
        s.EnsureDecidable(now);
        var selected = r.ItemIds is { Count: > 0 } ids ? ids.ToHashSet(StringComparer.Ordinal) : null;

        (object applied, bool partial) result = s.Capability switch
        {
            BuildAiCapability.DailyLogCopilot => await ApplyDailyLogAsync(s, ct),
            BuildAiCapability.ProgressInterpreter => await ApplyProgressAsync(s, selected, ct),
            BuildAiCapability.ModelMapper => await ApplyBindingsAsync(s, selected, ct),
            BuildAiCapability.ClientUpdate or BuildAiCapability.WeeklyDigest => ApplyClientUpdate(s),
            BuildAiCapability.DocumentIntelligence => await ApplyDocumentAsync(s, ct),
            _ => (new { acknowledged = true }, false),   // risk explanations: informative only
        };

        s.Accept(result.partial, _audit.UserId, _audit.UserName, r.Note, BuildAiContracts.Serialize(result.applied), now);
        _audit.Record(AuditActions.BuildAiSuggestionAccepted, "AiSuggestion", s.Id,
            $"Sugestão de IA {(result.partial ? "aceita em parte" : "aceita")} ({s.Capability}).",
            new { s.ProjectId, capability = s.Capability.ToString(), items = r.ItemIds, applied = result.applied });
        await _db.SaveBuildAsync(ct);
        if (s.Capability is BuildAiCapability.ProgressInterpreter) await _progress.RefreshTodaySnapshotAsync(s.ProjectId, ct);
        return Map(s);
    }

    private async Task<BuildAiSuggestion> LoadAsync(Guid id, CancellationToken ct)
        => await _db.BldAiSuggestions.FirstOrDefaultAsync(s => s.TenantId == Tid && s.Id == id, ct)
           ?? throw new NotFoundException("BuildAiSuggestion", id);

    private async Task ExpireDueAsync(Guid projectId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await _db.BldAiSuggestions
            .Where(s => s.TenantId == Tid && s.ProjectId == projectId && s.Status == BuildAiSuggestionStatus.Pending && s.ExpiresAt != null && s.ExpiresAt <= now)
            .ToListAsync(ct);
        if (due.Count == 0) return;
        foreach (var s in due) s.ExpireIfDue(now);
        await _db.SaveBuildAsync(ct);
    }

    // ── Generation plumbing ───────────────────────────────────────────────────

    /// <summary>
    /// One structured call. With <paramref name="fallbackAllowed"/>, "no usable provider" and "monthly limit reached" return
    /// null (the caller uses its deterministic fallback); any other provider failure surfaces (503 ai_provider_failed) and
    /// nothing is stored.
    /// </summary>
    private async Task<AiStructuredResult?> CallAsync(BuildAiCapability capability, object data, string? systemContext,
        Dictionary<string, JsonElement> schema, int maxOutputTokens, IReadOnlyList<AiAttachment>? attachments, bool fallbackAllowed,
        CancellationToken ct)
    {
        var promptType = BuildAiPrompts.For(capability);
        try
        {
            return await _ai.CompleteAsync(new AiStructuredRequest(promptType, promptType, Tid, _audit.UserId == Guid.Empty ? null : _audit.UserId,
                systemContext, JsonSerializer.Serialize(data, DataJson), schema, maxOutputTokens, attachments), ct);
        }
        catch (AiProviderUnavailableException) when (fallbackAllowed)
        {
            _logger.LogInformation("[Build AI] {Capability}: no usable provider, deterministic fallback.", capability);
            return null;
        }
        catch (AiProviderCallException ex) when (fallbackAllowed && ex.Failure == AiCallFailure.LimitReached)
        {
            _logger.LogInformation("[Build AI] {Capability}: AI limit reached, deterministic fallback.", capability);
            return null;
        }
    }

    private async Task<BuildAiSuggestionDto> StoreAsync(Guid projectId, BuildAiCapability capability, AiStructuredResult? llm, Guid? subjectId,
        object inputRefs, object output, decimal confidence, TimeSpan ttl, CancellationToken ct)
    {
        var promptType = BuildAiPrompts.For(capability);
        var provenance = llm is null
            ? BuildAiSuggestion.Provenance.Rules(promptType)
            : new BuildAiSuggestion.Provenance(llm.Provider, llm.Model, promptType, llm.PromptVersion, llm.PromptVersionId, llm.PromptHash,
                llm.InputTokens, llm.OutputTokens, llm.CostMicros, llm.LatencyMs);
        var s = BuildAiSuggestion.Create(Tid, projectId, capability, llm is null ? BuildAiSource.Rules : BuildAiSource.Llm, subjectId,
            BuildAiContracts.Serialize(inputRefs), BuildAiContracts.Serialize(output), confidence, provenance, _audit.UserId, ActorName,
            DateTime.UtcNow.Add(ttl));
        _db.BldAiSuggestions.Add(s);
        var details = new { projectId, capability = capability.ToString(), source = s.Source.ToString(), s.Provider, s.Model, s.PromptHash,
            s.InputTokens, s.OutputTokens, s.CostMicros };
        var description = $"Sugestão de IA gerada ({capability}, {(llm is null ? "regras" : s.Model)}).";
        if (SystemActorName is not null)
            _audit.RecordSystem(AuditActions.BuildAiSuggestionGenerated, Tid, "AiSuggestion", s.Id, description, SystemActorName, details);
        else
            _audit.Record(AuditActions.BuildAiSuggestionGenerated, "AiSuggestion", s.Id, description, details);
        await _db.SaveBuildAsync(ct);
        return Map(s);
    }

    private async Task<List<BuildAiStageRef>> StagesAsync(Guid projectId, CancellationToken ct)
        => await _db.BldStages.AsNoTracking().Where(s => s.TenantId == Tid && s.ProjectId == projectId)
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new BuildAiStageRef(s.Id, s.Name, s.Order, s.ProgressPercent))
            .ToListAsync(ct);

    /// <summary>Daily-log photos of this project sent to the model (vision): needs BuildVision, ≤ 5, JPEG/PNG/WebP ≤ 5 MB each.</summary>
    private async Task<(List<AiAttachment> Files, List<Guid> Ids)> PhotosAsync(Guid projectId, IReadOnlyList<Guid>? requested, CancellationToken ct)
    {
        var ids = (requested ?? []).Where(i => i != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return ([], []);
        await _settings.EnsureBuildVisionAsync(Tid, ct);
        if (ids.Count > MaxPhotos) throw new DomainException($"Envie até {MaxPhotos} fotos para a IA.", "too_many_photos");
        var rows = await (from p in _db.BldDailyLogPhotos.AsNoTracking()
                          join l in _db.BldDailyLogs.AsNoTracking() on p.DailyLogId equals l.Id
                          where p.TenantId == Tid && l.TenantId == Tid && l.ProjectId == projectId && ids.Contains(p.Id)
                          select new { p.Id, p.StorageKey, p.Caption, l.Date }).ToListAsync(ct);
        if (rows.Count != ids.Count) throw new DomainException("Uma das fotos não é desta obra.", "photo_not_found");
        if (!_flags.StorageEnabled) throw new BuildStorageUnavailableException("Armazenamento de arquivos indisponível.");

        var files = new List<AiAttachment>();
        foreach (var row in rows)
        {
            var bytes = await ReadAsync(row.StorageKey, MaxPhotoBytes, "photo_too_large", ct);
            var media = ImageMediaType(bytes) ?? throw new DomainException("Uma das fotos não é JPEG, PNG ou WebP.", "invalid_file_content");
            files.Add(new AiAttachment(media, bytes, $"foto {row.Id} ({row.Date:yyyy-MM-dd})"));
        }
        return (files, rows.Select(r => r.Id).ToList());
    }

    private async Task<byte[]> ReadAsync(string key, long max, string tooLargeCode, CancellationToken ct)
    {
        if (!StorageKeys.IsOfTenant(key, Tid)) throw new DomainException("Arquivo inválido.", "invalid_file_key");
        try
        {
            await using var stream = await _storage.OpenReadAsync(key, ct);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await ms.WriteAsync(buffer.AsMemory(0, read), ct);
                if (ms.Length > max) throw new DomainException($"Arquivo grande demais para a IA (até {max / (1024 * 1024)} MB).", tooLargeCode);
            }
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is not (DomainException or OperationCanceledException))
        {
            _logger.LogWarning(ex, "[Build AI] Could not read a stored file for the AI.");
            throw new BuildStorageUnavailableException("Não foi possível ler o arquivo no armazenamento.", ex);
        }
    }

    internal static string? ImageMediaType(byte[] bytes) => BuildFileSignatures.Sniff(bytes.AsSpan(0, Math.Min(bytes.Length, BuildFileSignatures.HeadLength))) switch
    {
        BuildFileKind.Jpeg => "image/jpeg",
        BuildFileKind.Png => "image/png",
        BuildFileKind.Webp => "image/webp",
        _ => null,
    };

    internal static BuildAiSuggestionDto Map(BuildAiSuggestion s)
        => new(s.Id, s.ProjectId, s.Capability.ToString(), s.Status.ToString(), s.Source.ToString(), s.SubjectId, s.Confidence,
            Parse(s.OutputJson), Parse(s.InputRefsJson), s.AppliedRefsJson is null ? null : Parse(s.AppliedRefsJson),
            s.Provider, s.Model, s.PromptType, s.PromptVersion, s.PromptVersionId, s.PromptHash, s.InputTokens, s.OutputTokens, s.CostMicros,
            s.LatencyMs, s.CreatedByName, BuildScope.Utc(s.CreatedAt), s.DecidedByName, BuildScope.Utc(s.DecidedAt), s.DecisionNote,
            BuildScope.Utc(s.ExpiresAt));

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

}
