namespace Nexo.Application.Features.Sales;

// ── Requests ────────────────────────────────────────────────────────────────

public record CreateSaleRequest(
    Guid? CustomerId = null,
    Guid? CashSessionId = null,
    string? Notes = null);

public record AddSaleItemRequest(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount = 0,
    string? Notes = null);

public record UpdateSaleItemRequest(
    decimal Quantity,
    decimal DiscountAmount);

/// <summary>
/// A single payment line on a sale confirmation.
/// PaymentMethod = HOW the customer pays (Cash, Pix, CreditCard…)
/// PaymentType   = WHEN the money enters (Cash = now, Credit = future)
/// </summary>
public record PaymentInput(
    string Method,          // "Cash" | "Debit" | "Credit" | "Pix" | "Transfer" | "Check" | "Mixed" | "Other"
    string Type,            // "Cash" (à vista) | "Credit" (a prazo)
    decimal Amount,
    DateTime? DueDate = null);  // required when Type = "Credit"

/// <summary>
/// Confirms a Draft sale.
/// The sum of all Payments must equal Sale.Total.
/// At least one PaymentInput is required.
/// </summary>
public record ConfirmSaleRequest(
    List<PaymentInput> Payments,
    decimal DiscountAmount = 0,
    decimal TaxAmount = 0,
    decimal SurchargesAmount = 0,   // couvert + service fee (restaurant orders)
    /// <summary>
    /// Products in this set are skipped during direct stock deduction.
    /// Used by the restaurant flow where stock is managed at ingredient level
    /// via recipe cards rather than at the finished-product level.
    /// </summary>
    IReadOnlySet<Guid>? SkipStockProductIds = null);

// ── Responses ───────────────────────────────────────────────────────────────

public record SaleItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string ProductCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal CostPrice,
    decimal DiscountAmount,
    decimal Total,
    string? Notes);

public record SalePaymentDto(
    Guid Id,
    string Method,
    string Type,
    decimal Amount,
    DateTime? DueDate);

public record SaleListItemDto(
    Guid Id,
    int Number,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    string SoldByName,
    decimal Total,
    DateTime Timestamp,
    int ItemCount,
    decimal TotalQuantity,
    string? FirstItemName,
    IReadOnlyList<string> PaymentMethods);

public record SaleDto(
    Guid Id,
    int Number,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    Guid SoldByUserId,
    string SoldByName,
    Guid? CashSessionId,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal Total,
    string? Notes,
    DateTime? ConfirmedAt,
    DateTime? PaidAt,
    DateTime? CancelledAt,
    IReadOnlyList<SaleItemDto> Items,
    IReadOnlyList<SalePaymentDto> Payments,
    DateTime CreatedAt,
    DateTime UpdatedAt);
