using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>Who acts in the SuperAdmin, for the audit trail (never a secret).</summary>
public sealed record PlatformActor(Guid? Id, string? Name, string UserAgent, string RequestId);

/// <summary>Changes to a provider; null = unchanged. <see cref="ClearMonthlyTokenLimit"/> removes the limit.</summary>
public sealed record AiProviderPatch(
    bool? IsEnabled = null,
    bool? IsDefault = null,
    string? ModelId = null,
    int? Priority = null,
    long? MonthlyTokenLimit = null,
    bool ClearMonthlyTokenLimit = false,
    decimal? InputUsdPerMillionTokens = null,
    decimal? OutputUsdPerMillionTokens = null);

public sealed record AiConnectionTestResult(string Status, string Message, string? ModelId, int LatencyMs, DateTime TestedAt);

public sealed record AiPlaygroundRequest(string Text, AnalyzerProvider? ForceProvider, Guid? TenantId);

public sealed record AiPlaygroundResult(Guid TenantId, AnalysisRun Run, InterpretationOutput Interpretation);

/// <summary>
/// The SuperAdmin's provider operations. Every change is audited (without secrets); a key enters as plaintext only for
/// the duration of the call that encrypts it and is never returned — the API knows only <c>hasApiKey</c> and the last four.
/// </summary>
public sealed class AiProviderAdminService
{
    public const int MinKeyLength = 20;
    public const int MaxKeyLength = 400;

    private readonly NexoDbContext _db;
    private readonly IAiProviderRegistry _registry;
    private readonly ISecretProtector _secrets;
    private readonly IEnumerable<IDocumentAnalyzer> _analyzers;
    private readonly IAnalysisRunner _runner;
    private readonly IInterpretationService _interpretation;
    private readonly IMovementMemoryService _memory;
    private readonly ITelemetryWriter _telemetry;
    private readonly IAuditWriter _audit;
    private readonly ILogger<AiProviderAdminService> _logger;

    public AiProviderAdminService(NexoDbContext db, IAiProviderRegistry registry, ISecretProtector secrets, IEnumerable<IDocumentAnalyzer> analyzers,
        IAnalysisRunner runner, IInterpretationService interpretation, IMovementMemoryService memory, ITelemetryWriter telemetry,
        IAuditWriter audit, ILogger<AiProviderAdminService> logger)
    {
        _db = db;
        _registry = registry;
        _secrets = secrets;
        _analyzers = analyzers;
        _runner = runner;
        _interpretation = interpretation;
        _memory = memory;
        _telemetry = telemetry;
        _audit = audit;
        _logger = logger;
    }

    public Task<IReadOnlyList<AiProviderState>> ListAsync(CancellationToken ct) => _registry.GetStatesAsync(ct);

    public bool SecretStorageAvailable => _secrets.IsAvailable;

    public async Task<AiProviderState> UpdateAsync(Guid id, AiProviderPatch patch, PlatformActor actor, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        var changes = new List<string>();
        if (patch.IsEnabled is { } enabled && enabled != row.IsEnabled) { row.SetEnabled(enabled); changes.Add(enabled ? "ativado" : "desativado"); }
        if (patch.ModelId is not null && patch.ModelId.Trim() != (row.ModelId ?? "")) { row.SetModel(patch.ModelId); changes.Add($"modelo {row.ModelId ?? "(nenhum)"}"); }
        if (patch.Priority is { } priority && priority != row.Priority) { row.SetPriority(priority); changes.Add($"prioridade {priority}"); }
        if (patch.ClearMonthlyTokenLimit) { row.SetMonthlyTokenLimit(null); changes.Add("sem limite mensal de tokens"); }
        else if (patch.MonthlyTokenLimit is { } limit) { row.SetMonthlyTokenLimit(limit); changes.Add($"limite mensal {limit} tokens"); }
        if (patch.InputUsdPerMillionTokens is not null || patch.OutputUsdPerMillionTokens is not null)
        {
            row.SetPrices(Micros(patch.InputUsdPerMillionTokens) ?? row.CostPerMillionInputMicros, Micros(patch.OutputUsdPerMillionTokens) ?? row.CostPerMillionOutputMicros);
            changes.Add("preços por milhão de tokens");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (patch.IsDefault is { } isDefault && isDefault != row.IsDefault)
        {
            if (isDefault)
            {
                // One default at a time (a partial unique index enforces it): clear the others first, in their own statement.
                foreach (var other in await _db.AiProviders.Where(p => p.Id != id && p.IsDefault).ToListAsync(ct)) other.SetDefault(false);
                await _db.SaveChangesAsync(ct);
            }
            row.SetDefault(isDefault);
            changes.Add(isDefault ? "definido como padrão" : "deixou de ser o padrão");
        }
        if (changes.Count > 0)
            Stage(AuditActions.AiProviderUpdated, AuditSeverity.Warning, row, $"Provedor de IA {row.Provider}: {string.Join(", ", changes)}.", actor,
                new { changes });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await StateAsync(id, ct);
    }

    public async Task<AiProviderState> SetKeyAsync(Guid id, string apiKey, PlatformActor actor, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        if (!RequiresKey(row))
            throw new DomainException("Este provedor não usa chave de API.", "ai_key_not_applicable");
        var key = apiKey.Trim();
        if (key.Length is < MinKeyLength or > MaxKeyLength || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new DomainException("Chave de API inválida: cole a chave inteira, sem espaços.", "ai_key_invalid");
        var envelope = _secrets.Protect(key, AiProviderRegistry.KeyPurpose(row.Id)); // throws if no master key: never stored in clear
        row.SetApiKey(envelope, key[^4..]);
        Stage(AuditActions.AiProviderKeySet, AuditSeverity.Critical, row, $"Chave de API do provedor {row.Provider} cadastrada (final {row.ApiKeyLastFour}).", actor,
            new { lastFour = row.ApiKeyLastFour });
        await _db.SaveChangesAsync(ct);
        return await StateAsync(id, ct);
    }

    public async Task<AiProviderState> ClearKeyAsync(Guid id, PlatformActor actor, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        if (row.ApiKeyEncrypted is null) return await StateAsync(id, ct);
        row.ClearApiKey();
        Stage(AuditActions.AiProviderKeyRemoved, AuditSeverity.Critical, row, $"Chave de API do provedor {row.Provider} removida.", actor, null);
        await _db.SaveChangesAsync(ct);
        return await StateAsync(id, ct);
    }

    /// <summary>
    /// "Testar conexão": one minimal call with a short timeout, only when asked. Records the sanitized result (status,
    /// message, time) — never the provider's response. A provider with no implementation is never called.
    /// </summary>
    public async Task<AiConnectionTestResult> TestAsync(Guid id, PlatformActor actor, CancellationToken ct)
    {
        var row = await LoadAsync(id, ct);
        var now = DateTime.UtcNow;
        var watch = Stopwatch.StartNew();
        AiConnectionTestResult result;
        if (!Enum.TryParse<AnalyzerProvider>(row.Provider, out var provider) || !_analyzers.Any(a => a.Provider == provider))
            result = new("implementation_missing", AiProviderRegistry.MessageOf(AiProviderStatus.ImplementationMissing, provider), row.ModelId, 0, now);
        else if (!RequiresKey(row))
            result = new("ok", "O leitor de regras roda no próprio Orken, sem chamada externa.", null, 0, now);
        else if (row.ApiKeyEncrypted is null)
            result = new("missing_key", AiProviderRegistry.MessageOf(AiProviderStatus.MissingKey, provider), row.ModelId, 0, now);
        else if (string.IsNullOrWhiteSpace(row.ModelId))
            result = new("missing_model", AiProviderRegistry.MessageOf(AiProviderStatus.MissingModel, provider), null, 0, now);
        else if (_secrets.Unprotect(row.ApiKeyEncrypted, AiProviderRegistry.KeyPurpose(row.Id)) is not { } key)
            result = new("secret_unavailable", AiProviderRegistry.MessageOf(AiProviderStatus.SecretUnavailable, provider), row.ModelId, 0, now);
        else
        {
            try
            {
                var claude = _analyzers.OfType<ClaudeAnalyzer>().Single();
                var model = await claude.PingAsync(key, row.ModelId, ct);
                result = new("ok", "Conexão com a Anthropic funcionando.", model, (int)watch.ElapsedMilliseconds, now);
            }
            catch (AiProviderCallException ex)
            {
                var status = ex.Failure switch
                {
                    AiCallFailure.InvalidCredentials => "invalid_credentials",
                    AiCallFailure.RateLimited => "rate_limited",
                    _ => ex.Reason,
                };
                result = new(status, ex.Message, row.ModelId, (int)watch.ElapsedMilliseconds, now);
            }
        }

        row.RecordTest(result.Status, result.Message, now);
        Stage(AuditActions.AiProviderTested, result.Status == "ok" ? AuditSeverity.Info : AuditSeverity.Warning, row,
            $"Teste de conexão do provedor {row.Provider}: {result.Status}.", actor, new { result.Status, result.ModelId, result.LatencyMs });
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("AI provider {Provider} connection test: {Status} in {LatencyMs} ms.", row.Provider, result.Status, result.LatencyMs);
        return result;
    }

    /// <summary>
    /// The Playground: the same runner the tenants use, for a chosen (or the first) company. Forced provider → that one or a
    /// controlled error; automatic → the normal order with fallback. Writes telemetry; never returns a provider's raw answer.
    /// </summary>
    public async Task<AiPlaygroundResult> PlaygroundAsync(AiPlaygroundRequest request, PlatformActor actor, CancellationToken ct)
    {
        var text = request.Text?.Trim() ?? "";
        if (text.Length is 0 or > 1000) throw new DomainException("Escreva um texto de até 1000 caracteres.", "ai_playground_text");
        Guid tenantId;
        if (request.TenantId is { } chosen)
        {
            if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == chosen, ct)) throw new NotFoundException("Empresa", chosen);
            tenantId = chosen;
        }
        else
        {
            tenantId = await _db.Tenants.IgnoreQueryFilters().OrderBy(t => t.CompanyName).Select(t => t.Id).FirstOrDefaultAsync(ct);
            if (tenantId == Guid.Empty) throw new DomainException("Nenhuma empresa cadastrada para servir de contexto.", "ai_playground_no_tenant");
        }
        var userId = actor.Id ?? Guid.Empty;
        var input = new AnalysisInput(InputSourceType.Text, text, null, tenantId, userId);
        var run = await _runner.RunAsync(input, request.ForceProvider, "Playground", ct);
        var memory = await _memory.GetCompactContextAsync(tenantId, userId, ct);
        var interpretation = await _interpretation.SuggestAsync(run.Output, memory, tenantId, userId, ct);
        var output = run.Output;
        await _telemetry.WriteAsync(new TelemetryEntry(tenantId, actor.Id, null, "Playground", run.Provider.ToString(),
            output.Prompt.PromptType, output.Prompt.PromptVersion, output.Prompt.PromptHash, output.InputTokens, output.OutputTokens,
            output.EstimatedCostMicros, run.DurationMs, true, null, run.FallbackUsed, run.FallbackFrom?.ToString(), run.Chain,
            new[] { output.Amount.Status, output.Date.Status, output.Payee.Status, output.Account.Status }.Count(s => s == FieldStatus.RequiresInput),
            output.Amount.Confidence, output.Date.Confidence, run.ModelId), ct);
        return new AiPlaygroundResult(tenantId, run, interpretation);
    }

    private async Task<AiProvider> LoadAsync(Guid id, CancellationToken ct)
        => await _db.AiProviders.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Provedor de IA", id);

    private async Task<AiProviderState> StateAsync(Guid id, CancellationToken ct)
        => (await _registry.GetStatesAsync(ct)).Single(s => s.Id == id);

    private static bool RequiresKey(AiProvider row)
        => !Enum.TryParse<AnalyzerProvider>(row.Provider, out var p) || AiProviderRegistry.RequiresKey(p);

    private static long? Micros(decimal? usdPerMillion)
        => usdPerMillion is { } usd ? (usd < 0 ? throw new DomainException("Preço não pode ser negativo.", "ai_price_invalid") : (long)Math.Round(usd * 1_000_000m)) : null;

    private void Stage(string action, string severity, AiProvider row, string description, PlatformActor actor, object? metadata)
        => _audit.Stage(action, severity, "AiProvider", row.Id.ToString(), description, tenantId: null, actorId: actor.Id, actorName: actor.Name,
            actorType: "platform", metadata: new { provider = row.Provider, userAgent = actor.UserAgent, correlationId = actor.RequestId, detail = metadata });
}
