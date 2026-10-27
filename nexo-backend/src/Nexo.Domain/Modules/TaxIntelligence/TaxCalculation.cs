using Nexo.Domain.Common;

namespace Nexo.Domain.Modules.TaxIntelligence;

/// <summary>
/// The official result of running the engine on a transaction. Immutable and reproducible: it keeps
/// the canonical input, the engine version, the profile version and every rule version it looked at
/// (with their content hashes), so <c>verify</c> can run it again and must get the same
/// <see cref="OutputHash"/>. <see cref="CalculationKey"/> makes recalculation idempotent: the same
/// input with the same rules and engine returns the existing calculation instead of a second one.
/// </summary>
public class TaxCalculation : StoreEntity
{
    private readonly List<TaxComponent> _components = [];
    private readonly List<TaxRuleExecution> _executions = [];

    private TaxCalculation() { }   // EF Core
    private TaxCalculation(Guid tenantId) : base(tenantId) { }

    public Guid TransactionId { get; private set; }
    public Guid ProfileVersionId { get; private set; }
    public DateOnly ReferenceDate { get; private set; }
    public string EngineVersion { get; private set; } = string.Empty;
    public string InputJson { get; private set; } = "{}";
    public string InputHash { get; private set; } = string.Empty;
    public string RuleSetHash { get; private set; } = string.Empty;
    public string CalculationKey { get; private set; } = string.Empty;
    public string OutputJson { get; private set; } = "{}";
    public string OutputHash { get; private set; } = string.Empty;
    public decimal TotalBase { get; private set; }
    public decimal TotalTax { get; private set; }
    public Guid CreatedById { get; private set; }

    public IReadOnlyCollection<TaxComponent> Components => _components.AsReadOnly();
    public IReadOnlyCollection<TaxRuleExecution> Executions => _executions.AsReadOnly();

    public static TaxCalculation Create(
        Guid tenantId, Guid transactionId, Guid profileVersionId, DateOnly referenceDate, string engineVersion,
        string inputJson, string inputHash, string ruleSetHash, string calculationKey, string outputJson,
        string outputHash, decimal totalBase, decimal totalTax, Guid createdById)
        => new(tenantId)
        {
            TransactionId = transactionId,
            ProfileVersionId = profileVersionId,
            ReferenceDate = referenceDate,
            EngineVersion = engineVersion,
            InputJson = inputJson,
            InputHash = inputHash,
            RuleSetHash = ruleSetHash,
            CalculationKey = calculationKey,
            OutputJson = outputJson,
            OutputHash = outputHash,
            TotalBase = totalBase,
            TotalTax = totalTax,
            CreatedById = createdById,
        };

    /// <summary>Ancillary-obligation assessment in force when the calculation ran (null = none covered the date).</summary>
    public Guid? ComplianceAssessmentId { get; private set; }

    public void UsedComplianceAssessment(Guid? assessmentId) => ComplianceAssessmentId = assessmentId;

    public void AddComponent(int lineIndex, TaxType tax, decimal baseAmount, decimal ratePercent, decimal amount, Guid ruleVersionId)
        => _components.Add(new TaxComponent(TenantId, Id, lineIndex, tax, baseAmount, ratePercent, amount, ruleVersionId));

    public void AddExecution(
        Guid ruleVersionId, string ruleKey, int ruleVersion, string contentHash, int? lineIndex, bool matched,
        string conditionsJson, string outputJson, string legalSourceCodes)
        => _executions.Add(new TaxRuleExecution(TenantId, Id, ruleVersionId, ruleKey, ruleVersion, contentHash, lineIndex,
            matched, conditionsJson, outputJson, legalSourceCodes));
}

/// <summary>One tax on one line of a calculation (base × rate = amount).</summary>
public class TaxComponent : StoreEntity
{
    private TaxComponent() { }   // EF Core

    internal TaxComponent(Guid tenantId, Guid calculationId, int lineIndex, TaxType tax, decimal baseAmount,
        decimal ratePercent, decimal amount, Guid ruleVersionId) : base(tenantId)
    {
        CalculationId = calculationId;
        LineIndex = lineIndex;
        Tax = tax;
        BaseAmount = baseAmount;
        RatePercent = ratePercent;
        Amount = amount;
        RuleVersionId = ruleVersionId;
    }

    public Guid CalculationId { get; private set; }
    public int LineIndex { get; private set; }
    public TaxType Tax { get; private set; }
    public decimal BaseAmount { get; private set; }
    public decimal RatePercent { get; private set; }
    public decimal Amount { get; private set; }
    public Guid RuleVersionId { get; private set; }
}

/// <summary>
/// The trace of one rule version looked at by a calculation: which conditions passed or failed with
/// which values, what came out, and which provisions back it.
/// </summary>
public class TaxRuleExecution : StoreEntity
{
    private TaxRuleExecution() { }   // EF Core

    internal TaxRuleExecution(Guid tenantId, Guid calculationId, Guid ruleVersionId, string ruleKey, int ruleVersion,
        string contentHash, int? lineIndex, bool matched, string conditionsJson, string outputJson, string legalSourceCodes)
        : base(tenantId)
    {
        CalculationId = calculationId;
        RuleVersionId = ruleVersionId;
        RuleKey = ruleKey;
        RuleVersion = ruleVersion;
        ContentHash = contentHash;
        LineIndex = lineIndex;
        Matched = matched;
        ConditionsJson = conditionsJson;
        OutputJson = outputJson;
        LegalSourceCodes = legalSourceCodes;
    }

    public Guid CalculationId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public string RuleKey { get; private set; } = string.Empty;
    public int RuleVersion { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    /// <summary>Line the rule ran on; null for rules about the whole operation.</summary>
    public int? LineIndex { get; private set; }
    public bool Matched { get; private set; }
    public string ConditionsJson { get; private set; } = "[]";
    public string OutputJson { get; private set; } = "{}";
    /// <summary>Codes of the legal sources, comma-separated, as they were when the rule ran.</summary>
    public string LegalSourceCodes { get; private set; } = string.Empty;
}
