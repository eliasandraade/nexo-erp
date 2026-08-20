using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// A closed commission period for one professional: freezes the entries that were open at closing
/// time and carries the amount owed. Store-scoped.
///
/// Lives as "a pagar" inside the Service module and touches the financeiro only when it is
/// actually paid — the owner's call: the expense is recorded on payment (regime de caixa),
/// not provisioned at closing. <see cref="MarkPaid"/> is the moment
/// ServiceFinancialPostingService writes the settled Payable.
/// </summary>
public class SvcCommissionPayout : StoreEntity
{
    private SvcCommissionPayout() { }
    private SvcCommissionPayout(Guid tenantId) : base(tenantId) { }

    public Guid                      ProfessionalId { get; private set; }
    public DateTime                  PeriodStart    { get; private set; }
    public DateTime                  PeriodEnd      { get; private set; }
    /// <summary>Sum of the entries frozen into this payout, snapshotted at closing.</summary>
    public decimal                   TotalAmount    { get; private set; }
    public int                       EntryCount     { get; private set; }
    public SvcCommissionPayoutStatus Status         { get; private set; }
    public DateTime?                 PaidAt         { get; private set; }
    public string?                   Notes          { get; private set; }

    public static SvcCommissionPayout Create(
        Guid tenantId, Guid professionalId, DateTime periodStart, DateTime periodEnd,
        decimal totalAmount, int entryCount, string? notes = null)
    {
        if (professionalId == Guid.Empty)  throw new DomainException("Professional is required.");
        if (periodStart > periodEnd)       throw new DomainException("PeriodStart must not be after PeriodEnd.");
        if (entryCount <= 0)               throw new DomainException("Cannot close a payout with no commission entries.");
        if (totalAmount <= 0m)             throw new DomainException("Payout total must be positive.");

        return new SvcCommissionPayout(tenantId)
        {
            ProfessionalId = professionalId,
            PeriodStart    = periodStart,
            PeriodEnd      = periodEnd,
            TotalAmount    = totalAmount,
            EntryCount     = entryCount,
            Status         = SvcCommissionPayoutStatus.Pending,
            Notes          = notes?.Trim(),
        };
    }

    public void MarkPaid(DateTime paidAt)
    {
        if (Status == SvcCommissionPayoutStatus.Paid)
            throw new DomainException("Payout is already paid.");

        Status = SvcCommissionPayoutStatus.Paid;
        PaidAt = paidAt;
        SetUpdatedAt();
    }
}

public enum SvcCommissionPayoutStatus
{
    Pending = 1,
    Paid    = 2,
}
