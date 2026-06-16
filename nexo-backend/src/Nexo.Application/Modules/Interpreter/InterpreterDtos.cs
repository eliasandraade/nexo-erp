namespace Nexo.Application.Modules.Interpreter;

// ═══════════════════════════════════════════════════════════
// REQUESTS
// ═══════════════════════════════════════════════════════════

public record AnalyzeMovementRequest(
    string? Text,
    Guid?   AttachmentId,
    string  InputSource = "Text");   // maps to InputSourceType enum

public record ConfirmMovementRequest(
    decimal  Amount,
    DateOnly Date,
    string   Description,
    string   Direction,              // MovementDirection enum value name
    string   Nature,                 // MovementNature enum value name
    Guid?    CategoryId,
    string   ContextType,            // FinancialContextType enum value name
    Guid?    ContextId,
    Guid?    AccountId,
    Guid     OriginalSuggestionId,   // required to compute corrections
    Guid?    SupplierId = null);     // optional counterparty (registered Supplier)

public record ReprocessMovementRequest(
    string  Reason,                  // TriggerReason enum value name
    string? ForceAnalyzer = null,    // AnalyzerProvider enum value name, null = auto-select
    string? Notes         = null);

// ═══════════════════════════════════════════════════════════
// EXTRACTION DTOs
// FieldStatus is always determined by backend — frontend only renders.
// ═══════════════════════════════════════════════════════════

public record DecimalFieldDto(
    decimal? Value,
    float    Confidence,
    string   Status,     // "AutoFilled" | "NeedsAttention" | "RequiresInput"
    string   Provider);  // "RuleBased" | "Claude" | "OpenAI"

public record DateFieldDto(
    string? Value,       // ISO 8601 date string or null
    float   Confidence,
    string  Status,
    string  Provider);

public record StringFieldDto(
    string? Value,
    float   Confidence,
    string  Status,
    string  Provider);

public record ExtractionSummaryDto(
    DecimalFieldDto Amount,
    DateFieldDto    Date,
    StringFieldDto  Payee,
    StringFieldDto  Account,
    string          AnalyzerUsed);

// ═══════════════════════════════════════════════════════════
// SUGGESTION DTOs
// Each field carries its SuggestionSource so frontend can display provenance.
// ═══════════════════════════════════════════════════════════

public record FieldSuggestionDto(
    string? Value,
    string? DisplayValue,
    string  Source);    // "LLM" | "RuleEngine" | "UserHistory" | "Manual" | "Hybrid"

public record CategorySuggestionDto(
    Guid?  Id,
    string? Name,
    string  Source);

public record ContextSuggestionDto(
    string? Type,
    Guid?   Id,
    string? DisplayName,
    string  Source);

public record AccountSuggestionDto(
    Guid?  Id,
    string? Name,
    string  Source);

public record MovementSuggestionDto(
    FieldSuggestionDto   Direction,
    FieldSuggestionDto   Nature,
    CategorySuggestionDto Category,
    ContextSuggestionDto  Context,
    AccountSuggestionDto  Account);

// ═══════════════════════════════════════════════════════════
// RESPONSES
// ═══════════════════════════════════════════════════════════

public record AnalyzeMovementResponse(
    Guid                  DraftId,
    Guid                  SuggestionId,   // required for ConfirmMovementRequest.OriginalSuggestionId
    ExtractionSummaryDto  Extraction,
    MovementSuggestionDto Suggestion);

public record CorrectionSummaryDto(
    string  Field,
    string? Original,
    string? Corrected);

public record ConfirmMovementResponse(
    Guid                               Id,
    string                             Status,
    DateTime                           ConfirmedAt,
    IReadOnlyList<CorrectionSummaryDto> Corrections);

public record ReprocessMovementResponse(
    Guid                  ReprocessLogId,
    Guid                  NewDraftId,
    ExtractionSummaryDto  Extraction,
    MovementSuggestionDto Suggestion);

public record VoidMovementResponse(Guid Id, string Status, DateTime VoidedAt);

public record MovementDetailsResponse(
    Guid     Id,
    string   Direction,
    string   Nature,
    decimal  Amount,
    DateOnly Date,
    string   Description,
    string   NormalizedDescription,
    Guid?    CategoryId,
    string   ContextType,
    Guid?    ContextId,
    Guid?    AccountId,
    string   Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record MovementListItemResponse(
    Guid     Id,
    string   Direction,
    string   Nature,
    decimal  Amount,
    DateOnly Date,
    string   Description,
    string   ContextType,
    Guid?    ContextId,
    Guid?    SupplierId,
    string   Status,
    DateTime CreatedAt);

public record MovementListResponse(
    IReadOnlyList<MovementListItemResponse> Items,
    int                                     TotalCount,
    int                                     Page,
    int                                     PageSize);

public record SuggestionStatsResponse(
    Dictionary<string, double> AcceptanceRateBySource,
    Dictionary<string, int>    CorrectionsByType,
    int                        TotalMovements,
    int                        TotalCorrections);
