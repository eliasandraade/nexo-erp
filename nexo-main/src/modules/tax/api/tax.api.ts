import { apiClient } from "@/services/api-client";
import type { TaxRegime } from "@/modules/fiscal/api/fiscal.api";

export type { TaxRegime };

// ── Enums (sent and received as their names) ─────────────────────────────────

export type TaxType = "Cbs" | "IbsState" | "IbsMunicipal";
export type CreditTax = "Cbs" | "Ibs";
export type TaxDirection = "Outbound" | "Inbound";
export type TaxItemKind = "Good" | "Service";
export type TaxTransactionSource = "Manual" | "Sale" | "Purchase" | "Service" | "FinancialEntry" | "FiscalDocument" | "External";
export type ImpactNature = "DefinitiveSaving" | "CalculatedTaxReduction" | "Deferral" | "CashFlow" | "Credit" | "RiskAvoided";
/** "Approved" is a scenario approval; whether the operation then happened as approved is ExecutionVerified / ExecutionDiverged. */
export type OpportunityStatus = "Identified" | "InReview" | "Approved" | "Rejected" | "Blocked" | "ExecutionVerified" | "ExecutionDiverged";
export type OpportunityType =
  | "TaxSaving" | "CreditOptimization" | "TaxDeferral" | "CashFlowOptimization" | "RefundOpportunity" | "RiskReduction"
  | "CalculatedTaxReduction";
export type RiskLevel = "Low" | "Medium" | "High";
export type ProhibitedStructure =
  | "Simulation" | "ArtificialTransaction" | "RevenueOmission" | "FalseExpense"
  | "FalseSupplier" | "UnderInvoicing" | "DocumentMismatch" | "PersonalExpenseAsBusiness";
export type RuleStatus = "Draft" | "PendingRegulation" | "Unverified" | "Active" | "Superseded" | "Revoked";
export type ConditionOutcome = "Passed" | "Failed" | "Unknown";
export type BaseComponent = "Surcharges" | "UnconditionalDiscount" | "ConditionalDiscount" | "ExcludedTaxes";
export type CreditMovement =
  | "Appropriate" | "Use" | "Block" | "Unblock" | "Reverse" | "Expire"
  | "StartRequirementsReview" | "ConfirmToBeAppropriated" | "Discard";
/** Potential → under review → to be appropriated → (ledger) appropriated/used/blocked/reversed/expired. */
export type CreditLifecycle =
  | "PotentialIdentified" | "RequirementsUnderReview" | "ToBeAppropriated" | "Appropriated" | "Used" | "Blocked" | "Reversed" | "Expired" | "Discarded";
export type CreditStage = "PotentialIdentified" | "RequirementsUnderReview" | "ToBeAppropriated" | "Discarded";
export type CreditEntryType = "Recognized" | "Appropriated" | "Used" | "Blocked" | "Unblocked" | "Expired" | "Reversed";
export type SplitProcedure = "Standard" | "Simplified";
export type SplitStatus = "Pending" | "Matched" | "Excess" | "Insufficient";
/** Only whether the excerpt matches the official text — never a legal opinion. */
export type TextVerificationStatus = "Verified" | "Unverified";
/** A tax professional's review of a rule version — separate from its technical status. */
export type ProfessionalReviewStatus = "Pending" | "ReviewReceived" | "Approved" | "ApprovedWithCaveats" | "ChangesRequired" | "Rejected";
/** Internal product/governance approval for the controlled pilot — never a professional review. */
export type InternalApprovalStatus = "NotApproved" | "ApprovedForControlledPilot" | "Withdrawn";
export type TaxApprovalMode = "ControlledPilot" | "ProfessionalFinal";

export interface TaxInternalApprovalDto {
  status: InternalApprovalStatus;
  policy: string | null;
  /** The governance role that decided (e.g. "Product Owner — Andrade Systems"), not a professional identity. */
  approvedBy: string | null;
  approvedAt: string | null;
  scope: string | null;
  validFrom: string | null;
  validTo: string | null;
  caveats: string | null;
  decisionReference: string | null;
  withdrawalReason: string | null;
  withdrawnAt: string | null;
  nature: string;
}

/** Both approval dimensions of one rule version, side by side. */
export interface TaxRuleGovernanceDto {
  ruleVersionId: string;
  key: string;
  version: number;
  internalApproval: InternalApprovalStatus;
  approvedForPilot: boolean;
  pilotCaveats: string | null;
  professionalReview: ProfessionalReviewStatus;
  professionallyApproved: boolean;
  professionalCaveats: string | null;
}

/** The approval governance a calculation or an opportunity runs under. */
export interface TaxGovernanceDto {
  mode: TaxApprovalMode;
  policy: string;
  enforced: boolean;
  nature: string;
  notice: string;
  rules: TaxRuleGovernanceDto[];
  notApproved: string[];
  professionalReviewPending: string[];
}
/** Only "Verified" satisfies a rule that needs compliance; a declaration, a remediation or PNCT participation never does. */
export type ComplianceAssessmentStatus =
  | "Unknown" | "Declared" | "Verified" | "InconsistencyIdentified" | "UnderRemediation" | "RemediationVerified" | "NonCompliant" | "NotApplicable";
export type CoverageStatus = "Supported" | "Unsupported" | "RequiresClassification" | "RequiresProfessionalReview" | "PendingRegulation";
export type SupplyPattern = "SingleSupply" | "ContinuedSupply" | "AdvancePayment" | "FutureDelivery" | "PublicProcurement" | "Other";
export type RequirementPhase = "ScenarioApproval" | "ExecutionVerification";
export type EvidenceType = "CommercialInstrument" | "FiscalDocument" | "FiscalDocumentEvent" | "PaymentRecord" | "Other";
export type EvidenceStatus = "Submitted" | "Accepted" | "Rejected";
export type ApprovalRequirement = "SelfApprovalWithJustification" | "IdentifiedReviewer" | "IndependentApprover";

// ── Overview ─────────────────────────────────────────────────────────────────

export interface TaxOverviewDto {
  profileConfigured: boolean;
  periodFrom: string;
  periodTo: string;
  calculations: number;
  taxedBase: number;
  estimatedTax: number;
  effectiveRatePercent: number | null;
  creditsAvailable: number;
  creditsBlocked: number;
  /** Estimates only — never eligibility, appropriation or availability. */
  creditsPotential: number;
  creditsToBeAppropriated: number;
  openOpportunities: number;
  potentialSavings: number;
  /** Lower IBS/CBS on the document — reported apart, never added to savings. */
  potentialCalculatedTaxReductions: number;
  potentialDeferrals: number;
  potentialCashFlow: number;
  potentialCredits: number;
  openHighRisk: number;
  blockedOpportunities: number;
  splitDivergences: number;
  activeRulesToday: number;
  pendingRulesToday: number;
}

// ── Profile ──────────────────────────────────────────────────────────────────

export interface TaxProfileVersionDto {
  id: string;
  versionNumber: number;
  validFrom: string;
  regime: TaxRegime;
  state: string;
  cityIbgeCode: string;
  primaryCnae: string;
  secondaryCnaes: string[];
  specialRegimes: string[];
  benefits: string[];
  reason: string;
  createdByName: string;
  createdAt: string;
}

export interface TaxProfileDto {
  current: TaxProfileVersionDto | null;
  versions: TaxProfileVersionDto[];
  suggestion: { regime: TaxRegime; state: string; cityIbgeCode: string } | null;
  complianceAssessments: TaxComplianceAssessmentDto[];
}

/** An assessment of the ancillary obligations for a period and scope — never "the company is compliant". */
export interface TaxComplianceAssessmentDto {
  id: string;
  status: ComplianceAssessmentStatus;
  periodFrom: string;
  periodTo: string;
  scope: string;
  obligationsChecked: string[];
  evidence: string | null;
  assessedBy: string;
  assessedAt: string;
  notes: string | null;
  limitations: string | null;
  pnctParticipation: boolean;
  recordedByName: string;
  createdAt: string;
}

export interface RecordComplianceAssessmentRequest {
  status: ComplianceAssessmentStatus;
  periodFrom: string;
  periodTo: string;
  scope: string | null;
  obligationsChecked: string[];
  evidence: string | null;
  assessedBy: string;
  assessedAt: string | null;
  notes: string | null;
  limitations: string | null;
  pnctParticipation: boolean;
}

export interface CreateTaxProfileVersionRequest {
  validFrom: string;
  regime: TaxRegime;
  state: string;
  cityIbgeCode: string;
  primaryCnae: string;
  secondaryCnaes: string[];
  specialRegimes: string[];
  benefits: string[];
  reason: string;
}

// ── Operations and calculations ──────────────────────────────────────────────

export interface TaxLineRequest {
  itemKind: TaxItemKind;
  description: string;
  ncm: string | null;
  quantity: number;
  grossAmount: number;
  unconditionalDiscount: number;
  conditionalDiscount: number;
  surcharges: number;
  /** null = "não sei" (the ICMS/ISS/PIS/Cofins inside the price are not known). */
  excludedTaxes: number | null;
  specialTreatment: string | null;
  /** Services: Nomenclatura Brasileira de Serviços. */
  nbs?: string | null;
  /** Código de Classificação Tributária do IBS/CBS — recorded, not interpreted by engine V1. */
  cClassTrib?: string | null;
}

/** When things happen. V1 supports only a single, immediate supply; anything else is refused. */
export interface TaxOperationTiming {
  supplyPattern: SupplyPattern;
  documentDate?: string | null;
  supplyDate?: string | null;
  paymentOrAdvanceDate?: string | null;
  competencePeriod?: string | null;
  executionFrom?: string | null;
  executionTo?: string | null;
}

export interface TaxCoverageFinding { status: CoverageStatus; code: string; message: string; lineIndex: number | null }

/** Whether engine V1 supports an operation. Outside it, nothing is calculated — never a zero. */
export interface TaxCoverageDecision {
  status: CoverageStatus;
  policyVersion: string;
  findings: TaxCoverageFinding[];
  assumptions: string[];
  supported: boolean;
}

export interface TaxOperationRequest {
  operationDate: string;
  direction: TaxDirection;
  counterpartyRegime: TaxRegime | null;
  counterpartyState: string | null;
  description: string | null;
  lines: TaxLineRequest[];
  timing?: TaxOperationTiming | null;
}

export interface TaxCalculationSummaryDto {
  id: string;
  referenceDate: string;
  source: TaxTransactionSource;
  sourceId: string | null;
  direction: TaxDirection;
  description: string | null;
  totalBase: number;
  totalTax: number;
  openOpportunities: number;
  createdAt: string;
}

export interface EngineLineInput {
  index: number;
  kind: TaxItemKind;
  gross: number;
  unconditionalDiscount: number;
  conditionalDiscount: number;
  surcharges: number;
  excludedTaxes: number;
  excludedTaxesInformed: boolean;
  specialTreatment: string | null;
}

export interface EngineInput {
  referenceDate: string;
  profile: { profileVersionId: string; regime: TaxRegime; state: string; cityIbgeCode: string; accessoryObligationsCompliant: boolean | null };
  direction: TaxDirection;
  counterpartyRegime: TaxRegime | null;
  lines: EngineLineInput[];
}

export interface ConditionTrace { kind: string; expected: string; actual: string; outcome: ConditionOutcome }

export interface RuleExecution {
  ruleVersionId: string;
  ruleKey: string;
  ruleVersion: number;
  contentHash: string;
  lineIndex: number | null;
  matched: boolean;
  conditions: ConditionTrace[];
  output: string;
  legalSourceCodes: string[];
  isHypothesis: boolean;
}

export interface ComponentResult {
  tax: TaxType;
  base: number;
  ratePercent: number;
  amount: number;
  ruleVersionId: string;
  ruleKey: string;
  isHypothesis: boolean;
}

export interface EngineOutput {
  engineVersion: string;
  lines: {
    index: number;
    gross: number;
    base: number;
    baseSteps: { ruleKey: string; ruleVersion: number; component: BaseComponent; included: boolean; delta: number }[];
    components: ComponentResult[];
  }[];
  totals: { tax: TaxType; base: number; amount: number }[];
  totalBase: number;
  totalTax: number;
  notes: { code: string; text: string; ruleKey: string; legalSourceCodes: string[] }[];
  warnings: string[];
  executions: RuleExecution[];
  notApplied: { ruleVersionId: string; ruleKey: string; ruleVersion: number; status: RuleStatus }[];
}

export interface TaxCalculationDto {
  id: string;
  transactionId: string;
  source: TaxTransactionSource;
  sourceId: string | null;
  direction: TaxDirection;
  description: string | null;
  referenceDate: string;
  profileVersionId: string;
  profileVersionNumber: number;
  engineVersion: string;
  inputHash: string;
  ruleSetHash: string;
  outputHash: string;
  totalBase: number;
  totalTax: number;
  reused: boolean;
  createdAt: string;
  input: EngineInput;
  output: EngineOutput;
  opportunityIds: string[];
  /** What the calculation says about the facts it used (e.g. obligations declared, not verified). */
  advisories: string[];
  coverage: TaxCoverageDecision;
  /** The assessment the calculation used — its result is frozen with it. */
  complianceAssessment: TaxComplianceAssessmentDto | null;
  /** The assessment in force today for the date, only when it is not the one used. */
  currentComplianceAssessment: TaxComplianceAssessmentDto | null;
  /** Mode, policy and both approvals of each rule, recorded when the calculation ran (null for older ones). */
  governance: TaxGovernanceDto | null;
}

export interface TaxVerificationDto {
  calculationId: string;
  reproducible: boolean;
  /** False: the engine that made it is not in this build — nothing was re-run. */
  engineAvailable: boolean;
  engineVersionMatches: boolean;
  storedEngineVersion: string;
  currentEngineVersion: string;
  storedOutputHash: string;
  recomputedOutputHash: string | null;
  differences: string[];
}

// ── Simulations ──────────────────────────────────────────────────────────────

export interface RateHypothesis { tax: TaxType; ratePercent: number; label: string }

export interface TaxScenarioRequest {
  name: string;
  regime: TaxRegime | null;
  referenceDate: string | null;
  direction: TaxDirection | null;
  counterpartyRegime: TaxRegime | null;
  lines: TaxLineRequest[] | null;
  convertConditionalDiscounts: boolean;
  rateHypotheses: RateHypothesis[] | null;
  assumeCreditable: boolean;
}

export interface CreateTaxSimulationRequest { name: string; baseline: TaxOperationRequest; scenarios: TaxScenarioRequest[] }

export interface ScenarioResult {
  name: string;
  isBaseline: boolean;
  input: EngineInput;
  output: EngineOutput;
  charged: number;
  taxTotal: number;
  creditEstimate: number;
  effectiveCost: number;
  taxDeltaVsBaseline: number;
  effectiveCostDeltaVsBaseline: number;
  noCashEffectInPeriod: boolean;
  hypotheses: string[];
}

export interface TaxSimulationSummaryDto { id: string; name: string; scenarios: number; createdAt: string }
export interface TaxSimulationDto { id: string; name: string; engineVersion: string; createdAt: string; scenarios: ScenarioResult[] }

export interface SupplierOfferRequest { supplier: string; supplierRegime: TaxRegime | null; lines: TaxLineRequest[] }
export interface CompareSuppliersRequest {
  operationDate: string;
  offers: SupplierOfferRequest[];
  rateHypotheses: RateHypothesis[] | null;
  assumeCreditable: boolean;
}
export interface SupplierOfferResultDto {
  supplier: string;
  charged: number;
  taxTotal: number;
  creditEstimate: number;
  effectiveCost: number;
  cheapest: boolean;
  hypotheses: string[];
  warnings: string[];
}

// ── Opportunities ────────────────────────────────────────────────────────────

export interface TaxOpportunitySummaryDto {
  id: string;
  type: OpportunityType;
  impactNature: ImpactNature;
  status: OpportunityStatus;
  title: string;
  estimatedImpact: number | null;
  risk: RiskLevel;
  confidence: number;
  createdAt: string;
}

export interface TaxRequirementDto {
  id: string;
  code: string;
  text: string;
  legalSourceCode: string | null;
  met: boolean | null;
  note: string | null;
  answeredByName: string | null;
  answeredAt: string | null;
  phase: RequirementPhase;
  requiredEvidence: EvidenceType | null;
  hasRequiredEvidence: boolean;
}

export interface TaxEvidenceDto {
  id: string;
  type: EvidenceType;
  requirementId: string | null;
  description: string;
  documentReference: string | null;
  contentHash: string;
  notes: string | null;
  status: EvidenceStatus;
  addedByName: string;
  createdAt: string;
  reviewedByName: string | null;
  reviewedAt: string | null;
  reviewNotes: string | null;
}

/** Whether the operation really happened as the scenario was approved — sealed onto the decision. */
export interface TaxExecutionVerificationDto {
  id: string;
  decisionId: string;
  outcome: "Verified" | "Diverged";
  fiscalDocumentReference: string | null;
  authorizationProtocol: string | null;
  notes: string | null;
  verifiedByName: string;
  verifiedAt: string;
  dossierHash: string;
  dossier: unknown;
}

export interface TaxDecisionDto {
  id: string;
  opportunityId: string;
  strategy: string;
  risk: RiskLevel;
  highRiskAcknowledged: boolean;
  engineVersion: string;
  approvedByName: string;
  approvedAt: string;
  selfApprovalJustification: string | null;
  dossierHash: string;
  dossier: unknown;
}

export interface TaxOpportunityDto extends TaxOpportunitySummaryDto {
  summary: string;
  detectorKey: string;
  engineVersion: string;
  sourceCalculationId: string | null;
  legalSourceCodes: string[];
  caveats: string | null;
  blockedAs: ProhibitedStructure | null;
  blockReason: string | null;
  rejectionReason: string | null;
  reviewStartedByName: string | null;
  reviewedByName: string | null;
  reviewedAt: string | null;
  /** Some rule behind it still awaits a tax professional's review. */
  professionalReviewPending: boolean;
  requirements: TaxRequirementDto[];
  evidence: TaxEvidenceDto[];
  current: ScenarioResult | null;
  proposed: ScenarioResult | null;
  decision: TaxDecisionDto | null;
  execution: TaxExecutionVerificationDto | null;
  /** Who may approve, by risk and materiality (maker-checker). */
  requiredApproval: ApprovalRequirement;
  /** The rules behind it under the configured approval mode: internal pilot approval beside the professional review. */
  governance: TaxGovernanceDto;
}

// ── Credits and split payment ────────────────────────────────────────────────

/** potential: an estimate; toBeAppropriated: requirements confirmed, not yet appropriated; only available can be used. */
export interface CreditBalance { potential: number; toBeAppropriated: number; appropriated: number; used: number; blocked: number; available: number }

export interface TaxCreditDto {
  id: string;
  tax: CreditTax;
  sourceDocumentType: string;
  sourceDocumentKey: string | null;
  supplierDocument: string | null;
  acquiredOn: string;
  expiresOn: string | null;
  originalAmount: number;
  potentialBasis: string;
  balance: CreditBalance;
  stage: CreditStage;
  lifecycle: CreditLifecycle;
  recognitionBasis: string | null;
  discardReason: string | null;
  /** occurredOn: the business date the movement refers to (a use may refer to an earlier obligation date). */
  entries: { id: string; type: CreditEntryType; amount: number; obligationReference: string | null; reason: string; occurredOn: string; actorName: string; createdAt: string }[];
  createdAt: string;
}

export interface TaxCreditTotalsDto extends CreditBalance { tax: CreditTax }

export interface RegisterTaxCreditRequest {
  tax: CreditTax;
  sourceDocumentType: string;
  sourceDocumentKey: string | null;
  supplierDocument: string | null;
  acquiredOn: string;
  expiresOn: string | null;
  originalAmount: number;
  /** An estimate — registering it validates nothing. */
  potentialAmount: number;
  potentialBasis: string;
}

export interface CreditMovementRequest {
  movement: CreditMovement;
  amount: number | null;
  reason: string | null;
  obligationTax: CreditTax | null;
  obligationReference: string | null;
  onDate: string | null;
}

/** A retention recorded for an expectation. A reversal has a negative amount and points at the entry it cancels. */
export interface SplitSettlementDto {
  id: string;
  actualAmount: number;
  source: "Manual" | "Provider";
  externalReference: string;
  settledAt: string;
  reversesSettlementId: string | null;
  reversed: boolean;
  reason: string | null;
}

export interface SplitExpectationDto {
  id: string;
  calculationId: string;
  tax: TaxType;
  procedure: SplitProcedure;
  expected: number;
  actual: number;
  difference: number;
  status: SplitStatus;
  settlements: SplitSettlementDto[];
  createdAt: string;
}

// ── Catalogue ────────────────────────────────────────────────────────────────

export interface TaxRuleVersionDto {
  id: string;
  version: number;
  status: RuleStatus;
  validFrom: string;
  validTo: string | null;
  priority: number;
  conditions: unknown;
  result: unknown;
  contentHash: string;
  origin: string;
  createdBy: string;
  notes: string | null;
  activatedAt: string | null;
  legalSourceCodes: string[];
  createdAt: string;
  professionalReview: ProfessionalReviewStatus;
  professionalReviewedBy: string | null;
  professionalReviewedAt: string | null;
  professionalReviewNotes: string | null;
  professionalReference: string | null;
  professionalReviewScope: string | null;
  professionalReviewValidFrom: string | null;
  professionalReviewValidTo: string | null;
  professionalReviewAssumptions: string | null;
  professionalReviewCaveats: string | null;
  internalApproval: TaxInternalApprovalDto;
}

export interface TaxRuleDto { id: string; key: string; name: string; description: string; versions: TaxRuleVersionDto[] }

export interface TaxLegalSourceDto {
  id: string;
  code: string;
  kind: string;
  title: string;
  reference: string;
  url: string;
  excerpt: string;
  publishedOn: string | null;
  textVerification: TextVerificationStatus;
  verifiedAt: string | null;
  verifiedBy: string | null;
  notes: string | null;
}

// ── Calls ────────────────────────────────────────────────────────────────────

export const TAX_KEY = ["tax"] as const;
export const TAX_KEYS = {
  overview: [...TAX_KEY, "overview"] as const,
  profile: [...TAX_KEY, "profile"] as const,
  calculations: [...TAX_KEY, "calculations"] as const,
  calculation: (id: string) => [...TAX_KEY, "calculations", id] as const,
  simulations: [...TAX_KEY, "simulations"] as const,
  simulation: (id: string) => [...TAX_KEY, "simulations", id] as const,
  opportunities: (status: string) => [...TAX_KEY, "opportunities", status] as const,
  opportunity: (id: string) => [...TAX_KEY, "opportunity", id] as const,
  credits: [...TAX_KEY, "credits"] as const,
  creditTotals: [...TAX_KEY, "credits", "totals"] as const,
  splitProvider: [...TAX_KEY, "split", "provider"] as const,
  splitExpectations: (calculationId: string) => [...TAX_KEY, "split", "expectations", calculationId] as const,
  rules: [...TAX_KEY, "rules"] as const,
  legalSources: [...TAX_KEY, "legal-sources"] as const,
};

export const getTaxOverview = () => apiClient.get<TaxOverviewDto>("/tax/overview");

export const getTaxProfile = () => apiClient.get<TaxProfileDto>("/tax/profile");
export const addTaxProfileVersion = (body: CreateTaxProfileVersionRequest) => apiClient.post<TaxProfileVersionDto>("/tax/profile/versions", body);
export const recordComplianceAssessment = (body: RecordComplianceAssessmentRequest) =>
  apiClient.post<TaxComplianceAssessmentDto>("/tax/compliance-assessments", body);

export const listTaxCalculations = (take = 50) => apiClient.get<TaxCalculationSummaryDto[]>(`/tax/calculations?take=${take}`);
export const createTaxCalculation = (body: TaxOperationRequest) => apiClient.post<TaxCalculationDto>("/tax/calculations", body);
export const previewTaxCoverage = (body: TaxOperationRequest) => apiClient.post<TaxCoverageDecision>("/tax/calculations/coverage", body);
export const calculateSale = (saleId: string) => apiClient.post<TaxCalculationDto>(`/tax/calculations/from-sale/${encodeURIComponent(saleId)}`);
export const getTaxCalculation = (id: string) => apiClient.get<TaxCalculationDto>(`/tax/calculations/${id}`);
export const verifyTaxCalculation = (id: string) => apiClient.post<TaxVerificationDto>(`/tax/calculations/${id}/verify`);
export const createSplitExpectations = (id: string, procedure: SplitProcedure) =>
  apiClient.post<SplitExpectationDto[]>(`/tax/calculations/${id}/split-expectations`, { procedure });

export const listTaxSimulations = () => apiClient.get<TaxSimulationSummaryDto[]>("/tax/simulations");
export const getTaxSimulation = (id: string) => apiClient.get<TaxSimulationDto>(`/tax/simulations/${id}`);
export const createTaxSimulation = (body: CreateTaxSimulationRequest) => apiClient.post<TaxSimulationDto>("/tax/simulations", body);
export const compareSuppliers = (body: CompareSuppliersRequest) => apiClient.post<SupplierOfferResultDto[]>("/tax/simulations/supplier-comparison", body);

export function listTaxOpportunities(status: OpportunityStatus | "" = "", take = 100) {
  const qs = new URLSearchParams({ take: String(take) });
  if (status) qs.set("status", status);
  return apiClient.get<TaxOpportunitySummaryDto[]>(`/tax/opportunities?${qs.toString()}`);
}
export const getTaxOpportunity = (id: string) => apiClient.get<TaxOpportunityDto>(`/tax/opportunities/${id}`);
export const startOpportunityReview = (id: string) => apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/review`, {});
export const answerRequirement = (id: string, requirementId: string, met: boolean, note: string | null) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/requirements/${requirementId}`, { met, note });
export interface AddEvidenceRequest {
  description: string;
  documentReference: string | null;
  type: EvidenceType;
  requirementId: string | null;
  notes: string | null;
}
export const addOpportunityEvidence = (id: string, body: AddEvidenceRequest) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence`, body);
export const reviewOpportunityEvidence = (id: string, evidenceId: string, accepted: boolean, notes: string | null) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence/${evidenceId}/review`, { accepted, notes });
export const verifyOpportunityExecution = (
  id: string, body: { outcome: "Verified" | "Diverged"; fiscalDocumentReference: string | null; authorizationProtocol: string | null; notes: string | null },
) => apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/execution`, body);
export const blockOpportunity = (id: string, structure: ProhibitedStructure, reason: string) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/block`, { structure, reason });
export const rejectOpportunity = (id: string, reason: string) => apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/reject`, { reason });
export const approveOpportunity = (id: string, acknowledgeHighRisk: boolean, selfApprovalJustification?: string | null) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/approve`, {
    acknowledgeHighRisk,
    selfApprovalJustification: selfApprovalJustification?.trim() || null,
  });
export const getTaxDecision = (id: string) => apiClient.get<TaxDecisionDto>(`/tax/decisions/${id}`);

export const listTaxCredits = () => apiClient.get<TaxCreditDto[]>("/tax/credits");
export const getTaxCreditTotals = () => apiClient.get<TaxCreditTotalsDto[]>("/tax/credits/totals");
export const registerTaxCredit = (body: RegisterTaxCreditRequest) => apiClient.post<TaxCreditDto>("/tax/credits", body);
export const moveTaxCredit = (id: string, body: CreditMovementRequest) => apiClient.post<TaxCreditDto>(`/tax/credits/${id}/movements`, body);

export const getSplitProvider = () => apiClient.get<{ configured: boolean; message: string }>("/tax/split/provider");
export const listSplitExpectations = (calculationId?: string) =>
  apiClient.get<SplitExpectationDto[]>(calculationId ? `/tax/split/expectations?calculationId=${encodeURIComponent(calculationId)}` : "/tax/split/expectations");
export const recordSplitSettlement = (id: string, actualAmount: number, externalReference: string, settledAt: string) =>
  apiClient.post<SplitExpectationDto>(`/tax/split/expectations/${id}/settlements`, { actualAmount, externalReference, settledAt });

/** Cancels a wrong retention with an opposite entry; the original stays on record. */
export const reverseSplitSettlement = (settlementId: string, reason: string) =>
  apiClient.post<SplitExpectationDto>(`/tax/split/settlements/${settlementId}/reverse`, { reason });

export const listTaxRules = () => apiClient.get<TaxRuleDto[]>("/tax/rules");
export const listLegalSources = () => apiClient.get<TaxLegalSourceDto[]>("/tax/legal-sources");
