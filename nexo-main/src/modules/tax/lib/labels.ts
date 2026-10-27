import { TAX_REGIMES } from "@/modules/fiscal/api/fiscal.api";
import type {
  BaseComponent, ConditionOutcome, CreditEntryType, CreditMovement, CreditTax, ImpactNature, OpportunityStatus,
  OpportunityType, ProhibitedStructure, RiskLevel, RuleStatus, SplitProcedure, SplitStatus, TaxDirection, TaxItemKind,
  TaxRegime, TaxTransactionSource, TaxType,
} from "../api/tax.api";

/** Visual tone of a badge: each impact nature has its own, so they are never read as the same thing. */
export type Tone = "saving" | "deferral" | "cash" | "credit" | "risk" | "neutral" | "danger" | "warning" | "success";

export interface ToneLabel { label: string; tone: Tone }

export const IMPACT_NATURES: Record<ImpactNature, ToneLabel & { hint: string }> = {
  DefinitiveSaving: { label: "Economia", tone: "saving", hint: "Imposto que deixa de ser pago de vez." },
  Deferral: { label: "Diferimento — não é economia", tone: "deferral", hint: "O imposto é pago depois, não deixa de ser pago." },
  CashFlow: { label: "Fluxo de caixa", tone: "cash", hint: "Muda quando o dinheiro sai ou entra, não quanto." },
  Credit: { label: "Crédito", tone: "credit", hint: "Valor que pode abater imposto devido, se as condições forem cumpridas." },
  RiskAvoided: { label: "Risco evitado", tone: "risk", hint: "Evita autuação ou cobrança; não reduz o imposto normal." },
};

export const OPPORTUNITY_STATUS: Record<OpportunityStatus, ToneLabel> = {
  Identified: { label: "Identificada", tone: "neutral" },
  InReview: { label: "Em análise", tone: "warning" },
  Approved: { label: "Aprovada", tone: "success" },
  Rejected: { label: "Recusada", tone: "neutral" },
  Blocked: { label: "Bloqueada", tone: "danger" },
};

export const OPPORTUNITY_TYPES: Record<OpportunityType, string> = {
  TaxSaving: "Redução de imposto",
  CreditOptimization: "Aproveitamento de crédito",
  TaxDeferral: "Adiamento de imposto",
  CashFlowOptimization: "Caixa",
  RefundOpportunity: "Restituição",
  RiskReduction: "Redução de risco",
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
};

export const CREDIT_ENTRY_TYPES: Record<CreditEntryType, string> = {
  Recognized: "Reconhecido",
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
  deferral: "border-warning/40 bg-warning/10 text-warning",
  cash: "border-sky-500/40 bg-sky-500/10 text-sky-600 dark:text-sky-400",
  credit: "border-teal-500/40 bg-teal-500/10 text-teal-700 dark:text-teal-300",
  risk: "border-violet-500/40 bg-violet-500/10 text-violet-700 dark:text-violet-300",
  neutral: "border-border bg-muted text-muted-foreground",
  danger: "border-destructive/40 bg-destructive/10 text-destructive",
  warning: "border-warning/40 bg-warning/10 text-warning",
  success: "border-success/40 bg-success/10 text-success",
};
