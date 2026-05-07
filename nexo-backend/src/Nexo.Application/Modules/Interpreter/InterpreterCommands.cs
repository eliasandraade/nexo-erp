using Nexo.Domain.Modules.Interpreter;

namespace Nexo.Application.Modules.Interpreter;

// Internal command records consumed by use cases.
// Separate from HTTP request DTOs to decouple transport from orchestration.

public record AnalyzeMovementCommand(
    Guid            TenantId,
    Guid            UserId,
    string?         Text,
    Guid?           AttachmentId,
    InputSourceType InputSource);

public record ConfirmMovementCommand(
    Guid                 TenantId,
    Guid                 UserId,
    Guid                 DraftId,
    decimal              Amount,
    DateOnly             Date,
    string               Description,
    MovementDirection    Direction,
    MovementNature       Nature,
    Guid?                CategoryId,
    FinancialContextType ContextType,
    Guid?                ContextId,
    Guid?                AccountId,
    Guid                 OriginalSuggestionId);

public record ReprocessMovementCommand(
    Guid              TenantId,
    Guid              UserId,
    Guid              MovementId,
    TriggerReason     Reason,
    AnalyzerProvider? ForceAnalyzer,
    string?           Notes);

public record VoidMovementCommand(
    Guid TenantId,
    Guid UserId,
    Guid MovementId);

public record RebuildMovementMemoryProfileCommand(
    Guid  TenantId,
    Guid? UserId);
