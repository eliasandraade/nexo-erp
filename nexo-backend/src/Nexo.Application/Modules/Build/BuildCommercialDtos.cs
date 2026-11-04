namespace Nexo.Application.Modules.Build;

// ═══════════════════════════════════════════════════════════════════════
// WAVE 2 — COMMERCIAL (internal API). Money in BRL with 2 decimal places.
// ═══════════════════════════════════════════════════════════════════════

// ── Budgets ───────────────────────────────────────────────────────────

/// <summary>Totals of a budget per kind of line, margin applied, per m² when the area is known.</summary>
public sealed record BuildBudgetTotalsDto(
    decimal  Material,
    decimal  Labor,
    decimal  Service,
    decimal  Equipment,
    decimal  Other,
    decimal  Subtotal,
    decimal  MarginPercent,
    decimal  MarginValue,
    decimal  Total,
    decimal? AreaM2,
    decimal? CostPerM2,
    decimal? TotalPerM2);

public record SetBuildBudgetTermsRequest(
    DateOnly? ValidUntil,
    string?   Notes,
    string?   InternalNotes,
    decimal?  AreaM2);

public record RejectBuildBudgetRequest(string? Reason = null);

public record DuplicateBuildBudgetRequest(string? Name = null);

public record BuildBudgetVersionDto(
    Guid           Id,
    int            Version,
    string         Name,
    string         Status,
    decimal        FinalPrice,
    Guid?          ParentBudgetId,
    DateOnly?      ValidUntil,
    bool           IsExpired,
    DateTimeOffset CreatedAt);

// ── Templates ─────────────────────────────────────────────────────────

public record BuildBudgetTemplateItemDto(
    Guid     Id,
    int      Order,
    string   Name,
    string   Kind,
    string   Category,
    string   Unit,
    decimal? QuantityPerM2,
    decimal? FixedQuantity,
    decimal  UnitCost,
    string?  StageName);

public record BuildBudgetTemplateDto(
    Guid                                      Id,
    string                                    Name,
    string?                                   Description,
    decimal                                   DefaultMarginPercent,
    IReadOnlyList<BuildBudgetTemplateItemDto> Items,
    DateTimeOffset                            CreatedAt,
    DateTimeOffset                            UpdatedAt);

public record BuildBudgetTemplateItemRequest(
    string   Name,
    string?  Kind,
    string?  Category,
    string   Unit,
    decimal? QuantityPerM2,
    decimal? FixedQuantity,
    decimal  UnitCost,
    string?  StageName = null);

/// <summary>PUT replaces the whole template, items included.</summary>
public record SaveBuildBudgetTemplateRequest(
    string                                         Name,
    string?                                        Description,
    decimal                                        DefaultMarginPercent,
    IReadOnlyList<BuildBudgetTemplateItemRequest>? Items);

public record SaveBudgetAsTemplateRequest(string Name, string? Description = null, bool PerSquareMeter = false);

public record CreateBudgetFromTemplateRequest(
    string    Name,
    Guid?     ProjectId     = null,
    decimal?  AreaM2        = null,
    decimal?  MarginPercent = null,   // default: the template's
    DateOnly? ValidUntil    = null,
    string?   Notes         = null);

// ── Change orders (aditivos) ──────────────────────────────────────────

public record BuildChangeOrderItemDto(Guid Id, int Order, string Name, string Kind, decimal Quantity, string Unit,
    decimal UnitCost, decimal TotalCost);

public record BuildChangeOrderDto(
    Guid                                   Id,
    Guid                                   PublicId,
    Guid                                   ProjectId,
    int                                    Number,
    string                                 Title,
    string?                                Description,
    string                                 Reason,
    decimal                                CostImpact,
    int                                    ScheduleImpactDays,
    Guid?                                  StageId,
    string?                                StageName,
    string                                 Status,
    DateTimeOffset?                        SentAt,
    DateTimeOffset?                        DecidedAt,
    string?                                DecidedBy,
    string?                                DecisionChannel,
    string?                                ClientComment,
    string?                                SignerName,
    Guid?                                  SourceSelectionId,
    bool                                   AppliedToBudget,
    string                                 CreatedByName,
    IReadOnlyList<BuildChangeOrderItemDto> Items,
    DateTimeOffset                         CreatedAt,
    DateTimeOffset                         UpdatedAt);

public record BuildChangeOrderItemRequest(string Name, string? Kind, decimal Quantity, string Unit, decimal UnitCost);

/// <summary>CostImpact null = Σ items (0 without items). Items replace the previous ones.</summary>
public record SaveBuildChangeOrderRequest(
    string                                      Title,
    string?                                     Description,
    string?                                     Reason,
    decimal?                                    CostImpact,
    int                                         ScheduleImpactDays = 0,
    Guid?                                       StageId            = null,
    IReadOnlyList<BuildChangeOrderItemRequest>? Items              = null);

public record DecideBuildChangeOrderRequest(string? Comment = null);

// ── Selections (escolhas do cliente) ──────────────────────────────────

public record BuildSelectionOptionDto(
    Guid    Id,
    int     Order,
    string  Name,
    string? Description,
    decimal PriceDelta,
    bool    IsStandard,
    string? ImageKey,
    string? ImageUrl,
    int?    LeadTimeDays);

public record BuildSelectionDto(
    Guid                                   Id,
    Guid                                   PublicId,
    Guid                                   ProjectId,
    string                                 Title,
    string?                                Description,
    Guid?                                  StageId,
    string?                                StageName,
    DateOnly?                              Deadline,
    bool                                   DeadlinePassed,
    string                                 Status,
    bool                                   ClientVisible,
    Guid?                                  ChosenOptionId,
    string?                                ClientComment,
    DateTimeOffset?                        DecidedAt,
    string?                                ChosenVia,
    DateTimeOffset?                        ConfirmedAt,
    string?                                ConfirmedByName,
    Guid?                                  ChangeOrderId,
    IReadOnlyList<BuildSelectionOptionDto> Options,
    DateTimeOffset                         CreatedAt,
    DateTimeOffset                         UpdatedAt);

public record SaveBuildSelectionRequest(
    string    Title,
    string?   Description   = null,
    Guid?     StageId       = null,
    DateOnly? Deadline      = null,
    bool      ClientVisible = true);

public record SaveBuildSelectionOptionRequest(
    string   Name,
    string?  Description  = null,
    decimal  PriceDelta   = 0m,
    bool     IsStandard   = false,
    string?  ImageKey     = null,
    int?     LeadTimeDays = null,
    int?     Order        = null);

public record ConfirmBuildSelectionRequest(Guid? OptionId = null);

// ── Materials ─────────────────────────────────────────────────────────

public record BuildMaterialDeliveryDto(Guid Id, decimal Quantity, DateOnly Date, string? Note, string? ReceivedBy,
    string RecordedByName, DateTimeOffset CreatedAt);

public record BuildMaterialRequestDto(
    Guid                                    Id,
    Guid                                    ProjectId,
    Guid?                                   StageId,
    string?                                 StageName,
    string                                  MaterialName,
    string                                  Unit,
    decimal                                 Quantity,
    DateOnly?                               NeededBy,
    string                                  Status,
    bool                                    IsLate,
    Guid?                                   SupplierId,
    string?                                 SupplierName,
    decimal?                                EstimatedUnitCost,
    decimal?                                EstimatedTotal,
    DateTimeOffset?                         QuotedAt,
    DateTimeOffset?                         OrderedAt,
    decimal                                 DeliveredQuantity,
    decimal                                 RemainingQuantity,
    DateOnly?                               DeliveredAt,
    Guid?                                   MovementId,
    string?                                 Notes,
    string                                  CreatedByName,
    IReadOnlyList<BuildMaterialDeliveryDto> Deliveries,
    DateTimeOffset                          CreatedAt,
    DateTimeOffset                          UpdatedAt);

public record SaveBuildMaterialRequest(
    string    MaterialName,
    string    Unit,
    decimal   Quantity,
    Guid?     StageId  = null,
    DateOnly? NeededBy = null,
    string?   Notes    = null);

public record QuoteBuildMaterialRequest(Guid? SupplierId = null, decimal? EstimatedUnitCost = null);

public record OrderBuildMaterialRequest(Guid? SupplierId = null, decimal? UnitCost = null);

public record DeliverBuildMaterialRequest(decimal Quantity, DateOnly? Date = null, string? Note = null, string? ReceivedBy = null);

public record LinkBuildMaterialMovementRequest(Guid? MovementId);

// ── Measurements (medições) ───────────────────────────────────────────

public record BuildMeasurementItemDto(
    Guid     Id,
    Guid     StageId,
    string?  StageName,
    int      PreviousPercent,
    int      MeasuredPercent,
    int      DeltaPercent,
    decimal? Amount,
    decimal? SuggestedAmount);   // delta% × stage budgeted amount (reference budget)

public record BuildMeasurementDto(
    Guid                                   Id,
    Guid                                   ProjectId,
    int                                    Number,
    DateOnly                               PeriodStart,
    DateOnly                               PeriodEnd,
    string                                 Status,
    string?                                Notes,
    DateTimeOffset?                        SubmittedAt,
    string?                                ApprovedByName,
    DateTimeOffset?                        ApprovedAt,
    DateTimeOffset?                        RejectedAt,
    string?                                RejectedByName,
    string?                                RejectionReason,
    decimal                                TotalAmount,
    string                                 CreatedByName,
    IReadOnlyList<BuildMeasurementItemDto> Items,
    DateTimeOffset                         CreatedAt,
    DateTimeOffset                         UpdatedAt);

public record BuildMeasurementItemRequest(Guid StageId, int MeasuredPercent, decimal? Amount = null);

/// <summary>Items replace the previous ones (Draft only).</summary>
public record SaveBuildMeasurementRequest(
    DateOnly                                    PeriodStart,
    DateOnly                                    PeriodEnd,
    string?                                     Notes,
    IReadOnlyList<BuildMeasurementItemRequest>? Items);

public record RejectBuildMeasurementRequest(string? Reason = null);
