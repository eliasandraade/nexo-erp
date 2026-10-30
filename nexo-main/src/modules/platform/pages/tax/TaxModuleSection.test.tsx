import type { ReactElement } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, render, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { TaxModuleSection } from "./TaxModuleSection";
import * as api from "../../services/taxAdminApi";

vi.mock("../../services/taxAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/taxAdminApi")>()),
  getModuleState: vi.fn(),
  getReadiness: vi.fn(),
  getActivationChecklist: vi.fn(),
  activateModule: vi.fn(),
  suspendModule: vi.fn(),
  deactivateModule: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const PILOT_PHRASE = "ATIVAR PILOTO DE TESTE";
const FINAL_PHRASE = "ATIVAR MODO DEFINITIVO DE TESTE";

const inactivePilot: api.TaxModuleStateDto = {
  configEnabled: true, configMode: "ControlledPilot", configurationProblems: [], status: "Inactive", activatedMode: null, latest: null,
  newWorkRuns: false, summary: "Módulo inativo: nenhum trabalho novo roda.", history: [],
};

const activation: api.TaxModuleActivationDto = {
  id: "h1", status: "Active", mode: "ControlledPilot", decisionReference: "REF-TESTE", reason: "Início do piloto de teste",
  actorName: "Operador Teste", actorRole: "SuperAdmin", at: "2026-10-28T10:00:00Z",
};

const readiness: api.TaxReadinessDto = {
  generatedAt: "2026-10-28T10:00:00Z", engineVersion: "motor-teste/1", canonicalVersion: "canon-teste/1", coveragePolicy: "cobertura-teste/1",
  module: inactivePilot,
  items: [
    { key: "config", area: "Configuração", label: "Configuração válida", level: "Ok", detail: "Nenhum problema encontrado." },
    { key: "storage", area: "Armazenamento", label: "Armazenamento privado", level: "Info", detail: "Disponível." },
    { key: "catalog", area: "Catálogo", label: "Catálogo de classificação", level: "Warning", detail: "Poucos códigos ativos." },
    { key: "rules", area: "Regras", label: "Regras ativas hoje", level: "Blocking", detail: "Nenhuma versão ativa." },
  ],
  rulesEligible: ["REGRA.TESTE.A v1"], rulesBlocking: [], pendingRegulation: [], lastAuditEvent: null,
};

const pilotChecklist: api.TaxActivationChecklistDto = {
  mode: "ControlledPilot", canActivate: true, confirmationPhrase: PILOT_PHRASE,
  items: [{ key: "rules.approved", area: "Regras", label: "Aprovação interna", level: "Ok", detail: "As 2 versões ativas hoje estão aprovadas para o modo." }],
  rulesMissingApproval: [], caveats: ["Ressalva de teste A", "Ressalva de teste B"],
};

const finalBlocked: api.TaxActivationChecklistDto = {
  mode: "ProfessionalFinal", canActivate: false, confirmationPhrase: FINAL_PHRASE,
  items: [{
    key: "rules.approved", area: "Regras", label: "Revisão profissional", level: "Blocking",
    detail: "2 de 3 versões ativas hoje sem a aprovação que o modo exige: REGRA.TESTE.A v1, REGRA.TESTE.B v2.",
  }],
  rulesMissingApproval: ["REGRA.TESTE.A v1", "REGRA.TESTE.B v2"], caveats: [],
};

function renderSection(ui: ReactElement = <TaxModuleSection />) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(<QueryClientProvider client={client}><MemoryRouter>{ui}</MemoryRouter></QueryClientProvider>);
  return client;
}

const next = () => screen.getByRole("button", { name: "Avançar" });
const advance = () => fireEvent.click(next());
const stepText = () => within(screen.getByRole("dialog")).getByText(/^Passo \d+ de \d+/).textContent;

describe("TaxModuleSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getModuleState).mockResolvedValue(inactivePilot);
    vi.mocked(api.getReadiness).mockResolvedValue(readiness);
    vi.mocked(api.getActivationChecklist).mockResolvedValue(pilotChecklist);
  });
  afterEach(cleanup);

  it("shows the module state as read from the installation, and the readiness diagnosis", async () => {
    renderSection();

    expect(await screen.findByText("Módulo inativo: nenhum trabalho novo roda.")).toBeInTheDocument();
    expect(screen.getByText("Inativo")).toBeInTheDocument();
    expect(screen.getByText("Novos cálculos e decisões NÃO rodam.")).toBeInTheDocument();
    expect(screen.getByText("Sim")).toBeInTheDocument();
    expect(screen.getByText("Piloto controlado")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ativar piloto controlado…" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Suspender…" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Desativar…" })).not.toBeInTheDocument();

    expect(await screen.findByText(/Motor motor-teste\/1 · canônico canon-teste\/1 · política de cobertura cobertura-teste\/1/)).toBeInTheDocument();
    for (const level of ["ok", "informação", "atenção", "bloqueia"]) expect(screen.getByLabelText(level)).toBeInTheDocument();
    expect(screen.getByText("Nenhuma versão ativa.")).toBeInTheDocument();
    expect(screen.getByText("REGRA.TESTE.A v1")).toBeInTheDocument();
    expect(screen.getAllByText("—")).toHaveLength(3); // activated mode, rules blocking, pending regulation
  });

  it("shows an active module with its invalid configuration (fail-closed) and the activation history", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({
      ...inactivePilot, status: "Active", activatedMode: "ControlledPilot", newWorkRuns: true, configEnabled: false, summary: "Ativo.",
      configurationProblems: ["Modo inválido na configuração de teste."], latest: activation, history: [activation],
    });
    renderSection();

    expect(await screen.findByText("Novos cálculos e decisões rodam.")).toBeInTheDocument();
    expect(screen.getByText("Não")).toBeInTheDocument();
    expect(screen.getByText(/Configuração inválida — o módulo recusa trabalho novo/)).toBeInTheDocument();
    expect(screen.getByText("Modo inválido na configuração de teste.")).toBeInTheDocument();
    expect(screen.getByText("Histórico de ativações (1)")).toBeInTheDocument();
    expect(screen.getByText(/REF-TESTE — Início do piloto de teste/)).toBeInTheDocument();
    expect(screen.getByText(/Operador Teste \(SuperAdmin\) em/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Ativar/ })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Suspender…" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Desativar…" })).toBeInTheDocument();
  });

  it("offers no mode to activate when the installation is configured as disabled", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, configMode: "Disabled", configEnabled: false });
    renderSection();

    expect(await screen.findByText(/A configuração da instalação está em “Desligado”: não há modo a ativar./)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Ativar/ })).not.toBeInTheDocument();
  });

  it("explains a 403 on the state and lets the readiness be retried after a failure", async () => {
    vi.mocked(api.getModuleState).mockRejectedValue(new ApiError(403, "Proibido"));
    vi.mocked(api.getReadiness).mockRejectedValueOnce(new Error("rede")).mockResolvedValue(readiness);
    renderSection();

    expect(await screen.findByText("Sem permissão")).toBeInTheDocument();
    expect(screen.getByText(/Seu perfil não pode ver o estado do módulo/)).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível carregar a prontidão")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Nenhuma versão ativa.")).toBeInTheDocument();
    expect(api.getReadiness).toHaveBeenCalledTimes(2);
  });

  it("walks the 9-step pilot activation with nothing pre-filled, gating every step, and sends exactly what was typed", async () => {
    vi.mocked(api.activateModule).mockResolvedValue({ ...inactivePilot, status: "Active" });
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar piloto controlado…" }));

    expect(screen.getByRole("heading", { name: "Ativar: Piloto controlado" })).toBeInTheDocument();
    expect(stepText()).toBe("Passo 1 de 9 — Modo");
    expect(within(screen.getByRole("list", { name: "Passos" })).getAllByRole("listitem")).toHaveLength(9);
    expect(next()).toBeDisabled(); // checklist still loading
    await waitFor(() => expect(next()).toBeEnabled());
    expect(api.getActivationChecklist).toHaveBeenCalledWith("ControlledPilot");
    expect(screen.getByText(/aprovação interna não é revisão profissional/)).toBeInTheDocument();

    advance();
    expect(stepText()).toBe("Passo 2 de 9 — Pré-condições");
    expect(screen.getByText("As 2 versões ativas hoje estão aprovadas para o modo.")).toBeInTheDocument();
    expect(next()).toBeEnabled();

    advance();
    expect(stepText()).toBe("Passo 3 de 9 — Regras sem aprovação");
    expect(screen.getByText("Todas as regras exigidas têm a aprovação que este modo pede.")).toBeInTheDocument();

    advance();
    expect(stepText()).toBe("Passo 4 de 9 — Ressalvas");
    expect(screen.getByText("Ressalva de teste A")).toBeInTheDocument();
    expect(screen.getByText("Ressalva de teste B")).toBeInTheDocument();

    advance();
    expect(stepText()).toBe("Passo 5 de 9 — Modo sombra");
    const shadow = screen.getByRole("checkbox");
    expect(shadow).not.toBeChecked();
    expect(next()).toBeDisabled();
    fireEvent.click(shadow);
    expect(shadow).toBeChecked();
    expect(next()).toBeEnabled();

    advance();
    expect(stepText()).toBe("Passo 6 de 9 — Referência da decisão");
    const reference = screen.getByLabelText("Referência da decisão (ata, chamado, documento)");
    expect(reference).toHaveValue("");
    expect(next()).toBeDisabled();
    fireEvent.change(reference, { target: { value: "   " } });
    expect(next()).toBeDisabled();
    fireEvent.change(reference, { target: { value: "  REF-TESTE " } });
    expect(next()).toBeEnabled();

    advance();
    expect(stepText()).toBe("Passo 7 de 9 — Motivo");
    const reason = screen.getByLabelText("Motivo");
    expect(reason).toHaveValue("");
    expect(next()).toBeDisabled();
    fireEvent.change(reason, { target: { value: "Motivo de teste" } });
    expect(next()).toBeEnabled();

    advance();
    expect(stepText()).toBe("Passo 8 de 9 — Frase de confirmação");
    const phrase = screen.getByLabelText(`Digite exatamente: ${PILOT_PHRASE}`);
    expect(phrase).toHaveValue("");
    expect(next()).toBeDisabled();
    for (const wrong of [PILOT_PHRASE.toLowerCase(), `${PILOT_PHRASE} `, PILOT_PHRASE.slice(0, -1)]) {
      fireEvent.change(phrase, { target: { value: wrong } });
      expect(next()).toBeDisabled();
    }
    fireEvent.change(phrase, { target: { value: PILOT_PHRASE } });
    expect(next()).toBeEnabled();

    // Going back keeps what was typed.
    fireEvent.click(screen.getByRole("button", { name: "Voltar" }));
    expect(screen.getByLabelText("Motivo")).toHaveValue("Motivo de teste");
    advance();

    advance();
    expect(stepText()).toBe("Passo 9 de 9 — Revisão final");
    expect(screen.getByText("REF-TESTE")).toBeInTheDocument();
    expect(screen.getByText("Motivo de teste")).toBeInTheDocument();
    expect(screen.getByText("Reconhecido")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Avançar" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Ativar" }));

    await waitFor(() => expect(api.activateModule).toHaveBeenCalledTimes(1));
    expect(api.activateModule).toHaveBeenCalledWith({
      mode: "ControlledPilot", decisionReference: "REF-TESTE", reason: "Motivo de teste", confirmation: PILOT_PHRASE, acknowledgeShadowMode: true,
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Módulo ativado. Ato registrado na auditoria.");
  });

  it("shows the server's refusal of the activation and keeps the wizard open", async () => {
    vi.mocked(api.activateModule).mockRejectedValue(new ApiError(422, "Frase de confirmação diferente da exigida.", "tax_module_confirmation"));
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar piloto controlado…" }));
    await waitFor(() => expect(next()).toBeEnabled());
    for (let i = 0; i < 4; i++) advance();
    fireEvent.click(screen.getByRole("checkbox"));
    advance();
    fireEvent.change(screen.getByLabelText("Referência da decisão (ata, chamado, documento)"), { target: { value: "REF-TESTE" } });
    advance();
    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "Motivo" } });
    advance();
    fireEvent.change(screen.getByLabelText(`Digite exatamente: ${PILOT_PHRASE}`), { target: { value: PILOT_PHRASE } });
    advance();
    fireEvent.click(screen.getByRole("button", { name: "Ativar" }));

    expect(await screen.findByText("Frase de confirmação diferente da exigida.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(toast.error).toHaveBeenCalled();
  });

  it("closes the wizard on cancel without activating", async () => {
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar piloto controlado…" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.activateModule).not.toHaveBeenCalled();
  });

  it("explains a failure to read the preconditions inside the wizard", async () => {
    vi.mocked(api.getActivationChecklist).mockRejectedValue(new ApiError(403, "Proibido"));
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar piloto controlado…" }));

    expect(await within(screen.getByRole("dialog")).findByText(/Seu perfil não pode ver as pré-condições/)).toBeInTheDocument();
    expect(next()).toBeDisabled();
  });

  it("blocks the definitive mode while rules lack a professional review: the rules are named and nothing is activated", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, configMode: "ProfessionalFinal" });
    vi.mocked(api.getActivationChecklist).mockResolvedValue(finalBlocked);
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar modo definitivo (revisão profissional)…" }));

    await waitFor(() => expect(next()).toBeEnabled());
    expect(stepText()).toBe("Passo 1 de 8 — Modo");
    expect(api.getActivationChecklist).toHaveBeenCalledWith("ProfessionalFinal");
    expect(screen.getByText(/cada regra exigida precisa de revisão profissional válida/)).toBeInTheDocument();

    advance();
    expect(stepText()).toBe("Passo 2 de 8 — Pré-condições");
    expect(screen.getByText(/sem a aprovação que o modo exige: REGRA\.TESTE\.A v1, REGRA\.TESTE\.B v2\./)).toBeInTheDocument();
    expect(screen.getByText("Há pré-condições bloqueando a ativação. Resolva-as e volte ao assistente.")).toBeInTheDocument();
    expect(next()).toBeDisabled();
    fireEvent.click(next());
    expect(stepText()).toBe("Passo 2 de 8 — Pré-condições");
    expect(screen.queryByRole("button", { name: "Ativar" })).not.toBeInTheDocument();
    expect(api.activateModule).not.toHaveBeenCalled();
  });

  it("blocks on the rules step, listing the missing rules, when the checklist turns blocked mid-wizard — and the final button stays disabled", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, configMode: "ProfessionalFinal" });
    vi.mocked(api.getActivationChecklist).mockResolvedValue({ ...finalBlocked, canActivate: true, items: [], rulesMissingApproval: [] });
    const client = renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar modo definitivo (revisão profissional)…" }));
    await waitFor(() => expect(next()).toBeEnabled());
    advance();
    advance();
    expect(stepText()).toBe("Passo 3 de 8 — Regras sem aprovação");

    client.setQueryData(api.PLATFORM_TAX_KEYS.checklist("ProfessionalFinal"), finalBlocked);

    expect(await screen.findByText("Bloqueado: regras sem revisão profissional válida")).toBeInTheDocument();
    const dialog = within(screen.getByRole("dialog"));
    expect(dialog.getByText("REGRA.TESTE.A v1")).toBeInTheDocument();
    expect(dialog.getByText("REGRA.TESTE.B v2")).toBeInTheDocument();
    expect(next()).toBeDisabled();
    expect(api.activateModule).not.toHaveBeenCalled();
  });

  it("names the pilot's missing internal approvals on the rules step, and keeps the final button disabled when blocked", async () => {
    vi.mocked(api.getActivationChecklist).mockResolvedValue(pilotChecklist);
    const client = renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Ativar piloto controlado…" }));
    await waitFor(() => expect(next()).toBeEnabled());
    advance();
    advance();
    client.setQueryData(api.PLATFORM_TAX_KEYS.checklist("ControlledPilot"), {
      ...pilotChecklist, canActivate: false, rulesMissingApproval: ["REGRA.TESTE.C v1"], caveats: [],
    });
    expect(await screen.findByText("Bloqueado: regras sem aprovação interna do piloto")).toBeInTheDocument();
    expect(screen.getByText("REGRA.TESTE.C v1")).toBeInTheDocument();

    // Once ready again the operator can continue; if it turns blocked at the end, "Ativar" is disabled.
    client.setQueryData(api.PLATFORM_TAX_KEYS.checklist("ControlledPilot"), { ...pilotChecklist, caveats: [] });
    await waitFor(() => expect(next()).toBeEnabled());
    advance();
    expect(screen.getByText("Sem ressalvas registradas.")).toBeInTheDocument();
    advance();
    fireEvent.click(screen.getByRole("checkbox"));
    advance();
    fireEvent.change(screen.getByLabelText("Referência da decisão (ata, chamado, documento)"), { target: { value: "REF-TESTE" } });
    advance();
    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "Motivo" } });
    advance();
    fireEvent.change(screen.getByLabelText(`Digite exatamente: ${PILOT_PHRASE}`), { target: { value: PILOT_PHRASE } });
    advance();
    client.setQueryData(api.PLATFORM_TAX_KEYS.checklist("ControlledPilot"), { ...pilotChecklist, canActivate: false });
    await waitFor(() => expect(screen.getByRole("button", { name: "Ativar" })).toBeDisabled());
    fireEvent.click(screen.getByRole("button", { name: "Ativar" }));
    expect(api.activateModule).not.toHaveBeenCalled();
  });

  it("suspends only with a reason and the exact phrase SUSPENDER", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, status: "Active", activatedMode: "ControlledPilot" });
    vi.mocked(api.suspendModule).mockResolvedValue({ ...inactivePilot, status: "Suspended" });
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Suspender…" }));

    expect(screen.getByRole("heading", { name: "Suspender o módulo" })).toBeInTheDocument();
    const confirm = screen.getByRole("button", { name: "Suspender" });
    const reason = screen.getByLabelText("Motivo");
    const phrase = screen.getByLabelText("Digite exatamente: SUSPENDER");
    expect(reason).toHaveValue("");
    expect(phrase).toHaveValue("");
    expect(confirm).toBeDisabled();

    fireEvent.change(phrase, { target: { value: "SUSPENDER" } });
    expect(confirm).toBeDisabled(); // no reason yet
    fireEvent.change(reason, { target: { value: "  " } });
    expect(confirm).toBeDisabled();
    fireEvent.change(reason, { target: { value: " Incidente de teste " } });
    expect(confirm).toBeEnabled();
    fireEvent.change(phrase, { target: { value: "suspender" } });
    expect(confirm).toBeDisabled();
    fireEvent.change(phrase, { target: { value: "SUSPENDER" } });

    fireEvent.click(confirm);
    await waitFor(() => expect(api.suspendModule).toHaveBeenCalledWith("Incidente de teste", "SUSPENDER"));
    expect(api.deactivateModule).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Módulo suspenso. Nenhum trabalho novo roda.");
  });

  it("deactivates only with a reason and the exact phrase DESATIVAR, and explains a refusal", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, status: "Suspended", activatedMode: "ControlledPilot" });
    vi.mocked(api.deactivateModule).mockRejectedValueOnce(new ApiError(403, "Só super admin desativa.")).mockResolvedValue(inactivePilot);
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Desativar…" }));

    expect(screen.getByRole("heading", { name: "Desativar o módulo" })).toBeInTheDocument();
    const confirm = screen.getByRole("button", { name: "Desativar" });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "Fim do piloto de teste" } });
    fireEvent.change(screen.getByLabelText("Digite exatamente: DESATIVAR"), { target: { value: "SUSPENDER" } });
    expect(confirm).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Digite exatamente: DESATIVAR"), { target: { value: "DESATIVAR" } });
    expect(confirm).toBeEnabled();

    fireEvent.click(confirm);
    expect(await screen.findByText("Só super admin desativa.")).toBeInTheDocument();
    expect(api.deactivateModule).toHaveBeenCalledWith("Fim do piloto de teste", "DESATIVAR");
    expect(toast.error).toHaveBeenCalled();

    fireEvent.click(confirm);
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Módulo desativado.");
    expect(api.suspendModule).not.toHaveBeenCalled();
  });

  it("closes the stop dialog on cancel without acting", async () => {
    vi.mocked(api.getModuleState).mockResolvedValue({ ...inactivePilot, status: "Active", activatedMode: "ControlledPilot" });
    renderSection();
    fireEvent.click(await screen.findByRole("button", { name: "Suspender…" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.suspendModule).not.toHaveBeenCalled();
  });
});
