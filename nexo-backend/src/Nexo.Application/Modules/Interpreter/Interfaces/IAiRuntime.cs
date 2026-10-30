using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

/// <summary>
/// The AI providers as the runtime sees them — read from the SuperAdmin configuration (<c>ai_providers</c>), combined
/// with what this deployment implements and allows. The only source of truth for which provider can run.
/// </summary>
public interface IAiProviderRegistry
{
    Task<IReadOnlyList<AiProviderState>> GetStatesAsync(CancellationToken ct = default);

    /// <summary>
    /// The runtime configuration of a usable provider (decrypted key in memory only). Throws
    /// <see cref="AiProviderUnavailableException"/> with the reason when it can't be used.
    /// </summary>
    Task<AnalyzerConfig?> ResolveAsync(AnalyzerProvider provider, CancellationToken ct = default);
}

/// <summary>A provider's configuration and whether it can run now. Never carries the key nor its envelope.</summary>
public sealed record AiProviderState(
    Guid Id,
    AnalyzerProvider Provider,
    string Name,
    AiProviderStatus Status,
    string StatusMessage,
    bool Implemented,
    bool RequiresKey,
    bool AllowedByDeployment,
    bool IsEnabled,
    bool IsDefault,
    int Priority,
    string? ModelId,
    bool HasApiKey,
    string? ApiKeyLastFour,
    DateTime? ApiKeyUpdatedAt,
    long? MonthlyTokenLimit,
    long CostPerMillionInputMicros,
    long CostPerMillionOutputMicros,
    string? LastTestStatus,
    string? LastTestMessage,
    DateTime? LastTestAt)
{
    public bool IsAvailable => Status == AiProviderStatus.Available;
    public string StatusName => AiProviderStatusNames.Of(Status);
}

/// <summary>
/// Which analyzers to try, in order. Auto: the usable providers, the default first, then by priority. Forced: exactly
/// that provider, or <see cref="AiProviderUnavailableException"/> — never another one in its place.
/// </summary>
public interface IAnalyzerSelector
{
    Task<AnalyzerPlan> PlanAsync(InputSourceType source, Guid tenantId, AnalyzerProvider? forceProvider = null, CancellationToken ct = default);
}

public sealed record AnalyzerPlan(IReadOnlyList<AnalyzerStep> Steps, bool Forced);

public sealed record AnalyzerStep(IDocumentAnalyzer Analyzer, AnalyzerConfig? Config)
{
    public AnalyzerProvider Provider => Analyzer.Provider;
}

/// <summary>
/// Runs the plan: each step in order; in automatic mode an expected provider failure moves on to the next usable
/// provider (recorded as fallback), in forced mode it is returned to the caller as is. Failed attempts are written to
/// telemetry; the caller records the successful one (it knows the movement).
/// </summary>
public interface IAnalysisRunner
{
    Task<AnalysisRun> RunAsync(AnalysisInput input, AnalyzerProvider? forceProvider, string operationType, CancellationToken ct = default);
}

public sealed record AnalysisAttempt(AnalyzerProvider Provider, string? ModelId, bool Succeeded, string? FailureReason, int DurationMs);

public sealed record AnalysisRun(
    AnalysisOutput Output,
    AnalyzerProvider Provider,
    string? ModelId,
    bool Forced,
    IReadOnlyList<AnalysisAttempt> Attempts,
    int DurationMs)
{
    public bool FallbackUsed => Attempts.Count > 1;
    public AnalyzerProvider? FallbackFrom => FallbackUsed ? Attempts[0].Provider : null;
    public IReadOnlyList<string> Chain => Attempts.Select(a => a.Provider.ToString()).ToList();
}

/// <summary>The active prompt of a type in the SuperAdmin (<c>stored_prompt_versions</c>), or the built-in one.</summary>
public interface IActivePromptProvider
{
    Task<ActivePrompt> GetAsync(string promptType, CancellationToken ct = default);
}
