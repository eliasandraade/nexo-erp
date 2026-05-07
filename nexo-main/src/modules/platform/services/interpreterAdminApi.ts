import type {
  AiProviderConfig,
  AiDashboardStats,
  AiTelemetryRecord,
  TenantCostSummary,
  PlaygroundResult,
  PromptVersion,
} from "../types/aiOperations";

// TODO: swap mock data for real API calls once backend endpoints are live.
// All endpoints will live under /api/v1/admin/interpreter/*

// ── Mock data ─────────────────────────────────────────────────────────────────

const MOCK_PROVIDERS: AiProviderConfig[] = [
  {
    id: "provider-rule",
    name: "Rule-Based",
    provider: "RuleBased",
    isEnabled: true,
    isDefault: false,
    apiKeyLastFour: null,
    modelId: null,
    monthlyTokenLimit: null,
    costPerInputTokenMicros: 0,
    costPerOutputTokenMicros: 0,
    fallbackProviderId: null,
    priority: 1,
    updatedAt: new Date().toISOString(),
  },
  {
    id: "provider-claude",
    name: "Claude (Anthropic)",
    provider: "Claude",
    isEnabled: false,
    isDefault: true,
    apiKeyLastFour: "4a9f",
    modelId: "claude-sonnet-4-6",
    monthlyTokenLimit: 5_000_000,
    costPerInputTokenMicros: 3,
    costPerOutputTokenMicros: 15,
    fallbackProviderId: "provider-rule",
    priority: 2,
    updatedAt: new Date().toISOString(),
  },
  {
    id: "provider-openai",
    name: "OpenAI",
    provider: "OpenAI",
    isEnabled: false,
    isDefault: false,
    apiKeyLastFour: null,
    modelId: "gpt-4o-mini",
    monthlyTokenLimit: 3_000_000,
    costPerInputTokenMicros: 1,
    costPerOutputTokenMicros: 3,
    fallbackProviderId: "provider-rule",
    priority: 3,
    updatedAt: new Date().toISOString(),
  },
];

function makeDays(n: number) {
  return Array.from({ length: n }, (_, i) => {
    const d = new Date();
    d.setDate(d.getDate() - (n - 1 - i));
    const total     = Math.floor(Math.random() * 80 + 20);
    const ruleBased = Math.floor(total * 0.72);
    return {
      date:      d.toISOString().slice(0, 10),
      total,
      ruleBased,
      llm:       total - ruleBased,
    };
  });
}

const MOCK_DASHBOARD: AiDashboardStats = {
  requestsToday:       47,
  requestsLast7d:      312,
  ruleBasedPct:        72,
  claudePct:           28,
  openAiPct:           0,
  avgConfidence:       0.87,
  acceptanceRate:      0.81,
  correctionRate:      0.19,
  reprocessRate:       0.06,
  activeTenantsCount:  9,
  requestsByDay:       makeDays(14),
  topCorrectedFields:  [
    { field: "Direction", count: 38 },
    { field: "Category",  count: 27 },
    { field: "Nature",    count: 18 },
    { field: "ContextId", count: 11 },
    { field: "Account",   count:  7 },
  ],
  topTenants: [
    { tenantId: "t1", tenantName: "Construtora ABC",      requests: 98  },
    { tenantId: "t2", tenantName: "Restaurante Sabor",    requests: 74  },
    { tenantId: "t3", tenantName: "Auto Peças Delta",     requests: 61  },
    { tenantId: "t4", tenantName: "Moda & Estilo Ltda",   requests: 43  },
    { tenantId: "t5", tenantName: "Distribuidora Central",requests: 36  },
  ],
};

const MOCK_TELEMETRY: AiTelemetryRecord[] = Array.from({ length: 40 }, (_, i) => ({
  id:                   `tel-${i}`,
  tenantId:             `t${(i % 5) + 1}`,
  tenantName:           ["Construtora ABC","Restaurante Sabor","Auto Peças Delta","Moda & Estilo","Distribuidora"][i % 5],
  userId:               `user-${i % 8}`,
  movementId:           i % 4 === 0 ? null : `mov-${i}`,
  operationType:        (["Analyze","Analyze","Reprocess","TestConsole"][i % 4]) as AiTelemetryRecord["operationType"],
  provider:             (["RuleBased","RuleBased","RuleBased","Claude"][i % 4]) as AiTelemetryRecord["provider"],
  promptType:           i % 2 === 0 ? "extraction" : "interpretation",
  promptVersion:        "1.0.0",
  promptHash:           `a${i}bc${i}def`,
  inputTokens:          i % 3 === 0 ? 0 : Math.floor(Math.random() * 400 + 100),
  outputTokens:         i % 3 === 0 ? 0 : Math.floor(Math.random() * 200 + 50),
  estimatedCostMicros:  i % 3 === 0 ? 0 : Math.floor(Math.random() * 5000),
  durationMs:           Math.floor(Math.random() * 800 + 40),
  success:              i % 10 !== 0,
  errorMessage:         i % 10 === 0 ? "Provider timeout" : null,
  fallbackUsed:         i % 7 === 0,
  fallbackFromProvider: i % 7 === 0 ? "Claude" : null,
  analyzerChain:        i % 4 === 3 ? ["RuleBased","Claude"] : ["RuleBased"],
  requiresInputCount:   Math.floor(Math.random() * 3),
  amountConfidence:     Math.random() * 0.3 + 0.7,
  dateConfidence:       Math.random() * 0.3 + 0.7,
  rawPrompt:            null,
  rawResponse:          null,
  createdAt:            new Date(Date.now() - i * 1_200_000).toISOString(),
}));

const MOCK_COSTS: TenantCostSummary[] = [
  { tenantId: "t1", tenantName: "Construtora ABC",       requests: 98,  inputTokens: 38_400, outputTokens: 18_200, estimatedCostMicros: 388_200,  softLimitCents: 2000, hardLimitCents: 5000, spentCents: 38  },
  { tenantId: "t2", tenantName: "Restaurante Sabor",     requests: 74,  inputTokens: 28_800, outputTokens: 13_700, estimatedCostMicros: 292_050,  softLimitCents: 1500, hardLimitCents: 3000, spentCents: 29  },
  { tenantId: "t3", tenantName: "Auto Peças Delta",      requests: 61,  inputTokens: 23_800, outputTokens: 11_300, estimatedCostMicros: 240_900,  softLimitCents: null, hardLimitCents: null, spentCents: 24  },
  { tenantId: "t4", tenantName: "Moda & Estilo Ltda",    requests: 43,  inputTokens: 16_800, outputTokens:  7_980, estimatedCostMicros: 169_920,  softLimitCents: 1000, hardLimitCents: 2000, spentCents: 17  },
  { tenantId: "t5", tenantName: "Distribuidora Central", requests: 36,  inputTokens: 14_100, outputTokens:  6_690, estimatedCostMicros: 142_290,  softLimitCents: null, hardLimitCents: 1000, spentCents: 14  },
];

const MOCK_PROMPTS: PromptVersion[] = [
  { id: "p1", promptType: "extraction",     version: "1.0.0", hash: "a1b2c3d4", isActive: true,  content: "Extraia os campos financeiros...", description: "Extração base PT-BR",        createdAt: "2026-04-01T10:00:00Z", createdBy: "super_admin" },
  { id: "p2", promptType: "extraction",     version: "0.9.0", hash: "e5f6a7b8", isActive: false, content: "Extract financial data...",       description: "Versão inicial (EN)",          createdAt: "2026-03-15T10:00:00Z", createdBy: "super_admin" },
  { id: "p3", promptType: "interpretation", version: "1.0.0", hash: "c9d0e1f2", isActive: true,  content: "Com base nos dados extraídos...", description: "Interpretação v1",             createdAt: "2026-04-01T10:00:00Z", createdBy: "super_admin" },
  { id: "p4", promptType: "memory",         version: "1.0.0", hash: "a3b4c5d6", isActive: true,  content: "Perfil de padrões do tenant...", description: "Contexto de memória compacto", createdAt: "2026-04-10T10:00:00Z", createdBy: "super_admin" },
];

// ── API functions (mock → will connect to real endpoints) ─────────────────────

export async function fetchAiProviders(): Promise<AiProviderConfig[]> {
  // return apiClient.get<AiProviderConfig[]>("/v1/admin/interpreter/providers");
  return new Promise(r => setTimeout(() => r(MOCK_PROVIDERS), 300));
}

export async function updateAiProvider(id: string, patch: Partial<AiProviderConfig>): Promise<void> {
  // return apiClient.patch(`/v1/admin/interpreter/providers/${id}`, patch);
  const p = MOCK_PROVIDERS.find(p => p.id === id);
  if (p) Object.assign(p, patch, { updatedAt: new Date().toISOString() });
}

export async function rotateAiProviderKey(id: string): Promise<{ lastFour: string }> {
  // return apiClient.post(`/v1/admin/interpreter/providers/${id}/rotate-key`, {});
  return { lastFour: Math.random().toString(36).slice(-4) };
}

export async function fetchAiDashboard(): Promise<AiDashboardStats> {
  // return apiClient.get<AiDashboardStats>("/v1/admin/interpreter/dashboard");
  return new Promise(r => setTimeout(() => r(MOCK_DASHBOARD), 400));
}

export async function fetchAiTelemetry(page = 1, pageSize = 20): Promise<{
  items: AiTelemetryRecord[];
  total: number;
}> {
  // return apiClient.get(`/v1/admin/interpreter/telemetry?page=${page}&pageSize=${pageSize}`);
  const start = (page - 1) * pageSize;
  return new Promise(r => setTimeout(() => r({
    items: MOCK_TELEMETRY.slice(start, start + pageSize),
    total: MOCK_TELEMETRY.length,
  }), 300));
}

export async function fetchAiCosts(): Promise<TenantCostSummary[]> {
  // return apiClient.get<TenantCostSummary[]>("/v1/admin/interpreter/costs");
  return new Promise(r => setTimeout(() => r(MOCK_COSTS), 300));
}

export async function runPlayground(input: {
  text?: string;
  provider?: string;
}): Promise<PlaygroundResult> {
  // return apiClient.post<PlaygroundResult>("/v1/admin/interpreter/playground", input);
  await new Promise(r => setTimeout(r, 600));
  const hasAmount = /R?\$?\s*\d/.test(input.text ?? "");
  const hasDate   = /\d{2}[\/\-]\d{2}[\/\-]\d{4}/.test(input.text ?? "");
  return {
    draftId:       null,
    analyzerChain: ["RuleBased"],
    extraction: {
      amount:  { value: hasAmount ? 850.00 : null, confidence: hasAmount ? 0.93 : 0, status: hasAmount ? "AutoFilled" : "RequiresInput", provider: "RuleBased" },
      date:    { value: hasDate ? "2026-05-07" : null, confidence: hasDate ? 0.92 : 0, status: hasDate ? "AutoFilled" : "RequiresInput", provider: "RuleBased" },
      payee:   { value: /para[:\s]+(\w+)/i.test(input.text ?? "") ? "João Carlos" : null, confidence: 0.78, status: "NeedsAttention", provider: "RuleBased" },
      account: { value: /conta[:\s]+(\w+)/i.test(input.text ?? "") ? "Banco Nubank" : null, confidence: 0.72, status: "NeedsAttention", provider: "RuleBased" },
      analyzerUsed: "RuleBased",
    },
    suggestion: {
      direction: { value: "Out",     source: "Rule" },
      nature:    { value: "Expense", source: "Rule" },
      category:  { value: null,      source: "Unknown" },
      context:   { contextType: null, contextId: null, source: "Unknown" },
    },
    rawPrompt:   null,
    rawResponse: null,
    tokenUsage:  null,
    durationMs:  Math.floor(Math.random() * 80 + 20),
  };
}

export async function fetchPromptVersions(promptType: string): Promise<PromptVersion[]> {
  // return apiClient.get<PromptVersion[]>(`/v1/admin/interpreter/prompts?type=${promptType}`);
  return new Promise(r =>
    setTimeout(() => r(MOCK_PROMPTS.filter(p => p.promptType === promptType)), 300)
  );
}

export async function setActivePrompt(id: string): Promise<void> {
  // return apiClient.post(`/v1/admin/interpreter/prompts/${id}/activate`, {});
  MOCK_PROMPTS.forEach(p => { if (p.id === id || p.promptType === MOCK_PROMPTS.find(x => x.id === id)?.promptType) p.isActive = p.id === id; });
}
