// ── AI Operations — shared types for the SuperAdmin AI module ─────────────────

export type AnalyzerProvider = "RuleBased" | "Claude" | "OpenAI";
export type OperationType    = "Analyze" | "Reprocess" | "TestConsole";
export type FieldStatus      = "AutoFilled" | "NeedsAttention" | "RequiresInput";

// ── Provider management ───────────────────────────────────────────────────────

/** Why a provider can (or cannot) run right now, as the backend computes it. */
export type AiProviderStatus =
  | "available"
  | "disabled"
  | "blocked_by_deployment"
  | "missing_key"
  | "missing_model"
  | "implementation_missing"
  | "secret_unavailable"
  | "invalid_credentials"
  | "rate_limited"
  | "unhealthy";

export interface AiProviderLastTest {
  status:  string;
  message: string;
  at:      string;
}

/** A provider as GET /platform/interpreter/providers returns it. The key itself never leaves the server. */
export interface AiProviderConfig {
  id:                        string;
  name:                      string;
  provider:                  AnalyzerProvider;
  status:                    AiProviderStatus;
  statusMessage:             string;
  implemented:               boolean;
  requiresKey:               boolean;
  allowedByDeployment:       boolean;
  isEnabled:                 boolean;
  isDefault:                 boolean;
  priority:                  number;   // 1..100, lower runs first after the default
  modelId:                   string | null;
  hasApiKey:                 boolean;
  apiKeyLastFour:            string | null;
  apiKeyUpdatedAt:           string | null;
  monthlyTokenLimit:         number | null;
  inputUsdPerMillionTokens:  number;
  outputUsdPerMillionTokens: number;
  lastTest:                  AiProviderLastTest | null;
}

export interface AiProvidersOverview {
  providers:              AiProviderConfig[];
  /** False when the deployment has no master key: no API key can be stored. */
  secretStorageAvailable: boolean;
  /** Providers the automatic mode would use, in order. */
  automaticOrder:         string[];
}

export interface AiProviderPatch {
  isEnabled?:                 boolean;
  isDefault?:                 boolean;
  modelId?:                   string;   // "" clears
  priority?:                  number;
  monthlyTokenLimit?:         number;
  clearMonthlyTokenLimit?:    boolean;
  inputUsdPerMillionTokens?:  number;
  outputUsdPerMillionTokens?: number;
}

export interface AiProviderTestResult {
  status:    string;   // "ok" | "invalid_credentials" | "rate_limited" | "model_not_found" | "timeout" | …
  message:   string;
  modelId:   string | null;
  latencyMs: number;
  testedAt:  string;
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

export interface DailyRequestPoint {
  date:      string;
  total:     number;
  ruleBased: number;
  llm:       number;
}

export interface AiDashboardStats {
  requestsToday:       number;
  requestsLast7d:      number;
  ruleBasedPct:        number;   // 0-100
  claudePct:           number;
  openAiPct:           number;
  avgConfidence:       number;   // 0-1
  acceptanceRate:      number;   // 0-1
  correctionRate:      number;   // 0-1
  reprocessRate:       number;   // 0-1
  activeTenantsCount:  number;
  requestsByDay:       DailyRequestPoint[];
  topCorrectedFields:  { field: string; count: number }[];
  topTenants:          { tenantId: string; tenantName: string; requests: number }[];
}

// ── Telemetry ─────────────────────────────────────────────────────────────────

export interface AiTelemetryRecord {
  id:                    string;
  tenantId:              string;
  tenantName:            string;
  userId:                string;
  movementId:            string | null;
  operationType:         OperationType;
  provider:              AnalyzerProvider;
  promptType:            string;
  promptVersion:         string;
  promptHash:            string;
  inputTokens:           number;
  outputTokens:          number;
  estimatedCostMicros:   number;
  durationMs:            number;
  success:               boolean;
  errorMessage:          string | null;
  fallbackUsed:          boolean;
  fallbackFromProvider:  AnalyzerProvider | null;
  analyzerChain:         AnalyzerProvider[];
  requiresInputCount:    number;
  amountConfidence:      number;
  dateConfidence:        number;
  modelId?:              string | null;
  createdAt:             string;
}

// ── Costs ─────────────────────────────────────────────────────────────────────

export interface TenantCostSummary {
  tenantId:          string;
  tenantName:        string;
  requests:          number;
  inputTokens:       number;
  outputTokens:      number;
  estimatedCostMicros: number;
  softLimitCents:    number | null;
  hardLimitCents:    number | null;
  spentCents:        number;
}

// ── Playground ────────────────────────────────────────────────────────────────

export interface PlaygroundExtractionField<T> {
  value:      T;
  confidence: number;
  status:     FieldStatus;
}

export interface PlaygroundAttempt {
  provider:      string;
  modelId:       string | null;
  succeeded:     boolean;
  failureReason: string | null;
  durationMs:    number;
}

/** POST /platform/interpreter/playground. There is deliberately no raw provider answer. */
export interface PlaygroundResult {
  tenantId:     string;
  provider:     string;          // the provider that actually ran
  modelId:      string | null;
  forced:       boolean;
  fallbackUsed: boolean;
  attempts:     PlaygroundAttempt[];
  elapsedMs:    number;
  prompt:       { type: string; version: string; hash: string };
  extraction: {
    amount:       PlaygroundExtractionField<number | null>;
    date:         PlaygroundExtractionField<string | null>;
    payee:        PlaygroundExtractionField<string | null>;
    account:      PlaygroundExtractionField<string | null>;
    inputTokens:  number;
    outputTokens: number;
    costUsd:      number;
  };
  interpretation: {
    direction:    string;
    dirSource:    string;
    nature:       string;
    natureSource: string;
    categoryId:   string | null;
    catSource:    string;
    contextType:  string | null;
    contextId:    string | null;
    ctxSource:    string;
    accountId:    string | null;
    accSource:    string;
  };
}

export interface PlaygroundInput {
  text:          string;
  /** null = automatic mode. */
  forceProvider: AnalyzerProvider | null;
  tenantId:      string | null;
}

// ── Prompts ───────────────────────────────────────────────────────────────────

export interface PromptVersion {
  id:          string;
  promptType:  string;  // "extraction" | "interpretation" | "memory"
  version:     string;
  hash:        string;
  isActive:    boolean;
  content:     string;
  description: string;
  createdAt:   string;
  createdBy:   string;
}
