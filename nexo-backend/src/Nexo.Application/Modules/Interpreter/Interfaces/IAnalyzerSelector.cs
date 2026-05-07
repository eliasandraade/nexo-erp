using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

public interface IAnalyzerSelector
{
    // Selects the best analyzer for the given input source and tenant.
    // Priority: RuleBased (zero-cost) → Claude (primary LLM) → OpenAI (fallback/config).
    // ForceProvider overrides automatic selection (used by reprocess flow).
    IDocumentAnalyzer Select(
        InputSourceType   source,
        Guid              tenantId,
        AnalyzerProvider? forceProvider = null);
}
