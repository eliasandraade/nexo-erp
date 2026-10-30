import type { ReactElement } from "react";
import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { AiProviderConfig, AiProvidersOverview, PlaygroundResult } from "../types/aiOperations";

export function renderAi(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

/** Radix (switch, radio, dialogs) needs these in jsdom. */
export function polyfillRadix() {
  globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
  Element.prototype.scrollIntoView ??= () => {};
  Element.prototype.hasPointerCapture ??= () => false;
  Element.prototype.releasePointerCapture ??= () => {};
}

export const claude: AiProviderConfig = {
  id: "p-claude", name: "Claude", provider: "Claude", status: "available", statusMessage: "Pronto para uso.",
  implemented: true, requiresKey: true, allowedByDeployment: true, isEnabled: true, isDefault: true, priority: 10,
  modelId: "claude-modelo-teste", hasApiKey: true, apiKeyLastFour: "abcd", apiKeyUpdatedAt: "2026-10-29T12:00:00Z",
  monthlyTokenLimit: null, inputUsdPerMillionTokens: 3, outputUsdPerMillionTokens: 15, lastTest: null,
};

export const ruleBased: AiProviderConfig = {
  id: "p-rules", name: "RuleBased", provider: "RuleBased", status: "available", statusMessage: "Motor local, sempre disponível.",
  implemented: true, requiresKey: false, allowedByDeployment: true, isEnabled: true, isDefault: false, priority: 90,
  modelId: null, hasApiKey: false, apiKeyLastFour: null, apiKeyUpdatedAt: null,
  monthlyTokenLimit: null, inputUsdPerMillionTokens: 0, outputUsdPerMillionTokens: 0, lastTest: null,
};

export const openAi: AiProviderConfig = {
  id: "p-openai", name: "OpenAI", provider: "OpenAI", status: "implementation_missing",
  statusMessage: "O OpenAI ainda não foi implementado nesta versão.",
  implemented: false, requiresKey: true, allowedByDeployment: true, isEnabled: false, isDefault: false, priority: 50,
  modelId: null, hasApiKey: false, apiKeyLastFour: null, apiKeyUpdatedAt: null,
  monthlyTokenLimit: null, inputUsdPerMillionTokens: 0, outputUsdPerMillionTokens: 0, lastTest: null,
};

export function overview(over: Partial<AiProvidersOverview> = {}): AiProvidersOverview {
  return { providers: [claude, ruleBased, openAi], secretStorageAvailable: true, automaticOrder: ["Claude", "RuleBased"], ...over };
}

export function playgroundResult(over: Partial<PlaygroundResult> = {}): PlaygroundResult {
  return {
    tenantId: "t-1", provider: "Claude", modelId: "claude-modelo-teste", forced: true, fallbackUsed: false,
    attempts: [{ provider: "Claude", modelId: "claude-modelo-teste", succeeded: true, failureReason: null, durationMs: 812 }],
    elapsedMs: 830, prompt: { type: "extraction", version: "1.2", hash: "hash-teste-123" },
    extraction: {
      amount:  { value: 850, confidence: 0.97, status: "AutoFilled" },
      date:    { value: "2026-05-07", confidence: 0.92, status: "AutoFilled" },
      payee:   { value: "João Carlos da Silva", confidence: 0.75, status: "NeedsAttention" },
      account: { value: null, confidence: 0, status: "RequiresInput" },
      inputTokens: 412, outputTokens: 96, costUsd: 0.002676,
    },
    interpretation: {
      direction: "Outflow", dirSource: "Extraction", nature: "Expense", natureSource: "Rule",
      categoryId: null, catSource: "None", contextType: null, contextId: null, ctxSource: "None", accountId: null, accSource: "None",
    },
    ...over,
  };
}
