using System.Text.Json;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

// ── Profile ──────────────────────────────────────────────────────────────────

public sealed record TaxProfileVersionDto(
    Guid Id, int VersionNumber, DateOnly ValidFrom, TaxRegime Regime, string State, string CityIbgeCode,
    string PrimaryCnae, IReadOnlyList<string> SecondaryCnaes, IReadOnlyList<string> SpecialRegimes,
    IReadOnlyList<string> Benefits, string Reason, string CreatedByName, DateTime CreatedAt,
    TaxSimplesSituation SimplesSituation = TaxSimplesSituation.Unknown, bool? IbsCbsRegularRegimeOption = null);

/// <summary>Facts already known from the fiscal profile (issuer data), offered to prefill a version.</summary>
public sealed record TaxProfileSuggestionDto(TaxRegime Regime, string State, string CityIbgeCode);

public sealed record TaxProfileDto(
    TaxProfileVersionDto? Current, IReadOnlyList<TaxProfileVersionDto> Versions, TaxProfileSuggestionDto? Suggestion,
    IReadOnlyList<TaxComplianceAssessmentDto> ComplianceAssessments);

/// <summary>An assessment of the IBS/CBS ancillary obligations for a period and scope. Never "the company is compliant".</summary>
public sealed record TaxComplianceAssessmentDto(
    Guid Id, ComplianceAssessmentStatus Status, DateOnly PeriodFrom, DateOnly PeriodTo, string Scope, IReadOnlyList<string> ObligationsChecked,
    string? Evidence, string AssessedBy, DateTime AssessedAt, string? Notes, string? Limitations, bool PnctParticipation,
    string RecordedByName, DateTime CreatedAt, IReadOnlyList<TaxObligationResultDto>? Results = null, Guid? SupersedesAssessmentId = null);

public sealed record RecordComplianceAssessmentRequest(
    ComplianceAssessmentStatus Status, DateOnly PeriodFrom, DateOnly PeriodTo, string? Scope, IReadOnlyList<string>? ObligationsChecked,
    string? Evidence, string AssessedBy, DateTime? AssessedAt, string? Notes, string? Limitations, bool PnctParticipation,
    IReadOnlyList<TaxObligationResultRequest>? Obligations = null, Guid? SupersedesAssessmentId = null);

public sealed record CreateTaxProfileVersionRequest(
    DateOnly ValidFrom, TaxRegime Regime, string State, string CityIbgeCode, string PrimaryCnae,
    IReadOnlyList<string>? SecondaryCnaes, IReadOnlyList<string>? SpecialRegimes, IReadOnlyList<string>? Benefits, string Reason,
    TaxSimplesSituation? SimplesSituation = null, bool? IbsCbsRegularRegimeOption = null);

// ── Transactions and calculations ────────────────────────────────────────────

/// <summary>
/// One line of an operation. <c>GrossAmount</c> is the price before discounts. Leave
/// <c>ExcludedTaxes</c> null when the ICMS/ISS/PIS/Cofins inside the price are not known.
/// Classification enough for the V1 recorte: goods NCM + cClassTrib, services NBS + cClassTrib.
/// </summary>
public sealed record TaxLineRequest(
    TaxItemKind ItemKind, string Description, string? Ncm, decimal Quantity, decimal GrossAmount,
    decimal UnconditionalDiscount, decimal ConditionalDiscount, decimal Surcharges, decimal? ExcludedTaxes,
    string? SpecialTreatment, string? Nbs = null, string? CClassTrib = null);

/// <summary>
/// When things happen in the operation. V1 supports only <see cref="TaxSupplyPattern.SingleSupply"/>
/// with all dates equal to the operation date; anything else is refused, never silently assumed.
/// </summary>
public sealed record TaxOperationTiming(
    TaxSupplyPattern SupplyPattern, DateOnly? DocumentDate = null, DateOnly? SupplyDate = null, DateOnly? PaymentOrAdvanceDate = null,
    string? CompetencePeriod = null, DateOnly? ExecutionFrom = null, DateOnly? ExecutionTo = null);

/// <summary>
/// An operation described by its facts. For a Simples supplier, its situation (MEI, SIMEI, sub-limit…) and whether it
/// opted for the regular IBS/CBS regime are informed separately — an unknown answer is never read as either one.
/// </summary>
public sealed record TaxOperationRequest(
    DateOnly OperationDate, TaxDirection Direction, TaxRegime? CounterpartyRegime, string? CounterpartyState,
    string? Description, IReadOnlyList<TaxLineRequest> Lines, TaxOperationTiming? Timing = null,
    TaxSimplesSituation? CounterpartySimplesSituation = null, bool? CounterpartyRegularRegimeOption = null);

public sealed record TaxCalculationSummaryDto(
    Guid Id, DateOnly ReferenceDate, TaxTransactionSource Source, Guid? SourceId, TaxDirection Direction, string? Description,
    decimal TotalBase, decimal TotalTax, int OpenOpportunities, DateTime CreatedAt);

public sealed record TaxCalculationDto(
    Guid Id, Guid TransactionId, TaxTransactionSource Source, Guid? SourceId, TaxDirection Direction, string? Description,
    DateOnly ReferenceDate, Guid ProfileVersionId, int ProfileVersionNumber, string EngineVersion,
    string InputHash, string RuleSetHash, string OutputHash, decimal TotalBase, decimal TotalTax,
    bool Reused, DateTime CreatedAt, TaxEngineInput Input, TaxEngineOutput Output,
    IReadOnlyList<Guid> OpportunityIds, IReadOnlyList<string> Advisories, TaxCoverageDecision Coverage,
    TaxComplianceAssessmentDto? ComplianceAssessment, TaxComplianceAssessmentDto? CurrentComplianceAssessment,
    TaxGovernanceDto? Governance);

/// <summary>
/// Result of running a stored calculation again with exactly the engine version and rule versions it
/// used. <see cref="EngineAvailable"/> false means that engine is no longer registered: nothing was
/// re-run (the current engine is never used in its place) and <see cref="RecomputedOutputHash"/> is null.
/// </summary>
public sealed record TaxCalculationVerificationDto(
    Guid CalculationId, bool Reproducible, bool EngineAvailable, bool EngineVersionMatches, string StoredEngineVersion,
    string CurrentEngineVersion, string StoredOutputHash, string? RecomputedOutputHash, IReadOnlyList<string> Differences);

// ── Simulations ──────────────────────────────────────────────────────────────

public sealed record RateHypothesisRequest(TaxType Tax, decimal RatePercent, string Label);

public sealed record TaxScenarioRequest(
    string Name, TaxRegime? Regime, DateOnly? ReferenceDate, TaxDirection? Direction, TaxRegime? CounterpartyRegime,
    IReadOnlyList<TaxLineRequest>? Lines, bool ConvertConditionalDiscounts, IReadOnlyList<RateHypothesisRequest>? RateHypotheses,
    bool AssumeCreditable);

public sealed record CreateTaxSimulationRequest(
    string Name, TaxOperationRequest Baseline, IReadOnlyList<TaxScenarioRequest> Scenarios, string? Assumptions = null, string? Caveats = null);

public sealed record TaxSimulationSummaryDto(Guid Id, string Name, int Scenarios, DateTime CreatedAt, string? CreatedByName = null);

/// <summary>
/// A stored simulation: its scenarios, what the person took as given, its caveats and the governance it ran under.
/// A lower calculated tax in a scenario is a calculated reduction — never presented as a saving.
/// </summary>
public sealed record TaxSimulationDto(
    Guid Id, string Name, string EngineVersion, DateTime CreatedAt, IReadOnlyList<ScenarioResult> Scenarios,
    string? Assumptions = null, string? Caveats = null, string? CreatedByName = null, TaxGovernanceDto? Governance = null);

/// <summary>Effective cost of buying the same thing from different suppliers (procurement).</summary>
public sealed record SupplierOfferRequest(string Supplier, TaxRegime? SupplierRegime, IReadOnlyList<TaxLineRequest> Lines);

public sealed record CompareSuppliersRequest(
    DateOnly OperationDate, IReadOnlyList<SupplierOfferRequest> Offers, IReadOnlyList<RateHypothesisRequest>? RateHypotheses,
    bool AssumeCreditable);

public sealed record SupplierOfferResultDto(
    string Supplier, decimal Charged, decimal TaxTotal, decimal CreditEstimate, decimal EffectiveCost, bool Cheapest,
    IReadOnlyList<string> Hypotheses, IReadOnlyList<string> Warnings);

// ── Opportunities and decisions ──────────────────────────────────────────────

public sealed record TaxOpportunityRequirementDto(
    Guid Id, string Code, string Text, string? LegalSourceCode, bool? Met, string? Note, string? AnsweredByName, DateTime? AnsweredAt,
    RequirementPhase Phase, TaxEvidenceType? RequiredEvidence, bool HasRequiredEvidence);

/// <summary>
/// A piece of evidence. <see cref="HashKind"/> says what <see cref="ContentHash"/> fingerprints: the uploaded file's bytes
/// ("sha256-file") or only the reference ("reference"). Superseded and revoked versions stay listed (history).
/// </summary>
public sealed record TaxOpportunityEvidenceDto(
    Guid Id, TaxEvidenceType Type, Guid? RequirementId, string Description, string? DocumentReference, string ContentHash, string? Notes,
    TaxEvidenceStatus Status, string AddedByName, DateTime CreatedAt, string? ReviewedByName, DateTime? ReviewedAt, string? ReviewNotes,
    string HashKind = "reference", TaxStoredFileDto? File = null, int Version = 1, Guid? SupersedesEvidenceId = null,
    Guid? SupersededByEvidenceId = null, DateTime? RevokedAt = null, string? RevokedByName = null, string? RevocationReason = null);

/// <summary>The second milestone: whether the operation really happened as the scenario was approved.</summary>
public sealed record TaxExecutionVerificationDto(
    Guid Id, Guid DecisionId, TaxExecutionOutcome Outcome, string? FiscalDocumentReference, string? AuthorizationProtocol, string? Notes,
    string VerifiedByName, DateTime VerifiedAt, string DossierHash, JsonElement Dossier);

public sealed record TaxDecisionDto(
    Guid Id, Guid OpportunityId, string Strategy, TaxRiskLevel Risk, bool HighRiskAcknowledged, string EngineVersion,
    string ApprovedByName, DateTime ApprovedAt, string? SelfApprovalJustification, string DossierHash, JsonElement Dossier);

/// <summary>
/// An opportunity. When it is blocked as a prohibited structure, <see cref="EstimatedImpact"/> and
/// <see cref="Proposed"/> are null: no saving is offered for it. <see cref="ProfessionalReviewPending"/>
/// says whether any rule behind it still awaits a tax professional's review (technical activation is not
/// professional approval). <see cref="Governance"/> shows, for each of those rules, the internal pilot
/// approval beside the professional review, under the configured approval mode.
/// </summary>
public sealed record TaxOpportunityDto(
    Guid Id, TaxOpportunityType Type, TaxImpactNature ImpactNature, TaxOpportunityStatus Status, string Title, string Summary,
    string DetectorKey, string EngineVersion, Guid? SourceCalculationId, decimal? EstimatedImpact, TaxRiskLevel Risk, int Confidence,
    IReadOnlyList<string> LegalSourceCodes, string? Caveats, ProhibitedStructure? BlockedAs, string? BlockReason,
    string? RejectionReason, string? ReviewStartedByName, string? ReviewedByName, DateTime? ReviewedAt, DateTime CreatedAt,
    bool ProfessionalReviewPending, ApprovalRequirement RequiredApproval,
    IReadOnlyList<TaxOpportunityRequirementDto> Requirements, IReadOnlyList<TaxOpportunityEvidenceDto> Evidence,
    ScenarioResult? Current, ScenarioResult? Proposed, TaxDecisionDto? Decision, TaxExecutionVerificationDto? Execution,
    TaxGovernanceDto Governance, Guid? AssignedToId = null, string? AssignedToName = null, DateTime? AssignedAt = null,
    TaxMaterialityPolicyDto? Materiality = null);

public sealed record TaxOpportunitySummaryDto(
    Guid Id, TaxOpportunityType Type, TaxImpactNature ImpactNature, TaxOpportunityStatus Status, string Title,
    decimal? EstimatedImpact, TaxRiskLevel Risk, int Confidence, DateTime CreatedAt, string? AssignedToName = null,
    string DetectorKey = "", int RequirementsTotal = 0, int RequirementsMet = 0);

public sealed record AnswerRequirementRequest(bool? Met, string? Note);
public sealed record AddEvidenceRequest(
    string Description, string? DocumentReference, TaxEvidenceType Type = TaxEvidenceType.Other, Guid? RequirementId = null, string? Notes = null);
public sealed record ReviewEvidenceRequest(bool Accepted, string? Notes);
public sealed record VerifyExecutionRequest(
    TaxExecutionOutcome Outcome, string? FiscalDocumentReference, string? AuthorizationProtocol, string? Notes);
public sealed record RejectOpportunityRequest(string Reason);
public sealed record BlockOpportunityRequest(ProhibitedStructure Structure, string Reason);
/// <summary>
/// <c>SelfApprovalJustification</c> lets whoever analysed alone approve only where the policy allows
/// self-approval (low risk, below materiality); it never bypasses a required reviewer or an
/// independent approver. It is recorded in the dossier and the audit trail.
/// </summary>
public sealed record ApproveOpportunityRequest(bool AcknowledgeHighRisk, string? SelfApprovalJustification = null);

// ── Credits ──────────────────────────────────────────────────────────────────

/// <summary>Registers a <b>potential</b> credit: an estimate, never a validated, appropriated or usable credit.</summary>
public sealed record RegisterTaxCreditRequest(
    CreditTax Tax, string SourceDocumentType, string? SourceDocumentKey, string? SupplierDocument, DateOnly AcquiredOn,
    DateOnly? ExpiresOn, decimal OriginalAmount, decimal PotentialAmount, string PotentialBasis);

public enum TaxCreditMovement
{
    Appropriate = 1,
    Use = 2,
    Block = 3,
    Unblock = 4,
    Reverse = 5,
    Expire = 6,
    /// <summary>Potential → requirements under review.</summary>
    StartRequirementsReview = 7,
    /// <summary>Requirements under review → to be appropriated (amount and basis required).</summary>
    ConfirmToBeAppropriated = 8,
    /// <summary>Potential or under review → discarded (reason required).</summary>
    Discard = 9,
}

public sealed record TaxCreditMovementRequest(
    TaxCreditMovement Movement, decimal? Amount, string? Reason, CreditTax? ObligationTax, string? ObligationReference, DateOnly? OnDate,
    string? IdempotencyKey = null);

public sealed record TaxCreditEntryDto(Guid Id, TaxCreditEntryType Type, decimal Amount, string? ObligationReference, string Reason, DateOnly OccurredOn, string ActorName, DateTime CreatedAt);

public sealed record TaxCreditDto(
    Guid Id, CreditTax Tax, string SourceDocumentType, string? SourceDocumentKey, string? SupplierDocument,
    DateOnly AcquiredOn, DateOnly? ExpiresOn, decimal OriginalAmount, string PotentialBasis, TaxCreditBalance Balance,
    IReadOnlyList<TaxCreditEntryDto> Entries, DateTime CreatedAt, TaxCreditStage Stage, TaxCreditLifecycle Lifecycle,
    string? RecognitionBasis, string? DiscardReason);

public sealed record TaxCreditTotalsDto(
    CreditTax Tax, decimal Potential, decimal ToBeAppropriated, decimal Appropriated, decimal Used, decimal Blocked, decimal Available);

// ── Split payment ────────────────────────────────────────────────────────────

public sealed record SplitSettlementDto(
    Guid Id, decimal ActualAmount, SplitSettlementSource Source, string ExternalReference, DateTime SettledAt,
    Guid? ReversesSettlementId, bool Reversed, string? Reason);

/// <summary>
/// Expected vs recorded retention of one tax of a calculation. <see cref="AgeDays"/> counts from the operation date while
/// nothing (or not enough) was recorded. An expectation alone never means anything was retained or paid.
/// </summary>
public sealed record SplitExpectationDto(
    Guid Id, Guid CalculationId, TaxType Tax, SplitProcedure Procedure, decimal Expected, decimal Actual, decimal Difference,
    SplitReconciliationStatus Status, IReadOnlyList<SplitSettlementDto> Settlements, DateTime CreatedAt,
    DateOnly? OperationDate = null, int? AgeDays = null);

public sealed record CreateSplitExpectationsRequest(SplitProcedure Procedure);
public sealed record RecordSplitSettlementRequest(decimal ActualAmount, string ExternalReference, DateTime SettledAt);
public sealed record ReverseSplitSettlementRequest(string Reason);
public sealed record SplitProviderStatusDto(bool Configured, string Message);

// ── Catalogue ────────────────────────────────────────────────────────────────

public sealed record TaxLegalSourceDto(
    Guid Id, string Code, LegalSourceKind Kind, string Title, string Reference, string Url, string Excerpt,
    DateOnly? PublishedOn, TextVerificationStatus TextVerification, DateTime? VerifiedAt, string? VerifiedBy, string? Notes,
    string? VerificationRecordedBy = null, int RulesUsing = 0);

public sealed record TaxRuleVersionDto(
    Guid Id, int Version, TaxRuleStatus Status, DateOnly ValidFrom, DateOnly? ValidTo, int Priority,
    JsonElement Conditions, JsonElement Result, string ContentHash, string Origin, string CreatedBy, string? Notes,
    DateTime? ActivatedAt, IReadOnlyList<string> LegalSourceCodes, DateTime CreatedAt,
    ProfessionalReviewStatus ProfessionalReview, string? ProfessionalReviewedBy, DateTime? ProfessionalReviewedAt,
    string? ProfessionalReviewNotes, string? ProfessionalReference, string? ProfessionalReviewScope, DateOnly? ProfessionalReviewValidFrom,
    DateOnly? ProfessionalReviewValidTo, string? ProfessionalReviewAssumptions, string? ProfessionalReviewCaveats,
    TaxInternalApprovalDto InternalApproval, IReadOnlyList<string>? ProfessionalReviewRegimes = null, DateOnly? ProfessionalReviewedOn = null,
    string? ProfessionalReviewRecordedBy = null, string? ProfessionalReviewRecordedByRole = null, Guid? ProfessionalReviewDocumentId = null);

/// <summary>
/// The internal product/governance approval of a version for the controlled pilot — shown beside the
/// professional review, never in its place. It is not a professional tax opinion.
/// </summary>
public sealed record TaxInternalApprovalDto(
    InternalApprovalStatus Status, string? Policy, Guid? ApprovedById, string? ApprovedBy, string? ApprovedByRole, DateTime? ApprovedAt, string? Scope,
    DateOnly? ValidFrom, DateOnly? ValidTo, string? Caveats, string? DecisionReference, string? WithdrawalReason, DateTime? WithdrawnAt,
    string Nature);

public sealed record TaxRuleDto(Guid Id, string Key, string Name, string Description, IReadOnlyList<TaxRuleVersionDto> Versions);

public sealed record CreateLegalSourceRequest(
    string Code, LegalSourceKind Kind, string Title, string Reference, string Url, string Excerpt, DateOnly? PublishedOn, string? Notes);

public sealed record VerifyLegalSourceRequest(string VerifiedBy);

public sealed record CreateTaxRuleRequest(string Key, string Name, string Description);

/// <summary>Conditions and result in the engine's JSON vocabulary ("kind" discriminator).</summary>
public sealed record CreateTaxRuleVersionRequest(
    DateOnly ValidFrom, DateOnly? ValidTo, int Priority, JsonElement Conditions, JsonElement Result,
    IReadOnlyList<string> LegalSourceCodes, string? Notes);

public sealed record ChangeTaxRuleVersionStatusRequest(TaxRuleStatus Status);

/// <summary>A tax professional's review of a rule version's interpretation (separate from its technical status).</summary>
/// <summary>
/// A professional review as received. <c>Reviewer</c> and <c>ProfessionalReference</c> are the professional as informed; who records
/// it in the system is the authenticated user, kept apart. An approval states its scope, the establishment regimes it covers and
/// from when it holds; <c>SupportingDocumentId</c> points at the opinion stored privately.
/// </summary>
public sealed record RecordProfessionalReviewRequest(
    ProfessionalReviewStatus Status, string Reviewer, string? Notes, string? ProfessionalReference = null, string? Scope = null,
    DateOnly? ValidFrom = null, DateOnly? ValidTo = null, string? Assumptions = null, string? Caveats = null,
    IReadOnlyList<TaxRegime>? CoveredRegimes = null, DateOnly? ReviewedOn = null, Guid? SupportingDocumentId = null);

/// <summary>
/// Internal pilot approval of an active rule version. Who approves is never taken from the request: it is
/// the authenticated platform user (id, display name, capability) and the server's clock. Scope, validity,
/// caveats and where the decision is recorded are required; the policy is fixed by the server.
/// </summary>
public sealed record ApproveRuleForPilotRequest(
    string Scope, DateOnly ValidFrom, DateOnly? ValidTo, string Caveats, string DecisionReference);

/// <summary>Opens (or changes) the controlled pilot window. Who and when come from the authenticated request.</summary>
public sealed record ConfigurePilotScopeRequest(bool Enabled, DateOnly ValidFrom, DateOnly? ValidTo, string DecisionReference);

/// <summary>A tenant (all its stores, or only <see cref="StoreId"/>) to take part in the pilot.</summary>
public sealed record AddPilotParticipantRequest(Guid TenantId, Guid? StoreId);

public sealed record RemovePilotParticipantRequest(string Reason);

public sealed record TaxPilotParticipantDto(
    Guid Id, Guid TenantId, Guid? StoreId, bool Active, string AddedByName, DateTime AddedAt,
    DateTime? RemovedAt, string? RemovedByName, string? RemovalReason);

public sealed record TaxPilotScopeDto(
    Guid? Id, bool Enabled, DateOnly? ValidFrom, DateOnly? ValidTo, string? DecisionReference, string? ActivatedByName, DateTime? ActivatedAt,
    IReadOnlyList<TaxPilotParticipantDto> Participants);

public sealed record WithdrawPilotApprovalRequest(string Reason);

// ── Overview ─────────────────────────────────────────────────────────────────

/// <summary>
/// Dashboard figures from real data only. Savings, calculated-tax reductions, deferrals and cash-flow
/// effects are reported apart and never added together: a lower IBS/CBS on the document is not, by
/// itself, a definitive saving.
/// </summary>
public sealed record TaxOverviewDto(
    bool ProfileConfigured, DateOnly PeriodFrom, DateOnly PeriodTo, int Calculations, decimal TaxedBase, decimal EstimatedTax,
    decimal? EffectiveRatePercent, decimal CreditsPotential, decimal CreditsToBeAppropriated, decimal CreditsAvailable, decimal CreditsBlocked,
    int OpenOpportunities, decimal PotentialSavings, decimal PotentialCalculatedTaxReductions, decimal PotentialDeferrals, decimal PotentialCashFlow, decimal PotentialCredits,
    int OpenHighRisk, int BlockedOpportunities, int SplitDivergences, int ActiveRulesToday, int PendingRulesToday,
    decimal OutboundTax = 0, decimal InboundTax = 0, decimal CreditsAppropriated = 0, decimal CreditsUsed = 0,
    int ApprovedAwaitingExecution = 0, int UnverifiedRulesToday = 0, int RulesPendingProfessionalReview = 0,
    ComplianceAssessmentStatus? ComplianceInForce = null, DateOnly? ComplianceInForceTo = null,
    int CoverageRefusals = 0, IReadOnlyDictionary<string, int>? CoverageRefusalsByStatus = null, int GovernanceRefusals = 0);
