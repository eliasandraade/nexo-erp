import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import AiProvidersPage from "./AiProvidersPage";
import * as api from "../../services/interpreterAdminApi";
import { renderAi, polyfillRadix, overview, claude, openAi } from "../../test/aiFixtures";

vi.mock("../../services/interpreterAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/interpreterAdminApi")>()),
  fetchAiProviders: vi.fn(),
  updateAiProvider: vi.fn(),
  setAiProviderKey: vi.fn(),
  clearAiProviderKey: vi.fn(),
  testAiProvider: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const section = (name: string) => screen.getByRole("region", { name: `Provedor ${name}` });

describe("AiProvidersPage", () => {
  beforeEach(() => { vi.mocked(api.fetchAiProviders).mockResolvedValue(overview()); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows each provider's real status with the backend's message and the automatic order", async () => {
    renderAi(<AiProvidersPage />);

    const claudeCard = await screen.findByRole("region", { name: "Provedor Claude" });
    expect(within(claudeCard).getByText("Disponível")).toBeInTheDocument();
    expect(within(claudeCard).getByText("Pronto para uso.")).toBeInTheDocument();
    expect(within(claudeCard).getByText("****abcd")).toBeInTheDocument();
    expect(within(section("OpenAI")).getByText("Não disponível nesta versão")).toBeInTheDocument();
    expect(within(section("OpenAI")).getByText("O OpenAI ainda não foi implementado nesta versão.")).toBeInTheDocument();
    expect(screen.getByText("Claude → RuleBased")).toBeInTheDocument();
    // no false claims left
    expect(screen.queryByText(/sem criptografia/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/fallback chain/i)).not.toBeInTheDocument();
  });

  it("says when the automatic mode has no provider", async () => {
    vi.mocked(api.fetchAiProviders).mockResolvedValue(overview({ automaticOrder: [] }));
    renderAi(<AiProvidersPage />);
    expect(await screen.findByText("Modo automático: nenhum provedor disponível.")).toBeInTheDocument();
  });

  it("locks every action of a provider that is not implemented in this version", async () => {
    renderAi(<AiProvidersPage />);
    const card = await screen.findByRole("region", { name: "Provedor OpenAI" });

    expect(within(card).getByText(/Não disponível nesta versão do Orken — nada aqui/)).toBeInTheDocument();
    expect(within(card).getByRole("switch", { name: "Ativar OpenAI" })).toBeDisabled();
    expect(within(card).getByRole("button", { name: /Definir como padrão/ })).toBeDisabled();
    expect(within(card).getByRole("button", { name: /Cadastrar chave/ })).toBeDisabled();
    expect(within(card).getByRole("button", { name: /Testar conexão/ })).toBeDisabled();
    expect(within(card).getByRole("button", { name: /Salvar alterações/ })).toBeDisabled();
    expect(within(card).getByLabelText("Modelo")).toBeDisabled();
    expect(within(card).getByLabelText(/Prioridade/)).toBeDisabled();
  });

  it("saves a key with PUT and never shows it back: only the last four", async () => {
    const typed = "sk-teste-0123456789ABCDEFGHwxyz";
    vi.mocked(api.setAiProviderKey).mockResolvedValue({ ...claude, apiKeyLastFour: "wxyz" });
    vi.mocked(api.fetchAiProviders)
      .mockResolvedValueOnce(overview())
      .mockResolvedValue(overview({ providers: [{ ...claude, apiKeyLastFour: "wxyz" }, openAi] }));
    renderAi(<AiProvidersPage />);

    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("button", { name: /Trocar chave/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/criptografada \(AES-256-GCM\) com uma chave mestra que fica fora do banco/)).toBeInTheDocument();
    const input = within(dialog).getByLabelText(/Nova chave/);
    expect(input).toHaveAttribute("type", "password");
    expect(input).toHaveValue("");

    fireEvent.change(input, { target: { value: typed } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar chave/ }));

    await waitFor(() => expect(api.setAiProviderKey).toHaveBeenCalledWith("p-claude", typed));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(await screen.findByText("****wxyz")).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith("Chave de Claude salva (****wxyz).");
    expect(document.body.innerHTML).not.toContain(typed);
    expect(screen.queryByDisplayValue(typed)).not.toBeInTheDocument();

    // reopening starts empty again
    fireEvent.click(within(section("Claude")).getByRole("button", { name: /Trocar chave/ }));
    expect(within(await screen.findByRole("dialog")).getByLabelText(/Nova chave/)).toHaveValue("");
  });

  it("explains a key the backend would reject before sending it", async () => {
    renderAi(<AiProvidersPage />);
    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("button", { name: /Trocar chave/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Nova chave/), { target: { value: "curta" } });
    expect(within(dialog).getByText("A chave precisa ter pelo menos 20 caracteres.")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /Salvar chave/ })).toBeDisabled();
    expect(api.setAiProviderKey).not.toHaveBeenCalled();
  });

  it("shows the backend's refusal of a key", async () => {
    vi.mocked(api.setAiProviderKey).mockRejectedValue(
      new ApiError(422, "A chave de API tem formato inválido.", "ai_key_invalid", "req-9", undefined, "A chave de API tem formato inválido."));
    renderAi(<AiProvidersPage />);
    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("button", { name: /Trocar chave/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Nova chave/), { target: { value: "x".repeat(30) } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar chave/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("A chave de API tem formato inválido."));
  });

  it("disables saving keys when the deployment cannot store them", async () => {
    vi.mocked(api.fetchAiProviders).mockResolvedValue(overview({ secretStorageAvailable: false }));
    renderAi(<AiProvidersPage />);

    expect(await screen.findByText("Esta implantação não consegue guardar chaves de API")).toBeInTheDocument();
    expect(screen.getByText(/Secrets:MasterKey\) está ausente/)).toBeInTheDocument();
    expect(within(section("Claude")).getByRole("button", { name: /Trocar chave/ })).toBeDisabled();
  });

  it("removes a key only after confirmation", async () => {
    vi.mocked(api.clearAiProviderKey).mockResolvedValue({ ...claude, hasApiKey: false, apiKeyLastFour: null, status: "missing_key" });
    renderAi(<AiProvidersPage />);
    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("button", { name: /Remover chave/ }));
    const confirm = await screen.findByRole("alertdialog");
    expect(api.clearAiProviderKey).not.toHaveBeenCalled();
    fireEvent.click(within(confirm).getByRole("button", { name: "Remover chave" }));
    await waitFor(() => expect(api.clearAiProviderKey).toHaveBeenCalledWith("p-claude"));
  });

  it("tests the connection and shows status, message, model and latency", async () => {
    vi.mocked(api.testAiProvider).mockResolvedValue({
      status: "ok", message: "Conexão confirmada.", modelId: "claude-modelo-teste", latencyMs: 412, testedAt: "2026-10-30T10:00:00Z",
    });
    renderAi(<AiProvidersPage />);
    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("button", { name: /Testar conexão/ }));

    const result = await within(section("Claude")).findByRole("status");
    expect(result).toHaveTextContent("Teste de conexão: OK");
    expect(result).toHaveTextContent("Conexão confirmada.");
    expect(result).toHaveTextContent("claude-modelo-teste");
    expect(result).toHaveTextContent("412 ms");
    expect(api.testAiProvider).toHaveBeenCalledWith("p-claude");
  });

  it("does not offer a test without a key and says why", async () => {
    vi.mocked(api.fetchAiProviders).mockResolvedValue(overview({
      providers: [{ ...claude, hasApiKey: false, apiKeyLastFour: null, status: "missing_key", statusMessage: "Cadastre a chave." }],
    }));
    renderAi(<AiProvidersPage />);
    const card = await screen.findByRole("region", { name: "Provedor Claude" });
    expect(within(card).getByRole("button", { name: /Testar conexão/ })).toBeDisabled();
    expect(within(card).getByText("Cadastre uma chave antes de testar.")).toBeInTheDocument();
    expect(within(card).getByText("Sem chave")).toBeInTheDocument();
  });

  it("disables a provider with PATCH and shows the backend's error when it refuses", async () => {
    vi.mocked(api.updateAiProvider).mockRejectedValueOnce(new ApiError(403, "Forbidden"));
    renderAi(<AiProvidersPage />);
    fireEvent.click(await within(await screen.findByRole("region", { name: "Provedor Claude" })).findByRole("switch", { name: "Ativar Claude" }));
    await waitFor(() => expect(api.updateAiProvider).toHaveBeenCalledWith("p-claude", { isEnabled: false }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(
      "Esta ação exige um super administrador conectado com verificação em duas etapas."));
  });

  it("sends only the changed settings and validates them first", async () => {
    vi.mocked(api.updateAiProvider).mockResolvedValue({ ...claude, priority: 5 });
    renderAi(<AiProvidersPage />);
    const card = await screen.findByRole("region", { name: "Provedor Claude" });

    fireEvent.change(within(card).getByLabelText(/Prioridade/), { target: { value: "500" } });
    expect(within(card).getByText("A prioridade deve ser um número inteiro de 1 a 100.")).toBeInTheDocument();
    expect(within(card).getByRole("button", { name: /Salvar alterações/ })).toBeDisabled();

    fireEvent.change(within(card).getByLabelText(/Prioridade/), { target: { value: "5" } });
    fireEvent.change(within(card).getByLabelText("Limite mensal (tokens)"), { target: { value: "" } });
    fireEvent.click(within(card).getByRole("checkbox", { name: "Sem limite mensal para Claude" }));
    fireEvent.change(within(card).getByLabelText("Limite mensal (tokens)"), { target: { value: "2000000" } });
    fireEvent.click(within(card).getByRole("button", { name: /Salvar alterações/ }));

    await waitFor(() => expect(api.updateAiProvider).toHaveBeenCalledWith("p-claude", { priority: 5, monthlyTokenLimit: 2_000_000 }));
  });
});
