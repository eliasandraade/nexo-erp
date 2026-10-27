namespace Nexo.Domain.Modules.TaxIntelligence;

// Enums of the Tax Intelligence domain. They are persisted by name (HasConversion<string>), so
// renaming a member is a data migration — add new members instead.

/// <summary>The tax a component or a credit refers to. IBS is split by sphere because the 2026 test
/// rate is state-only (LC 214/2025, art. 343) and from 2027 the state and municipal rates differ.</summary>
public enum TaxType
{
    Cbs = 1,
    IbsState = 2,
    IbsMunicipal = 3,
}

/// <summary>
/// Lifecycle of a rule version. Only <see cref="Active"/> versions take part in official
/// calculations. The other states keep the mechanism ready without inventing behaviour:
/// a rule awaiting regulation or with an unverified source is stored, shown and never applied.
/// </summary>
public enum TaxRuleStatus
{
    Draft = 1,
    PendingRegulation = 2,
    Unverified = 3,
    Active = 4,
    Superseded = 5,
    Revoked = 6,
}

public enum LegalSourceKind
{
    Constitution = 1,
    ConstitutionalAmendment = 2,
    ComplementaryLaw = 3,
    OrdinaryLaw = 4,
    Decree = 5,
    FederalRevenueNormative = 6,   // Receita Federal (IN, parecer normativo)
    IbsManagementCommittee = 7,    // Comitê Gestor do IBS
    StateLegislation = 8,
    MunicipalLegislation = 9,
    Other = 10,
}

/// <summary>Verified = the excerpt was checked against the official text (who and when are recorded).</summary>
public enum LegalSourceVerification
{
    Unverified = 1,
    Verified = 2,
}

/// <summary>Where the facts of a tax transaction came from. The engine never reads the source.</summary>
public enum TaxTransactionSource
{
    Manual = 1,
    Sale = 2,
    Purchase = 3,
    Service = 4,
    FinancialEntry = 5,
    FiscalDocument = 6,
    External = 7,
}

/// <summary>Outbound: the establishment supplies. Inbound: it acquires (the counterparty supplies).</summary>
public enum TaxDirection
{
    Outbound = 1,
    Inbound = 2,
}

public enum TaxItemKind
{
    Good = 1,
    Service = 2,
}

public enum TaxOpportunityType
{
    TaxSaving = 1,
    CreditOptimization = 2,
    TaxDeferral = 3,
    CashFlowOptimization = 4,
    RefundOpportunity = 5,
    RiskReduction = 6,
}

/// <summary>
/// What the impact of an opportunity is. Kept apart from the type so a deferral is never added
/// to savings: only <see cref="DefinitiveSaving"/> lowers the tax burden for good.
/// </summary>
public enum TaxImpactNature
{
    DefinitiveSaving = 1,
    Deferral = 2,
    CashFlow = 3,
    Credit = 4,
    RiskAvoided = 5,
}

/// <summary>
/// LOW: direct application of a well-founded rule. MEDIUM: defensible, depends on documentation,
/// interpretation or extra requirements. HIGH: controversial or needs specialised legal review —
/// approval must acknowledge the risk explicitly.
/// </summary>
public enum TaxRiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum TaxOpportunityStatus
{
    Identified = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    Blocked = 5,
}

/// <summary>
/// Structures the opportunity engine must never recommend. They are guardrails — a blocked
/// opportunity shows no saving and cannot be approved — not universal legal qualifications.
/// </summary>
public enum ProhibitedStructure
{
    Simulation = 1,
    ArtificialTransaction = 2,
    RevenueOmission = 3,
    FalseExpense = 4,
    FalseSupplier = 5,
    UnderInvoicing = 6,
    DocumentMismatch = 7,
    PersonalExpenseAsBusiness = 8,
}

/// <summary>IBS and CBS credits are kept apart: one never pays the other (LC 214/2025, art. 47, §1º, I).</summary>
public enum CreditTax
{
    Cbs = 1,
    Ibs = 2,
}

/// <summary>Movements of a credit. The ledger is append-only: balances are sums of entries.</summary>
public enum TaxCreditEntryType
{
    Recognized = 1,     // the eligible amount was identified
    Appropriated = 2,   // taken into the books
    Used = 3,           // extinguished a debt of the same tax
    Blocked = 4,
    Unblocked = 5,
    Expired = 6,
    Reversed = 7,       // reverses an appropriation
}

/// <summary>Split payment procedures (LC 214/2025, art. 31, §1º): standard (art. 32) and simplified (art. 33).</summary>
public enum SplitProcedure
{
    Standard = 1,
    Simplified = 2,
}

public enum SplitSettlementSource
{
    Manual = 1,
    Provider = 2,
}
