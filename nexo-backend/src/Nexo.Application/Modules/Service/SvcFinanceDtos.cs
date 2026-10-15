using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

// ── Raw rows (read side) ──────────────────────────────────────────────────────

public sealed record SvcFinancePaymentRow(
    Guid Id, Guid? OrderId, Guid? CustomerPackageId, decimal Amount, SvcPaymentMethod Method,
    SvcPaymentStatus Status, DateTime PaidAt, DateTime? VoidedAt);

/// <summary>An order item with the professional it is credited to (item first, then the order's).</summary>
public sealed record SvcFinanceOrderItemRow(Guid OrderId, Guid? ProfessionalId, decimal TotalAmount);

public sealed record SvcFinanceUsageRow(Guid? ProfessionalId, decimal BaseAmount);

public sealed record SvcFinanceCommissionRow(
    Guid ProfessionalId, SvcCommissionEntryKind Kind, decimal Amount, DateTime? ReversedAt, Guid? PayoutId);

public sealed record SvcFinanceRaw(
    IReadOnlyList<SvcFinancePaymentRow> Payments,
    IReadOnlyList<SvcFinanceOrderItemRow> OrderItems,
    IReadOnlyDictionary<Guid, Guid?> OrderProfessionals,
    IReadOnlyList<SvcFinanceUsageRow> Usages,
    IReadOnlyList<SvcFinanceCommissionRow> Commissions,
    IReadOnlyList<SvcAppointmentStatus> AppointmentStatuses,
    int PackagesSold,
    decimal PackagesSoldValue,
    IReadOnlyDictionary<Guid, string> ProfessionalNames);

// ── Summary (wire) ────────────────────────────────────────────────────────────

public sealed record SvcFinanceMethodDto(SvcPaymentMethod Method, decimal Amount, int Count);

public sealed record SvcFinanceDayDto(DateOnly Day, decimal Amount);

/// <summary>
/// One professional's period: what the paid comandas credited to them, what they did through
/// packages (value consumed) and the commission they earned. ProfessionalId null = not informed.
/// </summary>
public sealed record SvcFinanceProfessionalDto(
    Guid? ProfessionalId, string Name, decimal ReceivedFromOrders, decimal PackageServices, decimal Commission);

public sealed record SvcFinanceAppointmentsDto(int Total, int Completed, int Cancelled, int NoShow, int Open);

public sealed record SvcFinanceSummaryDto(
    DateOnly From,
    DateOnly To,
    decimal Received,
    int PaymentsCount,
    decimal AverageTicket,
    decimal ReceivedFromOrders,
    decimal ReceivedFromPackages,
    decimal Voided,
    int VoidedCount,
    IReadOnlyList<SvcFinanceMethodDto> ByMethod,
    IReadOnlyList<SvcFinanceDayDto> ByDay,
    IReadOnlyList<SvcFinanceProfessionalDto> ByProfessional,
    decimal CommissionTotal,
    int PackagesSold,
    decimal PackagesSoldValue,
    SvcFinanceAppointmentsDto Appointments);
