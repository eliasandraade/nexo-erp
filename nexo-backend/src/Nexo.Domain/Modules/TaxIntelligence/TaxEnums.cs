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

/// <summary>
/// Whether the excerpt reproduces the official text — and nothing more. Verified says the words match
/// the source (who compared and when are recorded); it is not a legal opinion about any rule. The
/// professional review of a rule's interpretation is <see cref="ProfessionalReviewStatus"/>.
/// </summary>
public enum TextVerificationStatus
{
    Unverified = 1,
    Verified = 2,
}

/// <summary>
/// A tax professional's review of how a rule version reads the law. Independent of the technical
/// status (a rule can be technically Active while its review is still Pending) and of the text
/// verification of its sources. Nothing in the catalogue starts Approved.
/// </summary>
public enum ProfessionalReviewStatus
{
    Pending = 1,
    Approved = 2,
    ChangesRequired = 3,
    Rejected = 4,
    /// <summary>A review was received and recorded but does not approve the reading (yet).</summary>
    ReviewReceived = 5,
    /// <summary>Approved within a stated scope and under stated caveats, which travel with every decision.</summary>
    ApprovedWithCaveats = 6,
}

/// <summary>
/// State of an assessment of the IBS/CBS ancillary obligations for a period and a scope (see
/// <see cref="TaxComplianceAssessment"/>). Only <see cref="Verified"/> may satisfy a rule that depends
/// on compliance: a declaration, an inconsistency being remediated (e.g. within the PNCT) or a
/// verified remediation are recorded as such and never read as "verified". What evidence proves
/// compliance is pending professional review.
/// </summary>
public enum ComplianceAssessmentStatus
{
    Unknown = 1,
    Declared = 2,
    Verified = 3,
    InconsistencyIdentified = 4,
    UnderRemediation = 5,
    RemediationVerified = 6,
    NonCompliant = 7,
    NotApplicable = 8,
}

/// <summary>
/// Whether engine V1 covers an operation's fact pattern. Anything but <see cref="Supported"/> stops
/// the calculation — it is never computed as zero.
/// </summary>
public enum TaxCoverageStatus
{
    Supported = 1,
    Unsupported = 2,
    RequiresClassification = 3,
    RequiresProfessionalReview = 4,
    PendingRegulation = 5,
}

/// <summary>How the supply happens in time. Engine V1 models only a single, immediate supply.</summary>
public enum TaxSupplyPattern
{
    SingleSupply = 1,
    ContinuedSupply = 2,
    AdvancePayment = 3,
    FutureDelivery = 4,
    PublicProcurement = 5,
    Other = 6,
}

/// <summary>When a requirement is checked: before the operation (scenario) or after it happened (execution).</summary>
public enum RequirementPhase
{
    ScenarioApproval = 1,
    ExecutionVerification = 2,
}

public enum TaxEvidenceType
{
    /// <summary>Proposal, order, contract, amendment or a draft of the document — supports a scenario.</summary>
    CommercialInstrument = 1,
    /// <summary>The authorised fiscal document of the operation — only after it happened.</summary>
    FiscalDocument = 2,
    FiscalDocumentEvent = 3,
    PaymentRecord = 4,
    Other = 5,
}

public enum TaxEvidenceStatus
{
    Submitted = 1,
    Accepted = 2,
    Rejected = 3,
}

public enum TaxExecutionOutcome
{
    Verified = 1,
    Diverged = 2,
}

/// <summary>Who may approve a scenario, by risk or materiality (maker-checker).</summary>
public enum ApprovalRequirement
{
    /// <summary>Whoever analysed alone may approve, recording a justification.</summary>
    SelfApprovalWithJustification = 1,
    /// <summary>Someone other than the approver must have taken part in the analysis.</summary>
    IdentifiedReviewer = 2,
    /// <summary>The approver must not have taken part in the analysis at all. No justification bypasses it.</summary>
    IndependentApprover = 3,
}

/// <summary>Where a credit stands before it can be appropriated. Ledger movements come only after.</summary>
public enum TaxCreditStage
{
    /// <summary>An estimate: no eligibility, appropriation or availability is implied.</summary>
    PotentialIdentified = 1,
    RequirementsUnderReview = 2,
    /// <summary>Requirements confirmed on record: the amount may be appropriated.</summary>
    ToBeAppropriated = 3,
    Discarded = 4,
}

/// <summary>Where a credit stands overall, derived from its stage and its ledger.</summary>
public enum TaxCreditLifecycle
{
    PotentialIdentified = 1,
    RequirementsUnderReview = 2,
    ToBeAppropriated = 3,
    Appropriated = 4,
    Used = 5,
    Blocked = 6,
    Reversed = 7,
    Expired = 8,
    Discarded = 9,
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
    /// <summary>Lower IBS/CBS as calculated/stated on the document — not, by itself, an economic saving.</summary>
    CalculatedTaxReduction = 7,
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
    /// <summary>
    /// The tax calculated (and stated on the document) goes down. Whether that becomes a definitive
    /// economic saving depends on the treatment of the period (e.g. 2026 compensation/waiver, art. 348)
    /// and on professional review — so it is never counted as a saving.
    /// </summary>
    CalculatedTaxReduction = 6,
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

/// <summary>
/// <see cref="Approved"/> is a scenario approval: the structure is acceptable before the operation.
/// Whether the operation then really happened as approved is <see cref="ExecutionVerified"/> or
/// <see cref="ExecutionDiverged"/> — a scenario approval never states that a future document exists.
/// </summary>
public enum TaxOpportunityStatus
{
    Identified = 1,
    InReview = 2,
    Approved = 3,
    Rejected = 4,
    Blocked = 5,
    ExecutionVerified = 6,
    ExecutionDiverged = 7,
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
