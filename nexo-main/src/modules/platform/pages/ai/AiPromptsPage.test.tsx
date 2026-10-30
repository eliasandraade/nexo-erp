import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import AiPromptsPage from "./AiPromptsPage";
import * as api from "../../services/interpreterAdminApi";
import { renderAi, polyfillRadix, promptVersion } from "../../test/aiFixtures";

vi.mock("../../services/interpreterAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/interpreterAdminApi")>()),
  fetchPromptVersions: vi.fn(),
  createPromptVersion: vi.fn(),
  activatePromptVersion: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const active = promptVersion({ id: "pv-1", version: "1.0.0", hash: "aaaabbbbccccdddd", isActive: true });
const draft  = promptVersion({ id: "pv-2", version: "1.1.0", hash: "1111222233334444" });

describe("AiPromptsPage", () => {
  beforeEach(() => { vi.mocked(api.fetchPromptVersions).mockResolvedValue([active, draft]); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("says extraction is used by the runtime and the other types are not, with the full hash", async () => {
    renderAi(<AiPromptsPage />);

    expect(await screen.findByTestId("prompt-runtime-usage")).toHaveTextContent("Usado pelo runtime.");
    expect(screen.getByTestId("prompt-runtime-usage")).toHaveTextContent("contrato de saída em JSON é fixo no código");
    expect(await screen.findAllByText("aaaabbbbccccdddd")).not.toHaveLength(0);
    expect(within(screen.getByRole("row", { name: "Versão 1.0.0" })).getByText("ATIVA")).toBeInTheDocument();
    expect(screen.getByText(/SHA-256 do conteúdo \(os 16 primeiros caracteres hexadecimais\)/)).toBeInTheDocument();

    for (const label of ["Interpretação", "Memória"]) {
      fireEvent.click(screen.getByRole("tab", { name: label }));
      expect(screen.getByTestId("prompt-runtime-usage")).toHaveTextContent("Ainda não usado pelo runtime");
    }
    await waitFor(() => expect(api.fetchPromptVersions).toHaveBeenCalledWith("memory"));
    expect(api.fetchPromptVersions).toHaveBeenCalledWith("interpretation");
  });

  it("creates a new version with POST, with a character counter", async () => {
    vi.mocked(api.createPromptVersion).mockResolvedValue({
      id: "pv-3", promptType: "extraction", version: "1.2.0", hash: "9999888877776666", isActive: false,
    });
    renderAi(<AiPromptsPage />);
    fireEvent.click(await screen.findByRole("button", { name: /Nova versão/ }));
    const dialog = await screen.findByRole("dialog");

    fireEvent.change(within(dialog).getByLabelText("Versão"), { target: { value: "1.2.0" } });
    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "Datas por extenso" } });
    fireEvent.change(within(dialog).getByLabelText("Conteúdo"), { target: { value: "Instruções novas" } });
    expect(within(dialog).getByTestId("prompt-content-counter")).toHaveTextContent("16 / 16.000");
    fireEvent.click(within(dialog).getByRole("button", { name: /Criar versão/ }));

    await waitFor(() => expect(api.createPromptVersion).toHaveBeenCalledWith({
      promptType: "extraction", version: "1.2.0", content: "Instruções novas", description: "Datas por extenso",
    }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Versão 1.2.0 criada (inativa). Hash 9999888877776666.");
    await waitFor(() => expect(api.fetchPromptVersions).toHaveBeenCalledTimes(2));
  });

  it("explains an invalid version before sending and a 403 after", async () => {
    vi.mocked(api.createPromptVersion).mockRejectedValue(new ApiError(403, "Forbidden"));
    renderAi(<AiPromptsPage />);
    fireEvent.click(await screen.findByRole("button", { name: /Nova versão/ }));
    const dialog = await screen.findByRole("dialog");

    fireEvent.change(within(dialog).getByLabelText("Conteúdo"), { target: { value: "Instruções" } });
    fireEvent.change(within(dialog).getByLabelText("Versão"), { target: { value: "v 2!" } });
    expect(within(dialog).getByText("Use só letras, números, ponto e hífen (ex.: 1.1.0).")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /Criar versão/ })).toBeDisabled();

    fireEvent.change(within(dialog).getByLabelText("Versão"), { target: { value: "2.0" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Criar versão/ }));
    const message = "Esta ação exige um super administrador conectado com verificação em duas etapas.";
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(message);
    expect(toast.error).toHaveBeenCalledWith(message);
  });

  it("activates only after confirmation, saying the runtime switches immediately", async () => {
    vi.mocked(api.activatePromptVersion).mockResolvedValue({
      activated: "pv-2", promptType: "extraction", version: "1.1.0", hash: "1111222233334444",
    });
    renderAi(<AiPromptsPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Ativar versão 1.1.0" }));

    const confirm = await screen.findByRole("alertdialog");
    expect(confirm).toHaveTextContent("O runtime passa a usar esta versão imediatamente.");
    expect(confirm).toHaveTextContent("1111222233334444");
    expect(api.activatePromptVersion).not.toHaveBeenCalled();

    fireEvent.click(within(confirm).getByRole("button", { name: "Ativar versão" }));
    await waitFor(() => expect(api.activatePromptVersion).toHaveBeenCalledWith("pv-2"));
    expect(toast.success).toHaveBeenCalledWith("Versão 1.1.0 ativada. Hash 1111222233334444.");
    await waitFor(() => expect(api.fetchPromptVersions).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
  });

  it("shows the 409 of a concurrent activation and refetches the list", async () => {
    const msg = "Outra versão deste prompt acabou de ser ativada. Recarregue a lista.";
    vi.mocked(api.activatePromptVersion).mockRejectedValue(new ApiError(409, msg, "conflict", "req-2", undefined, msg));
    renderAi(<AiPromptsPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Ativar versão 1.1.0" }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Ativar versão" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(msg));
    await waitFor(() => expect(api.fetchPromptVersions).toHaveBeenCalledTimes(2));
  });

  it("does not promise a runtime change when activating a type the runtime does not use", async () => {
    vi.mocked(api.fetchPromptVersions).mockResolvedValue([
      promptVersion({ id: "pm-1", version: "0.1", promptType: "memory" }),
    ]);
    renderAi(<AiPromptsPage />);
    fireEvent.click(screen.getByRole("tab", { name: "Memória" }));
    fireEvent.click(await screen.findByRole("button", { name: "Ativar versão 0.1" }));
    const confirm = await screen.findByRole("alertdialog");
    expect(confirm).toHaveTextContent("Ainda não usado pelo runtime");
    expect(confirm).not.toHaveTextContent("O runtime passa a usar esta versão imediatamente.");
  });
});
