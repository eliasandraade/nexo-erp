import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { opportunity, renderTax } from "../test/render";
import TaxOpportunityDetailPage from "./TaxOpportunityDetailPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

const session = { role: "diretoria" as string };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxOpportunity: vi.fn(),
  approveOpportunity: vi.fn(),
  answerRequirement: vi.fn(),
}));

const get = vi.mocked(api.getTaxOpportunity);
const approve = vi.mocked(api.approveOpportunity);

function show(o = opportunity()) {
  get.mockResolvedValue(o);
  return renderTax(<TaxOpportunityDetailPage />, { path: "/tributos/oportunidades/:id", route: `/tributos/oportunidades/${o.id}` });
}

describe("TaxOpportunityDetailPage", () => {
  beforeEach(() => { session.role = "diretoria"; vi.clearAllMocks(); });
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
    expect(screen.getByText(/Revisão tributária pendente: as regras usadas estão em vigor no motor/)).toBeInTheDocument();
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

    expect(await screen.findByText(/Aprovada por Diretora/)).toBeInTheDocument();
    expect(screen.getByText(/dossiê ab12cd · motor orken-tax-engine\/1\.0\.0/)).toBeInTheDocument();
    expect(screen.getByText(/Justificativa: Diretoria de uma pessoa/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Recusar" })).not.toBeInTheDocument();
  });

  it("shows why a rejected opportunity was closed", async () => {
    show(opportunity({ status: "Rejected", rejectionReason: "Cliente não aceita", reviewedByName: "Diretora" }));

    const reason = await screen.findByText("Cliente não aceita — Diretora");
    expect(reason.closest("[role='alert']")).toHaveTextContent("Recusada");
    expect(screen.queryByRole("button", { name: "Recusar" })).not.toBeInTheDocument();
  });
});
