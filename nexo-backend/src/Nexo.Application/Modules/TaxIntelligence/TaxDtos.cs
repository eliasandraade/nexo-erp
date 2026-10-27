using System.Text.Json;
using Nexo.Application.Modules.TaxIntelligence.Engine;
using Nexo.Domain.Enums;
using Nexo.Domain.Modules.TaxIntelligence;

namespace Nexo.Application.Modules.TaxIntelligence;

// ── Profile ──────────────────────────────────────────────────────────────────

public sealed record TaxProfileVersionDto(
    Guid Id, int VersionNumber, DateOnly ValidFrom, TaxRegime Regime, string State, string CityIbgeCode,
    string PrimaryCnae, IReadOnlyList<string> SecondaryCnaes, IReadOnlyList<string> SpecialRegimes,
    IReadOnlyList<string> Benefits, bool? AccessoryObligationsCompliant, string Reason, string CreatedByName, DateTime CreatedAt);

/// <summary>Facts already known from the fiscal profile (issuer data), offered to prefill a version.</summary>
public sealed record TaxProfileSuggestionDto(TaxRegime Regime, string State, string CityIbgeCode);

public sealed record TaxProfileDto(
    TaxProfileVersionDto? Current, IReadOnlyList<TaxProfileVersionDto> Versions, TaxProfileSuggestionDto? Suggestion);

public sealed record CreateTaxProfileVersionRequest(
    DateOnly ValidFrom, TaxRegime Regime, string State, string CityIbgeCode, string PrimaryCnae,
    IReadOnlyList<string>? SecondaryCnaes, IReadOnlyList<string>? SpecialRegimes, IReadOnlyList<string>? Benefits,
    bool? AccessoryObligationsCompliant, string Reason);

// ── Transactions and calculations ────────────────────────────────────────────

/// <summary>
/// One line of an operation. <c>GrossAmount</c> is the price before discounts. Leave
/// <c>ExcludedTaxes</c> null when the ICMS/ISS/PIS/Cofins inside the price are not known.
/// </summary>
public sealed record TaxLineRequest(
    TaxItemKind ItemKind, string Description, string? Ncm, decimal Quantity, decimal GrossAmount,
    decimal UnconditionalDiscount, decimal ConditionalDiscount, decimal Surcharges, decimal? ExcludedTaxes,
    string? SpecialTreatment);

public sealed record TaxOperationRequest(
    DateOnly OperationDate, TaxDirection Direction, TaxRegime? CounterpartyRegime, string? CounterpartyState,
    string? Description, IReadOnlyList<TaxLineRequest> Lines);

public sealed record TaxCalculationSummaryDto(
    Guid Id, DateOnly ReferenceDate, TaxTransactionSource Source, Guid? SourceId, TaxDirection Direction, string? Description,
    decimal TotalBase, decimal TotalTax, int OpenOpportunities, DateTime CreatedAt);

public sealed record TaxCalculationDto(
    Guid Id, Guid TransactionId, TaxTransactionSource Source, Guid? SourceId, TaxDirection Direction, string? Description,
    DateOnly ReferenceDate, Guid ProfileVersionId, int ProfileVersionNumber, string EngineVersion,
    string InputHash, string RuleSetHash, string OutputHash, decimal TotalBase, decimal TotalTax,
    bool Reused, DateTime CreatedAt, TaxEngineInput Input, TaxEngineOutput Output,
    IReadOnlyList<Guid> OpportunityIds);

/// <summary>Result of running a stored calculation again with exactly the rule versions it used.</summary>
public sealed record TaxCalculationVerificationDto(
    Guid CalculationId, bool Reproducible, bool EngineVersionMatches, string StoredEngineVersion, string CurrentEngineVersion,
    string StoredOutputHash, string RecomputedOutputHash, IReadOnlyList<string> Differences);

// ── Simulations ──────────────────────────────────────────────────────────────

public sealed record RateHypothesisRequest(TaxType Tax, decimal RatePercent, string Label);

public sealed record TaxScenarioRequest(
    string Name, TaxRegime? Regime, DateOnly? ReferenceDate, TaxDirection? Direction, TaxRegime? CounterpartyRegime,
    IReadOnlyList<TaxLineRequest>? Lines, bool ConvertConditionalDiscounts, IReadOnlyList<RateHypothesisRequest>? RateHypotheses,
    bool AssumeCreditable);

public sealed record CreateTaxSimulationRequest(string Name, TaxOperationRequest Baseline, IReadOnlyList<TaxScenarioRequest> Scenarios);

public sealed record TaxSimulationSummaryDto(Guid Id, string Name, int Scenarios, DateTime CreatedAt);

public sealed record TaxSimulationDto(Guid Id, string Name, string EngineVersion, DateTime CreatedAt, IReadOnlyList<ScenarioResult> Scenarios);

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
    Guid Id, string Code, string Text, string? LegalSourceCode, bool? Met, string? Note, string? AnsweredByName, DateTime? AnsweredAt);

public sealed record TaxOpportunityEvidenceDto(Guid Id, string Description, string? DocumentReference, string ContentHash, string AddedByName, DateTime CreatedAt);

public sealed record TaxDecisionDto(
    Guid Id, Guid OpportunityId, string Strategy, TaxRiskLevel Risk, bool HighRiskAcknowledged, string EngineVersion,
    string ApprovedByName, DateTime ApprovedAt, string DossierHash, JsonElement Dossier);

/// <summary>
/// An opportunity. When it is blocked as a prohibited structure, <see cref="EstimatedImpact"/> and
/// <see cref="Proposed"/> are null: no saving is offered for it.
/// </summary>
public sealed record TaxOpportunityDto(
    Guid Id, TaxOpportunityType Type, TaxImpactNature ImpactNature, TaxOpportunityStatus Status, string Title, string Summary,
    string DetectorKey, Guid? SourceCalculationId, decimal? EstimatedImpact, TaxRiskLevel Risk, int Confidence,
    IReadOnlyList<string> LegalSourceCodes, string? Caveats, ProhibitedStructure? BlockedAs, string? BlockReason,
    string? RejectionReason, string? ReviewedByName, DateTime? ReviewedAt, DateTime CreatedAt,
    IReadOnlyList<TaxOpportunityRequirementDto> Requirements, IReadOnlyList<TaxOpportunityEvidenceDto> Evidence,
    ScenarioResult? Current, ScenarioResult? Proposed, TaxDecisionDto? Decision);

public sealed record TaxOpportunitySummaryDto(
    Guid Id, TaxOpportunityType Type, TaxImpactNature ImpactNature, TaxOpportunityStatus Status, string Title,
    decimal? EstimatedImpact, TaxRiskLevel Risk, int Confidence, DateTime CreatedAt);

public sealed record AnswerRequirementRequest(bool? Met, string? Note);
public sealed record AddEvidenceRequest(string Description, string? DocumentReference);
public sealed record RejectOpportunityRequest(string Reason);
public sealed record BlockOpportunityRequest(ProhibitedStructure Structure, string Reason);
public sealed record ApproveOpportunityRequest(bool AcknowledgeHighRisk);

// ── Credits ──────────────────────────────────────────────────────────────────

public sealed record RegisterTaxCreditRequest(
    CreditTax Tax, string SourceDocumentType, string? SourceDocumentKey, string? SupplierDocument, DateOnly AcquiredOn,
    DateOnly? ExpiresOn, decimal OriginalAmount, decimal EligibleAmount, string EligibilityBasis);

public enum TaxCreditMovement
{
    Appropriate = 1,
    Use = 2,
    Block = 3,
    Unblock = 4,
    Reverse = 5,
    Expire = 6,
}

public sealed record TaxCreditMovementRequest(
    TaxCreditMovement Movement, decimal? Amount, string? Reason, CreditTax? ObligationTax, string? ObligationReference, DateOnly? OnDate);

public sealed record TaxCreditEntryDto(Guid Id, TaxCreditEntryType Type, decimal Amount, string? ObligationReference, string Reason, DateOnly OccurredOn, string ActorName, DateTime CreatedAt);

public sealed record TaxCreditDto(
    Guid Id, CreditTax Tax, string SourceDocumentType, string? SourceDocumentKey, string? SupplierDocument,
    DateOnly AcquiredOn, DateOnly? ExpiresOn, decimal OriginalAmount, string EligibilityBasis, TaxCreditBalance Balance,
    IReadOnlyList<TaxCreditEntryDto> Entries, DateTime CreatedAt);

public sealed record TaxCreditTotalsDto(CreditTax Tax, decimal Eligible, decimal Appropriated, decimal Used, decimal Blocked, decimal Available, decimal NotAppropriated);

// ── Split payment ────────────────────────────────────────────────────────────

public sealed record SplitSettlementDto(
    Guid Id, decimal ActualAmount, SplitSettlementSource Source, string ExternalReference, DateTime SettledAt,
    Guid? ReversesSettlementId, bool Reversed, string? Reason);

public sealed record SplitExpectationDto(
    Guid Id, Guid CalculationId, TaxType Tax, SplitProcedure Procedure, decimal Expected, decimal Actual, decimal Difference,
    SplitReconciliationStatus Status, IReadOnlyList<SplitSettlementDto> Settlements, DateTime CreatedAt);

public sealed record CreateSplitExpectationsRequest(SplitProcedure Procedure);
public sealed record RecordSplitSettlementRequest(decimal ActualAmount, string ExternalReference, DateTime SettledAt);
public sealed record ReverseSplitSettlementRequest(string Reason);
public sealed record SplitProviderStatusDto(bool Configured, string Message);

// ── Catalogue ────────────────────────────────────────────────────────────────

public sealed record TaxLegalSourceDto(
    Guid Id, string Code, LegalSourceKind Kind, string Title, string Reference, string Url, string Excerpt,
    DateOnly? PublishedOn, LegalSourceVerification Verification, DateTime? VerifiedAt, string? VerifiedBy, string? Notes);

public sealed record TaxRuleVersionDto(
    Guid Id, int Version, TaxRuleStatus Status, DateOnly ValidFrom, DateOnly? ValidTo, int Priority,
    JsonElement Conditions, JsonElement Result, string ContentHash, string Origin, string CreatedBy, string? Notes,
    DateTime? ActivatedAt, IReadOnlyList<string> LegalSourceCodes, DateTime CreatedAt);

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

// ── Overview ─────────────────────────────────────────────────────────────────

/// <summary>
/// Dashboard figures from real data only. Savings, deferrals and cash-flow effects are reported
/// apart and never added together.
/// </summary>
public sealed record TaxOverviewDto(
    bool ProfileConfigured, DateOnly PeriodFrom, DateOnly PeriodTo, int Calculations, decimal TaxedBase, decimal EstimatedTax,
    decimal? EffectiveRatePercent, decimal CreditsAvailable, decimal CreditsBlocked, decimal CreditsNotAppropriated,
    int OpenOpportunities, decimal PotentialSavings, decimal PotentialDeferrals, decimal PotentialCashFlow, decimal PotentialCredits,
    int OpenHighRisk, int BlockedOpportunities, int SplitDivergences, int ActiveRulesToday, int PendingRulesToday);
