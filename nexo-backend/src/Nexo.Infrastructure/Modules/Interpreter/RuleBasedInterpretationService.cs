using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Infrastructure.Modules.Interpreter;

/// <summary>
/// Rule-based interpreter: derives Direction, Nature, and placeholders for
/// CategoryId / ContextId from the extraction output.
/// All business-meaning fields are RequiresInput because rule heuristics alone
/// cannot reliably classify operational intent — the LLM layer or user confirmation
/// is expected to fill those gaps.
/// </summary>
public sealed class RuleBasedInterpretationService : IInterpretationService
{
    public Task<InterpretationOutput> SuggestAsync(
        AnalysisOutput    extraction,
        string            memoryContextJson,
        Guid              tenantId,
        Guid              userId,
        CancellationToken ct = default)
    {
        // Direction: default to Out (expense) — most operational movements are outflows.
        // A future LLM pass or user history can override this.
        var direction       = MovementDirection.Out;
        var directionSource = SuggestionSource.RuleEngine;

        // Nature: default to Expense for Out, Transfer for Internal.
        var nature       = MovementNature.Expense;
        var natureSource = SuggestionSource.RuleEngine;

        var output = new InterpretationOutput(
            Direction:    direction,   DirectionSource: directionSource,
            Nature:       nature,      NatureSource:    natureSource,
            CategoryId:   null,        CategorySource:  SuggestionSource.RuleEngine,
            ContextType:  null,        ContextId:       null,
            ContextSource: SuggestionSource.RuleEngine,
            AccountId:    null,        AccountSource:   SuggestionSource.RuleEngine);

        return Task.FromResult(output);
    }
}
