// ── AI Operations — shared types for the SuperAdmin AI module ─────────────────

export type AnalyzerProvider = "RuleBased" | "Claude" | "OpenAI";
export type OperationType    = "Analyze" | "Reprocess" | "TestConsole";
export type FieldStatus      = "AutoFilled" | "NeedsAttention" | "RequiresInput";

// ── Provider management ───────────────────────────────────────────────────────

export interface AiProviderConfig {
  id:                      string;
  name:                    string;
  provider:                AnalyzerProvider;
  isEnabled:               boolean;
  isDefault:               boolean;
  apiKeyLastFour:          string | null;
  modelId:                 string | null;
  monthlyTokenLimit:       number | null;
  costPerInputTokenMicros: number;   // microdollars per token
  costPerOutputTokenMicros: number;
  fallbackProviderId:      string | null;
  priority:                number;   // lower = higher priority in chain
  updatedAt:               string;
}

export interface RotateKeyResult {
  newKeyPrefix: string;   // first 8 chars for display
  newKeyLastFour: string;
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
  rawPrompt:             string | null;   // null unless EnablePromptLogging
  rawResponse:           string | null;   // null unless EnableRawResponseStorage
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
  provider:   AnalyzerProvider;
}

export interface PlaygroundResult {
  draftId:     string | null;
  analyzerChain: AnalyzerProvider[];
  extraction: {
    amount:      PlaygroundExtractionField<number | null>;
    date:        PlaygroundExtractionField<string | null>;
    payee:       PlaygroundExtractionField<string | null>;
    account:     PlaygroundExtractionField<string | null>;
    analyzerUsed: AnalyzerProvider;
  };
  suggestion: {
    direction: { value: string; source: string };
    nature:    { value: string; source: string };
    category:  { value: string | null; source: string };
    context:   { contextType: string | null; contextId: string | null; source: string };
  };
  rawPrompt:   string | null;
  rawResponse: string | null;
  tokenUsage:  { input: number; output: number } | null;
  durationMs:  number;
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
