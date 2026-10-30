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
/** Disabled is the default: no approval mode configured, so nothing runs where approval is enforced. */
export type TaxApprovalMode = "Disabled" | "ControlledPilot" | "ProfessionalFinal";
/** Runtime state kept by the platform on top of the configuration: new work runs only with the configured mode Active. */
export type TaxModuleStatus = "Inactive" | "Active" | "Suspended";
/** Simples Nacional situation as declared; Unknown is never read as the regular regime. */
export type SimplesSituation = "NotOptant" | "WithinSublimit" | "AboveSublimit" | "Mei" | "Simei" | "Unknown";
export type FileStatus = "Active" | "Replaced" | "Revoked";
export type FileScanStatus = "NotScanned" | "Clean" | "Rejected";
export type FileOwner = "OpportunityEvidence" | "Credit" | "ComplianceAssessment" | "SplitSettlement";
export type RetentionStart = "UploadDate" | "EndOfUploadYear";
export type ClassificationKind = "CClassTrib" | "Ncm" | "Nbs";
export type ClassificationStatus = "Draft" | "Active" | "Inactive";

/** One page of a list: the items, how many exist in total and where the page starts. */
export interface Page<T> { items: T[]; total: number; skip: number; take: number }

export interface TaxInternalApprovalDto {
  status: InternalApprovalStatus;
  policy: string | null;
  /** The authenticated user who recorded it — a governance actor, never a professional identity. */
  approvedById: string | null;
  approvedBy: string | null;
  approvedByRole: string | null;
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

/** Both approval dimensions of one rule version, as they stood when recorded (frozen with the calculation or dossier). */
export interface TaxRuleGovernanceDto {
  ruleVersionId: string;
  key: string;
  version: number;
  technicalStatus: RuleStatus;
  internalApproval: InternalApprovalStatus;
  approvedForPilot: boolean;
  internalApprovalPolicy: string | null;
  internalApprovedById: string | null;
  internalApprovedBy: string | null;
  internalApprovedByRole: string | null;
  internalApprovedAt: string | null;
  internalApprovalScope: string | null;
  internalApprovalValidFrom: string | null;
  internalApprovalValidTo: string | null;
  pilotCaveats: string | null;
  /** The exact decision record behind this version's internal approval. */
  internalApprovalDecisionRef: string | null;
  professionalReview: ProfessionalReviewStatus;
  professionallyApproved: boolean;
  professionalReviewScope: string | null;
  professionalCaveats: string | null;
  /** The exact review record frozen with the work (null for older snapshots). */
  professionalReviewRecordId?: string | null;
  professionalReviewer?: string | null;
  professionalReference?: string | null;
  professionalReviewValidFrom?: string | null;
  professionalReviewValidTo?: string | null;
  professionalReviewRegimes?: string[] | null;
}

export interface TaxPilotScopeSnapshotDto {
  decisionReference: string;
  validFrom: string;
  validTo: string | null;
  activatedByName: string;
  activatedAt: string;
}

/** The approval governance a calculation or an opportunity runs under. */
export interface TaxGovernanceDto {
  mode: TaxApprovalMode;
  policy: string;
  enforced: boolean;
  nature: string;
  notice: string;
  /** In the controlled pilot: it never acts on anything fiscal, accounting or financial by itself. */
  effectsNotice: string | null;
  pilotScope: TaxPilotScopeSnapshotDto | null;
  rules: TaxRuleGovernanceDto[];
  notApproved: string[];
  professionalReviewPending: string[];
  /** Every distinct decision record behind the rules. */
  decisionReferences: string[];
}

/** Where this company/store stands today: kill switch, mode, whether new work is admitted. */
export interface TaxGovernanceStatusDto {
  enabled: boolean;
  mode: TaxApprovalMode;
  policy: string;
  admitted: boolean;
  refusalCode: string | null;
  refusalMessage: string | null;
  effectsNotice: string | null;
  pilotScope: TaxPilotScopeSnapshotDto | null;
  /** The platform's runtime state of the module (activation / suspension). */
  moduleStatus?: TaxModuleStatus;
}
/** Only "Verified" satisfies a rule that needs compliance; a declaration, a remediation or PNCT participation never does. */
export type ComplianceAssessmentStatus =
  | "Unknown" | "Declared" | "Verified" | "InconsistencyIdentified" | "UnderRemediation" | "RemediationVerified" | "NonCompliant" | "NotApplicable";
export type CoverageStatus = "Supported" | "Unsupported" | "RequiresClassification" | "RequiresProfessionalReview" | "PendingRegulation";
export type SupplyPattern = "SingleSupply" | "ContinuedSupply" | "AdvancePayment" | "FutureDelivery" | "PublicProcurement" | "Other";
export type RequirementPhase = "ScenarioApproval" | "ExecutionVerification";
export type EvidenceType = "CommercialInstrument" | "FiscalDocument" | "FiscalDocumentEvent" | "PaymentRecord" | "Other";
export type EvidenceStatus = "Submitted" | "Accepted" | "Rejected" | "Superseded" | "Revoked";
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
  /** Tax of the establishment's own operations (outbound) and tax stated by suppliers (inbound) — never added up. */
  outboundTax: number;
  inboundTax: number;
  creditsAppropriated: number;
  creditsUsed: number;
  approvedAwaitingExecution: number;
  unverifiedRulesToday: number;
  /** Active versions today without a professional approval. */
  rulesPendingProfessionalReview: number;
  complianceInForce: ComplianceAssessmentStatus | null;
  complianceInForceTo: string | null;
  /** Operations refused by the coverage matrix in the period, by status. */
  coverageRefusals: number;
  coverageRefusalsByStatus: Record<string, number> | null;
  /** Refused by governance (module off, suspended, not in the pilot, rules not approved). */
  governanceRefusals: number;
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
  simplesSituation: SimplesSituation;
  /** Whether a Simples optant chose the regular IBS/CBS regime; null = not informed (the operation is not calculated). */
  ibsCbsRegularRegimeOption: boolean | null;
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
  /** Each obligation assessed, with its own status, evidence and remediation. */
  results: TaxObligationResultDto[] | null;
  supersedesAssessmentId: string | null;
}

export interface TaxObligationResultDto {
  obligationCode: string;
  obligationName: string;
  status: ComplianceAssessmentStatus;
  evidenceReference: string | null;
  evidenceFileId: string | null;
  remediation: string | null;
  remediationVerifiedOn: string | null;
  remediationVerifiedBy: string | null;
  notes: string | null;
}

export type TaxObligationResultRequest = Omit<TaxObligationResultDto, "obligationName">;

export interface TaxObligationDefinitionDto { id: string; code: string; name: string; description: string; reference: string; active: boolean; createdByName: string }

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
  obligations?: TaxObligationResultRequest[];
  supersedesAssessmentId?: string | null;
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
  simplesSituation?: SimplesSituation | null;
  ibsCbsRegularRegimeOption?: boolean | null;
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
  /** Inbound from a Simples supplier: its situation and whether it opted for the regular regime (null = not informed). */
  counterpartySimplesSituation?: SimplesSituation | null;
  counterpartyRegularRegimeOption?: boolean | null;
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

export interface CreateTaxSimulationRequest {
  name: string;
  baseline: TaxOperationRequest;
  scenarios: TaxScenarioRequest[];
  /** What the person took as given (prices, dates, supplier conditions). */
  assumptions?: string | null;
  caveats?: string | null;
}

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

export interface TaxSimulationSummaryDto { id: string; name: string; scenarios: number; createdAt: string; createdByName: string | null }
export interface TaxSimulationDto {
  id: string;
  name: string;
  engineVersion: string;
  createdAt: string;
  scenarios: ScenarioResult[];
  assumptions: string | null;
  caveats: string | null;
  createdByName: string | null;
  /** The governance the simulation ran under, frozen with it. */
  governance: TaxGovernanceDto | null;
  /** Each scenario's own governance (its date and regime), frozen with it. Absent on older simulations. */
  scenarioGovernance?: TaxScenarioGovernanceDto[] | null;
}
export interface TaxScenarioGovernanceDto { scenarioIndex: number; scenario: string; referenceDate: string; regime: string; governance: TaxGovernanceDto }

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
  assignedToName?: string | null;
  detectorKey?: string;
  requirementsTotal?: number;
  requirementsMet?: number;
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
  /** "sha256-file": the hash is the SHA-256 of the uploaded file's bytes; "reference": it only fingerprints the reference. */
  hashKind: "sha256-file" | "reference";
  file: TaxStoredFileDto | null;
  version: number;
  supersedesEvidenceId: string | null;
  supersededByEvidenceId: string | null;
  revokedAt: string | null;
  revokedByName: string | null;
  revocationReason: string | null;
}

/** A private file as measured on upload — never a URL; downloads go through the authorised endpoint. */
export interface TaxStoredFileDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  scanStatus: FileScanStatus;
  status: FileStatus;
  version: number;
  replacesFileId: string | null;
  description: string | null;
  uploadedByName: string;
  uploadedAt: string;
  revokedByName: string | null;
  revokedAt: string | null;
  revocationReason: string | null;
  legalHold: boolean;
  legalHoldReason: string | null;
}

export interface TaxEvidenceFileDto { file: TaxStoredFileDto; owner: FileOwner; ownerId: string; requirementId: string | null; evidenceType: EvidenceType }

export interface TaxFileStorageStatusDto { available: boolean; state: string; message: string; maxBytes: number; acceptedExtensions: string[]; scannerConfigured: boolean }

export interface TaxTimelineEventDto { action: string; description: string; actorName: string; at: string; severity: string }

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
  assignedToId: string | null;
  assignedToName: string | null;
  assignedAt: string | null;
  /** The company's materiality version in force today (null = installation default or none). */
  materiality: TaxMaterialityPolicyDto | null;
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
  /** Same key on a retried request = the movement is not written twice. */
  idempotencyKey?: string | null;
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
  operationDate: string | null;
  /** Days since the operation while nothing (or not enough) was recorded. */
  ageDays: number | null;
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
  professionalReviewRegimes: string[] | null;
  professionalReviewedOn: string | null;
  /** The authenticated user who recorded the current review — never the professional. */
  professionalReviewRecordedBy: string | null;
  professionalReviewRecordedByRole: string | null;
  professionalReviewDocumentId: string | null;
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
  verificationRecordedBy: string | null;
  rulesUsing: number;
}

// ── Policies and classification ──────────────────────────────────────────────

export interface TaxMaterialityPolicyDto {
  id: string;
  version: number;
  threshold: number;
  currency: string;
  effectiveFrom: string;
  effectiveTo: string | null;
  rationale: string;
  decisionReference: string;
  configuredByName: string;
  configuredAt: string;
}
export interface TaxMaterialityDto { inForce: TaxMaterialityPolicyDto | null; installationDefault: number | null; history: TaxMaterialityPolicyDto[] }
export interface ConfigureMaterialityRequest {
  threshold: number; currency: string; effectiveFrom: string; effectiveTo: string | null; rationale: string; decisionReference: string;
}

export interface TaxRetentionPolicyDto {
  id: string;
  tenantId: string | null;
  version: number;
  evidenceType: EvidenceType | null;
  retentionMonths: number;
  startsAt: RetentionStart;
  enabled: boolean;
  allowTenantOverride: boolean;
  rationale: string;
  reference: string;
  configuredByName: string;
  configuredAt: string;
  supersededAt: string | null;
}
export interface ConfigureRetentionRequest {
  evidenceType: EvidenceType | null; retentionMonths: number; startsAt: RetentionStart; enabled: boolean; allowTenantOverride: boolean;
  rationale: string; reference: string;
}
export interface TaxRetentionPreviewDto {
  asOf: string;
  automaticDisposal: boolean;
  filesConsidered: number;
  onLegalHold: number;
  withoutPolicy: number;
  eligible: { fileId: string; originalFileName: string; evidenceType: EvidenceType; uploadedAt: string; eligibleFrom: string; policyVersion: number; sha256: string }[];
  notice: string;
}

export interface TaxClassificationCodeDto {
  id: string;
  kind: ClassificationKind;
  code: string;
  description: string;
  source: string;
  sourceVersion: string | null;
  validFrom: string;
  validTo: string | null;
  status: ClassificationStatus;
  professionalReview: ProfessionalReviewStatus;
  professionalReviewNotes: string | null;
  notes: string | null;
  importBatchId: string | null;
  sourceDocumentId: string | null;
  createdByName: string;
  createdAt: string;
  statusChangedByName: string | null;
  statusChangedAt: string | null;
}

export interface TaxItemClassificationDto {
  id: string | null;
  productId: string;
  productName: string;
  productNcm: string | null;
  itemKind: TaxItemKind | null;
  ncm: string | null;
  nbs: string | null;
  cClassTrib: string | null;
  catalogStatus: ClassificationStatus | null;
  notes: string | null;
  setByName: string | null;
  updatedAt: string | null;
}
export interface SetItemClassificationRequest { itemKind: TaxItemKind; ncm: string | null; nbs: string | null; cClassTrib: string; notes: string | null }

// ── Calls ────────────────────────────────────────────────────────────────────

export const TAX_KEY = ["tax"] as const;
export const TAX_KEYS = {
  storage: [...TAX_KEY, "evidence", "storage"] as const,
  materiality: [...TAX_KEY, "materiality"] as const,
  retention: [...TAX_KEY, "retention"] as const,
  retentionPreview: [...TAX_KEY, "retention", "preview"] as const,
  items: (search: string, onlyMissing: boolean, skip: number) => [...TAX_KEY, "classification", "items", search, onlyMissing, skip] as const,
  codes: (search: string) => [...TAX_KEY, "classification", "codes", search] as const,
  obligations: [...TAX_KEY, "obligations"] as const,
  timeline: (id: string) => [...TAX_KEY, "opportunity", id, "timeline"] as const,
  files: (owner: FileOwner, ownerId: string) => [...TAX_KEY, "files", owner, ownerId] as const,
  overview: [...TAX_KEY, "overview"] as const,
  profile: [...TAX_KEY, "profile"] as const,
  calculations: [...TAX_KEY, "calculations"] as const,
  calculation: (id: string) => [...TAX_KEY, "calculations", id] as const,
  simulations: [...TAX_KEY, "simulations"] as const,
  simulation: (id: string) => [...TAX_KEY, "simulations", id] as const,
  opportunities: (filter: unknown) => [...TAX_KEY, "opportunities", filter] as const,
  opportunity: (id: string) => [...TAX_KEY, "opportunity", id] as const,
  credits: [...TAX_KEY, "credits"] as const,
  creditTotals: [...TAX_KEY, "credits", "totals"] as const,
  splitProvider: [...TAX_KEY, "split", "provider"] as const,
  governance: [...TAX_KEY, "governance"] as const,
  splitExpectations: (filter: unknown) => [...TAX_KEY, "split", "expectations", filter] as const,
  rules: [...TAX_KEY, "rules"] as const,
  legalSources: [...TAX_KEY, "legal-sources"] as const,
};

export const getTaxOverview = () => apiClient.get<TaxOverviewDto>("/tax/overview");

export const getTaxProfile = () => apiClient.get<TaxProfileDto>("/tax/profile");
export const addTaxProfileVersion = (body: CreateTaxProfileVersionRequest) => apiClient.post<TaxProfileVersionDto>("/tax/profile/versions", body);
export const recordComplianceAssessment = (body: RecordComplianceAssessmentRequest) =>
  apiClient.post<TaxComplianceAssessmentDto>("/tax/compliance-assessments", body);

export interface CalculationFilter { direction?: TaxDirection | ""; source?: TaxTransactionSource | ""; from?: string; to?: string; skip?: number; take?: number }
export const listTaxCalculations = (f: CalculationFilter = {}) =>
  apiClient.get<Page<TaxCalculationSummaryDto>>(`/tax/calculations${qs(f)}`);
export const createTaxCalculation = (body: TaxOperationRequest) => apiClient.post<TaxCalculationDto>("/tax/calculations", body);
export const previewTaxCoverage = (body: TaxOperationRequest) => apiClient.post<TaxCoverageDecision>("/tax/calculations/coverage", body);
export const calculateSale = (saleId: string) => apiClient.post<TaxCalculationDto>(`/tax/calculations/from-sale/${encodeURIComponent(saleId)}`);
export const getTaxCalculation = (id: string) => apiClient.get<TaxCalculationDto>(`/tax/calculations/${id}`);
export const verifyTaxCalculation = (id: string) => apiClient.post<TaxVerificationDto>(`/tax/calculations/${id}/verify`);
export const detectOpportunitiesAgain = (id: string) => apiClient.post<string[]>(`/tax/calculations/${id}/opportunities/detect`);
export const createSplitExpectations = (id: string, procedure: SplitProcedure) =>
  apiClient.post<SplitExpectationDto[]>(`/tax/calculations/${id}/split-expectations`, { procedure });

export const listTaxSimulations = (search = "", skip = 0, take = 50) =>
  apiClient.get<Page<TaxSimulationSummaryDto>>(`/tax/simulations${qs({ search, skip, take })}`);
export const simulationCsvUrl = (id: string) => `/tax/simulations/${id}/export`;
export const getTaxSimulation = (id: string) => apiClient.get<TaxSimulationDto>(`/tax/simulations/${id}`);
export const createTaxSimulation = (body: CreateTaxSimulationRequest) => apiClient.post<TaxSimulationDto>("/tax/simulations", body);
export const compareSuppliers = (body: CompareSuppliersRequest) => apiClient.post<SupplierOfferResultDto[]>("/tax/simulations/supplier-comparison", body);

export interface OpportunityFilter {
  status?: OpportunityStatus | ""; risk?: RiskLevel | ""; type?: OpportunityType | ""; open?: boolean; mine?: boolean; search?: string;
  skip?: number; take?: number;
}
export const listTaxOpportunities = (f: OpportunityFilter = {}) =>
  apiClient.get<Page<TaxOpportunitySummaryDto>>(`/tax/opportunities${qs(f)}`);
export const getOpportunityTimeline = (id: string) => apiClient.get<TaxTimelineEventDto[]>(`/tax/opportunities/${id}/timeline`);
/** Who moves the analysis forward — the server takes the name from the user record of this company. */
export const assignOpportunity = (id: string, userId: string | null) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/assign`, { userId });
/** Evidence with its file (PDF, image or XML). The hash becomes the SHA-256 of the file's bytes. */
export function uploadOpportunityEvidence(id: string, file: File, body: AddEvidenceRequest) {
  const form = new FormData();
  form.append("file", file);
  form.append("type", body.type);
  if (body.requirementId) form.append("requirementId", body.requirementId);
  form.append("description", body.description);
  if (body.documentReference) form.append("documentReference", body.documentReference);
  if (body.notes) form.append("notes", body.notes);
  return apiClient.postForm<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence/files`, form);
}
/** A newer version of a piece of evidence (new file optional); the old version stays on record. */
export function replaceOpportunityEvidence(id: string, evidenceId: string, description: string, documentReference: string | null, file?: File | null) {
  const form = new FormData();
  if (file) form.append("file", file);
  form.append("description", description);
  if (documentReference) form.append("documentReference", documentReference);
  return apiClient.postForm<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence/${evidenceId}/replace`, form);
}
export const revokeOpportunityEvidence = (id: string, evidenceId: string, reason: string) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence/${evidenceId}/revoke`, { reason });
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
export const decisionExportUrl = (id: string, includeFiles: boolean) => `/tax/decisions/${id}/export?includeFiles=${includeFiles}`;

// ── Private files ────────────────────────────────────────────────────────────

export const getEvidenceStorage = () => apiClient.get<TaxFileStorageStatusDto>("/tax/evidence/storage");
export const evidenceFileUrl = (fileId: string) => `/tax/evidence-files/${fileId}/content`;
export const listEvidenceFiles = (owner: FileOwner, ownerId: string) =>
  apiClient.get<TaxEvidenceFileDto[]>(`/tax/evidence-files?owner=${owner}&ownerId=${encodeURIComponent(ownerId)}`);
export function uploadEvidenceFile(owner: FileOwner, ownerId: string, type: EvidenceType, file: File, description: string | null) {
  const form = new FormData();
  form.append("file", file);
  form.append("owner", owner);
  form.append("ownerId", ownerId);
  form.append("type", type);
  if (description) form.append("description", description);
  return apiClient.postForm<TaxEvidenceFileDto>("/tax/evidence-files", form);
}
export const revokeEvidenceFile = (id: string, reason: string) => apiClient.post<TaxEvidenceFileDto>(`/tax/evidence-files/${id}/revoke`, { reason });
export const setLegalHold = (id: string, hold: boolean, reason: string) =>
  apiClient.post<TaxEvidenceFileDto>(`/tax/evidence-files/${id}/legal-hold`, { hold, reason });

// ── Policies and classification ──────────────────────────────────────────────

export const getMateriality = () => apiClient.get<TaxMaterialityDto>("/tax/materiality");
export const configureMateriality = (body: ConfigureMaterialityRequest) => apiClient.put<TaxMaterialityDto>("/tax/materiality", body);
export const listRetentionPolicies = () => apiClient.get<TaxRetentionPolicyDto[]>("/tax/retention-policies");
export const configureRetention = (body: ConfigureRetentionRequest) => apiClient.put<TaxRetentionPolicyDto>("/tax/retention-policies", body);
export const getRetentionPreview = () => apiClient.get<TaxRetentionPreviewDto>("/tax/retention-policies/preview");
export const listItemClassifications = (search = "", onlyMissing = false, skip = 0, take = 50) =>
  apiClient.get<Page<TaxItemClassificationDto>>(`/tax/classifications/items${qs({ search, onlyMissing, skip, take })}`);
export const setItemClassification = (productId: string, body: SetItemClassificationRequest) =>
  apiClient.put<TaxItemClassificationDto>(`/tax/classifications/items/${productId}`, body);
export const listClassificationCodes = (search = "") =>
  apiClient.get<Page<TaxClassificationCodeDto>>(`/tax/classifications/codes${qs({ search, take: 50 })}`);
export const listObligations = () => apiClient.get<TaxObligationDefinitionDto[]>("/tax/obligations");

/** "?a=1&b=2" from the defined, non-empty values, or "" when there are none. */
export function qs(values: object): string {
  const s = query(values);
  return s ? `?${s}` : "";
}

/** A query string from the defined, non-empty values (false and 0 are kept). */
export function query(values: object): string {
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(values)) if (v !== undefined && v !== null && v !== "") qs.set(k, String(v));
  return qs.toString();
}

export interface CreditFilter { tax?: CreditTax | ""; stage?: CreditStage | ""; expiring?: boolean; skip?: number; take?: number }
export const listTaxCredits = (f: CreditFilter = {}) => apiClient.get<Page<TaxCreditDto>>(`/tax/credits${qs(f)}`);
export const getTaxCreditTotals = () => apiClient.get<TaxCreditTotalsDto[]>("/tax/credits/totals");
export const registerTaxCredit = (body: RegisterTaxCreditRequest) => apiClient.post<TaxCreditDto>("/tax/credits", body);
export const moveTaxCredit = (id: string, body: CreditMovementRequest) => apiClient.post<TaxCreditDto>(`/tax/credits/${id}/movements`, body);

export const getSplitProvider = () => apiClient.get<{ configured: boolean; message: string }>("/tax/split/provider");
export interface SplitFilter { calculationId?: string; status?: SplitStatus | ""; tax?: TaxType | ""; minAgeDays?: number | ""; skip?: number; take?: number }
export const listSplitExpectations = (f: SplitFilter = {}) => apiClient.get<Page<SplitExpectationDto>>(`/tax/split/expectations${qs(f)}`);
export const splitCsvUrl = (f: SplitFilter = {}) => `/tax/split/expectations/export${qs({ ...f, skip: undefined, take: undefined, calculationId: undefined })}`;
export const recordSplitSettlement = (id: string, actualAmount: number, externalReference: string, settledAt: string) =>
  apiClient.post<SplitExpectationDto>(`/tax/split/expectations/${id}/settlements`, { actualAmount, externalReference, settledAt });

/** Cancels a wrong retention with an opposite entry; the original stays on record. */
export const reverseSplitSettlement = (settlementId: string, reason: string) =>
  apiClient.post<SplitExpectationDto>(`/tax/split/settlements/${settlementId}/reverse`, { reason });

export const listTaxRules = () => apiClient.get<TaxRuleDto[]>("/tax/rules");
export const getTaxGovernance = () => apiClient.get<TaxGovernanceStatusDto>("/tax/governance");
export const listLegalSources = () => apiClient.get<TaxLegalSourceDto[]>("/tax/legal-sources");
