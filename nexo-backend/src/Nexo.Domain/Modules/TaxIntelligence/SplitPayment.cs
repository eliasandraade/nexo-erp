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
/// Append-only: a correction is another settlement with its own reference.
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

    public static SplitReconciliation Of(decimal expected, IEnumerable<decimal> settlements)
    {
        var list = settlements.ToList();
        var actual = list.Sum();
        var diff = actual - expected;
        return new SplitReconciliation(expected, actual, diff, StatusOf(list.Count, diff));
    }

    private static SplitReconciliationStatus StatusOf(int settlements, decimal diff)
    {
        if (settlements == 0) return SplitReconciliationStatus.Pending;
        if (Math.Abs(diff) <= Tolerance) return SplitReconciliationStatus.Matched;
        return diff > 0 ? SplitReconciliationStatus.Excess : SplitReconciliationStatus.Insufficient;
    }
}
