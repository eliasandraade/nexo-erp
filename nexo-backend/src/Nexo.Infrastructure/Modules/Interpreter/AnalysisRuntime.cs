using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Persistence;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// The prompt of a type that is active in the SuperAdmin; the built-in one when none is active. The hash is computed from
/// the content actually sent (never trusted from the row), so telemetry identifies exactly what ran.
/// </summary>
public sealed class ActivePromptProvider : IActivePromptProvider
{
    public const string BuiltInVersion = "builtin-1";

    private static readonly Dictionary<string, string> BuiltIn = new(StringComparer.Ordinal)
    {
        ["extraction"] = "Você extrai dados de um lançamento financeiro de uma empresa brasileira (despesa, recibo, nota, mensagem). "
            + "Identifique o valor total em reais, a data do pagamento ou do documento, o favorecido (quem recebeu) e a conta ou meio de pagamento. "
            + "Não invente: quando um campo não estiver no texto, devolva null com confiança 0.",
    };

    private readonly NexoDbContext _db;

    public ActivePromptProvider(NexoDbContext db) => _db = db;

    public async Task<ActivePrompt> GetAsync(string promptType, CancellationToken ct = default)
    {
        var active = await _db.StoredPromptVersions.AsNoTracking()
            .Where(p => p.PromptType == promptType && p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Version, p.Content })
            .FirstOrDefaultAsync(ct);
        if (active is not null && !string.IsNullOrWhiteSpace(active.Content))
            return new ActivePrompt(promptType, active.Version, HashOf(active.Content), active.Content);
        var content = BuiltIn.GetValueOrDefault(promptType)
                      ?? Nexo.Application.Modules.Build.BuildAiPrompts.Defaults.GetValueOrDefault(promptType, "");
        return new ActivePrompt(promptType, BuiltInVersion, HashOf(content), content);
    }

    public static string HashOf(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16].ToLowerInvariant();
}

/// <summary>
/// Automatic: the usable providers, the default first, then by priority (the SuperAdmin's order). Forced: that provider
/// alone, or a controlled <see cref="AiProviderUnavailableException"/>. Nothing usable → the same controlled exception.
/// </summary>
public sealed class AnalyzerSelectorService : IAnalyzerSelector
{
    private readonly IEnumerable<IDocumentAnalyzer> _analyzers;
    private readonly IAiProviderRegistry _registry;

    public AnalyzerSelectorService(IEnumerable<IDocumentAnalyzer> analyzers, IAiProviderRegistry registry)
    {
        _analyzers = analyzers;
        _registry = registry;
    }

    public async Task<AnalyzerPlan> PlanAsync(InputSourceType source, Guid tenantId, AnalyzerProvider? forceProvider = null, CancellationToken ct = default)
    {
        if (forceProvider is { } forced)
        {
            if (!Enum.IsDefined(forced))
                throw new AiProviderUnavailableException("Provedor de IA desconhecido.", forced);
            var config = await _registry.ResolveAsync(forced, ct);
            return new AnalyzerPlan([new AnalyzerStep(AnalyzerFor(forced), config)], Forced: true);
        }

        var states = await _registry.GetStatesAsync(ct);
        var steps = new List<AnalyzerStep>();
        foreach (var state in states.Where(s => s.IsAvailable))
        {
            try
            {
                steps.Add(new AnalyzerStep(AnalyzerFor(state.Provider), await _registry.ResolveAsync(state.Provider, ct)));
            }
            catch (AiProviderUnavailableException)
            {
                // changed between the two reads (or its key can't be opened): skip it
            }
        }
        if (steps.Count == 0)
            throw new AiProviderUnavailableException(
                "Nenhum provedor de IA está disponível: ative um provedor (o leitor de regras não tem custo) no SuperAdmin.");
        return new AnalyzerPlan(steps, Forced: false);
    }

    private IDocumentAnalyzer AnalyzerFor(AnalyzerProvider provider)
        => _analyzers.FirstOrDefault(a => a.Provider == provider)
           ?? throw new AiProviderUnavailableException(AiProviderRegistry.MessageOf(AiProviderStatus.ImplementationMissing, provider),
               provider, AiProviderStatus.ImplementationMissing);
}

/// <summary>
/// Runs the plan. Automatic mode: an expected provider failure (<see cref="AiProviderCallException"/>) is recorded and the
/// next usable provider runs — the result says which ran and that a fallback happened. Forced mode: the failure goes back to
/// the caller as is, never hidden behind another provider. Bugs (any other exception) are not caught here.
/// </summary>
public sealed class AnalysisRunner : IAnalysisRunner
{
    /// <summary>The worst case of one call's answer, reserved against the limits before the call.</summary>
    public const int MaxOutputTokensEstimate = 1024;

    private readonly IAnalyzerSelector _selector;
    private readonly ITelemetryWriter _telemetry;
    private readonly IAiUsageGuard _usage;
    private readonly ILogger<AnalysisRunner> _logger;

    public AnalysisRunner(IAnalyzerSelector selector, ITelemetryWriter telemetry, IAiUsageGuard usage, ILogger<AnalysisRunner> logger)
    {
        _selector = selector;
        _telemetry = telemetry;
        _usage = usage;
        _logger = logger;
    }

    public async Task<AnalysisRun> RunAsync(AnalysisInput input, AnalyzerProvider? forceProvider, string operationType, CancellationToken ct = default)
    {
        var plan = await _selector.PlanAsync(input.Source, input.TenantId, forceProvider, ct);
        var attempts = new List<AnalysisAttempt>();
        var total = Stopwatch.StartNew();
        AiProviderCallException? last = null;
        foreach (var step in plan.Steps)
        {
            var watch = Stopwatch.StartNew();
            Guid? reservation = null;
            try
            {
                // A paid call reserves its worst case against the monthly limits first (over a limit → LimitReached).
                if (step.Config is { } paid)
                {
                    var inputTokens = (paid.Prompt.Content.Length + (input.RawText?.Length ?? 0)) / 3 + 100;
                    reservation = await _usage.ReserveAsync(input.TenantId, paid,
                        paid.CostMicros(inputTokens, MaxOutputTokensEstimate), inputTokens + MaxOutputTokensEstimate, ct);
                }
                var output = await step.Analyzer.AnalyzeAsync(input, step.Config, ct);
                if (reservation is { } settled)
                    await _usage.SettleAsync(settled, output.EstimatedCostMicros, output.InputTokens, output.OutputTokens, CancellationToken.None);
                attempts.Add(new AnalysisAttempt(step.Provider, step.Config?.ModelId, true, null, (int)watch.ElapsedMilliseconds));
                return new AnalysisRun(output, step.Provider, step.Config?.ModelId, plan.Forced, attempts, (int)total.ElapsedMilliseconds);
            }
            catch (AiProviderCallException ex)
            {
                if (reservation is { } released) await _usage.ReleaseAsync(released, CancellationToken.None);
                last = ex;
                var ms = (int)watch.ElapsedMilliseconds;
                attempts.Add(new AnalysisAttempt(step.Provider, step.Config?.ModelId, false, ex.Reason, ms));
                _logger.LogWarning("AI provider {Provider} failed ({Reason}) in {Operation}; {Next}.",
                    step.Provider, ex.Reason, operationType, plan.Forced ? "no fallback (forced)" : "trying the next usable provider");
                await _telemetry.WriteAsync(new TelemetryEntry(input.TenantId, input.UserId, null, operationType, step.Provider.ToString(),
                    step.Config?.Prompt.Type ?? "", step.Config?.Prompt.Version ?? "", step.Config?.Prompt.Hash ?? "", 0, 0, 0, ms,
                    Success: false, ErrorMessage: $"{ex.Reason}: {ex.Message}", FallbackUsed: attempts.Count > 1,
                    FallbackFromProvider: attempts.Count > 1 ? attempts[0].Provider.ToString() : null,
                    AnalyzerChain: attempts.Select(a => a.Provider.ToString()).ToList(), RequiresInputCount: 0,
                    AmountConfidence: 0, DateConfidence: 0, ModelId: step.Config?.ModelId), ct);
                if (plan.Forced) throw;
            }
        }
        throw last!;
    }
}

/// <summary>
/// At startup, re-seals AI provider keys still stored in the legacy Base64 form (or under a previous master key), so
/// nothing readable stays in the database once a master key is configured. Failures never stop the API.
/// </summary>
public sealed class AiKeyUpgradeHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiKeyUpgradeHostedService> _logger;

    public AiKeyUpgradeHostedService(IServiceScopeFactory scopes, ILogger<AiKeyUpgradeHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await AiProviderRegistry.UpgradeStoredKeysAsync(scope.ServiceProvider.GetRequiredService<NexoDbContext>(),
                scope.ServiceProvider.GetRequiredService<ISecretProtector>(), _logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AI provider key upgrade failed; keys stay as they were and the provider shows as unavailable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
