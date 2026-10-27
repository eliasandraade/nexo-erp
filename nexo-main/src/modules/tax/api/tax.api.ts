import { apiClient } from "@/services/api-client";
import type { TaxRegime } from "@/modules/fiscal/api/fiscal.api";

export type { TaxRegime };

// ── Enums (sent and received as their names) ─────────────────────────────────

export type TaxType = "Cbs" | "IbsState" | "IbsMunicipal";
export type CreditTax = "Cbs" | "Ibs";
export type TaxDirection = "Outbound" | "Inbound";
export type TaxItemKind = "Good" | "Service";
export type TaxTransactionSource = "Manual" | "Sale" | "Purchase" | "Service" | "FinancialEntry" | "FiscalDocument" | "External";
export type ImpactNature = "DefinitiveSaving" | "Deferral" | "CashFlow" | "Credit" | "RiskAvoided";
export type OpportunityStatus = "Identified" | "InReview" | "Approved" | "Rejected" | "Blocked";
export type OpportunityType = "TaxSaving" | "CreditOptimization" | "TaxDeferral" | "CashFlowOptimization" | "RefundOpportunity" | "RiskReduction";
export type RiskLevel = "Low" | "Medium" | "High";
export type ProhibitedStructure =
  | "Simulation" | "ArtificialTransaction" | "RevenueOmission" | "FalseExpense"
  | "FalseSupplier" | "UnderInvoicing" | "DocumentMismatch" | "PersonalExpenseAsBusiness";
export type RuleStatus = "Draft" | "PendingRegulation" | "Unverified" | "Active" | "Superseded" | "Revoked";
export type ConditionOutcome = "Passed" | "Failed" | "Unknown";
export type BaseComponent = "Surcharges" | "UnconditionalDiscount" | "ConditionalDiscount" | "ExcludedTaxes";
export type CreditMovement = "Appropriate" | "Use" | "Block" | "Unblock" | "Reverse" | "Expire";
export type CreditEntryType = "Recognized" | "Appropriated" | "Used" | "Blocked" | "Unblocked" | "Expired" | "Reversed";
export type SplitProcedure = "Standard" | "Simplified";
export type SplitStatus = "Pending" | "Matched" | "Excess" | "Insufficient";
export type LegalSourceVerification = "Verified" | "Unverified";

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
  creditsNotAppropriated: number;
  openOpportunities: number;
  potentialSavings: number;
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
  accessoryObligationsCompliant: boolean | null;
  reason: string;
  createdByName: string;
  createdAt: string;
}

export interface TaxProfileDto {
  current: TaxProfileVersionDto | null;
  versions: TaxProfileVersionDto[];
  suggestion: { regime: TaxRegime; state: string; cityIbgeCode: string } | null;
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
  accessoryObligationsCompliant: boolean | null;
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
}

export interface TaxOperationRequest {
  operationDate: string;
  direction: TaxDirection;
  counterpartyRegime: TaxRegime | null;
  counterpartyState: string | null;
  description: string | null;
  lines: TaxLineRequest[];
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
}

export interface TaxVerificationDto {
  calculationId: string;
  reproducible: boolean;
  engineVersionMatches: boolean;
  storedEngineVersion: string;
  currentEngineVersion: string;
  storedOutputHash: string;
  recomputedOutputHash: string;
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
}

export interface TaxEvidenceDto {
  id: string;
  description: string;
  documentReference: string | null;
  contentHash: string;
  addedByName: string;
  createdAt: string;
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
  dossierHash: string;
  dossier: unknown;
}

export interface TaxOpportunityDto extends TaxOpportunitySummaryDto {
  summary: string;
  detectorKey: string;
  sourceCalculationId: string | null;
  legalSourceCodes: string[];
  caveats: string | null;
  blockedAs: ProhibitedStructure | null;
  blockReason: string | null;
  rejectionReason: string | null;
  reviewedByName: string | null;
  reviewedAt: string | null;
  requirements: TaxRequirementDto[];
  evidence: TaxEvidenceDto[];
  current: ScenarioResult | null;
  proposed: ScenarioResult | null;
  decision: TaxDecisionDto | null;
}

// ── Credits and split payment ────────────────────────────────────────────────

export interface CreditBalance { eligible: number; appropriated: number; used: number; blocked: number; available: number; notAppropriated: number }

export interface TaxCreditDto {
  id: string;
  tax: CreditTax;
  sourceDocumentType: string;
  sourceDocumentKey: string | null;
  supplierDocument: string | null;
  acquiredOn: string;
  expiresOn: string | null;
  originalAmount: number;
  eligibilityBasis: string;
  balance: CreditBalance;
  entries: { id: string; type: CreditEntryType; amount: number; obligationReference: string | null; reason: string; actorName: string; createdAt: string }[];
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
  eligibleAmount: number;
  eligibilityBasis: string;
}

export interface CreditMovementRequest {
  movement: CreditMovement;
  amount: number | null;
  reason: string | null;
  obligationTax: CreditTax | null;
  obligationReference: string | null;
  onDate: string | null;
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
  settlements: { id: string; actualAmount: number; source: "Manual" | "Provider"; externalReference: string; settledAt: string }[];
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
  verification: LegalSourceVerification;
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

export const listTaxCalculations = (take = 50) => apiClient.get<TaxCalculationSummaryDto[]>(`/tax/calculations?take=${take}`);
export const createTaxCalculation = (body: TaxOperationRequest) => apiClient.post<TaxCalculationDto>("/tax/calculations", body);
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
export const addOpportunityEvidence = (id: string, description: string, documentReference: string | null) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/evidence`, { description, documentReference });
export const blockOpportunity = (id: string, structure: ProhibitedStructure, reason: string) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/block`, { structure, reason });
export const rejectOpportunity = (id: string, reason: string) => apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/reject`, { reason });
export const approveOpportunity = (id: string, acknowledgeHighRisk: boolean) =>
  apiClient.post<TaxOpportunityDto>(`/tax/opportunities/${id}/approve`, { acknowledgeHighRisk });
export const getTaxDecision = (id: string) => apiClient.get<TaxDecisionDto>(`/tax/decisions/${id}`);

export const listTaxCredits = () => apiClient.get<TaxCreditDto[]>("/tax/credits");
export const getTaxCreditTotals = () => apiClient.get<TaxCreditTotalsDto[]>("/tax/credits/totals");
export const registerTaxCredit = (body: RegisterTaxCreditRequest) => apiClient.post<TaxCreditDto>("/tax/credits", body);
export const moveTaxCredit = (id: string, body: CreditMovementRequest) => apiClient.post<TaxCreditDto>(`/tax/credits/${id}/movements`, body);

export const getSplitProvider = () => apiClient.get<{ configured: boolean; message: string }>("/tax/split/provider");
export const listSplitExpectations = (calculationId?: string) =>
  apiClient.get<SplitExpectationDto[]>(calculationId ? `/tax/split/expectations?calculationId=${calculationId}` : "/tax/split/expectations");
export const recordSplitSettlement = (id: string, actualAmount: number, externalReference: string, settledAt: string) =>
  apiClient.post<SplitExpectationDto>(`/tax/split/expectations/${id}/settlements`, { actualAmount, externalReference, settledAt });

export const listTaxRules = () => apiClient.get<TaxRuleDto[]>("/tax/rules");
export const listLegalSources = () => apiClient.get<TaxLegalSourceDto[]>("/tax/legal-sources");
