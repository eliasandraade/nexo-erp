using Nexo.Domain.Common;
using Nexo.Domain.Exceptions;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// What the split payment should retain for one tax of an official calculation (LC 214/2025, art. 31).
/// It follows from the calculated liability; nothing here talks to a payment provider. Immutable.
/// </summary>
public class SplitExpectation : StoreEntity
{
    private SplitExpectation() { }   // EF Core
    private SplitExpectation(Guid tenantId) : base(tenantId) { }

    public Guid CalculationId { get; private set; }
    public TaxType Tax { get; private set; }
    public SplitProcedure Procedure { get; private set; }
    public decimal ExpectedAmount { get; private set; }

    public static SplitExpectation Create(Guid tenantId, Guid calculationId, TaxType tax, SplitProcedure procedure, decimal expectedAmount)
    {
        if (expectedAmount < 0) throw new DomainException("Retenção esperada negativa.");
        return new SplitExpectation(tenantId) { CalculationId = calculationId, Tax = tax, Procedure = procedure, ExpectedAmount = expectedAmount };
    }
}

/// <summary>
/// What was actually retained for an expectation, as reported manually or by a provider adapter.
/// Append-only: a wrong entry is cancelled by a reversal — another row with the opposite amount
/// that points at it — and the right amount is then recorded again. Nothing is edited or deleted.
/// </summary>
public class SplitSettlement : StoreEntity
{
    private SplitSettlement() { }   // EF Core
    private SplitSettlement(Guid tenantId) : base(tenantId) { }

    public Guid ExpectationId { get; private set; }
    public decimal ActualAmount { get; private set; }
    public SplitSettlementSource Source { get; private set; }
    /// <summary>Reference of the payment transaction or report line; unique per expectation.</summary>
    public string ExternalReference { get; private set; } = string.Empty;
    public DateTime SettledAt { get; private set; }
    public Guid RecordedById { get; private set; }
    /// <summary>Set on a reversal: the settlement it cancels (one reversal per settlement).</summary>
    public Guid? ReversesSettlementId { get; private set; }
    public string? Reason { get; private set; }

    public bool IsReversal => ReversesSettlementId is not null;

    public static SplitSettlement Record(
        Guid tenantId, Guid expectationId, decimal actualAmount, SplitSettlementSource source, string externalReference,
        DateTime settledAtUtc, Guid recordedById)
    {
        if (actualAmount < 0 || decimal.Round(actualAmount, 2) != actualAmount)
            throw new DomainException("Informe o valor retido em centavos, sem sinal.");
        if (string.IsNullOrWhiteSpace(externalReference)) throw new DomainException("Informe a referência da liquidação.");
        return new SplitSettlement(tenantId)
        {
            ExpectationId = expectationId,
            ActualAmount = actualAmount,
            Source = source,
            ExternalReference = externalReference.Trim(),
            SettledAt = settledAtUtc,
            RecordedById = recordedById,
        };
    }

    /// <summary>Cancels a wrong settlement: same expectation, opposite amount, the reason recorded.</summary>
    public static SplitSettlement ReversalOf(SplitSettlement original, string reason, DateTime atUtc, Guid recordedById)
    {
        if (original.IsReversal) throw new DomainException("Um estorno não pode ser estornado.", "tax_split_reversal");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Informe o motivo do estorno.");
        return new SplitSettlement(original.TenantId)
        {
            ExpectationId = original.ExpectationId,
            ActualAmount = -original.ActualAmount,
            Source = original.Source,
            ExternalReference = $"estorno:{original.ExternalReference}",
            SettledAt = atUtc,
            RecordedById = recordedById,
            ReversesSettlementId = original.Id,
            Reason = reason.Trim(),
        };
    }
}

public enum SplitReconciliationStatus
{
    Pending = 1,        // nothing retained yet
    Matched = 2,
    Excess = 3,         // retained more than expected — possible refund
    Insufficient = 4,   // retained less — the difference remains due
}

/// <summary>Expected vs actual split for one expectation.</summary>
public sealed record SplitReconciliation(decimal Expected, decimal Actual, decimal Difference, SplitReconciliationStatus Status)
{
    /// <summary>Tolerance of one cent for rounding between engine and provider.</summary>
    public const decimal Tolerance = 0.01m;

    /// <param name="amounts">Every settlement amount, reversals (negative) included.</param>
    /// <param name="liveSettlements">Settlements neither reversed nor reversals — none means still pending.</param>
    public static SplitReconciliation Of(decimal expected, IEnumerable<decimal> amounts, int liveSettlements)
    {
        var actual = amounts.Sum();
        var diff = actual - expected;
        return new SplitReconciliation(expected, actual, diff, StatusOf(liveSettlements, diff));
    }

    private static SplitReconciliationStatus StatusOf(int settlements, decimal diff)
    {
        if (settlements == 0) return SplitReconciliationStatus.Pending;
        if (Math.Abs(diff) <= Tolerance) return SplitReconciliationStatus.Matched;
        return diff > 0 ? SplitReconciliationStatus.Excess : SplitReconciliationStatus.Insufficient;
    }
}
