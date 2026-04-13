namespace Nexo.Application.Modules.Restaurante;

// ═══════════════════════════════════════════════════════════
// AREA
// ═══════════════════════════════════════════════════════════

public record CreateAreaRequest(string Name, string? Description = null);
public record UpdateAreaRequest(string Name, string? Description = null);

public record AreaDto(
    Guid Id, string Name, string? Description,
    bool IsActive, int TableCount, DateTime CreatedAt);

// ═══════════════════════════════════════════════════════════
// TABLE
// ═══════════════════════════════════════════════════════════

public record CreateTableRequest(Guid AreaId, string Number, int Capacity = 4);
public record UpdateTableRequest(Guid AreaId, string Number, int Capacity);
public record UpdateTableStatusRequest(string Status);  // "Available"|"Reserved"|"Maintenance"

public record TableDto(
    Guid Id, Guid AreaId, string AreaName,
    string Number, int Capacity, string Status,
    bool IsActive, DateTime CreatedAt);

// ═══════════════════════════════════════════════════════════
// ORDER
// ═══════════════════════════════════════════════════════════

public record OpenOrderRequest(
    string       OrderType,           // "DineIn"|"Counter"|"Takeaway"
    Guid?        TableId    = null,
    int?         PartySize  = null,
    Guid?        CustomerId = null,
    string?      Notes      = null);

public record AddOrderItemRequest(
    Guid         ProductId,
    decimal      Quantity,
    string?      Notes      = null,
    List<ApplyModifierRequest>? Modifiers = null);

public record ApplyModifierRequest(Guid ModifierId);

public record UpdateOrderItemStatusRequest(string Status);  // kitchen flow

public record PayOrderRequest(
    List<PaymentInputDto> Payments,
    int?                  PartySize = null);   // set here when CouvertAutomatic=false

public record PaymentInputDto(string Method, string Type, decimal Amount, DateTime? DueDate = null);

public record OrderItemModifierDto(
    Guid   ModifierId,
    string LabelSnapshot,
    decimal PriceSnapshot);

public record OrderItemDto(
    Guid    Id, Guid ProductId, string ProductName,
    decimal Quantity, decimal UnitPrice, decimal Total,
    string  Status, string? Notes,
    IReadOnlyList<OrderItemModifierDto> Modifiers,
    DateTime? SentToKitchenAt, DateTime? PreparedAt,
    DateTime? DeliveredAt, DateTime? CancelledAt);

public record OrderDto(
    Guid    Id, int OrderNumber, string Status, string OrderType,
    Guid?   TableId, string? TableNumber,
    int?    PartySize,
    Guid    WaiterId, Guid? CustomerId, Guid? SaleId,
    decimal ItemsSubtotal, decimal CouvertAmount, decimal ServiceFeeAmount, decimal Total,
    string? Notes,
    DateTime OpenedAt, DateTime? ClosedAt, DateTime? CancelledAt,
    IReadOnlyList<OrderItemDto> Items);

public record CloseOrderResponse(
    Guid OrderId, Guid SaleId, decimal Total,
    string Message);

// ═══════════════════════════════════════════════════════════
// RECIPE CARD
// ═══════════════════════════════════════════════════════════

public record CreateRecipeCardRequest(
    Guid ProductId, decimal Yield, string YieldUnit, string? Notes = null);

public record UpdateRecipeCardRequest(decimal Yield, string YieldUnit, string? Notes = null);

public record AddIngredientRequest(
    Guid IngredientProductId, decimal Quantity, string Unit);

public record RecipeIngredientDto(
    Guid Id, Guid IngredientProductId, string IngredientName, string IngredientCode,
    decimal Quantity, string Unit,
    decimal CurrentCostPrice,
    decimal LineCost);   // Qty × CostPrice

public record RecipeCardDto(
    Guid Id, Guid ProductId, string ProductName, string ProductCode,
    decimal Yield, string YieldUnit,
    bool IsActive, string? Notes,
    decimal CalculatedCost,      // custo total dos ingredientes / rendimento
    decimal CmvPercent,          // (calculatedCost / product.SalePrice) × 100
    IReadOnlyList<RecipeIngredientDto> Ingredients,
    DateTime CreatedAt);

// ═══════════════════════════════════════════════════════════
// MODIFIERS
// ═══════════════════════════════════════════════════════════

public record CreateModifierGroupRequest(
    Guid ProductId, string Name,
    bool IsRequired, int MinSelections, int MaxSelections, int SortOrder);

public record UpdateModifierGroupRequest(
    string Name, bool IsRequired, int MinSelections, int MaxSelections, int SortOrder);

public record CreateModifierRequest(
    Guid GroupId, string Name, decimal PriceAdjustment, int SortOrder);

public record UpdateModifierRequest(
    string Name, decimal PriceAdjustment, int SortOrder);

public record ModifierDto(
    Guid Id, string Name, decimal PriceAdjustment, int SortOrder, bool IsActive);

public record ModifierGroupDto(
    Guid Id, Guid ProductId, string Name,
    bool IsRequired, int MinSelections, int MaxSelections, int SortOrder, bool IsActive,
    IReadOnlyList<ModifierDto> Modifiers);

// ═══════════════════════════════════════════════════════════
// FOOD SERVICE SETTINGS
// ═══════════════════════════════════════════════════════════

public record UpdateFoodServiceSettingsRequest(
    string   StoreType,
    bool     CouvertEnabled,
    decimal? CouvertPricePerPerson,
    bool     CouvertAutomatic,
    bool     ServiceFeeEnabled,
    decimal? ServiceFeePercent,
    string   OrderTypesEnabled);

public record FoodServiceSettingsDto(
    Guid     Id,
    string   StoreType,
    bool     CouvertEnabled,
    decimal? CouvertPricePerPerson,
    bool     CouvertAutomatic,
    bool     ServiceFeeEnabled,
    decimal? ServiceFeePercent,
    string   OrderTypesEnabled);
