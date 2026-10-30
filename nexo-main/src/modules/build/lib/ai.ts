import { ApiError } from "@/services/api-client";
import type {
  BuildAiSuggestionDto, BuildDocumentCategory, BuildRiskAnalysisDto, ClientUpdateDraftOutput, DailyLogDraftOutput,
  DocumentExtractionOutput, MapperSuggestionOutput, ProgressSuggestionOutput, RiskExplanationOutput,
} from "../api/build-ai.api";

/** 422 of the quality rule (same code as components/commercial/errors.ts). */
const STAGE_BLOCKED_CODE = "stage_has_critical_nonconformity";

/** Shared copy of the Build AI (docs/build/ai.md). */
export const AI_TAGLINE = "A IA sugere; você decide.";
export const AI_DISABLED_MESSAGE = "IA não habilitada para sua empresa. Fale com o suporte Orken.";
export const VISION_DISABLED_MESSAGE = "Leitura de fotos e arquivos pela IA não habilitada para sua empresa. Fale com o suporte Orken.";
export const AI_UNAVAILABLE_MESSAGE = "Nenhum provedor de IA disponível agora — tente mais tarde.";
export const AI_EXPIRED_MESSAGE = "Esta sugestão venceu ou já foi decidida. Gere uma nova.";
export const RULES_SOURCE_TEXT = "gerado por regras, sem IA";

export const CAPABILITY_LABEL: Record<string, string> = {
  DailyLogCopilot:      "Copiloto do diário",
  ProgressInterpreter:  "Interpretação do progresso",
  ModelMapper:          "Vínculos do modelo",
  ClientUpdate:         "Atualização ao cliente",
  DelayRisk:            "Explicação do risco de prazo",
  CostRisk:             "Explicação do risco de custo",
  DocumentIntelligence: "Análise de documento",
  WeeklyDigest:         "Resumo semanal",
};

export const STATUS_LABEL: Record<string, string> = {
  Pending: "Pendente", Accepted: "Aceita", PartiallyAccepted: "Aceita em parte", Rejected: "Recusada",
  Expired: "Vencida", Failed: "Falhou",
};

const FAILURE_REASON: Record<string, string> = {
  invalid_output:      "a resposta veio fora do formato esperado",
  invalid_credentials: "credenciais do provedor inválidas",
  rate_limited:        "o provedor limitou as requisições",
  timeout:             "o provedor demorou demais",
  refused:             "o provedor recusou o pedido",
  limit_reached:       "o limite mensal de uso de IA foi atingido",
};

/** Codes of the Build AI routes → a sentence for people (the server's own sentence wins when it sends one). */
const CODE_MESSAGE: Record<string, string> = {
  build_ai_disabled:     AI_DISABLED_MESSAGE,
  build_vision_disabled: VISION_DISABLED_MESSAGE,
  ai_provider_unavailable: AI_UNAVAILABLE_MESSAGE,
  ai_no_evidence:        "Não há registros no diário no período (nem fotos) para interpretar. Registre o diário primeiro.",
  model_not_ready:       "O modelo ainda não está pronto. Aguarde o processamento da versão.",
  ai_no_stages:          "Cadastre as etapas da obra antes de pedir vínculos.",
  ai_item_not_found:     "Um dos itens selecionados não faz parte da sugestão.",
  ai_input_required:     "Escreva o relato do dia.",
  ai_input_too_long:     "O relato passou de 8.000 caracteres.",
  too_many_photos:       "Selecione no máximo 5 fotos.",
  photo_not_found:       "Uma das fotos não foi encontrada no diário desta obra.",
  photo_too_large:       "Uma das fotos passa de 5 MB e não pode ser enviada à IA.",
  invalid_period:        "Período inválido (até hoje e no máximo 93 dias).",
  invalid_date:          "A data não pode ser futura.",
  invalid_file_content:  "O arquivo não é um PDF, JPEG, PNG ou WebP válido.",
  file_too_large:        "Arquivo grande demais.",
  storage_unavailable:   "Armazenamento de arquivos indisponível agora. Tente mais tarde.",
};

/** Message of a failed AI call (generate, accept, reject, analyze) for an inline alert or a toast. */
export function aiErrorMessage(e: unknown, fallback = "Não foi possível concluir."): string {
  if (!(e instanceof ApiError)) return e instanceof Error && e.message ? e.message : fallback;
  if (e.code === "build_ai_disabled") return AI_DISABLED_MESSAGE;
  if (e.code === "build_vision_disabled") return VISION_DISABLED_MESSAGE;
  if (e.code === "ai_provider_unavailable") return AI_UNAVAILABLE_MESSAGE;
  if (e.status === 503 && e.code === "ai_provider_failed") {
    const reason = e.details?.[0];
    const why = reason ? FAILURE_REASON[reason] ?? reason : null;
    return `A IA não conseguiu responder agora${why ? ` (${why})` : ""}. Nada foi gravado — tente novamente.`;
  }
  if (e.status === 409) return e.serverMessage || AI_EXPIRED_MESSAGE;
  if (e.code === STAGE_BLOCKED_CODE)
    return `${e.serverMessage || "Etapa com não conformidade crítica aberta."} Resolva ou cancele a não conformidade crítica `
      + "(aba Qualidade). A sugestão continua pendente.";
  if (e.status === 413) return "Arquivo grande demais (máximo 20 MB).";
  if (e.code && CODE_MESSAGE[e.code]) return e.serverMessage || CODE_MESSAGE[e.code];
  return e.serverMessage || e.message || fallback;
}

/** Codes that mean "the switch is off" — the button stays disabled after them. */
export function isGateError(e: unknown): boolean {
  return e instanceof ApiError && (e.code === "build_ai_disabled" || e.code === "build_vision_disabled");
}

// ── Formatting ────────────────────────────────────────────────────────────────

export function confidencePercent(c: unknown): number {
  const n = typeof c === "number" && Number.isFinite(c) ? c : 0;
  return Math.max(0, Math.min(100, Math.round(n * 100)));
}

export function fmtUsdMicros(micros: number): string {
  const usd = micros / 1_000_000;
  return `US$ ${usd.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: usd < 0.01 ? 6 : 4 })}`;
}

export const shortId = (id: string) => id.slice(0, 8);

export function isExpired(s: Pick<BuildAiSuggestionDto, "status" | "expiresAt">, now = Date.now()): boolean {
  if (s.status === "Expired") return true;
  return s.status === "Pending" && !!s.expiresAt && new Date(s.expiresAt).getTime() <= now;
}

// ── Resilient readers (the output is validated server-side, but the UI never trusts a shape blindly) ──

const obj = (v: unknown): Record<string, unknown> | null =>
  v && typeof v === "object" && !Array.isArray(v) ? (v as Record<string, unknown>) : null;
const arr = <T = unknown>(v: unknown): T[] => (Array.isArray(v) ? (v as T[]) : []);
const str = (v: unknown): string | null => (typeof v === "string" && v.trim() ? v : null);
const num = (v: unknown): number | null => (typeof v === "number" && Number.isFinite(v) ? v : null);
const strings = (v: unknown): string[] => arr(v).filter((x): x is string => typeof x === "string" && !!x.trim());

export function readDailyLogDraft(output: unknown): DailyLogDraftOutput {
  const o = obj(output) ?? {};
  return {
    date: str(o.date) ?? "",
    activities: arr(o.activities).map(obj).filter(Boolean).map((a) => ({
      stageId: str(a!.stageId), stageName: str(a!.stageName), description: str(a!.description) ?? "",
      progressHint: num(a!.progressHint), confidence: num(a!.confidence) ?? 0,
    })),
    workforce: arr(o.workforce).map(obj).filter(Boolean).map((w) => ({ role: str(w!.role) ?? "—", count: num(w!.count) ?? 0 })),
    equipment: strings(o.equipment),
    issues: strings(o.issues),
    weatherNote: str(o.weatherNote),
    clientSummary: str(o.clientSummary),
    confidence: num(o.confidence) ?? 0,
    discardedRefs: num(o.discardedRefs) ?? 0,
  };
}

export function readProgressSuggestion(output: unknown): ProgressSuggestionOutput {
  const o = obj(output) ?? {};
  return {
    items: arr(o.items).map(obj).filter(Boolean).map((i) => ({
      id: str(i!.id) ?? str(i!.stageId) ?? "", stageId: str(i!.stageId) ?? "", stageName: str(i!.stageName) ?? "Etapa",
      currentPercent: num(i!.currentPercent) ?? 0, suggestedPercent: num(i!.suggestedPercent) ?? 0,
      rationale: str(i!.rationale) ?? "", evidenceRefs: strings(i!.evidenceRefs), confidence: num(i!.confidence) ?? 0,
    })).filter((i) => !!i.id),
    discardedRefs: num(o.discardedRefs) ?? 0,
  };
}

export function readMapperSuggestion(output: unknown): MapperSuggestionOutput {
  const o = obj(output) ?? {};
  return {
    modelVersionId: str(o.modelVersionId) ?? "",
    bindings: arr(o.bindings).map(obj).filter(Boolean).map((b) => ({
      id: str(b!.id) ?? "", stageId: str(b!.stageId) ?? "", stageName: str(b!.stageName) ?? "Etapa",
      scope: str(b!.scope) ?? "Category", category: str(b!.category), zoneId: str(b!.zoneId), zoneName: str(b!.zoneName),
      confidence: num(b!.confidence) ?? 0, rationale: str(b!.rationale) ?? "",
    })).filter((b) => !!b.id),
    discardedRefs: num(o.discardedRefs) ?? 0,
  };
}

export function readClientUpdateDraft(output: unknown): ClientUpdateDraftOutput {
  const o = obj(output) ?? {};
  return {
    kind: str(o.kind) ?? "Update", from: str(o.from) ?? "", to: str(o.to) ?? "",
    title: str(o.title) ?? "", body: str(o.body) ?? "", highlights: strings(o.highlights),
  };
}

export function readRiskExplanation(output: unknown): RiskExplanationOutput {
  const o = obj(output) ?? {};
  return {
    kind: o.kind === "cost" ? "cost" : "delay",
    summary: str(o.summary) ?? "",
    items: arr(o.items).map(obj).filter(Boolean).map((i) => ({ key: str(i!.key) ?? "", explanation: str(i!.explanation) ?? "" })),
    recommendedActions: arr(o.recommendedActions).map(obj).filter(Boolean)
      .map((a) => ({ text: str(a!.text) ?? "", itemKeys: strings(a!.itemKeys) })).filter((a) => !!a.text),
    discardedRefs: num(o.discardedRefs) ?? 0,
  };
}

export function readDocumentExtraction(output: unknown): DocumentExtractionOutput {
  const o = obj(output) ?? {};
  return {
    documentType: str(o.documentType), issuer: str(o.issuer), issuerCnpj: str(o.issuerCnpj), number: str(o.number),
    issueDate: str(o.issueDate), dueDate: str(o.dueDate), validUntil: str(o.validUntil), totalAmount: num(o.totalAmount),
    items: arr(o.items).map(obj).filter(Boolean).map((i) => ({
      description: str(i!.description) ?? "", quantity: num(i!.quantity), unitPrice: num(i!.unitPrice), total: num(i!.total),
    })),
    summary: str(o.summary), confidence: num(o.confidence) ?? 0,
  };
}

/** A risk analysis as the UI uses it: anything that is not one (an empty body, a list) becomes null. */
export function readRiskAnalysis(v: unknown): BuildRiskAnalysisDto | null {
  const o = obj(v);
  if (!o || !obj(o.metrics)) return null;
  return {
    projectId: str(o.projectId) ?? "", kind: o.kind === "cost" ? "cost" : "delay", asOf: str(o.asOf) ?? "",
    level: str(o.level) ?? "none", metrics: o.metrics as Record<string, number | null>,
    items: arr(o.items).map(obj).filter(Boolean).map((i) => ({
      key: str(i!.key) ?? "", kind: str(i!.kind) ?? "", severity: str(i!.severity) ?? "low", title: str(i!.title) ?? "",
      evidence: str(i!.evidence) ?? "", metrics: (obj(i!.metrics) ?? {}) as Record<string, number | null>, refs: strings(i!.refs),
    })),
    methodology: str(o.methodology) ?? "",
  };
}

/** The ids a suggestion read, grouped for the "Fontes usadas" list. */
export interface ProvenanceGroup { key: string; label: string; ids: string[]; value?: string }

const REF_LABEL: Record<string, string> = {
  dailyLogIds: "Registros do diário", photoIds: "Fotos do diário", stageIds: "Etapas", zoneIds: "Zonas do modelo",
  modelVersionId: "Versão do modelo", itemKeys: "Itens da análise de risco", documentId: "Documento",
  changeOrderIds: "Aditivos", measurementIds: "Medições", eventIds: "Eventos de progresso",
};
const REF_VALUE_LABEL: Record<string, string> = {
  date: "Data", lookbackDays: "Dias analisados", textLength: "Tamanho do relato", categories: "Categorias do modelo",
  kind: "Tipo", methodology: "Metodologia", from: "De", to: "Até",
};

export function readProvenance(inputRefs: unknown): ProvenanceGroup[] {
  const o = obj(inputRefs);
  if (!o) return [];
  const out: ProvenanceGroup[] = [];
  for (const [key, v] of Object.entries(o)) {
    if (Array.isArray(v)) {
      const ids = v.filter((x): x is string => typeof x === "string");
      if (ids.length) out.push({ key, label: REF_LABEL[key] ?? key, ids });
    } else if (typeof v === "string" && /Id$/.test(key)) {
      out.push({ key, label: REF_LABEL[key] ?? key, ids: [v] });
    } else if (typeof v === "string" || typeof v === "number") {
      out.push({ key, label: REF_VALUE_LABEL[key] ?? key, ids: [], value: key === "textLength" ? `${v} caracteres` : String(v) });
    }
  }
  return out;
}

// ── Documents ─────────────────────────────────────────────────────────────────

export const DOCUMENT_CATEGORY_LABEL: Record<BuildDocumentCategory, string> = {
  Contrato: "Contrato", Projeto: "Projeto", ArtRrt: "ART / RRT", Alvara: "Alvará", NotaFiscal: "Nota fiscal",
  Laudo: "Laudo", Manual: "Manual", Garantia: "Garantia", Foto: "Foto", Outro: "Outro",
};
export const DOCUMENT_CATEGORIES = Object.keys(DOCUMENT_CATEGORY_LABEL) as BuildDocumentCategory[];

export const DOCUMENT_MAX_BYTES = 20 * 1024 * 1024;
export const DOCUMENT_AI_MAX_BYTES = 5 * 1024 * 1024;
export const DOCUMENT_TYPES = ["application/pdf", "image/jpeg", "image/png", "image/webp"];
const DOCUMENT_EXT = /\.(pdf|jpe?g|png|webp)$/i;

/** Same limits as the server (PDF/JPEG/PNG/WEBP ≤ 20 MB) — only to explain before sending. */
export function documentFileProblem(file: File): string | null {
  const typeOk = DOCUMENT_TYPES.includes(file.type) || (!file.type && DOCUMENT_EXT.test(file.name));
  if (!typeOk) return "Formato não aceito. Envie PDF, JPEG, PNG ou WEBP.";
  if (file.size > DOCUMENT_MAX_BYTES) return "Arquivo grande demais: o limite é 20 MB.";
  if (file.size === 0) return "O arquivo está vazio.";
  return null;
}

/** Validity badge: "Vencido", "Vence hoje", "Vence em N dias" (≤ 30) or "Válido até …". */
export function validityBadge(validUntil: string | null, today: string): { label: string; tone: "expired" | "soon" | "ok" } | null {
  if (!validUntil) return null;
  const days = Math.round((Date.parse(`${validUntil.slice(0, 10)}T12:00:00`) - Date.parse(`${today}T12:00:00`)) / 86_400_000);
  if (days < 0) return { label: "Vencido", tone: "expired" };
  if (days === 0) return { label: "Vence hoje", tone: "soon" };
  if (days <= 30) return { label: `Vence em ${days} dia${days === 1 ? "" : "s"}`, tone: "soon" };
  return { label: `Válido até ${validUntil.slice(8, 10)}/${validUntil.slice(5, 7)}/${validUntil.slice(0, 4)}`, tone: "ok" };
}

export function fmtBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toLocaleString("pt-BR", { maximumFractionDigits: 0 })} KB`;
  return `${(n / 1024 / 1024).toLocaleString("pt-BR", { maximumFractionDigits: 1 })} MB`;
}

/** The newest pending (and still valid) suggestion of a list — what a panel shows when it opens. */
export function latestPending(list: unknown, now = Date.now()): BuildAiSuggestionDto | null {
  const items = arr<BuildAiSuggestionDto>(list).filter((s) => obj(s) && s.status === "Pending" && !isExpired(s, now));
  items.sort((a, b) => String(b.createdAt).localeCompare(String(a.createdAt)));
  return items[0] ?? null;
}
