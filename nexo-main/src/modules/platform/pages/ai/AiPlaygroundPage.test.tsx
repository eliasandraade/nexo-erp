import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/services/api-client";
import AiPlaygroundPage from "./AiPlaygroundPage";
import * as api from "../../services/interpreterAdminApi";
import { renderAi, polyfillRadix, overview, playgroundResult } from "../../test/aiFixtures";

vi.mock("../../services/interpreterAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/interpreterAdminApi")>()),
  fetchAiProviders: vi.fn(),
  runPlayground: vi.fn(),
}));

polyfillRadix();

const TEXT = "Pix enviado em 07/05/2026 R$ 850,00";

async function typeAndRun() {
  fireEvent.change(screen.getByLabelText("Texto do movimento"), { target: { value: TEXT } });
  fireEvent.click(screen.getByRole("button", { name: /Analisar/ }));
}

describe("AiPlaygroundPage", () => {
  beforeEach(() => { vi.mocked(api.fetchAiProviders).mockResolvedValue(overview()); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("never offers an unavailable provider as a normal option, and says why", async () => {
    renderAi(<AiPlaygroundPage />);

    const openAi = await screen.findByRole("radio", { name: "OpenAI" });
    expect(openAi).toBeDisabled();
    expect(screen.getByText("O OpenAI ainda não foi implementado nesta versão.")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Claude" })).toBeEnabled();
    expect(screen.getByRole("radio", { name: "Automático" })).toBeChecked();
    expect(screen.getByText("Usa: Claude → RuleBased")).toBeInTheDocument();
  });

  it("sends forceProvider null in automatic mode", async () => {
    vi.mocked(api.runPlayground).mockResolvedValue(playgroundResult({ forced: false }));
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });

    await typeAndRun();
    await waitFor(() => expect(api.runPlayground).toHaveBeenCalled());
    expect(vi.mocked(api.runPlayground).mock.calls[0][0]).toEqual({ text: TEXT, forceProvider: null, tenantId: null });
    expect(await screen.findByText("Automático", { selector: "span.rounded-full" })).toBeInTheDocument();
  });

  it("shows a provider failure (503) with message, code, reason and requestId", async () => {
    vi.mocked(api.runPlayground).mockRejectedValue(new ApiError(
      503, "invalid_credentials (código: req-abc-123)", "ai_provider_failed", "req-abc-123",
      ["invalid_credentials"], "O Claude recusou a chave de API.",
    ));
    renderAi(<AiPlaygroundPage />);
    fireEvent.click(await screen.findByRole("radio", { name: "Claude" }));

    await typeAndRun();
    const alert = await screen.findByRole("alert");
    expect(vi.mocked(api.runPlayground).mock.calls[0][0]).toMatchObject({ forceProvider: "Claude" });
    expect(alert).toHaveTextContent("O provedor falhou");
    expect(alert).toHaveTextContent("O Claude recusou a chave de API.");
    expect(alert).toHaveTextContent("ai_provider_failed");
    expect(alert).toHaveTextContent("Chave recusada pelo provedor (invalid_credentials)");
    expect(alert).toHaveTextContent("req-abc-123");
    expect(screen.queryByTestId("playground-result")).not.toBeInTheDocument();
  });

  it("shows an unavailable provider (422) instead of an empty result", async () => {
    vi.mocked(api.runPlayground).mockRejectedValue(new ApiError(
      422, "O provedor Claude está sem chave.", "ai_provider_unavailable", "req-422", undefined, "O provedor Claude está sem chave."));
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });
    await typeAndRun();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("O provedor Claude está sem chave.");
    expect(alert).toHaveTextContent("ai_provider_unavailable");
    expect(alert).toHaveTextContent("req-422");
  });

  it("shows a successful Claude run: model, tokens, cost, prompt and attempts — never a raw answer", async () => {
    vi.mocked(api.runPlayground).mockResolvedValue(playgroundResult());
    renderAi(<AiPlaygroundPage />);
    fireEvent.click(await screen.findByRole("radio", { name: "Claude" }));
    await typeAndRun();

    const result = await screen.findByTestId("playground-result");
    expect(within(result).getByText("Executado por", { exact: false })).toHaveTextContent("Executado por Claude");
    expect(within(result).getAllByText("claude-modelo-teste").length).toBeGreaterThan(0);
    expect(within(result).getByText("Forçado")).toBeInTheDocument();
    expect(within(result).queryByText("Fallback usado")).not.toBeInTheDocument();
    expect(result).toHaveTextContent("Tokens: 412 entrada / 96 saída");
    expect(result).toHaveTextContent("Custo: US$ 0,002676");
    expect(result).toHaveTextContent("extraction v1.2");
    expect(result).toHaveTextContent("hash-teste-123");
    expect(within(result).getByText("R$ 850.00")).toBeInTheDocument();
    expect(within(result).getByText("João Carlos da Silva")).toBeInTheDocument();
    expect(screen.queryByText(/resposta crua/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/EnablePromptLogging/)).not.toBeInTheDocument();
  });

  it("shows when the automatic mode fell back, with the failed attempt's reason", async () => {
    vi.mocked(api.runPlayground).mockResolvedValue(playgroundResult({
      provider: "RuleBased", modelId: null, forced: false, fallbackUsed: true,
      attempts: [
        { provider: "Claude", modelId: "claude-modelo-teste", succeeded: false, failureReason: "timeout", durationMs: 15000 },
        { provider: "RuleBased", modelId: null, succeeded: true, failureReason: null, durationMs: 3 },
      ],
      extraction: { ...playgroundResult().extraction, inputTokens: 0, outputTokens: 0, costUsd: 0 },
    }));
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });
    await typeAndRun();

    const result = await screen.findByTestId("playground-result");
    expect(within(result).getByText("Fallback usado")).toBeInTheDocument();
    expect(result).toHaveTextContent("Executado por RuleBased");
    expect(result).toHaveTextContent("Tempo esgotado (timeout)");
    expect(result).toHaveTextContent("Custo: US$ 0");
  });
});
