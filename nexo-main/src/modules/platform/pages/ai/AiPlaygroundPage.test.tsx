import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/services/api-client";
import AiPlaygroundPage from "./AiPlaygroundPage";
import * as api from "../../services/interpreterAdminApi";
import * as platformApi from "../../services/platformApi";
import { renderAi, polyfillRadix, overview, platformTenant, playgroundResult } from "../../test/aiFixtures";

vi.mock("../../services/interpreterAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/interpreterAdminApi")>()),
  fetchAiProviders: vi.fn(),
  runPlayground: vi.fn(),
}));

vi.mock("../../services/platformApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/platformApi")>()),
  fetchPlatformTenants: vi.fn(),
}));

polyfillRadix();

const TEXT = "Pix enviado em 07/05/2026 R$ 850,00";

/** "0000 Primeira" sorts first on purpose: the page must never fall back to it. */
const TENANTS = [platformTenant("t-first", "0000 Primeira Empresa"), platformTenant("t-acme", "Acme Ltda")];

async function pickCompany(name = "Acme Ltda") {
  const company = await screen.findByRole("combobox", { name: "Empresa" });
  await waitFor(() => expect(company).toBeEnabled());
  fireEvent.click(company);
  fireEvent.click(await screen.findByRole("option", { name }));
  await waitFor(() => expect(company).toHaveTextContent(name));
}

function typeText() {
  fireEvent.change(screen.getByLabelText("Texto do movimento"), { target: { value: TEXT } });
}

async function typeAndRun() {
  await pickCompany();
  typeText();
  fireEvent.click(screen.getByRole("button", { name: /Analisar/ }));
}

describe("AiPlaygroundPage", () => {
  beforeEach(() => {
    vi.mocked(api.fetchAiProviders).mockResolvedValue(overview());
    vi.mocked(platformApi.fetchPlatformTenants).mockResolvedValue(TENANTS);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("keeps Analisar disabled until a company is chosen, and never picks one by itself", async () => {
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });
    await waitFor(() => expect(platformApi.fetchPlatformTenants).toHaveBeenCalled());

    typeText();
    const run = screen.getByRole("button", { name: /Analisar/ });
    expect(run).toBeDisabled();
    fireEvent.click(run);
    expect(api.runPlayground).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox", { name: "Empresa" })).toHaveTextContent("Escolha a empresa");
    expect(screen.queryByText(/padrão/i)).not.toBeInTheDocument();

    await pickCompany("Acme Ltda");
    expect(run).toBeEnabled();
    fireEvent.click(run);
    await waitFor(() => expect(api.runPlayground).toHaveBeenCalledTimes(1));
    expect(vi.mocked(api.runPlayground).mock.calls[0][0]).toEqual({ text: TEXT, forceProvider: null, tenantId: "t-acme" });
  });

  it("cannot run at all when there is no company to choose", async () => {
    vi.mocked(platformApi.fetchPlatformTenants).mockResolvedValue([]);
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });

    const company = screen.getByRole("combobox", { name: "Empresa" });
    await waitFor(() => expect(company).toHaveTextContent("Nenhuma empresa cadastrada"));
    expect(company).toBeDisabled();
    typeText();
    expect(screen.getByRole("button", { name: /Analisar/ })).toBeDisabled();
    expect(api.runPlayground).not.toHaveBeenCalled();
  });

  it("never offers an unavailable provider as a normal option, and says why", async () => {
    renderAi(<AiPlaygroundPage />);

    const openAi = await screen.findByRole("radio", { name: "OpenAI" });
    expect(openAi).toBeDisabled();
    expect(screen.getByText("O OpenAI ainda não foi implementado nesta versão.")).toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Claude" })).toBeEnabled();
    expect(screen.getByRole("radio", { name: "Automático" })).toBeChecked();
    expect(screen.getByText("Usa: Claude → RuleBased")).toBeInTheDocument();
  });

  it("sends forceProvider null in automatic mode, with the chosen company", async () => {
    vi.mocked(api.runPlayground).mockResolvedValue(playgroundResult({ forced: false }));
    renderAi(<AiPlaygroundPage />);
    await screen.findByRole("radio", { name: "Claude" });

    await typeAndRun();
    await waitFor(() => expect(api.runPlayground).toHaveBeenCalled());
    expect(vi.mocked(api.runPlayground).mock.calls[0][0]).toEqual({ text: TEXT, forceProvider: null, tenantId: "t-acme" });
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
