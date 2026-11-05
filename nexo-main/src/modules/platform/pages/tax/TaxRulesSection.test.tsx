import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";
import { renderTax } from "@/modules/tax/test/render";
import { brazilTodayIso } from "@/modules/tax/lib/forms";
import type { TaxRuleDto, TaxRuleVersionDto, TaxStoredFileDto } from "@/modules/tax/api/tax.api";
import { TaxRulesSection } from "./TaxRulesSection";
import * as api from "../../services/taxAdminApi";

vi.mock("../../services/taxAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/taxAdminApi")>()),
  listRules: vi.fn(),
  listProfessionalReviews: vi.fn(),
  changeRuleVersionStatus: vi.fn(),
  approveForPilot: vi.fn(),
  withdrawPilotApproval: vi.fn(),
  recordProfessionalReview: vi.fn(),
  uploadDocument: vi.fn(),
}));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const notApproved: TaxRuleVersionDto["internalApproval"] = {
  status: "NotApproved", policy: null, approvedById: null, approvedBy: null, approvedByRole: null, approvedAt: null, scope: null,
  validFrom: null, validTo: null, caveats: null, decisionReference: null, withdrawalReason: null, withdrawnAt: null, nature: "",
};

const base: TaxRuleVersionDto = {
  id: "v1", version: 1, status: "Active", validFrom: "2026-01-01", validTo: "2026-12-31", priority: 10, conditions: [], result: {},
  contentHash: "hash", origin: "seed:teste", createdBy: "Catálogo de teste", notes: null, activatedAt: "2026-10-26T00:00:00Z",
  legalSourceCodes: ["FONTE-TESTE-1"], createdAt: "2026-10-26T00:00:00Z",
  professionalReview: "Approved", professionalReviewedBy: "Profissional Teste", professionalReviewedAt: "2026-10-27T00:00:00Z",
  professionalReviewNotes: null, professionalReference: "REG-TESTE-0", professionalReviewScope: "Escopo de teste", professionalReviewValidFrom: "2026-01-01",
  professionalReviewValidTo: null, professionalReviewAssumptions: null, professionalReviewCaveats: null, professionalReviewRegimes: ["LucroReal"],
  professionalReviewedOn: "2026-10-01", professionalReviewRecordedBy: "Operador Teste", professionalReviewRecordedByRole: "SuperAdmin",
  professionalReviewDocumentId: null,
  internalApproval: {
    ...notApproved, status: "ApprovedForControlledPilot", approvedBy: "Operador Teste", approvedByRole: "SuperAdmin", approvedAt: "2026-10-28T10:00:00Z",
    decisionReference: "REF-APROVACAO-TESTE",
  },
};

const unverified: TaxRuleVersionDto = {
  ...base, id: "v2", version: 2, status: "Unverified", validTo: null, legalSourceCodes: [], professionalReview: "Pending", professionalReviewedBy: null,
  professionalReference: null, professionalReviewRegimes: null, internalApproval: notApproved,
};

const withdrawn: TaxRuleVersionDto = {
  ...base, id: "v3", version: 3, professionalReview: "Pending", professionalReviewedBy: null,
  internalApproval: { ...base.internalApproval, status: "Withdrawn", withdrawalReason: "Fonte revista no teste" },
};

const rule: TaxRuleDto = { id: "r1", key: "REGRA.TESTE", name: "Regra de teste", description: "Descrição da regra de teste.", versions: [base, unverified, withdrawn] };

const storedFile: TaxStoredFileDto = {
  id: "d1", originalFileName: "revisao-teste.pdf", contentType: "application/pdf", sizeBytes: 2048, sha256: "abc123", scanStatus: "Clean" as never,
  status: "Active", version: 1, replacesFileId: null, description: null, uploadedByName: "Operador Teste", uploadedAt: "2026-10-28T10:00:00Z",
  revokedByName: null, revokedAt: null, revocationReason: null, legalHold: false, legalHoldReason: null,
};

const fullReview: api.TaxProfessionalReviewRecordDto = {
  id: "rv1", ruleVersionId: "v1", status: "ApprovedWithCaveats", previousStatus: "Pending", reviewer: "Profissional Teste",
  professionalReference: "REG-TESTE-1", reviewedOn: "2026-10-01", scope: "Escopo revisado", coveredRegimes: ["LucroReal", "LucroPresumido"],
  validFrom: "2026-01-01", validTo: "2026-12-31", assumptions: "Premissa de teste", caveats: "Ressalva de teste", notes: "Nota de teste",
  supportingDocument: storedFile, recordedByName: "Operador Teste", recordedByRole: "SuperAdmin", recordedAt: "2026-10-28T10:00:00Z",
};

const minimalReview: api.TaxProfessionalReviewRecordDto = {
  ...fullReview, id: "rv2", status: "ReviewReceived", previousStatus: "ApprovedWithCaveats", professionalReference: null, reviewedOn: null, scope: null,
  coveredRegimes: [], validFrom: null, validTo: null, assumptions: null, caveats: null, notes: null, supportingDocument: null,
};

function openHistory(index = 0) {
  const details = screen.getAllByText("Histórico de revisões profissionais")[index].closest("details")!;
  details.open = true;
  fireEvent(details, new Event("toggle"));
  return details;
}

const reviewButton = async (index = 0) => (await screen.findAllByRole("button", { name: "Registrar revisão profissional recebida…" }))[index];
const submitReview = () => fireEvent.click(screen.getByRole("button", { name: "Registrar revisão" }));

async function chooseStatus(label: string) {
  fireEvent.click(screen.getByRole("combobox", { name: "Resultado da revisão" }));
  fireEvent.click(await screen.findByRole("option", { name: label }));
}

describe("TaxRulesSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listRules).mockResolvedValue([rule]);
    vi.mocked(api.listProfessionalReviews).mockResolvedValue([]);
  });
  afterEach(cleanup);

  it("shows each version with its internal approval and professional review as separate records, and the acts each allows", async () => {
    renderTax(<TaxRulesSection />);

    expect(await screen.findByText("Regra de teste")).toBeInTheDocument();
    expect(screen.getByText("Descrição da regra de teste.")).toBeInTheDocument();
    expect(screen.getByText(/Aprovação interna por Operador Teste \(SuperAdmin\) em .* · REF-APROVACAO-TESTE$/)).toBeInTheDocument();
    expect(screen.getByText(/retirada: Fonte revista no teste/)).toBeInTheDocument();
    expect(screen.getByText("Revisão: Profissional Teste (REG-TESTE-0) · registrada por Operador Teste · regimes LucroReal")).toBeInTheDocument();
    expect(screen.getByText(/01\/01\/2026 em diante · fontes —/)).toBeInTheDocument();
    expect(screen.getByText("Aprovado internamente para piloto controlado")).toBeInTheDocument();
    expect(screen.getByText("Revisão profissional aprovada")).toBeInTheDocument();

    expect(screen.getAllByRole("button", { name: "Revogar versão" })).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: "Ativar versão" })).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: "Retirar aprovação interna" })).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: "Aprovar para o piloto…" })).toHaveLength(1);
    expect(screen.getAllByRole("button", { name: "Registrar revisão profissional recebida…" })).toHaveLength(3);
    expect(api.listProfessionalReviews).not.toHaveBeenCalled(); // the history loads only when opened
  });

  it("says when the catalogue is empty and explains a 403", async () => {
    vi.mocked(api.listRules).mockResolvedValueOnce([]);
    renderTax(<TaxRulesSection />);
    expect(await screen.findByText("Nenhuma regra no catálogo.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.listRules).mockRejectedValueOnce(new ApiError(403, "Proibido"));
    renderTax(<TaxRulesSection />);
    expect(await screen.findByText("Sem permissão")).toBeInTheDocument();
    expect(screen.getByText(/Seu perfil não pode ver as regras/)).toBeInTheDocument();
  });

  it("activates a version only after confirming, and does nothing on cancel", async () => {
    vi.mocked(api.changeRuleVersionStatus).mockResolvedValue(unverified);
    renderTax(<TaxRulesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Ativar versão" }));

    let dialog = within(screen.getByRole("alertdialog"));
    expect(dialog.getByText("Ativar REGRA.TESTE v2?")).toBeInTheDocument();
    expect(dialog.getByText(/Ativar não aprova a regra para o piloto nem registra revisão profissional/)).toBeInTheDocument();
    fireEvent.click(dialog.getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(api.changeRuleVersionStatus).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Ativar versão" }));
    dialog = within(screen.getByRole("alertdialog"));
    fireEvent.click(dialog.getByRole("button", { name: "Ativar versão" }));
    await waitFor(() => expect(api.changeRuleVersionStatus).toHaveBeenCalledWith("v2", "Active"));
    expect(toast.success).toHaveBeenCalledWith("Situação da versão alterada.");
  });

  it("revokes a version after confirming, and toasts a refusal", async () => {
    vi.mocked(api.changeRuleVersionStatus).mockRejectedValue(new ApiError(422, "Recusado no teste.", "tax_rule_status"));
    renderTax(<TaxRulesSection />);
    fireEvent.click((await screen.findAllByRole("button", { name: "Revogar versão" }))[0]);

    const dialog = within(screen.getByRole("alertdialog"));
    expect(dialog.getByText("Revogar REGRA.TESTE v1?")).toBeInTheDocument();
    expect(dialog.getByText(/cálculos já feitos continuam reproduzíveis/)).toBeInTheDocument();
    fireEvent.click(dialog.getByRole("button", { name: "Revogar versão" }));

    await waitFor(() => expect(api.changeRuleVersionStatus).toHaveBeenCalledWith("v1", "Revoked"));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it("withdraws the internal approval only with a reason", async () => {
    vi.mocked(api.withdrawPilotApproval).mockResolvedValue(base);
    renderTax(<TaxRulesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Retirar aprovação interna" }));

    const dialog = within(screen.getByRole("dialog"));
    const confirm = dialog.getByRole("button", { name: "Retirar" });
    expect(confirm).toBeDisabled();
    fireEvent.change(dialog.getByLabelText("Motivo"), { target: { value: "Motivo de teste" } });
    fireEvent.click(confirm);

    await waitFor(() => expect(api.withdrawPilotApproval).toHaveBeenCalledWith("v1", "Motivo de teste"));
    expect(toast.success).toHaveBeenCalledWith("Aprovação interna retirada.");
  });

  it("approves for the pilot only with scope, caveats and a decision reference", async () => {
    vi.mocked(api.approveForPilot).mockRejectedValueOnce(new ApiError(422, "Fontes não conferidas no teste.")).mockResolvedValue(withdrawn);
    renderTax(<TaxRulesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Aprovar para o piloto…" }));

    expect(screen.getByRole("heading", { name: "Aprovar REGRA.TESTE v3 para o piloto" })).toBeInTheDocument();
    expect(within(screen.getByRole("dialog")).getByText(/Não é parecer jurídico, contábil ou tributário profissional/)).toBeInTheDocument();
    const register = screen.getByRole("button", { name: "Registrar aprovação interna" });
    expect(screen.getByLabelText("Escopo")).toHaveValue("");
    expect(screen.getByLabelText("Ressalvas aceitas")).toHaveValue("");
    expect(screen.getByLabelText("Referência da decisão")).toHaveValue("");
    expect(screen.getByLabelText("Vale de")).toHaveValue(brazilTodayIso());
    expect(register).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Escopo"), { target: { value: " Recorte de teste " } });
    expect(register).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Ressalvas aceitas"), { target: { value: "Ressalva de teste" } });
    expect(register).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Referência da decisão"), { target: { value: "   " } });
    expect(register).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Referência da decisão"), { target: { value: "REF-TESTE" } });
    expect(register).toBeEnabled();
    fireEvent.change(screen.getByLabelText("Vale de"), { target: { value: "" } });
    expect(register).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Vale de"), { target: { value: "2026-02-01" } });
    fireEvent.change(screen.getByLabelText("até (opcional)"), { target: { value: "2026-11-30" } });

    fireEvent.click(register);
    expect(await screen.findByText("Fontes não conferidas no teste.")).toBeInTheDocument();
    fireEvent.click(register);

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.approveForPilot).toHaveBeenLastCalledWith("v3", {
      scope: "Recorte de teste", validFrom: "2026-02-01", validTo: "2026-11-30", caveats: "Ressalva de teste", decisionReference: "REF-TESTE",
    });
    expect(toast.success).toHaveBeenCalledWith("Aprovação interna registrada.");
  });

  it("closes the pilot approval on cancel without approving", async () => {
    renderTax(<TaxRulesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Aprovar para o piloto…" }));
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.approveForPilot).not.toHaveBeenCalled();
  });

  describe("review history", () => {
    it("loads only when opened and shows the informed professional apart from who recorded it", async () => {
      vi.mocked(api.listProfessionalReviews).mockResolvedValue([fullReview, minimalReview]);
      renderTax(<TaxRulesSection />);
      await screen.findByText("Regra de teste");
      const details = within(openHistory(0));

      const informed = await details.findByText("Profissional informado: Profissional Teste (REG-TESTE-1) · revisão de 01/10/2026");
      const recorded = details.getAllByText(/^Registrado por Operador Teste \(SuperAdmin\) em .* — horário do servidor\.$/)[0];
      expect(informed).not.toBe(recorded);
      expect(informed.textContent).not.toContain("Operador Teste");
      expect(recorded.textContent).not.toContain("Profissional Teste");
      expect(api.listProfessionalReviews).toHaveBeenCalledWith("v1");

      expect(details.getByText("Escopo: Escopo revisado")).toBeInTheDocument();
      expect(details.getByText("Regimes: LucroReal, LucroPresumido")).toBeInTheDocument();
      expect(details.getByText("Vale de 01/01/2026 a 31/12/2026")).toBeInTheDocument();
      expect(details.getByText("Premissas: Premissa de teste")).toBeInTheDocument();
      expect(details.getByText("Ressalvas: Ressalva de teste")).toBeInTheDocument();
      expect(details.getByText("Notas: Nota de teste")).toBeInTheDocument();
      expect(details.getByText("SHA-256 abc123")).toBeInTheDocument();
      expect(details.getByText("Profissional informado: Profissional Teste")).toBeInTheDocument(); // minimal record
      expect(details.getByText("antes: Revisão profissional aprovou com ressalvas")).toBeInTheDocument();

      fireEvent.click(details.getByRole("button", { name: "Baixar" }));
      await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/platform/tax/documents/d1/content", "revisao-teste.pdf", "Falha ao baixar"));
    });

    it("says when a version has no review, and explains a failure", async () => {
      vi.mocked(api.listProfessionalReviews).mockImplementation(async (id) => {
        if (id === "v1") return [];
        throw new ApiError(403, "Proibido");
      });
      renderTax(<TaxRulesSection />);
      await screen.findByText("Regra de teste");

      expect(await within(openHistory(0)).findByText("Nenhuma revisão registrada — a versão está pendente.")).toBeInTheDocument();
      expect(await within(openHistory(1)).findByText("Sem permissão")).toBeInTheDocument();
    });
  });

  describe("professional review", () => {
    it("starts empty, offers only the statuses an operator can record, and refuses without the confirmation of receipt", async () => {
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(0));
      await screen.findByRole("dialog");

      expect(screen.getByRole("heading", { name: "Revisão profissional de REGRA.TESTE v1" })).toBeInTheDocument();
      expect(screen.getByLabelText("Profissional (como informado na revisão)")).toHaveValue("");
      expect(screen.getByLabelText("Registro profissional informado (opcional)")).toHaveValue("");
      expect(screen.getByLabelText("Data da revisão")).toHaveValue("");
      expect(screen.getByLabelText("Vale de")).toHaveValue("");
      expect(screen.getByRole("checkbox", { name: /foi de fato recebida/ })).not.toBeChecked();
      for (const box of screen.getAllByRole("checkbox")) expect(box).not.toBeChecked();
      expect(screen.getByRole("combobox", { name: "Resultado da revisão" })).toHaveTextContent("Revisão recebida — não é aprovação");

      fireEvent.click(screen.getByRole("combobox", { name: "Resultado da revisão" }));
      const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
      expect(options).toEqual([
        "Revisão recebida — não é aprovação", "Revisão profissional aprovada", "Revisão profissional aprovou com ressalvas",
        "Revisão profissional pediu ajustes", "Revisão profissional rejeitou",
      ]);
      expect(options).not.toContain("Revisão profissional pendente");
      fireEvent.click(screen.getByRole("option", { name: "Revisão recebida — não é aprovação" }));

      submitReview();
      expect(screen.getByText("Informe o profissional, como consta na revisão recebida.")).toBeInTheDocument();
      expect(screen.getByText("Confirme que esta revisão foi de fato recebida.")).toBeInTheDocument();

      fireEvent.change(screen.getByLabelText("Profissional (como informado na revisão)"), { target: { value: "Profissional Teste" } });
      submitReview();
      expect(screen.queryByText("Informe o profissional, como consta na revisão recebida.")).not.toBeInTheDocument();
      expect(screen.getByText("Confirme que esta revisão foi de fato recebida.")).toBeInTheDocument();
      expect(api.recordProfessionalReview).not.toHaveBeenCalled();
      expect(api.uploadDocument).not.toHaveBeenCalled();
    });

    it("records a received review with nulls for what was not informed", async () => {
      vi.mocked(api.recordProfessionalReview).mockResolvedValue(base);
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(1));
      await screen.findByRole("dialog");

      fireEvent.change(screen.getByLabelText("Profissional (como informado na revisão)"), { target: { value: " Profissional Teste " } });
      fireEvent.click(screen.getByRole("checkbox", { name: /foi de fato recebida/ }));
      submitReview();

      await waitFor(() => expect(api.recordProfessionalReview).toHaveBeenCalledWith("v2", {
        status: "ReviewReceived", reviewer: "Profissional Teste", notes: null, professionalReference: null, scope: null, validFrom: null, validTo: null,
        assumptions: null, caveats: null, coveredRegimes: [], reviewedOn: null, supportingDocumentId: null,
      }));
      expect(api.uploadDocument).not.toHaveBeenCalled();
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(toast.success).toHaveBeenCalledWith("Revisão profissional registrada no histórico.");
    });

    it("requires scope, start date and at least one regime for an approval, then uploads the document first and links it", async () => {
      vi.mocked(api.uploadDocument).mockResolvedValue({ ...storedFile, id: "doc-novo" });
      vi.mocked(api.recordProfessionalReview).mockResolvedValue(base);
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(0));
      await screen.findByRole("dialog");

      await chooseStatus("Revisão profissional aprovada");
      fireEvent.change(screen.getByLabelText("Profissional (como informado na revisão)"), { target: { value: "Profissional Teste" } });
      fireEvent.click(screen.getByRole("checkbox", { name: /foi de fato recebida/ }));
      submitReview();
      expect(screen.getByText("Uma aprovação diz o escopo.")).toBeInTheDocument();
      expect(screen.getByText("Uma aprovação diz desde quando vale.")).toBeInTheDocument();
      expect(screen.getByText("Uma aprovação diz a quais regimes se aplica.")).toBeInTheDocument();

      fireEvent.change(screen.getByLabelText("Escopo"), { target: { value: "Escopo aprovado de teste" } });
      fireEvent.change(screen.getByLabelText("Vale de"), { target: { value: "2026-01-01" } });
      fireEvent.change(screen.getByLabelText("até (opcional)"), { target: { value: "2026-12-31" } });
      fireEvent.change(screen.getByLabelText("Data da revisão"), { target: { value: "2026-10-01" } });
      fireEvent.change(screen.getByLabelText("Registro profissional informado (opcional)"), { target: { value: "REG-TESTE-9" } });
      fireEvent.change(screen.getByLabelText("Premissas"), { target: { value: "Premissa" } });
      fireEvent.change(screen.getByLabelText("Notas"), { target: { value: "Nota" } });
      fireEvent.click(screen.getByRole("checkbox", { name: "Lucro Real" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "MEI" }));
      fireEvent.click(screen.getByRole("checkbox", { name: "MEI" })); // unticked again
      fireEvent.click(screen.getByRole("checkbox", { name: "Lucro Presumido" }));
      expect(api.recordProfessionalReview).not.toHaveBeenCalled();

      const file = new File(["%PDF-1.4"], "revisao-teste.pdf", { type: "application/pdf" });
      fireEvent.change(document.getElementById("pr-file")!, { target: { files: [file] } });
      expect(screen.getByText(/revisao-teste\.pdf/)).toBeInTheDocument();
      submitReview();

      await waitFor(() => expect(api.recordProfessionalReview).toHaveBeenCalledTimes(1));
      expect(api.uploadDocument).toHaveBeenCalledWith("professional-review", file, "Revisão de REGRA.TESTE v1");
      expect(vi.mocked(api.uploadDocument).mock.invocationCallOrder[0]).toBeLessThan(vi.mocked(api.recordProfessionalReview).mock.invocationCallOrder[0]);
      expect(api.recordProfessionalReview).toHaveBeenCalledWith("v1", {
        status: "Approved", reviewer: "Profissional Teste", notes: "Nota", professionalReference: "REG-TESTE-9", scope: "Escopo aprovado de teste",
        validFrom: "2026-01-01", validTo: "2026-12-31", assumptions: "Premissa", caveats: null, coveredRegimes: ["LucroReal", "LucroPresumido"],
        reviewedOn: "2026-10-01", supportingDocumentId: "doc-novo",
      });
    });

    it("requires the caveats of an approval with caveats", async () => {
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(0));
      await screen.findByRole("dialog");
      await chooseStatus("Revisão profissional aprovou com ressalvas");
      submitReview();
      expect(screen.getByText("Aprovação com ressalvas precisa das ressalvas.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Ressalvas"), { target: { value: "Ressalva" } });
      expect(screen.queryByText("Aprovação com ressalvas precisa das ressalvas.")).not.toBeInTheDocument();
    });

    it("requires notes when changes were asked or the rule was rejected, and shows the server's refusal", async () => {
      vi.mocked(api.recordProfessionalReview).mockRejectedValue(new ApiError(422, "Revisão recusada no teste."));
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(0));
      await screen.findByRole("dialog");
      await chooseStatus("Revisão profissional rejeitou");
      fireEvent.change(screen.getByLabelText("Profissional (como informado na revisão)"), { target: { value: "Profissional Teste" } });
      fireEvent.click(screen.getByRole("checkbox", { name: /foi de fato recebida/ }));
      submitReview();
      expect(screen.getByText("Diga o que foi pedido ou por que foi rejeitada.")).toBeInTheDocument();
      expect(api.recordProfessionalReview).not.toHaveBeenCalled();

      await chooseStatus("Revisão profissional pediu ajustes");
      expect(screen.getByText("Diga o que foi pedido ou por que foi rejeitada.")).toBeInTheDocument();
      fireEvent.change(screen.getByLabelText("Notas"), { target: { value: "Ajustar a vigência" } });
      submitReview();

      expect(await within(screen.getByRole("dialog")).findByText("Revisão recusada no teste.")).toBeInTheDocument();
      expect(api.recordProfessionalReview).toHaveBeenCalledWith("v1", expect.objectContaining({ status: "ChangesRequired", notes: "Ajustar a vigência" }));
      expect(toast.error).toHaveBeenCalled();
    });

    it("closes on cancel without recording", async () => {
      renderTax(<TaxRulesSection />);
      fireEvent.click(await reviewButton(0));
      await screen.findByRole("dialog");
      fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(api.recordProfessionalReview).not.toHaveBeenCalled();
    });
  });
});

