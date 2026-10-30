import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import * as aiApi from "../../api/build-ai.api";
import * as twinApi from "../../api/build-twin.api";
import * as buildApi from "../../api/build.api";
import type { BuildModelVersionDto } from "../../api/build-twin.api";
import { localToday } from "../../lib/format";
import { AI_DISABLED_MESSAGE, AI_UNAVAILABLE_MESSAGE } from "../../lib/ai";
import { AiSuggestionCard } from "./AiSuggestionCard";
import { DailyLogCopilot } from "./DailyLogCopilot";
import { ProgressInterpreterAi } from "./ProgressInterpreterAi";
import { ModelMapperAi } from "./ModelMapperAi";
import { ClientUpdateAi } from "./ClientUpdateAi";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-ai.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-ai.api")>()),
  fetchSuggestions:               vi.fn(),
  acceptSuggestion:               vi.fn(),
  rejectSuggestion:               vi.fn(),
  generateDailyLogCopilot:        vi.fn(),
  generateProgressInterpretation: vi.fn(),
  generateModelMapping:           vi.fn(),
  generateClientUpdate:           vi.fn(),
  generateWeeklyDigest:           vi.fn(),
}));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchBuildSettings: vi.fn(),
}));
vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchDailyLogs: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const future = new Date(Date.now() + 5 * 86_400_000).toISOString();

const suggestion = (over: Partial<aiApi.BuildAiSuggestionDto> = {}): aiApi.BuildAiSuggestionDto => ({
  id: "sg1", projectId: "p1", capability: "ProgressInterpreter", status: "Pending", source: "Llm", subjectId: null,
  confidence: 0.82, output: {}, inputRefs: { lookbackDays: 14, dailyLogIds: ["log-aaaa-1111"], photoIds: [] }, applied: null,
  provider: "Claude", model: "claude-modelo-teste", promptType: "build-progress-interpreter", promptVersion: "1.0.0",
  promptVersionId: "pv1", promptHash: "0123456789abcdef", inputTokens: 1200, outputTokens: 300, costMicros: 8400,
  latencyMs: 900, createdByName: "Ana", createdAt: "2026-10-30T10:00:00Z", decidedByName: null, decidedAt: null,
  decisionNote: null, expiresAt: future, ...over,
});

const progressOutput = {
  items: [
    { id: "s1", stageId: "s1", stageName: "Alvenaria", currentPercent: 40, suggestedPercent: 60, rationale: "Paredes do 2º pavimento",
      evidenceRefs: ["log-aaaa-1111"], confidence: 0.9 },
    { id: "s2", stageId: "s2", stageName: "Cobertura", currentPercent: 0, suggestedPercent: 10, rationale: "Madeiramento iniciado",
      evidenceRefs: [], confidence: 0.55 },
  ],
  discardedRefs: 0,
};

const settings = (ai: boolean, vision = false) =>
  ({ tenantId: "t", build3DEnabled: true, buildAiEnabled: ai, buildVisionEnabled: vision, buildPredictEnabled: true,
     customized: true, updatedByName: null, updatedAt: null });

beforeEach(() => {
  vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true));
  vi.mocked(aiApi.fetchSuggestions).mockResolvedValue([]);
  vi.mocked(buildApi.fetchDailyLogs).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 30 });
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("AiSuggestionCard", () => {
  const items = progressOutput.items.map((i) => ({ id: i.id, label: `Aplicar ${i.stageName}`, content: <span>{i.stageName}</span> }));

  it("shows provenance: source IA, confidence, provider/model, prompt version and short hash, tokens, cost and expiry", () => {
    wrap(<AiSuggestionCard suggestion={suggestion()} items={items} />);
    expect(screen.getByTestId("ai-source")).toHaveTextContent("IA");
    expect(screen.getByRole("progressbar", { name: "Confiança" })).toHaveAttribute("aria-valuenow", "82");
    expect(screen.getByText("82%")).toBeInTheDocument();
    const meta = screen.getByTestId("ai-provenance-meta");
    expect(meta).toHaveTextContent("Claude · claude-modelo-teste");
    expect(meta).toHaveTextContent("build-progress-interpreter v1.0.0 #01234567");
    expect(meta).toHaveTextContent("1.500 (entrada 1.200 · saída 300)");
    expect(meta).toHaveTextContent("US$");
    expect(meta).toHaveTextContent("Vence:");
    expect(screen.getByTestId("ai-provenance")).toHaveTextContent("Registros do diário: log-aaaa");
    expect(screen.getByText(/A IA sugere; você decide\./)).toBeInTheDocument();
  });

  it("Rules source says it was generated by rules, without AI", () => {
    wrap(<AiSuggestionCard suggestion={suggestion({ source: "Rules", provider: null, model: null })} />);
    expect(screen.getByTestId("ai-source")).toHaveTextContent("Regras — gerado por regras, sem IA");
  });

  it("partial acceptance sends only the selected item ids and the note", async () => {
    vi.mocked(aiApi.acceptSuggestion).mockResolvedValue(suggestion({ status: "PartiallyAccepted" }));
    const onDecided = vi.fn();
    wrap(<AiSuggestionCard suggestion={suggestion()} items={items} onDecided={onDecided} />);

    fireEvent.click(screen.getByRole("checkbox", { name: "Aplicar Cobertura" }));
    fireEvent.change(screen.getByLabelText("Observação (opcional)"), { target: { value: "Cobertura ainda não" } });
    fireEvent.click(screen.getByRole("button", { name: /Aceitar 1 selecionado/ }));

    await waitFor(() => expect(aiApi.acceptSuggestion).toHaveBeenCalledWith("sg1", { itemIds: ["s1"], note: "Cobertura ainda não" }));
    await waitFor(() => expect(onDecided).toHaveBeenCalled());
  });

  it("accepting everything sends no itemIds (the whole suggestion)", async () => {
    vi.mocked(aiApi.acceptSuggestion).mockResolvedValue(suggestion({ status: "Accepted" }));
    wrap(<AiSuggestionCard suggestion={suggestion()} items={items} acceptLabel="Aplicar progresso" />);
    fireEvent.click(screen.getByRole("button", { name: /Aplicar progresso/ }));
    await waitFor(() => expect(aiApi.acceptSuggestion).toHaveBeenCalledWith("sg1", { itemIds: undefined, note: undefined }));
  });

  it("nothing selected → accept disabled", () => {
    wrap(<AiSuggestionCard suggestion={suggestion()} items={items} />);
    fireEvent.click(screen.getByRole("button", { name: "Desmarcar todos" }));
    expect(screen.getByRole("button", { name: /Aceitar 0 selecionados/ })).toBeDisabled();
  });

  it("reject sends the note and changes nothing else", async () => {
    vi.mocked(aiApi.rejectSuggestion).mockResolvedValue(suggestion({ status: "Rejected" }));
    wrap(<AiSuggestionCard suggestion={suggestion()} items={items} />);
    fireEvent.change(screen.getByLabelText("Observação (opcional)"), { target: { value: "Percentuais altos demais" } });
    fireEvent.click(screen.getByRole("button", { name: /Recusar/ }));
    await waitFor(() => expect(aiApi.rejectSuggestion).toHaveBeenCalledWith("sg1", "Percentuais altos demais"));
    expect(aiApi.acceptSuggestion).not.toHaveBeenCalled();
  });

  it("409 (expired) shows the server's message", async () => {
    vi.mocked(aiApi.acceptSuggestion).mockRejectedValue(
      new ApiError(409, "Conflict", "conflict", undefined, undefined, "Esta sugestão expirou. Gere uma nova."));
    wrap(<AiSuggestionCard suggestion={suggestion()} />);
    fireEvent.click(screen.getByRole("button", { name: /Aceitar/ }));
    expect(await screen.findByRole("alert")).toHaveTextContent("Esta sugestão expirou. Gere uma nova.");
  });

  it("an expired suggestion offers no accept/reject", () => {
    wrap(<AiSuggestionCard suggestion={suggestion({ expiresAt: "2020-01-01T00:00:00Z" })} />);
    expect(screen.queryByRole("button", { name: /Aceitar/ })).not.toBeInTheDocument();
    expect(screen.getByText(/venceu sem decisão/)).toBeInTheDocument();
  });
});

describe("AI gates", () => {
  it("BuildAI off → generate buttons disabled with the support message", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(false));
    wrap(<ProgressInterpreterAi projectId="p1" />);
    expect(await screen.findByText(AI_DISABLED_MESSAGE)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Interpretar progresso com IA/ })).toBeDisabled();
  });

  it("unexpected settings payload counts as off", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue([] as unknown as twinApi.BuildTenantSettingsDto);
    wrap(<ClientUpdateAi projectId="p1" />);
    expect(await screen.findByText(AI_DISABLED_MESSAGE)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Gerar atualização/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Resumo semanal/ })).toBeDisabled();
  });

  it("422 ai_provider_unavailable and 503 ai_provider_failed become friendly messages", async () => {
    vi.mocked(aiApi.generateProgressInterpretation)
      .mockRejectedValueOnce(new ApiError(422, "x", "ai_provider_unavailable"))
      .mockRejectedValueOnce(new ApiError(503, "x", "ai_provider_failed", "req-1", ["timeout"]));
    wrap(<ProgressInterpreterAi projectId="p1" />);
    const btn = await screen.findByRole("button", { name: /Interpretar progresso com IA/ });
    await waitFor(() => expect(btn).toBeEnabled());
    fireEvent.click(btn);
    expect(await screen.findByRole("alert")).toHaveTextContent(AI_UNAVAILABLE_MESSAGE);
    fireEvent.click(screen.getByRole("button", { name: /Interpretar progresso com IA/ }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("A IA não conseguiu responder agora (o provedor demorou demais)"));
  });
});

describe("DailyLogCopilot", () => {
  it("free text → structured draft → accept creates the log (client summary stays hidden)", async () => {
    const draft = suggestion({
      id: "c1", capability: "DailyLogCopilot", confidence: 0.7,
      output: {
        date: localToday(), confidence: 0.7, discardedRefs: 0,
        activities: [{ stageId: "s1", stageName: "Alvenaria", description: "Paredes do 2º pavimento", progressHint: 60, confidence: 0.8 }],
        workforce: [{ role: "pedreiros", count: 4 }], equipment: ["betoneira"], issues: ["Falta de areia"],
        weatherNote: "Chuva à tarde", clientSummary: "Avançamos nas paredes do segundo andar.",
      },
    });
    vi.mocked(aiApi.generateDailyLogCopilot).mockResolvedValue(draft);
    vi.mocked(aiApi.acceptSuggestion).mockResolvedValue({ ...draft, status: "Accepted" });

    wrap(<DailyLogCopilot projectId="p1" logs={[]} />);
    fireEvent.click(screen.getByRole("button", { name: /Copiloto do diário/ }));
    const generate = screen.getByRole("button", { name: /Gerar rascunho/ });
    expect(generate).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Relato do dia"), { target: { value: "4 pedreiros terminaram as paredes; chuva à tarde" } });
    await waitFor(() => expect(generate).toBeEnabled());
    fireEvent.click(generate);

    await waitFor(() => expect(aiApi.generateDailyLogCopilot).toHaveBeenCalledWith("p1",
      { date: localToday(), text: "4 pedreiros terminaram as paredes; chuva à tarde", photoIds: undefined }));
    const view = await screen.findByTestId("copilot-draft");
    expect(view).toHaveTextContent("Alvenaria");
    expect(view).toHaveTextContent("Paredes do 2º pavimento");
    expect(view).toHaveTextContent("4 pedreiros");
    expect(view).toHaveTextContent("betoneira");
    expect(view).toHaveTextContent("Falta de areia");
    expect(view).toHaveTextContent("Chuva à tarde");
    expect(view).toHaveTextContent("Fica oculto do cliente até você torná-lo visível");
    // nothing applied before the click
    expect(aiApi.acceptSuggestion).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /Aceitar e registrar no diário/ }));
    await waitFor(() => expect(aiApi.acceptSuggestion).toHaveBeenCalledWith("c1", { itemIds: undefined, note: undefined }));
  });

  it("with vision on, offers the photos of the chosen date", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true, true));
    const log = {
      id: "l1", projectId: "p1", date: localToday(), weatherSummary: null, notes: "x", createdAt: "", updatedAt: "",
      photos: [{ id: "ph1", dailyLogId: "l1", storageKey: "k", url: null, caption: null, createdAt: "" }],
    };
    wrap(<DailyLogCopilot projectId="p1" logs={[log]} />);
    fireEvent.click(screen.getByRole("button", { name: /Copiloto do diário/ }));
    expect(await screen.findByRole("checkbox", { name: "Usar a foto 1" })).toBeInTheDocument();
  });
});

describe("ProgressInterpreterAi", () => {
  it("lists per-stage suggestions; accepting with an open critical NC shows the 422 message and keeps it pending", async () => {
    vi.mocked(aiApi.fetchSuggestions).mockResolvedValue([suggestion({ output: progressOutput })]);
    vi.mocked(aiApi.acceptSuggestion).mockRejectedValue(new ApiError(422, "x", "stage_has_critical_nonconformity",
      undefined, undefined, "A etapa tem uma não conformidade crítica aberta."));
    wrap(<ProgressInterpreterAi projectId="p1" />);

    expect(await screen.findByText(/Alvenaria:/)).toHaveTextContent("40% → 60%");
    expect(screen.getByText("Paredes do 2º pavimento")).toBeInTheDocument();
    expect(screen.getByText(/confirmado a partir da IA/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Aplicar progresso/ }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("A etapa tem uma não conformidade crítica aberta.");
    expect(alert).toHaveTextContent("A sugestão continua pendente.");
    expect(screen.getByRole("button", { name: /Aplicar progresso/ })).toBeInTheDocument();
  });
});

describe("ModelMapperAi", () => {
  const version = { id: "v1", number: 2, sourceFileName: "casa.ifc", status: "Ready" } as BuildModelVersionDto;

  it("proposes bindings by category and zone with the Rules badge; accept sends the selection", async () => {
    vi.mocked(aiApi.generateModelMapping).mockResolvedValue(suggestion({
      id: "m1", capability: "ModelMapper", source: "Rules", provider: null, model: null, subjectId: "v1", confidence: 0.6,
      output: { modelVersionId: "v1", discardedRefs: 0, bindings: [
        { id: "b1", stageId: "s1", stageName: "Alvenaria", scope: "Category", category: "IfcWall", zoneId: null, zoneName: null, confidence: 0.6, rationale: "IfcWall → alvenaria" },
        { id: "b2", stageId: "s2", stageName: "Fundação", scope: "Zone", category: null, zoneId: "z1", zoneName: "Térreo", confidence: 0.6, rationale: "" },
      ] },
    }));
    vi.mocked(aiApi.acceptSuggestion).mockResolvedValue(suggestion({ id: "m1", capability: "ModelMapper", status: "PartiallyAccepted" }));
    wrap(<ModelMapperAi projectId="p1" version={version} />);
    const btn = await screen.findByRole("button", { name: /Sugerir vínculos/ });
    await waitFor(() => expect(btn).toBeEnabled());
    fireEvent.click(btn);

    const card = await screen.findByTestId("ai-suggestion");
    expect(within(card).getByTestId("ai-source")).toHaveTextContent("gerado por regras, sem IA");
    expect(card).toHaveTextContent("Por categoria");
    expect(card).toHaveTextContent("Por zona");
    expect(aiApi.generateModelMapping).toHaveBeenCalledWith("p1", { modelVersionId: "v1" });
    fireEvent.click(within(card).getByRole("checkbox", { name: "Vincular Térreo a Fundação" }));
    fireEvent.click(within(card).getByRole("button", { name: /Aceitar 1 selecionado/ }));
    await waitFor(() => expect(aiApi.acceptSuggestion).toHaveBeenCalledWith("m1", { itemIds: ["b1"], note: undefined }));
  });
});

describe("ClientUpdateAi", () => {
  it("weekly digest → accepted suggestion becomes a draft the user still publishes", async () => {
    vi.mocked(aiApi.generateWeeklyDigest).mockResolvedValue(suggestion({
      id: "w1", capability: "WeeklyDigest",
      output: { kind: "WeeklyDigest", from: "2026-10-23", to: "2026-10-30", title: "Semana na obra", body: "Concluímos a fundação.", highlights: ["Fundação 100%"] },
    }));
    wrap(<ClientUpdateAi projectId="p1" />);
    const btn = await screen.findByRole("button", { name: /Resumo semanal/ });
    await waitFor(() => expect(btn).toBeEnabled());
    fireEvent.click(btn);
    expect(await screen.findByTestId("client-update-draft")).toHaveTextContent("Semana na obra");
    expect(screen.getByText(/cria um rascunho/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Criar rascunho/ })).toBeInTheDocument();
  });
});
