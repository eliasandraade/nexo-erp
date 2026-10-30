import { apiClient } from "@/services/api-client";
import type {
  AiProviderConfig,
  AiProviderPatch,
  AiProvidersOverview,
  AiProviderTestResult,
  AiDashboardStats,
  AiTelemetryRecord,
  TenantCostSummary,
  PlaygroundInput,
  PlaygroundResult,
  PromptVersion,
} from "../types/aiOperations";

const BASE = "/platform/interpreter";

// ── Providers ─────────────────────────────────────────────────────────────────
// The backend computes each provider's status and never returns the key (nor its ciphertext): only the last four.

export async function fetchAiProviders(): Promise<AiProvidersOverview> {
  const raw = await apiClient.get<AiProvidersOverview>(`${BASE}/providers`);
  return {
    providers:              raw.providers ?? [],
    secretStorageAvailable: raw.secretStorageAvailable ?? false,
    automaticOrder:         raw.automaticOrder ?? [],
  };
}

/** super_admin only. Sends only the fields given; answers the updated provider. */
export function updateAiProvider(id: string, patch: AiProviderPatch): Promise<AiProviderConfig> {
  return apiClient.patch<AiProviderConfig>(`${BASE}/providers/${id}`, patch);
}

/** Stores a new key (encrypted server-side). The answer carries only the last four characters. */
export function setAiProviderKey(id: string, apiKey: string): Promise<AiProviderConfig> {
  return apiClient.put<AiProviderConfig>(`${BASE}/providers/${id}/api-key`, { apiKey });
}

export function clearAiProviderKey(id: string): Promise<AiProviderConfig> {
  return apiClient.delete<AiProviderConfig>(`${BASE}/providers/${id}/api-key`);
}

/** One minimal (paid, for external providers) call to the provider. Only when the operator clicks. */
export function testAiProvider(id: string): Promise<AiProviderTestResult> {
  return apiClient.post<AiProviderTestResult>(`${BASE}/providers/${id}/test`, {});
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
  modelId?:             string | null;
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
    modelId:              r.modelId ?? null,
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

/** super_admin only. forceProvider null = automatic mode. Errors (422/503/400/404) surface as ApiError. */
export function runPlayground(input: PlaygroundInput): Promise<PlaygroundResult> {
  return apiClient.post<PlaygroundResult>(`${BASE}/playground`, {
    text:          input.text,
    forceProvider: input.forceProvider,
    tenantId:      input.tenantId,
  });
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
