import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import type {
  ApprovalRequirement, BaseComponent, ComplianceAssessmentStatus, ConditionOutcome, CoverageStatus, CreditLifecycle, EvidenceStatus, EvidenceType,
  RequirementPhase, SupplyPattern, CreditEntryType, CreditMovement, CreditTax, ImpactNature, OpportunityStatus,
  InternalApprovalStatus, OpportunityType, ProfessionalReviewStatus, ProhibitedStructure, RiskLevel, RuleStatus, SplitProcedure, SplitStatus, TaxDirection,
  TaxApprovalMode, TaxItemKind, TaxRegime, TaxTransactionSource, TaxType, TaxVerificationDto, TextVerificationStatus,
} from "../api/tax.api";

/** Visual tone of a badge: each impact nature has its own, so they are never read as the same thing. */
export type Tone = "saving" | "reduction" | "deferral" | "cash" | "credit" | "risk" | "neutral" | "danger" | "warning" | "success" | "pilot";

export interface ToneLabel { label: string; tone: Tone }

export const IMPACT_NATURES: Record<ImpactNature, ToneLabel & { hint: string }> = {
  DefinitiveSaving: { label: "Economia", tone: "saving", hint: "Imposto que deixa de ser pago de vez." },
  CalculatedTaxReduction: {
    label: "Redução simulada do IBS/CBS calculado",
    tone: "reduction",
    hint: "O tributo calculado no documento diminui. Se isso vira economia depende do período (em 2026 é compensável ou dispensado) e de revisão profissional.",
  },
  Deferral: { label: "Diferimento — não é economia", tone: "deferral", hint: "O imposto é pago depois, não deixa de ser pago." },
  CashFlow: { label: "Fluxo de caixa", tone: "cash", hint: "Muda quando o dinheiro sai ou entra, não quanto." },
  Credit: { label: "Crédito", tone: "credit", hint: "Valor que pode abater imposto devido, se as condições forem cumpridas." },
  RiskAvoided: { label: "Risco evitado", tone: "risk", hint: "Evita autuação ou cobrança; não reduz o imposto normal." },
};

export const OPPORTUNITY_STATUS: Record<OpportunityStatus, ToneLabel> = {
  Identified: { label: "Identificada", tone: "neutral" },
  InReview: { label: "Em análise", tone: "warning" },
  Approved: { label: "Cenário aprovado", tone: "success" },
  Rejected: { label: "Recusada", tone: "neutral" },
  Blocked: { label: "Bloqueada", tone: "danger" },
  ExecutionVerified: { label: "Execução verificada", tone: "success" },
  ExecutionDiverged: { label: "Execução divergente", tone: "danger" },
};

export const OPPORTUNITY_TYPES: Record<OpportunityType, string> = {
  TaxSaving: "Redução de imposto",
  CreditOptimization: "Aproveitamento de crédito",
  TaxDeferral: "Adiamento de imposto",
  CashFlowOptimization: "Caixa",
  RefundOpportunity: "Restituição",
  RiskReduction: "Redução de risco",
  CalculatedTaxReduction: "Redução do IBS/CBS calculado",
};

export const RISK_LEVELS: Record<RiskLevel, ToneLabel> = {
  Low: { label: "Risco baixo", tone: "success" },
  Medium: { label: "Risco médio", tone: "warning" },
  High: { label: "Risco alto", tone: "danger" },
};

export const PROHIBITED_STRUCTURES: Record<ProhibitedStructure, string> = {
  Simulation: "Simulação (negócio que não aconteceu de verdade)",
  ArtificialTransaction: "Operação artificial, sem propósito além do imposto",
  RevenueOmission: "Omissão de receita",
  FalseExpense: "Despesa falsa",
  FalseSupplier: "Fornecedor de fachada",
  UnderInvoicing: "Subfaturamento",
  DocumentMismatch: "Documento que não corresponde à operação",
  PersonalExpenseAsBusiness: "Gasto pessoal lançado como da empresa",
};

export const TAX_NAMES: Record<TaxType, string> = { Cbs: "CBS", IbsState: "IBS estadual", IbsMunicipal: "IBS municipal" };
export const CREDIT_TAX_NAMES: Record<CreditTax, string> = { Cbs: "CBS", Ibs: "IBS" };

export const RULE_STATUS: Record<RuleStatus, ToneLabel> = {
  Draft: { label: "Rascunho", tone: "neutral" },
  PendingRegulation: { label: "Aguardando regulamentação", tone: "warning" },
  Unverified: { label: "Fonte não conferida", tone: "warning" },
  Active: { label: "Em vigor", tone: "success" },
  Superseded: { label: "Substituída", tone: "neutral" },
  Revoked: { label: "Revogada", tone: "danger" },
};

/** Text verification says only that the excerpt matches the official text. */
export const TEXT_VERIFICATION: Record<TextVerificationStatus, ToneLabel> = {
  Verified: { label: "Texto oficial conferido", tone: "success" },
  Unverified: { label: "Texto não conferido", tone: "warning" },
};

/** A tax professional's review of the rule — technical activation is not professional approval. */
export const PROFESSIONAL_REVIEW: Record<ProfessionalReviewStatus, ToneLabel> = {
  Pending: { label: "Revisão profissional pendente", tone: "warning" },
  ReviewReceived: { label: "Revisão recebida — não é aprovação", tone: "warning" },
  Approved: { label: "Revisão profissional aprovada", tone: "success" },
  ApprovedWithCaveats: { label: "Revisão profissional aprovou com ressalvas", tone: "success" },
  ChangesRequired: { label: "Revisão profissional pediu ajustes", tone: "danger" },
  Rejected: { label: "Revisão profissional rejeitou", tone: "danger" },
};

/**
 * The internal product/governance approval for the controlled pilot. It sits beside the professional
 * review — both can show at once — and never reads as a professional approval.
 */
export const INTERNAL_APPROVAL: Record<InternalApprovalStatus, ToneLabel> = {
  ApprovedForControlledPilot: { label: "Aprovado internamente para piloto controlado", tone: "pilot" },
  NotApproved: { label: "Sem aprovação para o piloto", tone: "neutral" },
  Withdrawn: { label: "Aprovação para o piloto retirada", tone: "danger" },
};

export const APPROVAL_MODE: Record<TaxApprovalMode, ToneLabel> = {
  Disabled: { label: "Nenhum modo de aprovação configurado", tone: "warning" },
  ControlledPilot: { label: "Piloto controlado", tone: "pilot" },
  ProfessionalFinal: { label: "Produção definitiva (só aprovação profissional)", tone: "neutral" },
};

/** Said wherever the internal approval shows: it is a governance decision, not a professional opinion. */
export const INTERNAL_APPROVAL_NOTICE =
  "Decisão interna de produto e governança da Andrade Systems para o piloto controlado. Não é parecer jurídico, contábil ou tributário profissional.";

/**
 * Ancillary-obligation assessments, per period and scope. Only "Verificado" satisfies a rule that
 * depends on compliance — and even then the copy says "verified in the period and scope", never "compliant".
 */
export const COMPLIANCE_STATUS: Record<ComplianceAssessmentStatus, ToneLabel & { hint: string }> = {
  Unknown: { label: "Não informado", tone: "neutral", hint: "Nenhuma conclusão é aplicada." },
  Declared: { label: "Declarado, não verificado", tone: "warning", hint: "Declarar não é verificar: não satisfaz regras que dependem do cumprimento." },
  Verified: { label: "Verificado no período e escopo", tone: "success", hint: "Obrigações verificadas no período e no escopo indicados." },
  InconsistencyIdentified: { label: "Inconsistência identificada", tone: "danger", hint: "Não equivale a verificado." },
  UnderRemediation: { label: "Em regularização", tone: "warning", hint: "Regularização em curso (ex.: PNCT) não equivale a verificado." },
  RemediationVerified: { label: "Regularização verificada", tone: "warning", hint: "Não equivale automaticamente a verificado; depende de revisão profissional." },
  NonCompliant: { label: "Não cumpre", tone: "danger", hint: "Regras que dependem do cumprimento não se aplicam." },
  NotApplicable: { label: "Não aplicável", tone: "neutral", hint: "Nenhuma conclusão é aplicada." },
};

export const COMPLIANCE_VERIFIED_COPY = "Obrigações verificadas no período e no escopo indicados.";
export const SYSTEM_CALCULATION_COPY = "Cálculo do sistema com as regras e os dados indicados.";
export const SCENARIO_APPROVED_COPY = "Cenário aprovado no escopo indicado. Não executa alteração comercial ou fiscal.";
export const SPLIT_EXPECTATION_COPY = "Expectativa gerencial de segregação. Não comprova retenção ou recolhimento.";
export const POTENTIAL_CREDIT_COPY =
  "Crédito potencial estimado. Não representa validação de elegibilidade, apropriação ou disponibilidade para utilização.";
export const NOT_SUPPORTED_COPY = "Operação não suportada pela versão atual da engine.";

export const COVERAGE_STATUS: Record<CoverageStatus, ToneLabel> = {
  Supported: { label: "Suportada pela V1", tone: "success" },
  Unsupported: { label: "Não suportada", tone: "danger" },
  RequiresClassification: { label: "Falta classificação", tone: "warning" },
  RequiresProfessionalReview: { label: "Precisa de revisão profissional", tone: "warning" },
  PendingRegulation: { label: "Aguardando regulamentação", tone: "warning" },
};

export const SUPPLY_PATTERNS: Record<SupplyPattern, string> = {
  SingleSupply: "Fornecimento único e imediato",
  ContinuedSupply: "Fornecimento continuado ou fracionado",
  AdvancePayment: "Pagamento antecipado",
  FutureDelivery: "Entrega futura",
  PublicProcurement: "Aquisição por órgão público",
  Other: "Outro",
};

export const REQUIREMENT_PHASES: Record<RequirementPhase, string> = {
  ScenarioApproval: "Antes da operação (cenário)",
  ExecutionVerification: "Depois da operação (execução)",
};

export const EVIDENCE_TYPES: Record<EvidenceType, string> = {
  CommercialInstrument: "Instrumento comercial (proposta, pedido, contrato, aditivo)",
  FiscalDocument: "Documento fiscal autorizado",
  FiscalDocumentEvent: "Evento do documento fiscal",
  PaymentRecord: "Comprovante de pagamento",
  Other: "Outro",
};

export const EVIDENCE_STATUS: Record<EvidenceStatus, ToneLabel> = {
  Submitted: { label: "Enviada", tone: "neutral" },
  Accepted: { label: "Aceita", tone: "success" },
  Rejected: { label: "Recusada", tone: "danger" },
};

export const APPROVAL_REQUIREMENTS: Record<ApprovalRequirement, string> = {
  SelfApprovalWithJustification: "Quem analisou pode aprovar, registrando justificativa",
  IdentifiedReviewer: "Outra pessoa identificada precisa ter revisado",
  IndependentApprover: "Aprovador independente: não pode ter participado da análise",
};

export const CREDIT_LIFECYCLE: Record<CreditLifecycle, ToneLabel> = {
  PotentialIdentified: { label: "Potencial estimado", tone: "neutral" },
  RequirementsUnderReview: { label: "Requisitos em revisão", tone: "warning" },
  ToBeAppropriated: { label: "A apropriar", tone: "credit" },
  Appropriated: { label: "Apropriado", tone: "success" },
  Used: { label: "Utilizado", tone: "neutral" },
  Blocked: { label: "Bloqueado", tone: "danger" },
  Reversed: { label: "Estornado", tone: "neutral" },
  Expired: { label: "Vencido", tone: "neutral" },
  Discarded: { label: "Descartado", tone: "neutral" },
};

export const CONDITION_OUTCOMES: Record<ConditionOutcome, ToneLabel> = {
  Passed: { label: "Atendida", tone: "success" },
  Failed: { label: "Não atendida", tone: "danger" },
  Unknown: { label: "Não informada", tone: "warning" },
};

export const BASE_COMPONENTS: Record<BaseComponent, string> = {
  Surcharges: "Acréscimos",
  UnconditionalDiscount: "Desconto incondicional",
  ConditionalDiscount: "Desconto condicional",
  ExcludedTaxes: "ICMS/ISS/PIS/Cofins no preço",
};

export const CREDIT_MOVEMENTS: Record<CreditMovement, string> = {
  Appropriate: "Apropriar",
  Use: "Usar contra imposto devido",
  Block: "Bloquear",
  Unblock: "Desbloquear",
  Reverse: "Estornar apropriação",
  Expire: "Dar como vencido",
  StartRequirementsReview: "Iniciar revisão dos requisitos",
  ConfirmToBeAppropriated: "Confirmar requisitos (a apropriar)",
  Discard: "Descartar o crédito potencial",
};

export const CREDIT_ENTRY_TYPES: Record<CreditEntryType, string> = {
  Recognized: "Requisitos confirmados (a apropriar)",
  Appropriated: "Apropriado",
  Used: "Usado",
  Blocked: "Bloqueado",
  Unblocked: "Desbloqueado",
  Expired: "Vencido",
  Reversed: "Estornado",
};

export const SPLIT_STATUS: Record<SplitStatus, ToneLabel> = {
  Pending: { label: "Aguardando retenção", tone: "neutral" },
  Matched: { label: "Conferido", tone: "success" },
  Excess: { label: "Retido a mais", tone: "warning" },
  Insufficient: { label: "Retido a menos", tone: "danger" },
};

export const SPLIT_PROCEDURES: Record<SplitProcedure, string> = { Standard: "Padrão", Simplified: "Simplificado" };

export const DIRECTIONS: Record<TaxDirection, string> = { Outbound: "Venda / saída", Inbound: "Compra / entrada" };
export const ITEM_KINDS: Record<TaxItemKind, string> = { Good: "Mercadoria", Service: "Serviço" };

export const SOURCES: Record<TaxTransactionSource, string> = {
  Manual: "Manual",
  Sale: "Venda",
  Purchase: "Compra",
  Service: "Serviço",
  FinancialEntry: "Lançamento financeiro",
  FiscalDocument: "Documento fiscal",
  External: "Externo",
};

export function regimeLabel(regime: TaxRegime | null | undefined): string {
  if (!regime) return "Não informado";
  return TAX_REGIMES.find((r) => r.value === regime)?.label ?? regime;
}

/** Label for an enum value the frontend may not know yet (a newer server): never crashes, shows the raw name. */
export function labelOf<K extends string>(map: Record<K, string | ToneLabel>, key: K | string | null | undefined): string {
  if (!key) return "—";
  const v = (map as Record<string, string | ToneLabel>)[key];
  if (v === undefined) return key;
  return typeof v === "string" ? v : v.label;
}

export function toneOf<K extends string>(map: Record<K, ToneLabel>, key: K | string): Tone {
  return (map as Record<string, ToneLabel>)[key]?.tone ?? "neutral";
}

/** Tailwind classes for each tone. Indigo stays reserved for actions, so no tone uses primary. */
export const TONE_CLASSES: Record<Tone, string> = {
  saving: "border-success/40 bg-success/10 text-success",
  reduction: "border-slate-500/40 bg-slate-500/10 text-slate-700 dark:text-slate-300",
  deferral: "border-warning/40 bg-warning/10 text-warning",
  cash: "border-sky-500/40 bg-sky-500/10 text-sky-600 dark:text-sky-400",
  credit: "border-teal-500/40 bg-teal-500/10 text-teal-700 dark:text-teal-300",
  risk: "border-violet-500/40 bg-violet-500/10 text-violet-700 dark:text-violet-300",
  neutral: "border-border bg-muted text-muted-foreground",
  danger: "border-destructive/40 bg-destructive/10 text-destructive",
  warning: "border-warning/40 bg-warning/10 text-warning",
  success: "border-success/40 bg-success/10 text-success",
  // The internal pilot approval: its own tone, never the green of a professional approval.
  pilot: "border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300",
};

/** What a verification proved, in plain words. The engine that made a calculation is the only one that re-runs it. */
export function verificationHeadline(v: TaxVerificationDto): string {
  if (!v.engineAvailable) return `Não foi possível reproduzir: o motor ${v.storedEngineVersion} que fez este cálculo não está disponível. Nenhum outro motor foi usado no lugar dele.`;
  if (v.reproducible) return "Reproduzido: o mesmo motor, as mesmas regras e a mesma entrada dão exatamente o mesmo resultado.";
  return "Não reproduziu: o resultado recalculado é diferente do guardado.";
}
