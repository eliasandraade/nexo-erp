using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// MVP stub for the Claude LLM analyzer.
/// Returns Unknown for all fields — forces all fields to RequiresInput status.
/// Replace with real Anthropic API integration in the next iteration.
/// </summary>
public sealed class ClaudeAnalyzerStub : IDocumentAnalyzer
{
    public AnalyzerProvider Provider => AnalyzerProvider.Claude;

    public Task<AnalysisOutput> AnalyzeAsync(AnalysisInput input, CancellationToken ct = default)
    {
        var output = new AnalysisOutput(
            Amount:              ExtractedField<decimal?>.Unknown(),
            Date:                ExtractedField<DateOnly?>.Unknown(),
            Payee:               ExtractedField<string?>.Unknown(),
            Account:             ExtractedField<string?>.Unknown(),
            RawProviderResponse: "{}",
            Prompt:              new PromptMetadata("claude-extraction", "0.0.0-stub",
                                     "0000000000000000"));

        return Task.FromResult(output);
    }
}
