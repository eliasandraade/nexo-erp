using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.Service;

/// <summary>
/// Append-only record that a professional earned (or gave back) commission on one concrete event.
/// Store-scoped.
///
/// Never deleted. Two fields change after creation, each through a conditional UPDATE in the
/// repository so concurrent requests cannot both win:
///   - <see cref="PayoutId"/>, stamped when the entry is closed into a payout (… WHERE payout_id IS NULL);
///   - <see cref="ReversedAt"/>, stamped when the money behind an earning is reversed (a voided
///     payment un-settles the order). A reversed earning no longer counts. If it had already been
///     closed into a payout, a <see cref="SvcCommissionEntryKind.Reversal"/> entry with the negative
///     amount is also written, so the next payout discounts what was already paid out.
///
/// Uniqueness (database): one ACTIVE earning per (tenant, <see cref="Source"/>, <see cref="SourceId"/>)
/// — replaying a trigger cannot double-count, yet a source that was reversed can earn again when
/// it is settled again — and at most one reversal per earning.
/// </summary>
public class SvcCommissionEntry : StoreEntity
{
    private SvcCommissionEntry() { }
    private SvcCommissionEntry(Guid tenantId) : base(tenantId) { }

    public Guid                   ProfessionalId    { get; private set; }
    public Guid                   CustomerId        { get; private set; }
    public SvcCommissionEntryKind Kind              { get; private set; }
    public SvcCommissionSource    Source            { get; private set; }
    /// <summary>Id of the order item / appointment / package usage that produced this entry.</summary>
    public Guid                   SourceId          { get; private set; }
    /// <summary>The amount commission was computed on, snapshotted at recognition (negative on a reversal).</summary>
    public decimal                BaseAmount        { get; private set; }
    /// <summary>Rate snapshotted at recognition — later rate changes never rewrite it.</summary>
    public decimal                CommissionPercent { get; private set; }
    /// <summary>Positive for an earning, negative for a reversal.</summary>
    public decimal                CommissionAmount  { get; private set; }
    public DateTime               RecognizedAt      { get; private set; }
    public Guid?                  PayoutId          { get; private set; }
    /// <summary>Set on an earning whose underlying money was reversed; it then no longer counts.</summary>
    public DateTime?              ReversedAt        { get; private set; }
    /// <summary>On a reversal entry: the earning it discounts.</summary>
    public Guid?                  ReversalOfEntryId { get; private set; }
    public string?                Notes             { get; private set; }

    public bool IsSettled => PayoutId is not null;
    public bool IsActiveEarning => Kind == SvcCommissionEntryKind.Earning && ReversedAt is null;

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
            Kind              = SvcCommissionEntryKind.Earning,
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

    /// <summary>
    /// The negative counterpart of an earning that was already closed into a payout: it stays open
    /// and is discounted by the professional's next payout. Exact negation — no re-rounding.
    /// </summary>
    public static SvcCommissionEntry CreateReversalOf(SvcCommissionEntry earning, DateTime recognizedAt, string? notes)
    {
        if (earning.Kind != SvcCommissionEntryKind.Earning)
            throw new DomainException("Only an earning can be reversed.");

        return new SvcCommissionEntry(earning.TenantId)
        {
            ProfessionalId    = earning.ProfessionalId,
            CustomerId        = earning.CustomerId,
            Kind              = SvcCommissionEntryKind.Reversal,
            Source            = earning.Source,
            SourceId          = earning.SourceId,
            BaseAmount        = -earning.BaseAmount,
            CommissionPercent = earning.CommissionPercent,
            CommissionAmount  = -earning.CommissionAmount,
            RecognizedAt      = recognizedAt,
            ReversalOfEntryId = earning.Id,
            Notes             = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
    }
}

public enum SvcCommissionEntryKind
{
    Earning  = 1,
    Reversal = 2,
}
