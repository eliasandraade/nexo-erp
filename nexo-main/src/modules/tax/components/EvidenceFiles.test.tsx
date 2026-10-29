import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { renderTax } from "../test/render";
import { EvidenceFiles, StoredFileLine } from "./EvidenceFiles";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";

const session = { role: "diretoria" as string, userId: "u-dir" };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn().mockResolvedValue(undefined) }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getEvidenceStorage: vi.fn(),
  listEvidenceFiles: vi.fn(),
  uploadEvidenceFile: vi.fn(),
  revokeEvidenceFile: vi.fn(),
  setLegalHold: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

function storedFile(over: Partial<api.TaxStoredFileDto> = {}): api.TaxStoredFileDto {
  return {
    id: "f1", originalFileName: "contrato.pdf", contentType: "application/pdf", sizeBytes: 20480, sha256: "abc123", scanStatus: "Clean",
    status: "Active", version: 1, replacesFileId: null, description: null, uploadedByName: "Gerente Exemplo", uploadedAt: "2026-10-27T12:00:00Z",
    revokedByName: null, revokedAt: null, revocationReason: null, legalHold: false, legalHoldReason: null, ...over,
  };
}
const entry = (file: api.TaxStoredFileDto): api.TaxEvidenceFileDto => ({ file, owner: "Credit", ownerId: "c1", requirementId: null, evidenceType: "Other" });
const ready: api.TaxFileStorageStatusDto = {
  available: true, state: "ready", message: "Armazenamento privado disponível.", maxBytes: 20 * 1024 * 1024, acceptedExtensions: [".pdf"], scannerConfigured: false,
};
const pdf = () => new File(["%PDF-1.7"], "nota.pdf", { type: "application/pdf" });

describe("StoredFileLine", () => {
  afterEach(cleanup);

  it("shows the name, the SHA-256 of the bytes, the size, who sent it and an authorised download", async () => {
    renderTax(<StoredFileLine file={storedFile({ scanStatus: "NotScanned", legalHold: true })} canReadRevoked={false} />);

    expect(screen.getByText("contrato.pdf")).toBeInTheDocument();
    expect(screen.getByText("Em vigor")).toBeInTheDocument();
    expect(screen.getByText("Retenção legal")).toBeInTheDocument();
    expect(screen.getByText("não verificado por antivírus")).toBeInTheDocument();
    expect(screen.getByText("SHA-256 abc123")).toBeInTheDocument();
    expect(screen.getByText(/20 KB · v1 · Gerente Exemplo/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Baixar" }));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/tax/evidence-files/f1/content", "contrato.pdf", "Falha ao baixar"));
  });

  it("keeps a revoked file on record but lets only whoever may read revoked copies download it", () => {
    const revoked = storedFile({ status: "Revoked", revokedAt: "2026-10-28T10:00:00Z", revokedByName: "Diretora Exemplo", revocationReason: "arquivo errado" });
    renderTax(<StoredFileLine file={revoked} canReadRevoked={false} />);

    expect(screen.getByText("Revogado")).toBeInTheDocument();
    expect(screen.getByText(/revogado por Diretora Exemplo: arquivo errado/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Baixar" })).not.toBeInTheDocument();
    cleanup();

    renderTax(<StoredFileLine file={revoked} canReadRevoked />);
    expect(screen.getByRole("button", { name: "Baixar" })).toBeInTheDocument();
  });
});

describe("EvidenceFiles", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    session.role = "diretoria";
    vi.mocked(api.getEvidenceStorage).mockResolvedValue(ready);
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([]);
  });
  afterEach(cleanup);

  it("says when nothing is attached and uploads a file with its type and description", async () => {
    vi.mocked(api.uploadEvidenceFile).mockResolvedValue(entry(storedFile()));
    const { container } = renderTax(<EvidenceFiles owner="Credit" ownerId="c1" defaultType="FiscalDocument" />);

    expect(await screen.findByText("Nenhum arquivo anexado.")).toBeInTheDocument();
    expect(await screen.findByText(/Até 20 MB\. O tipo é conferido pelo conteúdo/)).toBeInTheDocument();
    const attach = screen.getByRole("button", { name: "Anexar" });
    expect(attach).toBeDisabled();

    fireEvent.click(screen.getByRole("combobox", { name: "Tipo" }));
    fireEvent.click(await screen.findByRole("option", { name: /^Instrumento comercial/ }));
    fireEvent.change(screen.getByLabelText("Descrição (opcional)"), { target: { value: "  Pedido assinado " } });
    const file = pdf();
    fireEvent.change(container.querySelector("#ef-file-c1")!, { target: { files: [file] } });
    fireEvent.click(attach);

    await waitFor(() => expect(api.uploadEvidenceFile).toHaveBeenCalledWith("Credit", "c1", "CommercialInstrument", file, "Pedido assinado"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Arquivo guardado no armazenamento privado."));
    expect(screen.getByLabelText("Descrição (opcional)")).toHaveValue("");
    expect(api.listEvidenceFiles).toHaveBeenCalledTimes(2);
  });

  it("sends no description when none was typed, and explains a refused upload", async () => {
    vi.mocked(api.uploadEvidenceFile).mockRejectedValue(new ApiError(422, "Tipo de arquivo não aceito.", "tax_file_type"));
    const { container } = renderTax(<EvidenceFiles owner="SplitSettlement" ownerId="s1" />);

    await screen.findByRole("button", { name: "Anexar" });
    const file = pdf();
    fireEvent.change(container.querySelector("#ef-file-s1")!, { target: { files: [file] } });
    fireEvent.click(screen.getByRole("button", { name: "Anexar" }));

    await waitFor(() => expect(api.uploadEvidenceFile).toHaveBeenCalledWith("SplitSettlement", "s1", "Other", file, null));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Tipo de arquivo não aceito", { description: "Tipo de arquivo não aceito." }));
  });

  it("says why files cannot be sent when the private storage is unavailable", async () => {
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({ ...ready, available: false, state: "unconfigured", message: "Armazenamento privado não configurado." });
    renderTax(<EvidenceFiles owner="Credit" ownerId="c1" />);

    expect(await screen.findByText("Armazenamento privado não configurado.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Anexar" })).not.toBeInTheDocument();
  });

  it("revokes a file only with a reason — it stays listed", async () => {
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([entry(storedFile())]);
    vi.mocked(api.revokeEvidenceFile).mockResolvedValue(entry(storedFile({ status: "Revoked" })));
    renderTax(<EvidenceFiles owner="Credit" ownerId="c1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Revogar" }));
    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("O arquivo deixa de valer, mas continua registrado e auditável.");
    const confirm = within(dialog).getByRole("button", { name: "Revogar" });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: "enviado por engano" } });
    fireEvent.click(confirm);

    await waitFor(() => expect(api.revokeEvidenceFile).toHaveBeenCalledWith("f1", "enviado por engano"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Arquivo revogado (continua registrado)."));
    expect(screen.getByText("contrato.pdf")).toBeInTheDocument();
  });

  it("toasts a refused revocation", async () => {
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([entry(storedFile())]);
    vi.mocked(api.revokeEvidenceFile).mockRejectedValue(new ApiError(409, "Arquivo em retenção legal."));
    renderTax(<EvidenceFiles owner="Credit" ownerId="c1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Revogar" }));
    fireEvent.change(await screen.findByLabelText("Motivo"), { target: { value: "x" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Revogar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Arquivo em retenção legal." }));
  });

  it("lets only Diretoria apply and lift a legal hold, with a reason", async () => {
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([
      entry(storedFile()),
      entry(storedFile({ id: "f2", originalFileName: "retido.pdf", legalHold: true, status: "Revoked" })),
    ]);
    vi.mocked(api.setLegalHold).mockResolvedValue(entry(storedFile({ legalHold: true })));
    renderTax(<EvidenceFiles owner="ComplianceAssessment" ownerId="a1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Reter legalmente" }));
    let dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("Aplicar retenção legal");
    expect(dialog).toHaveTextContent("nenhuma política de retenção torna o arquivo elegível para descarte");
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: "processo em curso" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar" }));
    await waitFor(() => expect(api.setLegalHold).toHaveBeenCalledWith("f1", true, "processo em curso"));

    // The revoked file has no revoke action, but its hold can still be lifted.
    expect(screen.getAllByRole("button", { name: "Revogar" })).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Retirar retenção legal" }));
    dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: "processo encerrado" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar" }));
    await waitFor(() => expect(api.setLegalHold).toHaveBeenCalledWith("f2", false, "processo encerrado"));
  });

  it("toasts a refused legal hold", async () => {
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([entry(storedFile())]);
    vi.mocked(api.setLegalHold).mockRejectedValue(new ApiError(403, "Só a diretoria."));
    renderTax(<EvidenceFiles owner="Credit" ownerId="c1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Reter legalmente" }));
    fireEvent.change(await screen.findByLabelText("Motivo"), { target: { value: "x" } });
    fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Confirmar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Só a diretoria." }));
  });

  it("never offers a manager the legal hold nor the download of a revoked copy", async () => {
    session.role = "gerente";
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([
      entry(storedFile()),
      entry(storedFile({ id: "f2", originalFileName: "antigo.pdf", status: "Revoked", revokedAt: "2026-10-28T10:00:00Z", revokedByName: "Diretora", revocationReason: "x" })),
    ]);
    renderTax(<EvidenceFiles owner="Credit" ownerId="c1" />);

    expect(await screen.findByText("antigo.pdf")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reter legalmente" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Retirar retenção legal" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Baixar" })).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Revogar" })).toBeInTheDocument();
  });
});
