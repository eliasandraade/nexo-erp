namespace Nexo.Domain.Modules.Interpreter;

public interface IDocumentAnalyzer
{
    AnalyzerProvider Provider { get; }

    /// <summary>
    /// Extracts the fields of the input. <paramref name="config"/> carries what the SuperAdmin configured for the provider
    /// (model, decrypted key, active prompt, prices); null for analyzers that need none (rule-based). Expected provider
    /// failures surface as <see cref="AiProviderCallException"/>.
    /// </summary>
    Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, AnalyzerConfig? config, CancellationToken ct = default);
}

/// <summary>
/// The runtime configuration of a provider for one call. Holds the decrypted key in memory only: <see cref="ToString"/>
/// redacts it, so it never reaches a log line or an exception message by accident.
/// </summary>
public sealed class AnalyzerConfig
{
    public AnalyzerConfig(AnalyzerProvider provider, string modelId, string apiKey, ActivePrompt prompt,
        long costPerMillionInputMicros, long costPerMillionOutputMicros)
    {
        Provider = provider;
        ModelId = modelId;
        ApiKey = apiKey;
        Prompt = prompt;
        CostPerMillionInputMicros = costPerMillionInputMicros;
        CostPerMillionOutputMicros = costPerMillionOutputMicros;
    }

    public AnalyzerProvider Provider { get; }
    public string ModelId { get; }
    public string ApiKey { get; }
    public ActivePrompt Prompt { get; }
    public long CostPerMillionInputMicros { get; }
    public long CostPerMillionOutputMicros { get; }

    public long CostMicros(long inputTokens, long outputTokens)
        => Cost(inputTokens, CostPerMillionInputMicros) + Cost(outputTokens, CostPerMillionOutputMicros);

    private static long Cost(long tokens, long perMillion)
        => tokens <= 0 || perMillion <= 0 ? 0 : (long)Math.Ceiling(tokens * (decimal)perMillion / 1_000_000m);

    public override string ToString() => $"AnalyzerConfig({Provider}, model {ModelId}, key ***)";
}

/// <summary>The prompt the runtime uses: the active version of its type in the SuperAdmin, or the built-in one.</summary>
public sealed record ActivePrompt(string Type, string Version, string Hash, string Content)
{
    public PromptMetadata Metadata => new(Type, Version, Hash);
}

public sealed record AnalysisInput(
    InputSourceType Source,
    string?         RawText,
    string?         StorageKey,       // file path/blob key when Source = File
    Guid            TenantId,
    Guid            UserId,
    // Partial results from a prior pass (e.g. RuleBased pre-filled some fields).
    // Analyzer should not re-extract fields already resolved above threshold.
    PartialExtraction? KnownFields = null);

public sealed record PartialExtraction(
    ExtractedField<decimal?>?  Amount  = null,
    ExtractedField<DateOnly?>? Date    = null,
    ExtractedField<string?>?   Payee   = null,
    ExtractedField<string?>?   Account = null);

public sealed record AnalysisOutput(
    ExtractedField<decimal?>  Amount,
    ExtractedField<DateOnly?> Date,
    ExtractedField<string?>   Payee,
    ExtractedField<string?>   Account,
    string                    RawProviderResponse,   // validated structured output only — never the provider's raw body
    PromptMetadata            Prompt,
    // Token usage — zero for RuleBased; populated by LLM analyzers.
    int  InputTokens          = 0,
    int  OutputTokens         = 0,
    long EstimatedCostMicros  = 0,
    AnalyzerProvider Provider = AnalyzerProvider.RuleBased,
    string? ModelId           = null)
{
    // Convenience alias for existing code that reads Prompt
    public PromptMetadata PromptMetadata => Prompt;
}
