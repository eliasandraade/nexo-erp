import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { renderTax } from "../test/render";
import { ComplianceAssessments } from "./ComplianceAssessments";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  recordComplianceAssessment: vi.fn(),
  listObligations: vi.fn(),
  getEvidenceStorage: vi.fn(),
  listEvidenceFiles: vi.fn(),
}));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { role: "diretoria", userId: "u-dir" } }) }));

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

describe("ComplianceAssessments", { timeout: 30_000 }, () => {
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

  it("asks what is being remediated, and for a verified remediation also when and by whom", async () => {
    vi.mocked(api.listObligations).mockResolvedValue([
      catalogue[0],
      { id: "o3", code: "EFD-IBSCBS", name: "Escrituração do IBS/CBS", description: "", reference: "LC 214/2025", active: true, createdByName: "Plataforma" },
    ]);
    vi.mocked(api.recordComplianceAssessment).mockResolvedValue(verified);
    renderTax(<ComplianceAssessments items={[]} canRecord />);

    await screen.findByLabelText("Escrituração do IBS/CBS");
    fireEvent.change(screen.getByLabelText("Período: de"), { target: { value: "2026-01-01" } });
    fireEvent.change(screen.getByLabelText("até"), { target: { value: "2026-06-30" } });
    fireEvent.change(screen.getByLabelText("Escopo"), { target: { value: "Estabelecimento" } });
    fireEvent.change(screen.getByLabelText("Quem avaliou"), { target: { value: "Contabilidade" } });
    fireEvent.click(screen.getByRole("combobox", { name: "Situação" }));
    fireEvent.click(await screen.findByRole("option", { name: "Regularização verificada" }));

    // In remediation: what is being remediated, but not yet when or by whom.
    fireEvent.click(screen.getByRole("combobox", { name: "Escrituração do IBS/CBS" }));
    fireEvent.click(await screen.findByRole("option", { name: "Em regularização" }));
    expect(document.getElementById("ob-EFD-IBSCBS-rem")).not.toBeNull();
    expect(document.getElementById("ob-EFD-IBSCBS-remon")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));
    expect(screen.getByText("Escrituração do IBS/CBS: descreva o que está sendo regularizado.")).toBeInTheDocument();
    expect(screen.getByText("Regularização verificada exige ao menos uma regularização verificada e nenhuma obrigação em aberto.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("combobox", { name: "Escrituração do IBS/CBS" }));
    fireEvent.click(await screen.findByRole("option", { name: "Verificado no período e escopo" }));
    expect(document.getElementById("ob-EFD-IBSCBS-rem")).toBeNull();
    fireEvent.change(document.getElementById("ob-EFD-IBSCBS-ev")!, { target: { value: "Recibo EFD" } });

    fireEvent.click(screen.getByRole("combobox", { name: "NF-e com IBS/CBS" }));
    fireEvent.click(await screen.findByRole("option", { name: "Regularização verificada" }));
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));
    expect(screen.getByText(/NF-e com IBS\/CBS: a regularização verificada diz o que foi regularizado, quando, por quem/)).toBeInTheDocument();
    fireEvent.change(document.getElementById("ob-NFE-IBSCBS-rem")!, { target: { value: "Notas reemitidas" } });
    fireEvent.change(document.getElementById("ob-NFE-IBSCBS-remon")!, { target: { value: "2026-07-10" } });
    fireEvent.change(document.getElementById("ob-NFE-IBSCBS-remby")!, { target: { value: "Contadora Exemplo" } });
    fireEvent.change(document.getElementById("ob-NFE-IBSCBS-ev")!, { target: { value: "Relatório R-8" } });
    fireEvent.change(document.getElementById("ob-NFE-IBSCBS-notes")!, { target: { value: "lote de junho" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar avaliação" }));

    await waitFor(() => expect(api.recordComplianceAssessment).toHaveBeenCalledWith(expect.objectContaining({
      status: "RemediationVerified",
      obligations: [
        { obligationCode: "NFE-IBSCBS", status: "RemediationVerified", evidenceReference: "Relatório R-8", evidenceFileId: null, remediation: "Notas reemitidas",
          remediationVerifiedOn: "2026-07-10", remediationVerifiedBy: "Contadora Exemplo", notes: "lote de junho" },
        { obligationCode: "EFD-IBSCBS", status: "Verified", evidenceReference: "Recibo EFD", evidenceFileId: null, remediation: null,
          remediationVerifiedOn: null, remediationVerifiedBy: null, notes: null },
      ],
    })));
  });

  it("shows each remediation, who verified it, and the files of each assessment to whoever records", async () => {
    vi.mocked(api.listObligations).mockResolvedValue([]);
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: true, state: "ready", message: "ok", maxBytes: 20 * 1024 * 1024, acceptedExtensions: [".pdf"], scannerConfigured: false,
    });
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([]);
    renderTax(<ComplianceAssessments canRecord items={[{
      ...verified, status: "RemediationVerified", evidence: "Pacote de documentos", notes: "Reemissão concluída", obligationsChecked: ["Livro fiscal"],
      results: [
        { ...verified.results![0], status: "RemediationVerified", remediation: "Notas reemitidas", remediationVerifiedOn: "2026-07-10",
          remediationVerifiedBy: "Contadora Exemplo", notes: "lote de junho" },
        { ...verified.results![0], obligationCode: "EFD", obligationName: "Escrituração", status: "UnderRemediation", evidenceReference: null,
          remediation: "Retificação em curso" },
      ],
    }]} />);

    expect(screen.getByText("regularização: Notas reemitidas — verificada por Contadora Exemplo em 10/07/2026")).toBeInTheDocument();
    expect(screen.getByText("regularização: Retificação em curso")).toBeInTheDocument();
    expect(screen.getByText("lote de junho")).toBeInTheDocument();
    expect(screen.getByText("Conferido: Livro fiscal")).toBeInTheDocument();
    expect(screen.getByText("Evidência: Pacote de documentos")).toBeInTheDocument();
    expect(screen.getByText("Notas: Reemissão concluída")).toBeInTheDocument();
    expect(screen.queryByText("Obrigações verificadas no período e no escopo indicados.")).not.toBeInTheDocument();
    expect(screen.getByText("Arquivos desta avaliação")).toBeInTheDocument();
    await waitFor(() => expect(api.listEvidenceFiles).toHaveBeenCalledWith("ComplianceAssessment", "a1"));
    expect(await screen.findByText("Nenhum arquivo anexado.")).toBeInTheDocument();
  });

  it("offers no files to whoever cannot record", () => {
    renderTax(<ComplianceAssessments items={[verified]} canRecord={false} />);
    expect(screen.queryByText("Arquivos desta avaliação")).not.toBeInTheDocument();
  });
});
