import { ApiError } from "@/services/api-client";
import { onlyDigits } from "@/lib/fiscal-codes";
import type {
  ComplianceAssessmentStatus, CompareSuppliersRequest, RecordComplianceAssessmentRequest, SupplyPattern, CreateTaxProfileVersionRequest, CreditMovement, CreditMovementRequest, CreditTax, RegisterTaxCreditRequest, TaxDirection, TaxItemKind, TaxLineRequest, TaxOperationRequest,
  TaxRegime, TaxScenarioRequest, TaxType,
} from "../api/tax.api";

// ── Numbers and dates ────────────────────────────────────────────────────────

/**
 * Reads a money/quantity typed the Brazilian way ("1.234,56") or the plain way ("1234.56").
 * Empty → null. Anything that is not a number → NaN (so validation can complain).
 */
export function parseDecimal(raw: string): number | null {
  const v = raw.trim().replace(/^R\$\s*/, "");
  if (v === "") return null;
  const normalized = v.includes(",") ? v.replace(/\./g, "").replace(",", ".") : v;
  if (!/^-?\d+(\.\d+)?$/.test(normalized)) return Number.NaN;
  return Number(normalized);
}

const isCents = (n: number) => Math.abs(Math.round(n * 100) - n * 100) < 1e-6;

/** Today in São Paulo as YYYY-MM-DD (the calendar the server uses). */
export function brazilTodayIso(now = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
}

/** "2026-01-15" → "15/01/2026" without passing through Date (no timezone shift). */
export function formatIsoDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
}

export function formatPercent(value: number | null | undefined, digits = 2): string {
  if (value === null || value === undefined) return "—";
  return `${value.toLocaleString("pt-BR", { minimumFractionDigits: 0, maximumFractionDigits: digits })}%`;
}

/** "a, b;c\nd" → ["a","b","c","d"] (trimmed, no empties). */
export function splitList(raw: string): string[] {
  return raw.split(/[,;\n]/).map((s) => s.trim()).filter(Boolean);
}

// ── Errors ───────────────────────────────────────────────────────────────────

/** The server refused because the establishment has no tax profile yet. */
export const isProfileMissing = (e: unknown) => e instanceof ApiError && e.code === "tax_profile_missing";

export const errorMessage = (e: unknown, fallback = "Não foi possível concluir.") =>
  e instanceof Error && e.message ? e.message : fallback;

// ── Operation lines ──────────────────────────────────────────────────────────

export interface LineDraft {
  key: string;
  itemKind: TaxItemKind;
  description: string;
  ncm: string;
  /** Services: NBS. */
  nbs: string;
  /** Recorded, not interpreted by engine V1 — but without it V1 does not calculate (no presumed general regime). */
  cClassTrib: string;
  quantity: string;
  grossAmount: string;
  unconditionalDiscount: string;
  conditionalDiscount: string;
  surcharges: string;
  excludedTaxes: string;
  /** "não sei": the ICMS/ISS/PIS/Cofins inside the price are not known — sent as null. */
  excludedTaxesUnknown: boolean;
  specialTreatment: string;
}

let lineSeq = 0;
export function emptyLine(): LineDraft {
  lineSeq += 1;
  return {
    key: `line-${lineSeq}`, itemKind: "Good", description: "", ncm: "", nbs: "", cClassTrib: "", quantity: "1", grossAmount: "",
    unconditionalDiscount: "", conditionalDiscount: "", surcharges: "", excludedTaxes: "", excludedTaxesUnknown: true,
    specialTreatment: "",
  };
}

/** Why the server would refuse this line — same rules, in words, before sending. */
export function lineProblems(line: LineDraft, position: number): string[] {
  const at = `Linha ${position}:`;
  const gross = parseDecimal(line.grossAmount);
  return [...identityProblems(line, at), ...grossProblems(gross, at), ...amountProblems(line, gross, at)];
}

function identityProblems(line: LineDraft, at: string): string[] {
  const p: string[] = [];
  if (!line.description.trim()) p.push(`${at} descreva o item.`);
  if (line.description.length > 300) p.push(`${at} descrição até 300 caracteres.`);
  if (line.ncm.trim() && onlyDigits(line.ncm).length !== 8) p.push(`${at} NCM tem 8 números.`);
  if (line.cClassTrib.trim() && !/^[A-Za-z0-9]{1,10}$/.test(line.cClassTrib.trim())) p.push(`${at} cClassTrib: até 10 letras ou números.`);
  const qty = parseDecimal(line.quantity);
  if (qty === null || Number.isNaN(qty) || qty <= 0) p.push(`${at} quantidade maior que zero.`);
  return p;
}

function grossProblems(gross: number | null, at: string): string[] {
  if (gross === null || Number.isNaN(gross) || gross < 0) return [`${at} informe o valor da linha.`];
  return isCents(gross) ? [] : [`${at} valor em centavos (2 casas).`];
}

function amountProblems(line: LineDraft, gross: number | null, at: string): string[] {
  const p: string[] = [];
  const money: [string, string][] = [
    [line.unconditionalDiscount, "desconto incondicional"],
    [line.conditionalDiscount, "desconto condicional"],
    [line.surcharges, "acréscimos"],
  ];
  if (!line.excludedTaxesUnknown) money.push([line.excludedTaxes, "ICMS/ISS/PIS/Cofins no preço"]);
  for (const [raw, name] of money) {
    const n = parseDecimal(raw);
    if (n !== null && (Number.isNaN(n) || n < 0 || !isCents(n))) p.push(`${at} ${name} em reais, sem valor negativo.`);
  }
  if (!line.excludedTaxesUnknown && parseDecimal(line.excludedTaxes) === null)
    p.push(`${at} informe o ICMS/ISS/PIS/Cofins no preço ou marque "não sei".`);
  const disc = (parseDecimal(line.unconditionalDiscount) ?? 0) + (parseDecimal(line.conditionalDiscount) ?? 0);
  if (gross !== null && !Number.isNaN(gross) && !Number.isNaN(disc) && disc > gross) p.push(`${at} os descontos passam do valor da linha.`);
  if (line.specialTreatment.length > 60) p.push(`${at} tratamento especial até 60 caracteres.`);
  return p;
}

export function linesProblems(lines: LineDraft[]): string[] {
  if (lines.length === 0) return ["Inclua ao menos uma linha."];
  if (lines.length > 500) return ["No máximo 500 linhas."];
  return lines.flatMap((l, i) => lineProblems(l, i + 1));
}

const money0 = (raw: string) => parseDecimal(raw) ?? 0;

export function toLineRequest(line: LineDraft): TaxLineRequest {
  const ncm = onlyDigits(line.ncm);
  return {
    itemKind: line.itemKind,
    description: line.description.trim(),
    ncm: ncm === "" ? null : ncm,
    nbs: line.nbs.trim() === "" ? null : line.nbs.trim(),
    cClassTrib: line.cClassTrib.trim() === "" ? null : line.cClassTrib.trim(),
    quantity: parseDecimal(line.quantity) ?? 1,
    grossAmount: money0(line.grossAmount),
    unconditionalDiscount: money0(line.unconditionalDiscount),
    conditionalDiscount: money0(line.conditionalDiscount),
    surcharges: money0(line.surcharges),
    excludedTaxes: line.excludedTaxesUnknown ? null : money0(line.excludedTaxes),
    specialTreatment: line.specialTreatment.trim() === "" ? null : line.specialTreatment.trim(),
  };
}

export interface OperationDraft {
  operationDate: string;
  direction: TaxDirection;
  counterpartyRegime: TaxRegime | "";
  counterpartyState: string;
  description: string;
  /** How the supply happens in time. V1 only calculates a single, immediate supply. */
  supplyPattern: SupplyPattern;
  lines: LineDraft[];
}

export function emptyOperation(today = brazilTodayIso()): OperationDraft {
  return {
    operationDate: today, direction: "Outbound", counterpartyRegime: "", counterpartyState: "", description: "", supplyPattern: "SingleSupply",
    lines: [emptyLine()],
  };
}

export function operationProblems(op: OperationDraft): string[] {
  const p: string[] = [];
  if (!/^\d{4}-\d{2}-\d{2}$/.test(op.operationDate)) p.push("Informe a data da operação.");
  if (op.counterpartyState && op.counterpartyState.length !== 2) p.push("UF da outra parte: 2 letras.");
  if (op.description.length > 300) p.push("Descrição até 300 caracteres.");
  return [...p, ...linesProblems(op.lines)];
}

export function toOperationRequest(op: OperationDraft): TaxOperationRequest {
  return {
    operationDate: op.operationDate,
    direction: op.direction,
    counterpartyRegime: op.counterpartyRegime === "" ? null : op.counterpartyRegime,
    counterpartyState: op.counterpartyState.trim() === "" ? null : op.counterpartyState.trim().toUpperCase(),
    description: op.description.trim() === "" ? null : op.description.trim(),
    lines: op.lines.map(toLineRequest),
    timing: { supplyPattern: op.supplyPattern },
  };
}

// ── Profile version ──────────────────────────────────────────────────────────

export interface ProfileDraft {
  validFrom: string;
  regime: TaxRegime;
  state: string;
  cityIbgeCode: string;
  primaryCnae: string;
  secondaryCnaes: string;
  specialRegimes: string;
  benefits: string;
  reason: string;
}

export function profileDraftProblems(d: ProfileDraft): string[] {
  const p: string[] = [];
  if (!/^\d{4}-\d{2}-\d{2}$/.test(d.validFrom)) p.push("Informe a data de início.");
  if (d.state.length !== 2) p.push("Escolha a UF.");
  if (onlyDigits(d.cityIbgeCode).length !== 7) p.push("Código IBGE do município: 7 números.");
  if (onlyDigits(d.primaryCnae).length !== 7) p.push("CNAE principal: 7 números.");
  if (splitList(d.secondaryCnaes).some((c) => onlyDigits(c).length !== 7)) p.push("Cada CNAE secundário tem 7 números.");
  if (!d.reason.trim()) p.push("Diga o motivo da nova versão.");
  if (d.reason.length > 500) p.push("Motivo até 500 caracteres.");
  return p;
}

export function toProfileVersionRequest(d: ProfileDraft): CreateTaxProfileVersionRequest {
  return {
    validFrom: d.validFrom,
    regime: d.regime,
    state: d.state,
    cityIbgeCode: onlyDigits(d.cityIbgeCode),
    primaryCnae: onlyDigits(d.primaryCnae),
    secondaryCnaes: splitList(d.secondaryCnaes).map(onlyDigits),
    specialRegimes: splitList(d.specialRegimes),
    benefits: splitList(d.benefits),
    reason: d.reason.trim(),
  };
}

// ── Ancillary-obligation assessment ──────────────────────────────────────────

export interface ComplianceDraft {
  status: ComplianceAssessmentStatus;
  periodFrom: string;
  periodTo: string;
  scope: string;
  /** One per line. */
  obligationsChecked: string;
  evidence: string;
  assessedBy: string;
  notes: string;
  limitations: string;
  pnctParticipation: boolean;
}

export const emptyCompliance = (): ComplianceDraft => ({
  status: "Declared", periodFrom: "", periodTo: "", scope: "", obligationsChecked: "", evidence: "", assessedBy: "", notes: "",
  limitations: "", pnctParticipation: false,
});

/** The same rules the server applies: a verification states period, scope, what was checked and the evidence. */
export function complianceProblems(d: ComplianceDraft): string[] {
  const p: string[] = [];
  const iso = /^\d{4}-\d{2}-\d{2}$/;
  if (!iso.test(d.periodFrom) || !iso.test(d.periodTo)) p.push("Informe o período (início e fim).");
  else if (d.periodTo < d.periodFrom) p.push("O fim do período é anterior ao início.");
  if (d.status !== "Unknown" && !d.scope.trim()) p.push("Informe o escopo (o que a avaliação cobre).");
  if (!d.assessedBy.trim()) p.push("Informe quem avaliou.");
  if ((d.status === "Verified" || d.status === "RemediationVerified") && (splitList(d.obligationsChecked).length === 0 || !d.evidence.trim()))
    p.push("Para verificar, diga quais obrigações foram conferidas e com qual evidência.");
  if (["InconsistencyIdentified", "UnderRemediation", "NonCompliant"].includes(d.status) && !d.notes.trim())
    p.push("Descreva a inconsistência ou o que está sendo regularizado.");
  return p;
}

export function toComplianceRequest(d: ComplianceDraft): RecordComplianceAssessmentRequest {
  const opt = (v: string) => (v.trim() === "" ? null : v.trim());
  return {
    status: d.status, periodFrom: d.periodFrom, periodTo: d.periodTo, scope: opt(d.scope), obligationsChecked: splitList(d.obligationsChecked),
    evidence: opt(d.evidence), assessedBy: d.assessedBy.trim(), assessedAt: null, notes: opt(d.notes), limitations: opt(d.limitations),
    pnctParticipation: d.pnctParticipation,
  };
}

// ── Simulation scenarios and supplier offers ─────────────────────────────────

export interface HypothesisDraft { key: string; tax: TaxType; ratePercent: string; label: string }

export interface ScenarioDraft {
  key: string;
  name: string;
  /** "" = keep what the baseline has. */
  regime: TaxRegime | "";
  referenceDate: string;
  direction: TaxDirection | "";
  counterpartyRegime: TaxRegime | "";
  convertConditionalDiscounts: boolean;
  assumeCreditable: boolean;
  hypotheses: HypothesisDraft[];
}

let draftSeq = 0;
const nextKey = (prefix: string) => { draftSeq += 1; return `${prefix}-${draftSeq}`; };

export function emptyScenario(name = ""): ScenarioDraft {
  return {
    key: nextKey("sc"), name, regime: "", referenceDate: "", direction: "", counterpartyRegime: "",
    convertConditionalDiscounts: false, assumeCreditable: false, hypotheses: [],
  };
}

export const emptyHypothesis = (): HypothesisDraft => ({ key: nextKey("hy"), tax: "Cbs", ratePercent: "", label: "" });

export function scenarioProblems(s: ScenarioDraft, position: number): string[] {
  const p: string[] = [];
  const at = `Cenário ${position}:`;
  if (!s.name.trim()) p.push(`${at} dê um nome.`);
  if (s.referenceDate && !/^\d{4}-\d{2}-\d{2}$/.test(s.referenceDate)) p.push(`${at} data inválida.`);
  if (s.hypotheses.length > 3) p.push(`${at} no máximo 3 alíquotas hipotéticas.`);
  for (const h of s.hypotheses) {
    const r = parseDecimal(h.ratePercent);
    if (r === null || Number.isNaN(r) || r < 0 || r > 100) p.push(`${at} alíquota hipotética entre 0 e 100%.`);
    if (!h.label.trim()) p.push(`${at} diga de onde vem a alíquota hipotética.`);
  }
  return p;
}

export function toScenarioRequest(s: ScenarioDraft): TaxScenarioRequest {
  return {
    name: s.name.trim(),
    regime: s.regime === "" ? null : s.regime,
    referenceDate: s.referenceDate === "" ? null : s.referenceDate,
    direction: s.direction === "" ? null : s.direction,
    counterpartyRegime: s.counterpartyRegime === "" ? null : s.counterpartyRegime,
    lines: null,
    convertConditionalDiscounts: s.convertConditionalDiscounts,
    rateHypotheses: s.hypotheses.length === 0 ? null : s.hypotheses.map((h) => ({ tax: h.tax, ratePercent: parseDecimal(h.ratePercent) ?? 0, label: h.label.trim() })),
    assumeCreditable: s.assumeCreditable,
  };
}

export function simulationProblems(name: string, baseline: OperationDraft, scenarios: ScenarioDraft[]): string[] {
  const p: string[] = [];
  if (!name.trim()) p.push("Dê um nome à simulação.");
  if (scenarios.length < 1 || scenarios.length > 10) p.push("De 1 a 10 cenários.");
  return [...p, ...operationProblems(baseline), ...scenarios.flatMap((s, i) => scenarioProblems(s, i + 1))];
}

export interface OfferDraft { key: string; supplier: string; regime: TaxRegime | ""; lines: LineDraft[] }

export const emptyOffer = (): OfferDraft => ({ key: nextKey("of"), supplier: "", regime: "", lines: [emptyLine()] });

export function offersProblems(offers: OfferDraft[]): string[] {
  if (offers.length < 2 || offers.length > 10) return ["Compare de 2 a 10 fornecedores."];
  return offers.flatMap((o, i) => {
    const name = o.supplier.trim() || `Fornecedor ${i + 1}`;
    const own = o.supplier.trim() ? [] : [`Fornecedor ${i + 1}: informe o nome.`];
    return [...own, ...linesProblems(o.lines).map((m) => `${name} — ${m}`)];
  });
}

export function toCompareRequest(operationDate: string, offers: OfferDraft[], assumeCreditable: boolean): CompareSuppliersRequest {
  return {
    operationDate,
    offers: offers.map((o) => ({ supplier: o.supplier.trim(), supplierRegime: o.regime === "" ? null : o.regime, lines: o.lines.map(toLineRequest) })),
    rateHypotheses: null,
    assumeCreditable,
  };
}

// ── Credits ──────────────────────────────────────────────────────────────────

export interface CreditDraft {
  tax: CreditTax;
  sourceDocumentType: string;
  sourceDocumentKey: string;
  supplierDocument: string;
  acquiredOn: string;
  expiresOn: string;
  originalAmount: string;
  /** An estimate — registering it validates nothing. */
  potentialAmount: string;
  potentialBasis: string;
}

export const emptyCredit = (today = brazilTodayIso()): CreditDraft => ({
  tax: "Cbs", sourceDocumentType: "NF-e", sourceDocumentKey: "", supplierDocument: "", acquiredOn: today, expiresOn: "",
  originalAmount: "", potentialAmount: "", potentialBasis: "",
});

const validMoney = (n: number | null, allowZero: boolean) => n !== null && !Number.isNaN(n) && (allowZero ? n >= 0 : n > 0) && isCents(n);

export function creditProblems(d: CreditDraft): string[] {
  const p: string[] = [];
  if (!d.sourceDocumentType.trim()) p.push("Informe o tipo do documento.");
  if (d.sourceDocumentKey.trim() && onlyDigits(d.sourceDocumentKey).length !== 44) p.push("A chave de acesso tem 44 números.");
  const doc = onlyDigits(d.supplierDocument);
  if (d.supplierDocument.trim() && doc.length !== 11 && doc.length !== 14) p.push("CPF ou CNPJ do fornecedor.");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(d.acquiredOn)) p.push("Informe a data de aquisição.");
  if (d.expiresOn && d.expiresOn < d.acquiredOn) p.push("O vencimento é anterior à aquisição.");
  const original = parseDecimal(d.originalAmount);
  const potential = parseDecimal(d.potentialAmount);
  if (!validMoney(original, false)) p.push("Valor do imposto destacado maior que zero.");
  if (!validMoney(potential, true)) p.push("Informe o valor estimado do crédito potencial.");
  else if (validMoney(original, false) && potential! > original!) p.push("O crédito potencial não passa do imposto destacado.");
  if (!d.potentialBasis.trim()) p.push("Diga de onde vem a estimativa.");
  return p;
}

export function toCreditRequest(d: CreditDraft): RegisterTaxCreditRequest {
  const key = onlyDigits(d.sourceDocumentKey);
  const doc = onlyDigits(d.supplierDocument);
  return {
    tax: d.tax,
    sourceDocumentType: d.sourceDocumentType.trim(),
    sourceDocumentKey: key === "" ? null : key,
    supplierDocument: doc === "" ? null : doc,
    acquiredOn: d.acquiredOn,
    expiresOn: d.expiresOn === "" ? null : d.expiresOn,
    originalAmount: parseDecimal(d.originalAmount) ?? 0,
    potentialAmount: parseDecimal(d.potentialAmount) ?? 0,
    potentialBasis: d.potentialBasis.trim(),
  };
}

export interface MovementDraft {
  movement: CreditMovement;
  amount: string;
  reason: string;
  obligationTax: CreditTax;
  obligationReference: string;
  onDate: string;
}

export function movementProblems(m: MovementDraft): string[] {
  const p: string[] = [];
  const noAmount = m.movement === "Expire" || m.movement === "StartRequirementsReview" || m.movement === "Discard";
  if (!noAmount && !validMoney(parseDecimal(m.amount), false)) p.push("Informe o valor (maior que zero).");
  if (["Block", "Reverse", "Discard"].includes(m.movement) && !m.reason.trim()) p.push("Diga o motivo.");
  if (m.movement === "ConfirmToBeAppropriated" && !m.reason.trim()) p.push("Diga a base da confirmação dos requisitos.");
  if (m.movement === "Use" && !m.obligationReference.trim()) p.push("Informe a obrigação (ex.: apuração 10/2026).");
  return p;
}

export function toMovementRequest(m: MovementDraft): CreditMovementRequest {
  const use = m.movement === "Use";
  return {
    movement: m.movement,
    // Expiry always writes off the whole available balance: it never carries an amount.
    amount: m.movement === "Expire" || (["StartRequirementsReview", "Discard"].includes(m.movement) && m.amount.trim() === "") ? null : parseDecimal(m.amount),
    reason: m.reason.trim() === "" ? null : m.reason.trim(),
    obligationTax: use ? m.obligationTax : null,
    obligationReference: use ? m.obligationReference.trim() : null,
    // Only a use may refer to another date; every other movement is registered today.
    onDate: use && m.onDate !== "" ? m.onDate : null,
  };
}
