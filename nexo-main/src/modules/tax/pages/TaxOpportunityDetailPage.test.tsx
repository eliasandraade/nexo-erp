import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { opportunity, renderTax } from "../test/render";
import TaxOpportunityDetailPage from "./TaxOpportunityDetailPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";
import { listUsers } from "@/modules/users/api/users.api";

const session = { role: "diretoria" as string, userId: "u-dir" };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn().mockResolvedValue(undefined) }));

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxOpportunity: vi.fn(),
  approveOpportunity: vi.fn(),
  verifyOpportunityExecution: vi.fn(),
  reviewOpportunityEvidence: vi.fn(),
  answerRequirement: vi.fn(),
  getEvidenceStorage: vi.fn(),
  uploadOpportunityEvidence: vi.fn(),
  addOpportunityEvidence: vi.fn(),
  replaceOpportunityEvidence: vi.fn(),
  revokeOpportunityEvidence: vi.fn(),
  assignOpportunity: vi.fn(),
  getOpportunityTimeline: vi.fn(),
  startOpportunityReview: vi.fn(),
}));
vi.mock("@/modules/users/api/users.api", () => ({
  listUsers: vi.fn().mockResolvedValue([
    { id: "u-ger", fullName: "Gerente Um", role: "gerente", status: "active" },
    { id: "u-ven", fullName: "Vendedor Um", role: "vendedor", status: "active" },
  ]),
}));
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
// jsdom lacks what Radix selects use to scroll and capture the pointer.
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};


const get = vi.mocked(api.getTaxOpportunity);
const approve = vi.mocked(api.approveOpportunity);

function show(o = opportunity()) {
  get.mockResolvedValue(o);
  return renderTax(<TaxOpportunityDetailPage />, { path: "/tributos/oportunidades/:id", route: `/tributos/oportunidades/${o.id}` });
}

describe("TaxOpportunityDetailPage", () => {
  beforeEach(() => {
    session.role = "diretoria";
    vi.clearAllMocks();
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: true, state: "ready", message: "Armazenamento privado disponível.", maxBytes: 20 * 1024 * 1024, acceptedExtensions: [".pdf"], scannerConfigured: false,
    });
  });
  afterEach(cleanup);

  it("answers the business questions and shows a calculated-tax reduction, never as a saving", async () => {
    show();

    expect(await screen.findByText("O que aconteceu?")).toBeInTheDocument();
    expect(screen.getByText("Quanto muda?")).toBeInTheDocument();
    expect(screen.getByText("Qual o risco e qual a fonte?")).toBeInTheDocument();
    expect(screen.getByText("Quem precisa aprovar?")).toBeInTheDocument();
    expect(screen.getAllByText("Redução do IBS/CBS calculado — não é economia").length).toBeGreaterThan(0);
    expect(screen.queryByText("Economia")).not.toBeInTheDocument();
    expect(screen.getByText("R$ 0,50")).toBeInTheDocument();
    expect(screen.getByText(/Revisão profissional pendente: as regras usadas estão em vigor no motor/)).toBeInTheDocument();
    expect(screen.getByText("Piloto controlado")).toBeInTheDocument();
    expect(screen.getByText("Aprovado internamente para piloto controlado")).toBeInTheDocument();
    expect(screen.getByText(/Não é parecer jurídico, contábil ou tributário profissional/)).toBeInTheDocument();
    expect(screen.getByText(/Não executa alteração fiscal, contábil ou financeira automaticamente/)).toBeInTheDocument();
    expect(screen.getByText(/Decisões internas registradas: DEC-REGRA-1/)).toBeInTheDocument();
  });

  it("asks for a justification when whoever analysed alone approves, and sends it", async () => {
    const o = opportunity({ requirements: opportunity().requirements.map((r) => ({ ...r, met: true })) });
    approve
      .mockRejectedValueOnce(new ApiError(422, "Quem fez a análise sozinho não aprova a própria análise sem justificar.", "tax_self_approval_requires_justification"))
      .mockResolvedValueOnce({ ...o, status: "Approved" });
    show(o);

    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Aprovar" }).at(-1)!);
    const field = await screen.findByLabelText("Por que você aprova a própria análise?");
    const confirm = screen.getAllByRole("button", { name: "Aprovar" }).at(-1)!;
    expect(confirm).toBeDisabled();

    fireEvent.change(field, { target: { value: "Diretoria de uma pessoa" } });
    fireEvent.click(confirm);
    await waitFor(() => expect(approve).toHaveBeenLastCalledWith("op-1", false, "Diretoria de uma pessoa"));
  });

  it("never offers a blocked structure: no saving, no approval, the reason in a red alert", async () => {
    show(opportunity({
      status: "Blocked", blockedAs: "DocumentMismatch", estimatedImpact: null, proposed: null,
      blockReason: "O desconto ainda depende de evento posterior.",
      summary: "Proposta bloqueada como estrutura proibida.",
    }));

    expect(await screen.findByText(/Estrutura proibida: .* — O desconto ainda depende de evento posterior\./)).toBeInTheDocument();
    expect(screen.queryByText("Quanto muda?")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Aprovar/ })).not.toBeInTheDocument();
    expect(screen.queryByText("R$ 0,50")).not.toBeInTheDocument();
  });

  it("shows only Diretoria the approval, and a manager only review actions", async () => {
    session.role = "gerente";
    show();

    expect(await screen.findByRole("button", { name: "Recusar" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Aprovar/ })).not.toBeInTheDocument();
  });

  it("requires the high risk to be acknowledged before approving", async () => {
    const o = opportunity({ risk: "High", requirements: opportunity().requirements.map((r) => ({ ...r, met: true })) });
    approve.mockResolvedValue({ ...o, status: "Approved" });
    show(o);

    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    const confirm = screen.getAllByRole("button", { name: "Aprovar" }).at(-1)!;
    expect(confirm).toBeDisabled();

    fireEvent.click(screen.getByRole("checkbox"));
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() => expect(approve).toHaveBeenCalledWith("op-1", true, null));
  });

  it("warns that unmet requirements will be refused, without hiding the server's decision", async () => {
    show();

    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    expect(screen.getByText(/1 condição ainda não está cumprida/)).toBeInTheDocument();
  });

  it("records the approved decision and its dossier fingerprint", async () => {
    show(opportunity({
      status: "Approved",
      decision: { id: "d1", opportunityId: "op-1", strategy: "Conceder o desconto como incondicional", risk: "Low", highRiskAcknowledged: false,
        engineVersion: "orken-tax-engine/1.0.0", approvedByName: "Diretora", approvedAt: "2026-10-27T13:00:00Z",
        selfApprovalJustification: "Diretoria de uma pessoa", dossierHash: "ab12cd", dossier: { strategy: "x" } },
    }));

    expect(await screen.findByText(/Cenário aprovado por Diretora/)).toBeInTheDocument();
    expect(screen.getByText(/dossiê ab12cd · motor orken-tax-engine\/1\.0\.0/)).toBeInTheDocument();
    expect(screen.getByText(/Justificativa: Diretoria de uma pessoa/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Recusar" })).not.toBeInTheDocument();
  });

  it("separates what is checked before the operation from what is checked after it", async () => {
    show();

    expect(await screen.findByText("O que preciso fazer antes da operação?")).toBeInTheDocument();
    expect(screen.getByText("O que confirmar depois da operação?")).toBeInTheDocument();
    expect(screen.getByText("Fica disponível depois que o cenário for aprovado.")).toBeInTheDocument();
    expect(screen.getByText(/Evidência vinculada: Instrumento comercial/)).toBeInTheDocument();
    expect(screen.getByText(/Falta evidência: Documento fiscal autorizado/)).toBeInTheDocument();
    expect(screen.getByText("Pedido com o preço já reduzido · PED-1")).toBeInTheDocument();
    expect(screen.getByText(/Quem pode aprovar: Quem analisou pode aprovar, registrando justificativa/)).toBeInTheDocument();
  });

  it("rejects a piece of evidence only with a reason", async () => {
    vi.mocked(api.reviewOpportunityEvidence).mockResolvedValue(opportunity());
    show();

    const reject = await screen.findByRole("button", { name: "Recusar evidência" });
    expect(reject).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Motivo da recusa de Pedido com o preço já reduzido"), { target: { value: "sem assinatura" } });
    fireEvent.click(reject);
    await waitFor(() => expect(api.reviewOpportunityEvidence).toHaveBeenCalledWith("op-1", "e1", false, "sem assinatura"));
  });

  it("never offers a justification where the policy requires an independent approver", async () => {
    const o = opportunity({ requiredApproval: "IndependentApprover", requirements: opportunity().requirements.map((r) => ({ ...r, met: true })) });
    approve.mockRejectedValueOnce(new ApiError(422, "Esta aprovação exige alguém que não participou da análise.", "tax_approval_requires_independent_approver"));
    show(o);

    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    expect(screen.getAllByText(/Aprovador independente: não pode ter participado da análise/).length).toBeGreaterThan(0);
    fireEvent.click(screen.getAllByRole("button", { name: "Aprovar" }).at(-1)!);
    await waitFor(() => expect(approve).toHaveBeenCalledTimes(1));
    expect(screen.queryByLabelText("Por que você aprova a própria análise?")).not.toBeInTheDocument();
  });

  it("verifies the execution only after the scenario was approved", async () => {
    const approved = opportunity({
      status: "Approved",
      decision: { id: "d1", opportunityId: "op-1", strategy: "Conceder o desconto", risk: "Low", highRiskAcknowledged: false,
        engineVersion: "orken-tax-engine/1.0.0", approvedByName: "Diretora", approvedAt: "2026-10-27T13:00:00Z",
        selfApprovalJustification: null, dossierHash: "ab12cd", dossier: {} },
    });
    vi.mocked(api.verifyOpportunityExecution).mockResolvedValue({ ...approved, status: "ExecutionVerified" });
    show(approved);

    expect(await screen.findByText(/Cenário aprovado no escopo indicado\. Não executa alteração comercial ou fiscal\./)).toBeInTheDocument();
    expect(screen.getByText(/não a veracidade material dos fatos/)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Documento fiscal (chave)"), { target: { value: "3526chave" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar verificação" }));
    await waitFor(() => expect(api.verifyOpportunityExecution).toHaveBeenCalledWith("op-1",
      { outcome: "Verified", fiscalDocumentReference: "3526chave", authorizationProtocol: null, notes: null }));
  });

  it("shows a sealed execution verification apart from the scenario decision", async () => {
    show(opportunity({
      status: "ExecutionVerified",
      decision: { id: "d1", opportunityId: "op-1", strategy: "Conceder o desconto", risk: "Low", highRiskAcknowledged: false,
        engineVersion: "orken-tax-engine/1.0.0", approvedByName: "Diretora", approvedAt: "2026-10-27T13:00:00Z",
        selfApprovalJustification: null, dossierHash: "ab12cd", dossier: {} },
      execution: { id: "x1", decisionId: "d1", outcome: "Verified", fiscalDocumentReference: "3526chave", authorizationProtocol: "135",
        notes: null, verifiedByName: "Diretora", verifiedAt: "2026-11-02T10:00:00Z", dossierHash: "ff00ee", dossier: {} },
    }));

    expect(await screen.findByText("Execução verificada conforme o cenário aprovado")).toBeInTheDocument();
    expect(screen.getByText(/dossiê da execução ff00ee/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar verificação" })).not.toBeInTheDocument();
  });

  it("shows why a rejected opportunity was closed", async () => {
    show(opportunity({ status: "Rejected", rejectionReason: "Cliente não aceita", reviewedByName: "Diretora" }));

    const reason = await screen.findByText("Cliente não aceita — Diretora");
    expect(reason.closest("[role='alert']")).toHaveTextContent("Recusada");
    expect(screen.queryByRole("button", { name: "Recusar" })).not.toBeInTheDocument();
  });
});

const decision = {
  id: "d1", opportunityId: "op-1", strategy: "Conceder o desconto", risk: "Low" as const, highRiskAcknowledged: true,
  engineVersion: "orken-tax-engine/1.0.0", approvedByName: "Diretora", approvedAt: "2026-10-27T13:00:00Z",
  selfApprovalJustification: null, dossierHash: "ab12cd", dossier: {},
};
const file = (over: Partial<api.TaxStoredFileDto> = {}): api.TaxStoredFileDto => ({
  id: "f1", originalFileName: "pedido.pdf", contentType: "application/pdf", sizeBytes: 4096, sha256: "f00d", scanStatus: "Clean",
  status: "Active", version: 1, replacesFileId: null, description: null, uploadedByName: "Gerente", uploadedAt: "2026-10-27T12:00:00Z",
  revokedByName: null, revokedAt: null, revocationReason: null, legalHold: false, legalHoldReason: null, ...over,
});
const evidence = (over: Partial<api.TaxEvidenceDto> = {}): api.TaxEvidenceDto => ({
  ...opportunity().evidence[0], hashKind: "reference", file: null, version: 1, supersedesEvidenceId: null, supersededByEvidenceId: null,
  revokedAt: null, revokedByName: null, revocationReason: null, ...over,
});
const pdf = () => new File(["%PDF-1.7"], "pedido-v2.pdf", { type: "application/pdf" });

/** Opens the lazy "Histórico" section the way the browser does: the details element toggles open. */
async function openTimeline() {
  const details = (await screen.findByText("Ver o que aconteceu, em ordem")).closest("details")!;
  details.open = true;
  fireEvent(details, new Event("toggle"));
}

describe("TaxOpportunityDetailPage — responsible, history, evidence and dossier", { timeout: 30_000 }, () => {
  beforeEach(() => {
    session.role = "diretoria";
    session.userId = "u-dir";
    vi.clearAllMocks();
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: true, state: "ready", message: "Armazenamento privado disponível.", maxBytes: 20 * 1024 * 1024, acceptedExtensions: [".pdf"], scannerConfigured: false,
    });
    vi.mocked(listUsers).mockResolvedValue([
      { id: "u-ger", fullName: "Gerente Um", role: "gerente", status: "active" },
      { id: "u-dir2", fullName: "Diretora Dois", role: "Diretoria", status: "Active" },
      { id: "u-ven", fullName: "Vendedor Um", role: "vendedor", status: "active" },
      { id: "u-old", fullName: "Gerente Antigo", role: "gerente", status: "inactive" },
    ] as Awaited<ReturnType<typeof listUsers>>);
  });
  afterEach(cleanup);

  it("assigns the analysis to whoever takes it — only the user id goes to the server", async () => {
    vi.mocked(api.assignOpportunity).mockResolvedValue(opportunity({ assignedToId: "u-dir", assignedToName: "Diretora Exemplo" }));
    show(opportunity({ assignedToId: null, assignedToName: null }));

    expect(await screen.findByText("ninguém")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Assumir" }));
    await waitFor(() => expect(api.assignOpportunity).toHaveBeenCalledWith("op-1", "u-dir"));
    expect(vi.mocked(api.assignOpportunity).mock.calls[0]).toHaveLength(2);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Responsável: Diretora Exemplo."));
    expect(await screen.findByText("Diretora Exemplo")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assumir" })).not.toBeInTheDocument();
  });

  it("offers as responsible only active managers and directors, and can clear the responsible", async () => {
    vi.mocked(api.assignOpportunity)
      .mockResolvedValueOnce(opportunity({ assignedToId: "u-ger", assignedToName: "Gerente Um" }))
      .mockResolvedValueOnce(opportunity({ assignedToId: null, assignedToName: null }));
    show(opportunity({ assignedToId: null, assignedToName: null }));

    fireEvent.click(await screen.findByRole("combobox", { name: "Escolher o responsável" }));
    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).getAllByRole("option").map((o) => o.textContent)).toEqual(["Ninguém", "Gerente Um", "Diretora Dois"]);
    fireEvent.click(within(listbox).getByRole("option", { name: "Gerente Um" }));
    await waitFor(() => expect(api.assignOpportunity).toHaveBeenCalledWith("op-1", "u-ger"));

    fireEvent.click(await screen.findByRole("combobox", { name: "Escolher o responsável" }));
    fireEvent.click(await screen.findByRole("option", { name: "Ninguém" }));
    await waitFor(() => expect(api.assignOpportunity).toHaveBeenLastCalledWith("op-1", null));
    await waitFor(() => expect(toast.success).toHaveBeenLastCalledWith("Responsável retirado."));
    for (const [, userId] of vi.mocked(api.assignOpportunity).mock.calls) expect(userId === null || userId.startsWith("u-")).toBe(true);
  });

  it("explains a refused assignment", async () => {
    vi.mocked(api.assignOpportunity).mockRejectedValue(new ApiError(422, "Só gerência ou diretoria."));
    show(opportunity({ assignedToId: null, assignedToName: null }));

    fireEvent.click(await screen.findByRole("button", { name: "Assumir" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Só gerência ou diretoria." }));
  });

  it("does not offer to take a closed opportunity, nor to take one already yours", async () => {
    show(opportunity({ status: "Rejected", rejectionReason: "Cliente não aceita", assignedToId: null, assignedToName: null }));
    await screen.findByText("ninguém");
    expect(screen.queryByRole("button", { name: "Assumir" })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Escolher o responsável" })).not.toBeInTheDocument();
    expect(listUsers).not.toHaveBeenCalled();
    cleanup();

    show(opportunity({ assignedToId: "u-dir", assignedToName: "Diretora Exemplo" }));
    expect(await screen.findByRole("combobox", { name: "Escolher o responsável" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Assumir" })).not.toBeInTheDocument();
  });

  it("loads the history only when it is opened", async () => {
    vi.mocked(api.getOpportunityTimeline).mockResolvedValue([
      { action: "assign", description: "Responsável definido: Gerente Um", actorName: "Diretora", at: "2026-10-27T12:00:00Z", severity: "info" },
      { action: "review", description: "Revisão iniciada", actorName: "Gerente Um", at: "2026-10-27T13:00:00Z", severity: "info" },
    ]);
    show();

    await screen.findByText("Ver o que aconteceu, em ordem");
    expect(api.getOpportunityTimeline).not.toHaveBeenCalled();
    await openTimeline();

    expect(await screen.findByText("Responsável definido: Gerente Um")).toBeInTheDocument();
    expect(screen.getByText("Revisão iniciada")).toBeInTheDocument();
    expect(api.getOpportunityTimeline).toHaveBeenCalledWith("op-1");
  });

  it("says when the history is empty, and when it cannot be loaded", async () => {
    vi.mocked(api.getOpportunityTimeline).mockResolvedValueOnce([]);
    show();
    await openTimeline();
    expect(await screen.findByText("Nada registrado ainda.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.getOpportunityTimeline).mockRejectedValueOnce(new ApiError(403, "proibido"));
    show();
    await openTimeline();
    expect(await screen.findByText("Seu perfil não pode ver o histórico. Peça acesso à diretoria.")).toBeInTheDocument();
  });

  it("keeps superseded and revoked evidence visible as history, apart from what is in force", async () => {
    show(opportunity({
      evidence: [
        evidence({ id: "e2", description: "Pedido revisado", version: 2, status: "Accepted", reviewNotes: "confere", reviewedByName: "Diretora",
          hashKind: "sha256-file", file: file() }),
        evidence({ id: "e1", description: "Pedido original", status: "Superseded", supersededByEvidenceId: "e2" }),
        evidence({ id: "e0", description: "Contrato antigo", status: "Revoked", revokedAt: "2026-10-28T10:00:00Z", revokedByName: "Diretora",
          revocationReason: "documento errado", contentHash: "cafe", requirementId: null }),
      ],
    }));

    expect(await screen.findByText("Pedido revisado · PED-1")).toBeInTheDocument();
    expect(screen.getByText("versão 2")).toBeInTheDocument();
    expect(screen.getByText("Revisão: confere — Diretora")).toBeInTheDocument();
    expect(screen.getByText("pedido.pdf")).toBeInTheDocument();
    expect(screen.getByText("SHA-256 f00d")).toBeInTheDocument();
    const history = screen.getByText("Versões anteriores e revogadas (2)").closest("details")!;
    expect(within(history).getByText("Pedido original · PED-1")).toBeInTheDocument();
    expect(within(history).getByText("Substituída por versão nova")).toBeInTheDocument();
    expect(within(history).getByText("Contrato antigo · PED-1")).toBeInTheDocument();
    expect(within(history).getByText(/Revogada por Diretora em .*: documento errado/)).toBeInTheDocument();
    expect(within(history).getByText("sem arquivo · impressão do registro cafe")).toBeInTheDocument();
    // History is never editable.
    expect(within(history).queryByRole("button", { name: "Revogar" })).not.toBeInTheDocument();
    expect(within(history).queryByRole("button", { name: "Substituir por versão nova" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Substituir por versão nova" })).toHaveLength(1);
  });

  it("says when no evidence is in force, keeping the revoked ones in the history", async () => {
    show(opportunity({ evidence: [evidence({ status: "Revoked", revokedAt: "2026-10-28T10:00:00Z", revokedByName: "Diretora", revocationReason: "x" })] }));

    expect(await screen.findByText("Nenhuma evidência em vigor.")).toBeInTheDocument();
    expect(screen.getByText("Versões anteriores e revogadas (1)")).toBeInTheDocument();
  });

  it("registers evidence with a file through the private upload", async () => {
    vi.mocked(api.uploadOpportunityEvidence).mockResolvedValue(opportunity());
    const { container } = show();

    await screen.findByText("Adicionar evidência");
    await waitFor(() => expect(container.querySelector("#ev-file")).not.toBeNull());
    fireEvent.click(screen.getByRole("combobox", { name: "Para o requisito" }));
    fireEvent.click(await screen.findByRole("option", { name: "O desconto não depende de evento posterior." }));
    fireEvent.change(screen.getByLabelText("Evidência"), { target: { value: " Pedido assinado " } });
    fireEvent.change(screen.getByLabelText("Documento (opcional)"), { target: { value: " PED-2 " } });
    const f = pdf();
    fireEvent.change(container.querySelector("#ev-file")!, { target: { files: [f] } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar evidência" }));

    await waitFor(() => expect(api.uploadOpportunityEvidence).toHaveBeenCalledWith("op-1", f, {
      description: "Pedido assinado", documentReference: "PED-2", type: "CommercialInstrument", requirementId: "r2", notes: null,
    }));
    expect(api.addOpportunityEvidence).not.toHaveBeenCalled();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Evidência e arquivo guardados."));
    expect(screen.getByLabelText("Evidência")).toHaveValue("");
  });

  it("registers evidence by reference when there is no file, and asks for a description first", async () => {
    vi.mocked(api.addOpportunityEvidence).mockResolvedValue(opportunity());
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Registrar evidência" }));
    expect(toast.error).toHaveBeenCalledWith("Descreva a evidência.");
    expect(api.addOpportunityEvidence).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("combobox", { name: "Tipo" }));
    fireEvent.click(await screen.findByRole("option", { name: "Comprovante de pagamento" }));
    fireEvent.change(screen.getByLabelText("Evidência"), { target: { value: "Pedido no ERP do cliente" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar evidência" }));

    await waitFor(() => expect(api.addOpportunityEvidence).toHaveBeenCalledWith("op-1", {
      description: "Pedido no ERP do cliente", documentReference: null, type: "PaymentRecord", requirementId: null, notes: null,
    }));
    expect(api.uploadOpportunityEvidence).not.toHaveBeenCalled();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Evidência registrada."));
  });

  it("explains a refused evidence", async () => {
    vi.mocked(api.addOpportunityEvidence).mockRejectedValue(new ApiError(422, "Requisito de outra fase."));
    show();

    fireEvent.change(await screen.findByLabelText("Evidência"), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar evidência" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Requisito de outra fase." }));
  });

  it("says why no file can be attached when the private storage is unavailable", async () => {
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: false, state: "unconfigured", message: "Armazenamento privado não configurado.", maxBytes: 0, acceptedExtensions: [], scannerConfigured: false,
    });
    const { container } = show();

    expect(await screen.findByText("Armazenamento privado não configurado.")).toBeInTheDocument();
    expect(container.querySelector("#ev-file")).toBeNull();
    expect(screen.getByRole("button", { name: "Registrar evidência" })).toBeInTheDocument();
  });

  it("links execution evidence to the execution requirements, as a fiscal document by default", async () => {
    vi.mocked(api.addOpportunityEvidence).mockResolvedValue(opportunity());
    show(opportunity({ status: "Approved", decision }));

    fireEvent.click(await screen.findByRole("combobox", { name: "Para o requisito" }));
    const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
    expect(options).toEqual(["O desconto consta do documento fiscal autorizado da operação."]);
    fireEvent.click(screen.getByRole("option", { name: "O desconto consta do documento fiscal autorizado da operação." }));
    fireEvent.change(screen.getByLabelText("Evidência"), { target: { value: "NF-e autorizada" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar evidência" }));

    await waitFor(() => expect(api.addOpportunityEvidence).toHaveBeenCalledWith("op-1", expect.objectContaining({ type: "FiscalDocument", requirementId: "r3" })));
  });

  it("replaces a piece of evidence by a new version, with an optional new file", async () => {
    vi.mocked(api.replaceOpportunityEvidence).mockResolvedValue(opportunity());
    const { baseElement } = show(opportunity({ evidence: [evidence()] }));

    fireEvent.click(await screen.findByRole("button", { name: "Substituir por versão nova" }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("A versão 1 fica registrada como substituída; a nova volta para revisão.");
    expect(within(dialog).getByLabelText("Evidência")).toHaveValue("Pedido com o preço já reduzido");
    expect(within(dialog).getByLabelText("Documento (opcional)")).toHaveValue("PED-1");
    fireEvent.change(within(dialog).getByLabelText("Evidência"), { target: { value: "Pedido assinado pelas partes" } });
    fireEvent.change(within(dialog).getByLabelText("Documento (opcional)"), { target: { value: "" } });
    const f = pdf();
    await waitFor(() => expect(baseElement.querySelector("#rep-file")).not.toBeNull());
    fireEvent.change(baseElement.querySelector("#rep-file")!, { target: { files: [f] } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar versão nova" }));

    await waitFor(() => expect(api.replaceOpportunityEvidence).toHaveBeenCalledWith("op-1", "e1", "Pedido assinado pelas partes", null, f));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Versão nova registrada; a anterior ficou no histórico."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("replaces without a file, never with an empty description, and can be cancelled", async () => {
    vi.mocked(api.replaceOpportunityEvidence).mockRejectedValueOnce(new ApiError(409, "Evidência já substituída."));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Substituir por versão nova" }));
    let dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Evidência"), { target: { value: "  " } });
    expect(within(dialog).getByRole("button", { name: "Registrar versão nova" })).toBeDisabled();
    fireEvent.submit(dialog.querySelector("form")!);
    expect(api.replaceOpportunityEvidence).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("Evidência"), { target: { value: "Pedido v2" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar versão nova" }));
    await waitFor(() => expect(api.replaceOpportunityEvidence).toHaveBeenCalledWith("op-1", "e1", "Pedido v2", "PED-1", null));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Evidência já substituída." }));

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Substituir por versão nova" }));
    dialog = await screen.findByRole("dialog");
    fireEvent.keyDown(dialog, { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("revokes a piece of evidence only with a reason — it goes to the history, not away", async () => {
    vi.mocked(api.revokeOpportunityEvidence).mockResolvedValue(opportunity({
      evidence: [evidence({ status: "Revoked", revokedAt: "2026-10-28T10:00:00Z", revokedByName: "Diretora", revocationReason: "documento errado" })],
    }));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Revogar" }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Ela deixa de sustentar o requisito, mas continua no histórico e na auditoria.");
    expect(within(dialog).getByRole("button", { name: "Revogar" })).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: "documento errado" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Revogar" }));

    await waitFor(() => expect(api.revokeOpportunityEvidence).toHaveBeenCalledWith("op-1", "e1", "documento errado"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Evidência revogada — continua no histórico."));
    expect(await screen.findByText("Versões anteriores e revogadas (1)")).toBeInTheDocument();
    expect(screen.getByText("Pedido com o preço já reduzido · PED-1")).toBeInTheDocument();
  });

  it("explains a refused revocation", async () => {
    vi.mocked(api.revokeOpportunityEvidence).mockRejectedValue(new ApiError(422, "Decisão já tomada."));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Revogar" }));
    fireEvent.change(await screen.findByLabelText("Motivo"), { target: { value: "x" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Revogar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Decisão já tomada." }));
  });

  it("accepts a submitted piece of evidence", async () => {
    vi.mocked(api.reviewOpportunityEvidence).mockResolvedValue(opportunity());
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Aceitar evidência" }));
    await waitFor(() => expect(api.reviewOpportunityEvidence).toHaveBeenCalledWith("op-1", "e1", true, null));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Revisão da evidência registrada."));
  });

  it("lets Diretoria export the frozen dossier, with or without the files", async () => {
    show(opportunity({ status: "Approved", decision }));

    expect(await screen.findByText(/O ZIP traz o dossiê exatamente como foi congelado/)).toBeInTheDocument();
    expect(screen.getByText(/risco alto assumido/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Exportar dossiê (ZIP)" }));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/tax/decisions/d1/export?includeFiles=false", "dossie-d1.zip", "Falha ao baixar"));
    fireEvent.click(screen.getByRole("button", { name: "Exportar com os arquivos" }));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/tax/decisions/d1/export?includeFiles=true", "dossie-d1-com-arquivos.zip", "Falha ao baixar"));
  });

  it("does not offer a manager the dossier export", async () => {
    session.role = "gerente";
    show(opportunity({ status: "Approved", decision }));

    expect(await screen.findByText(/Cenário aprovado por Diretora/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Exportar dossiê (ZIP)" })).not.toBeInTheDocument();
    expect(screen.queryByText(/O ZIP traz o dossiê/)).not.toBeInTheDocument();
    expect(screen.getByText("Verificação da execução pendente.")).toBeInTheDocument();
  });

  it("states the materiality in force, or that the installation default applies", async () => {
    show(opportunity({ materiality: {
      id: "m2", version: 2, threshold: 10000, currency: "BRL", effectiveFrom: "2026-01-01", effectiveTo: null, rationale: "Porte",
      decisionReference: "ATA-7", configuredByName: "Diretora", configuredAt: "2026-01-01T12:00:00Z",
    } }));
    expect(await screen.findByText("Materialidade em vigor: R$ 10.000,00 (versão 2, ATA-7).")).toBeInTheDocument();
    cleanup();

    show(opportunity({ materiality: null }));
    expect(await screen.findByText("Materialidade: sem versão própria da empresa — vale o padrão da instalação.")).toBeInTheDocument();
  });

  it("starts the review of an identified opportunity and tells a manager who approves", async () => {
    session.role = "gerente";
    vi.mocked(api.startOpportunityReview).mockResolvedValue(opportunity({ status: "InReview" }));
    show(opportunity({ status: "Identified" }));

    expect(await screen.findByText("Só a diretoria vê o botão de aprovar.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Iniciar revisão" }));
    await waitFor(() => expect(api.startOpportunityReview).toHaveBeenCalledWith("op-1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Revisão iniciada."));
  });
});
