using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter.Interfaces;

// Interprets extracted data into business suggestions (category, context, direction, nature).
// Separate from IDocumentAnalyzer: extraction = what's on the document,
// interpretation = what it means in the Orken operational context.
public interface IInterpretationService
{
    Task<InterpretationOutput> SuggestAsync(
        AnalysisOutput    extraction,
        string            memoryContextJson,
        Guid              tenantId,
        Guid              userId,
        CancellationToken ct = default);
}

public sealed record InterpretationOutput(
    MovementDirection    Direction,    SuggestionSource DirectionSource,
    MovementNature       Nature,       SuggestionSource NatureSource,
    Guid?                CategoryId,   SuggestionSource CategorySource,
    FinancialContextType? ContextType, Guid?            ContextId,      SuggestionSource ContextSource,
    Guid?                AccountId,   SuggestionSource AccountSource);
