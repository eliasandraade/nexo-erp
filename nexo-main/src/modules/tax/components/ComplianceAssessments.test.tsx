import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { renderTax } from "../test/render";
import { ComplianceAssessments } from "./ComplianceAssessments";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  recordComplianceAssessment: vi.fn(),
  listObligations: vi.fn(),
}));

// jsdom lacks what Radix selects use to scroll and capture the pointer.
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const verified: api.TaxComplianceAssessmentDto = {
  id: "a1", status: "Verified", periodFrom: "2026-01-01", periodTo: "2026-06-30", scope: "Estabelecimento — NF-e emitidas",
  obligationsChecked: ["NF-e com IBS/CBS"], evidence: "Relatório de conferência", assessedBy: "Contabilidade", assessedAt: "2026-07-05T12:00:00Z",
  notes: null, limitations: "Amostra de documentos", pnctParticipation: false, recordedByName: "Diretora", createdAt: "2026-07-05T12:00:00Z",
  results: [{
    obligationCode: "NFE-IBSCBS", obligationName: "NF-e com IBS/CBS", status: "Verified", evidenceReference: "Relatório R-7", evidenceFileId: null,
    remediation: null, remediationVerifiedOn: null, remediationVerifiedBy: null, notes: null,
  }],
  supersedesAssessmentId: null,
};

const catalogue: api.TaxObligationDefinitionDto[] = [
  { id: "o1", code: "NFE-IBSCBS", name: "NF-e com IBS/CBS", description: "", reference: "LC 214/2025", active: true, createdByName: "Plataforma" },
  { id: "o2", code: "OLD", name: "Obrigação inativa", description: "", reference: "—", active: false, createdByName: "Plataforma" },
];

describe("ComplianceAssessments", () => {
  afterEach(cleanup);

  it("says obligations were verified in the period and scope — never that the company is compliant", () => {
    renderTax(<ComplianceAssessments items={[verified]} canRecord={false} />);

    expect(screen.getByText("Obrigações verificadas no período e no escopo indicados.")).toBeInTheDocument();
    expect(screen.getByText(/01\/01\/2026 a 30\/06\/2026/)).toBeInTheDocument();
    expect(screen.getByText(/Escopo: Estabelecimento — NF-e emitidas/)).toBeInTheDocument();
    expect(screen.getByText("Limitações: Amostra de documentos")).toBeInTheDocument();
    expect(screen.queryByText(/empresa conforme/i)).not.toBeInTheDocument();
    expect(screen.getByText("Só a diretoria registra uma avaliação.")).toBeInTheDocument();
  });

  it("marks PNCT participation as not being full compliance", () => {
    renderTax(<ComplianceAssessments items={[{ ...verified, id: "a2", status: "UnderRemediation", notes: "Campos ausentes", pnctParticipation: true }]} canRecord={false} />);

    expect(screen.getByText("Em regularização")).toBeInTheDocument();
    expect(screen.getByText("Participa do PNCT — não é regularidade integral")).toBeInTheDocument();
    expect(screen.queryByText("Obrigações verificadas no período e no escopo indicados.")).not.toBeInTheDocument();
  });

  it("shows each obligation assessed with its own status and evidence", () => {
    renderTax(<ComplianceAssessments items={[verified]} canRecord={false} />);
    expect(screen.getByText("NF-e com IBS/CBS")).toBeInTheDocument();
    expect(screen.getByText("evidência: Relatório R-7")).toBeInTheDocument();
  });

  it("refuses a verification without each obligation and its evidence", async () => {
    vi.mocked(api.listObligations).mockResolvedValue(catalogue);
    vi.mocked(api.recordComplianceAssessment).mockResolvedValue(verified);
    renderTax(<ComplianceAssessments items={[]} canRecord />);

    await screen.findByText("Cada obrigação do catálogo");
    await screen.findByLabelText("NF-e com IBS/CBS");
    expect(screen.queryByLabelText("Obrigação inativa")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Período: de"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("até"), { target: { value: "2026-06-30" } });
    fireEvent.change(screen.getByLabelText("Escopo"), { target: { value: "Estabelecimento" } });
    fireEvent.change(screen.getByLabelText("Quem avaliou"), { target: { value: "Contabilidade" } });
    // Verificado sem obrigação avaliada: recusado aqui, como no servidor.
    fireEvent.click(screen.getByRole("combobox", { name: "Situação" }));
    fireEvent.click(await screen.findByRole("option", { name: "Verificado no período e escopo" }));
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));
    expect(screen.getByText("Uma verificação lista cada obrigação conferida, com sua evidência — não só texto livre.")).toBeInTheDocument();
    expect(api.recordComplianceAssessment).not.toHaveBeenCalled();
  });

  it("records an assessment only with period, scope and who assessed", async () => {
    vi.mocked(api.listObligations).mockResolvedValue([]);
    vi.mocked(api.recordComplianceAssessment).mockResolvedValue(verified);
    renderTax(<ComplianceAssessments items={[]} canRecord />);

    expect(screen.getByText(/Nenhuma avaliação registrada: nenhuma conclusão/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));
    expect(screen.getByText("Informe o período (início e fim).")).toBeInTheDocument();
    expect(api.recordComplianceAssessment).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Período: de"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("até"), { target: { value: "2026-06-30" } });
    fireEvent.change(screen.getByLabelText("Escopo"), { target: { value: "Estabelecimento" } });
    fireEvent.change(screen.getByLabelText("Quem avaliou"), { target: { value: "Contabilidade" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));

    await waitFor(() => expect(api.recordComplianceAssessment).toHaveBeenCalledWith(expect.objectContaining({
      status: "Declared", periodFrom: "2026-01-01", periodTo: "2026-06-30", scope: "Estabelecimento", assessedBy: "Contabilidade", obligations: [],
    })));
  });
});
