using Nexo.Domain.Modules.Service;

namespace Nexo.Application.Modules.Service;

public sealed record SvcCommissionEntryDto(
    Guid                Id,
    Guid                StoreId,
    Guid                ProfessionalId,
    Guid                CustomerId,
    SvcCommissionSource Source,
    Guid                SourceId,
    decimal             BaseAmount,
    decimal             CommissionPercent,
    decimal             CommissionAmount,
    DateTime            RecognizedAt,
    Guid?               PayoutId,
    string?             Description,
    DateTime            CreatedAt);

public sealed record SvcCommissionPayoutDto(
    Guid                      Id,
    Guid                      StoreId,
    Guid                      ProfessionalId,
    DateTime                  PeriodStart,
    DateTime                  PeriodEnd,
    decimal                   TotalAmount,
    int                       EntryCount,
    SvcCommissionPayoutStatus Status,
    DateTime?                 PaidAt,
    string?                   Notes,
    DateTime                  CreatedAt,
    DateTime                  UpdatedAt);

public sealed record SvcCommissionPayoutDetailDto(
    SvcCommissionPayoutDto              Payout,
    IReadOnlyList<SvcCommissionEntryDto> Entries);

/// <summary>Per-professional position: open (not yet closed), closed-but-unpaid, and paid.</summary>
public sealed record SvcCommissionSummaryDto(
    Guid    ProfessionalId,
    int     OpenCount,
    decimal OpenAmount,
    int     PendingPayoutCount,
    decimal PendingPayoutAmount,
    int     PaidPayoutCount,
    decimal PaidPayoutAmount);

/// <summary>Closes the open entries of one professional recognised inside [PeriodStart, PeriodEnd].</summary>
public sealed record CloseSvcCommissionPayoutRequest(
    Guid     ProfessionalId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    string?  Notes = null);

/// <summary>PaidAt defaults to now. A repeated call on a paid payout is a no-op.</summary>
public sealed record MarkSvcCommissionPayoutPaidRequest(DateTime? PaidAt = null);
