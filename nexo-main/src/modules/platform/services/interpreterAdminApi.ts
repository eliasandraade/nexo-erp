import { apiClient } from "@/services/api-client";
import type {
  AiProviderConfig,
  AiDashboardStats,
  AiTelemetryRecord,
  TenantCostSummary,
  PlaygroundResult,
  PromptVersion,
} from "../types/aiOperations";

const BASE = "/platform/interpreter";

// ── Providers ─────────────────────────────────────────────────────────────────

interface RawProvider {
  id: string;
  name: string;
  provider: string;
  isEnabled: boolean;
  isDefault: boolean;
  modelId: string | null;
  apiKeyLastFour: string | null;
  hasApiKey: boolean;
  monthlyTokenLimit: number | null;
  costPerInputToken: number;   // USD per token (backend divides micros by 1M)
  costPerOutputToken: number;
  priority: number;
  updatedAt: string;
}

function mapProvider(r: RawProvider): AiProviderConfig {
  return {
    id:                       r.id,
    name:                     r.name,
    provider:                 r.provider as AiProviderConfig["provider"],
    isEnabled:                r.isEnabled,
    isDefault:                r.isDefault,
    apiKeyLastFour:           r.apiKeyLastFour,
    modelId:                  r.modelId,
    monthlyTokenLimit:        r.monthlyTokenLimit,
    costPerInputTokenMicros:  Math.round(r.costPerInputToken  * 1_000_000),
    costPerOutputTokenMicros: Math.round(r.costPerOutputToken * 1_000_000),
    fallbackProviderId:       null,   // not exposed from backend yet
    priority:                 r.priority,
    updatedAt:                r.updatedAt,
  };
}

export async function fetchAiProviders(): Promise<AiProviderConfig[]> {
  const raw = await apiClient.get<RawProvider[]>(`${BASE}/providers`);
  return raw.map(mapProvider);
}

export async function updateAiProvider(
  id: string,
  patch: {
    isEnabled?:              boolean;
    isDefault?:              boolean;
    monthlyTokenLimit?:      number | null;
    costPerInputTokenMicros?:  number;
    costPerOutputTokenMicros?: number;
  }
): Promise<void> {
  await apiClient.patch<void>(`${BASE}/providers/${id}`, {
    isEnabled:              patch.isEnabled,
    isDefault:              patch.isDefault,
    monthlyTokenLimit:      patch.monthlyTokenLimit,
    costPerInputTokenMicros:  patch.costPerInputTokenMicros,
    costPerOutputTokenMicros: patch.costPerOutputTokenMicros,
  });
}

export async function rotateAiProviderKey(
  id: string,
  apiKey: string
): Promise<{ lastFour: string | null }> {
  return apiClient.post<{ lastFour: string | null }>(
    `${BASE}/providers/${id}/rotate-key`,
    { apiKey }
  );
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

interface RawDashboard {
  today:   { total: number; llm: number; errors: number; costUsd: number };
  week7d:  { total: number; llm: number; acceptanceRate: number; correctionCount: number; avgAmountConf: number; avgDateConf: number; costUsd: number };
  month30d: { total: number; costUsd: number };
  providerSplit: { provider: string; count: number }[];
  dailyChart:    { date: string; total: number; llm: number }[];
  topTenants:    { tenantId: string; tenantName: string; count: number }[];
}

function mapDashboard(r: RawDashboard): AiDashboardStats {
  const w7 = r.week7d;
  const ruleCount   = r.providerSplit.find(p => p.provider === "RuleBased")?.count  ?? 0;
  const claudeCount = r.providerSplit.find(p => p.provider === "Claude")?.count     ?? 0;
  const openAiCount = r.providerSplit.find(p => p.provider === "OpenAI")?.count     ?? 0;
  const total7      = w7.total || 1; // avoid /0

  return {
    requestsToday:      r.today.total,
    requestsLast7d:     w7.total,
    ruleBasedPct:       Math.round((ruleCount   / total7) * 100),
    claudePct:          Math.round((claudeCount / total7) * 100),
    openAiPct:          Math.round((openAiCount / total7) * 100),
    avgConfidence:      Math.round(((w7.avgAmountConf + w7.avgDateConf) / 2) * 1000) / 1000,
    acceptanceRate:     w7.acceptanceRate / 100,   // backend returns 0-100, type expects 0-1
    correctionRate:     w7.total > 0 ? w7.correctionCount / w7.total : 0,
    reprocessRate:      0,   // not tracked separately yet
    activeTenantsCount: r.topTenants.length,
    requestsByDay: r.dailyChart.map(d => ({
      date:      d.date,
      total:     d.total,
      ruleBased: d.total - d.llm,
      llm:       d.llm,
    })),
    topCorrectedFields: [],   // not tracked server-side yet
    topTenants: r.topTenants.map(t => ({
      tenantId:   t.tenantId,
      tenantName: t.tenantName,
      requests:   t.count,
    })),
  };
}

export async function fetchAiDashboard(): Promise<AiDashboardStats> {
  const raw = await apiClient.get<RawDashboard>(`${BASE}/dashboard`);
  return mapDashboard(raw);
}

// ── Telemetry ─────────────────────────────────────────────────────────────────

interface RawTelemetryRow {
  id:                   string;
  tenantId:             string;
  tenantName:           string;
  userId:               string | null;
  movementId:           string | null;
  operationType:        string;
  provider:             string;
  promptType:           string;
  promptVersion:        string;
  promptHash:           string;
  inputTokens:          number;
  outputTokens:         number;
  costUsd:              number;
  durationMs:           number;
  success:              boolean;
  errorMessage:         string | null;
  fallbackUsed:         boolean;
  fallbackFromProvider: string | null;
  requiresInputCount:   number;
  amountConfidence:     number;
  dateConfidence:       number;
  createdAt:            string;
}

interface RawTelemetryPage {
  total:      number;
  page:       number;
  pageSize:   number;
  totalPages: number;
  items:      RawTelemetryRow[];
}

function mapTelemetryRow(r: RawTelemetryRow): AiTelemetryRecord {
  return {
    id:                   r.id,
    tenantId:             r.tenantId,
    tenantName:           r.tenantName,
    userId:               r.userId ?? "",
    movementId:           r.movementId,
    operationType:        r.operationType as AiTelemetryRecord["operationType"],
    provider:             r.provider     as AiTelemetryRecord["provider"],
    promptType:           r.promptType,
    promptVersion:        r.promptVersion,
    promptHash:           r.promptHash,
    inputTokens:          r.inputTokens,
    outputTokens:         r.outputTokens,
    estimatedCostMicros:  Math.round(r.costUsd * 1_000_000),
    durationMs:           r.durationMs,
    success:              r.success,
    errorMessage:         r.errorMessage,
    fallbackUsed:         r.fallbackUsed,
    fallbackFromProvider: r.fallbackFromProvider as AiTelemetryRecord["fallbackFromProvider"],
    analyzerChain:        [r.provider as AiTelemetryRecord["provider"]],
    requiresInputCount:   r.requiresInputCount,
    amountConfidence:     r.amountConfidence,
    dateConfidence:       r.dateConfidence,
    rawPrompt:            null,
    rawResponse:          null,
    createdAt:            r.createdAt,
  };
}

export async function fetchAiTelemetry(
  page = 1,
  pageSize = 20,
  filters?: { provider?: string; opType?: string; success?: boolean }
): Promise<{ items: AiTelemetryRecord[]; total: number }> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (filters?.provider) params.set("provider", filters.provider);
  if (filters?.opType)   params.set("opType",   filters.opType);
  if (filters?.success !== undefined) params.set("success", String(filters.success));

  const raw = await apiClient.get<RawTelemetryPage>(`${BASE}/telemetry?${params}`);
  return {
    items: raw.items.map(mapTelemetryRow),
    total: raw.total,
  };
}

// ── Costs ─────────────────────────────────────────────────────────────────────

interface RawCostItem {
  tenantId:      string;
  tenantName:    string;
  costUsd:       number;
  totalCalls:    number;
  llmCalls:      number;
  softLimitUsd:  number | null;
  hardLimitUsd:  number | null;
  softLimitHit:  boolean;
  hardLimitHit:  boolean;
}

interface RawCostsResponse {
  totalUsd: number;
  items:    RawCostItem[];
}

function mapCostItem(r: RawCostItem): TenantCostSummary {
  return {
    tenantId:           r.tenantId,
    tenantName:         r.tenantName,
    requests:           r.totalCalls,
    inputTokens:        0,   // not aggregated separately in backend yet
    outputTokens:       0,
    estimatedCostMicros: Math.round(r.costUsd * 1_000_000),
    softLimitCents:     r.softLimitUsd != null ? Math.round(r.softLimitUsd * 100) : null,
    hardLimitCents:     r.hardLimitUsd != null ? Math.round(r.hardLimitUsd * 100) : null,
    spentCents:         Math.round(r.costUsd * 100),
  };
}

export async function fetchAiCosts(): Promise<TenantCostSummary[]> {
  const raw = await apiClient.get<RawCostsResponse>(`${BASE}/costs`);
  return raw.items.map(mapCostItem);
}

// ── Playground ────────────────────────────────────────────────────────────────

interface RawPlaygroundResponse {
  provider:   string;
  elapsedMs:  number;
  extraction: {
    amount:       { value: number | null;  confidence: number; status: string };
    date:         { value: string | null;  confidence: number; status: string };
    payee:        { value: string | null;  confidence: number; status: string };
    account:      { value: string | null;  confidence: number; status: string };
    inputTokens:  number;
    outputTokens: number;
    costUsd:      number;
  };
  interpretation: {
    direction:   string;
    dirSource:   string;
    nature:      string;
    natureSource: string;
    categoryId:  string | null;
    catSource:   string;
    contextType: string | null;
    contextId:   string | null;
    ctxSource:   string;
    accountId:   string | null;
    accSource:   string;
  };
  rawProviderResponse: string;
}

export async function runPlayground(input: {
  text?:     string;
  provider?: string;
  tenantId?: string;
}): Promise<PlaygroundResult> {
  const raw = await apiClient.post<RawPlaygroundResponse>(`${BASE}/playground`, {
    text:          input.text,
    forceProvider: input.provider ?? null,
    tenantId:      input.tenantId ?? null,
  });

  const e = raw.extraction;
  const i = raw.interpretation;

  return {
    draftId:       null,   // playground never creates a draft
    analyzerChain: [raw.provider as PlaygroundResult["analyzerChain"][0]],
    extraction: {
      amount:  { value: e.amount.value,  confidence: e.amount.confidence,  status: e.amount.status  as any, provider: raw.provider as any },
      date:    { value: e.date.value,    confidence: e.date.confidence,    status: e.date.status    as any, provider: raw.provider as any },
      payee:   { value: e.payee.value,   confidence: e.payee.confidence,   status: e.payee.status   as any, provider: raw.provider as any },
      account: { value: e.account.value, confidence: e.account.confidence, status: e.account.status as any, provider: raw.provider as any },
      analyzerUsed: raw.provider as any,
    },
    suggestion: {
      direction: { value: i.direction,   source: i.dirSource },
      nature:    { value: i.nature,      source: i.natureSource },
      category:  { value: i.categoryId,  source: i.catSource },
      context:   { contextType: i.contextType, contextId: i.contextId, source: i.ctxSource },
    },
    rawPrompt:   null,
    rawResponse: raw.rawProviderResponse || null,
    tokenUsage:  e.inputTokens > 0
      ? { input: e.inputTokens, output: e.outputTokens }
      : null,
    durationMs: raw.elapsedMs,
  };
}

// ── Prompt versions ───────────────────────────────────────────────────────────

interface RawPromptVersion {
  id:             string;
  promptType:     string;
  version:        string;
  hash:           string;
  isActive:       boolean;
  description:    string;
  createdBy:      string;
  createdAt:      string;
  contentPreview: string;
}

function mapPromptVersion(r: RawPromptVersion): PromptVersion {
  return {
    id:          r.id,
    promptType:  r.promptType,
    version:     r.version,
    hash:        r.hash,
    isActive:    r.isActive,
    content:     r.contentPreview,   // full content is only needed in detail view
    description: r.description,
    createdAt:   r.createdAt,
    createdBy:   r.createdBy,
  };
}

export async function fetchPromptVersions(promptType: string): Promise<PromptVersion[]> {
  const raw = await apiClient.get<RawPromptVersion[]>(
    `${BASE}/prompts?type=${encodeURIComponent(promptType)}`
  );
  return raw.map(mapPromptVersion);
}

export async function setActivePrompt(id: string): Promise<void> {
  await apiClient.post<void>(`${BASE}/prompts/${id}/activate`, {});
}
