import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { opportunity, renderTax } from "../test/render";
import TaxOpportunityDetailPage from "./TaxOpportunityDetailPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

const session = { role: "diretoria" as string, userId: "u-dir" };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));

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
