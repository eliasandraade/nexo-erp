using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Selects the appropriate IDocumentAnalyzer.
/// Strategy: RuleBased always runs first (zero cost).
/// For sources that require content understanding (File, Audio, Xml, Email, Webhook),
/// falls back to LLM (Claude by default, OpenAI as secondary).
/// ForceProvider overrides auto-selection (reprocess flow).
/// Disabled providers (via IInterpreterFeatureFlags) fall back to RuleBased.
/// </summary>
public sealed class AnalyzerSelectorService : IAnalyzerSelector
{
    private readonly IEnumerable<IDocumentAnalyzer> _analyzers;
    private readonly IInterpreterFeatureFlags       _flags;

    public AnalyzerSelectorService(
        IEnumerable<IDocumentAnalyzer> analyzers,
        IInterpreterFeatureFlags       flags)
    {
        _analyzers = analyzers;
        _flags     = flags;
    }

    public IDocumentAnalyzer Select(
        InputSourceType   source,
        Guid              tenantId,
        AnalyzerProvider? forceProvider = null)
    {
        if (forceProvider.HasValue)
        {
            if (!IsEnabled(forceProvider.Value))
                throw new InvalidOperationException(
                    $"Analyzer '{forceProvider.Value}' is disabled by feature flags.");
            return Require(forceProvider.Value);
        }

        // Plain text → RuleBased (zero cost, always available if enabled).
        // Other sources → prefer Claude, fall back to RuleBased if disabled.
        if (source == InputSourceType.Text)
            return Require(AnalyzerProvider.RuleBased);

        if (_flags.EnableClaudeAnalyzer)
            return Require(AnalyzerProvider.Claude);

        if (_flags.EnableOpenAIAnalyzer)
            return Require(AnalyzerProvider.OpenAI);

        // Both LLM providers disabled — degrade to RuleBased.
        return Require(AnalyzerProvider.RuleBased);
    }

    private bool IsEnabled(AnalyzerProvider provider) => provider switch
    {
        AnalyzerProvider.RuleBased => _flags.EnableRuleBasedAnalyzer,
        AnalyzerProvider.Claude    => _flags.EnableClaudeAnalyzer,
        AnalyzerProvider.OpenAI    => _flags.EnableOpenAIAnalyzer,
        _                          => false,
    };

    private IDocumentAnalyzer Require(AnalyzerProvider provider)
        => _analyzers.FirstOrDefault(a => a.Provider == provider)
           ?? throw new InvalidOperationException(
               $"No IDocumentAnalyzer registered for provider '{provider}'.");
}
