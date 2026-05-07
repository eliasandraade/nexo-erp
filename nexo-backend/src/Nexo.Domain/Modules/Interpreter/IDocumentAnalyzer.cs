namespace Nexo.Domain.Modules.Interpreter;

public interface IDocumentAnalyzer
{
    AnalyzerProvider Provider { get; }

    Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken ct = default);
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
    string                    RawProviderResponse,   // LlmRawResponse — never treated as contract
    PromptMetadata            Prompt,
    // Token usage — zero for RuleBased; populated by LLM analyzers.
    int  InputTokens          = 0,
    int  OutputTokens         = 0,
    long EstimatedCostMicros  = 0)
{
    // Convenience alias for existing code that reads Prompt
    public PromptMetadata PromptMetadata => Prompt;
}
