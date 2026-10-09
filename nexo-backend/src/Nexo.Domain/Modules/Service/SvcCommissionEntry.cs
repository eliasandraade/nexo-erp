using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Append-only record that a professional earned commission on one concrete event. Store-scoped.
///
/// Never edited and never deleted: a correction is a new entry, the same discipline
/// <see cref="SvcPackageUsage"/> and <see cref="SvcRecordEntry"/> already follow. The only field
/// that changes after creation is <see cref="PayoutId"/>, stamped when the entry is closed into a
/// payout. That stamp is written by a conditional UPDATE (… WHERE payout_id IS NULL) in the
/// repository, which is what stops two concurrent closings from capturing the same entry.
///
/// (<see cref="Source"/>, <see cref="SourceId"/>) is unique per tenant: each order item,
/// appointment or package usage yields at most one entry, so replaying a trigger cannot
/// double-count.
/// </summary>
public class SvcCommissionEntry : StoreEntity
{
    private SvcCommissionEntry() { }
    private SvcCommissionEntry(Guid tenantId) : base(tenantId) { }

    public Guid                ProfessionalId    { get; private set; }
    public Guid                CustomerId        { get; private set; }
    public SvcCommissionSource Source            { get; private set; }
    /// <summary>Id of the order item / appointment / package usage that produced this entry.</summary>
    public Guid                SourceId          { get; private set; }
    /// <summary>The amount commission was computed on, snapshotted at recognition.</summary>
    public decimal             BaseAmount        { get; private set; }
    /// <summary>Rate snapshotted at recognition — later rate changes never rewrite it.</summary>
    public decimal             CommissionPercent { get; private set; }
    public decimal             CommissionAmount  { get; private set; }
    public DateTime            RecognizedAt      { get; private set; }
    public Guid?               PayoutId          { get; private set; }
    public string?             Notes             { get; private set; }

    public bool IsSettled => PayoutId is not null;

    public static SvcCommissionEntry Create(
        Guid tenantId, Guid professionalId, Guid customerId,
        SvcCommissionSource source, Guid sourceId,
        decimal baseAmount, decimal commissionPercent, DateTime recognizedAt, string? notes = null)
    {
        if (professionalId == Guid.Empty) throw new DomainException("Professional is required.");
        if (customerId == Guid.Empty)     throw new DomainException("Customer is required.");
        if (sourceId == Guid.Empty)       throw new DomainException("SourceId is required.");
        if (!Enum.IsDefined(source))      throw new DomainException("Invalid commission source.");
        if (baseAmount < 0m)              throw new DomainException("Base amount cannot be negative.");
        if (commissionPercent <= 0m || commissionPercent > 100m)
            throw new DomainException("Commission percent must be between 0 (exclusive) and 100.");

        return new SvcCommissionEntry(tenantId)
        {
            ProfessionalId    = professionalId,
            CustomerId        = customerId,
            Source            = source,
            SourceId          = sourceId,
            BaseAmount        = baseAmount,
            CommissionPercent = commissionPercent,
            // Rounded at recognition so the stored amount is the one that gets paid — recomputing
            // it later from percent × base could drift by a cent per entry.
            CommissionAmount  = Math.Round(baseAmount * commissionPercent / 100m, 2, MidpointRounding.AwayFromZero),
            RecognizedAt      = recognizedAt,
            Notes             = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }
}
