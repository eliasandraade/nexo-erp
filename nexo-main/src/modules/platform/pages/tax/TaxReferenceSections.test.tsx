import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";
import { renderTax } from "@/modules/tax/test/render";
import type {
  Page, TaxClassificationCodeDto, TaxLegalSourceDto, TaxObligationDefinitionDto, TaxRetentionPolicyDto, TaxStoredFileDto,
} from "@/modules/tax/api/tax.api";
import {
  TaxAuditSection, TaxClassificationsSection, TaxDocumentsSection, TaxObligationsSection, TaxRetentionSection, TaxSourcesSection,
} from "./TaxReferenceSections";
import * as api from "../../services/taxAdminApi";

vi.mock("../../services/taxAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/taxAdminApi")>()),
  listLegalSources: vi.fn(),
  createLegalSource: vi.fn(),
  verifyLegalSource: vi.fn(),
  listPlatformClassifications: vi.fn(),
  importClassifications: vi.fn(),
  changeClassificationStatus: vi.fn(),
  recordClassificationReview: vi.fn(),
  listPlatformObligations: vi.fn(),
  defineObligation: vi.fn(),
  setObligationActive: vi.fn(),
  listPlatformRetention: vi.fn(),
  configurePlatformRetention: vi.fn(),
  listDocuments: vi.fn(),
  uploadDocument: vi.fn(),
  revokeDocument: vi.fn(),
  getRefusals: vi.fn(),
  getTaxAudit: vi.fn(),
}));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const forbidden = () => new ApiError(403, "Proibido");

async function choose(combobox: string, option: string) {
  fireEvent.click(screen.getByRole("combobox", { name: combobox }));
  fireEvent.click(await screen.findByRole("option", { name: option }));
}

async function confirmReason(reason: string, confirmLabel: string) {
  const dialog = within(await screen.findByRole("dialog"));
  const confirm = dialog.getByRole("button", { name: confirmLabel });
  expect(confirm).toBeDisabled();
  fireEvent.change(dialog.getByLabelText("Motivo"), { target: { value: reason } });
  fireEvent.click(confirm);
}

const storedFile: TaxStoredFileDto = {
  id: "f1", originalFileName: "fonte-teste.pdf", contentType: "application/pdf", sizeBytes: 2048, sha256: "sha-teste-1", scanStatus: "Clean" as never,
  status: "Active", version: 1, replacesFileId: null, description: "Documento de teste", uploadedByName: "Operador Teste",
  uploadedAt: "2026-10-28T10:00:00Z", revokedByName: null, revokedAt: null, revocationReason: null, legalHold: false, legalHoldReason: null,
};

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

// ── Sources ───────────────────────────────────────────────────────────────────

const verifiedSource: TaxLegalSourceDto = {
  id: "s1", code: "FONTE-A", kind: "ComplementaryLaw", title: "Fonte de teste A", reference: "art. 1", url: "https://exemplo.test/a", excerpt: "Trecho A",
  publishedOn: null, textVerification: "Verified", verifiedAt: "2026-10-27T10:00:00Z", verifiedBy: "Pessoa Teste",
  notes: null, verificationRecordedBy: "Operador Teste", rulesUsing: 2,
};
const unverifiedSource: TaxLegalSourceDto = {
  ...verifiedSource, id: "s2", code: "FONTE-B", kind: "TipoDesconhecido", title: "Fonte de teste B", url: "https://exemplo.test/b", textVerification: "Unverified",
  verifiedAt: null, verifiedBy: null, verificationRecordedBy: null, rulesUsing: 0,
};

describe("TaxSourcesSection", () => {
  beforeEach(() => vi.mocked(api.listLegalSources).mockResolvedValue([verifiedSource, unverifiedSource]));

  it("lists sources with who compared the text and who recorded it, and offers the comparison only for unverified ones", async () => {
    renderTax(<TaxSourcesSection />);

    expect(await screen.findByText("Fonte de teste A")).toBeInTheDocument();
    expect(screen.getByText("Lei complementar · art. 1 · 2 regra(s)")).toBeInTheDocument();
    expect(screen.getByText("TipoDesconhecido · art. 1 · 0 regra(s)")).toBeInTheDocument();
    expect(screen.getByText(/Conferido por Pessoa Teste em .* · registrado por Operador Teste/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "https://exemplo.test/a" })).toHaveAttribute("rel", "noopener noreferrer");
    expect(screen.getByText("Texto oficial conferido")).toBeInTheDocument();
    expect(screen.getByText("Texto não conferido")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Conferir texto…" })).toHaveLength(1);
  });

  it("records the comparison with who did it, only once it is informed", async () => {
    vi.mocked(api.verifyLegalSource).mockResolvedValue({ ...unverifiedSource, textVerification: "Verified" });
    renderTax(<TaxSourcesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Conferir texto…" }));

    const register = screen.getByRole("button", { name: "Registrar conferência" });
    expect(screen.getByLabelText("Quem comparou com o texto oficial")).toHaveValue("");
    expect(register).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("button", { name: "Registrar conferência" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Conferir texto…" }));
    fireEvent.change(screen.getByLabelText("Quem comparou com o texto oficial"), { target: { value: " Pessoa Teste " } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar conferência" }));

    await waitFor(() => expect(api.verifyLegalSource).toHaveBeenCalledWith("FONTE-B", "Pessoa Teste"));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Registrar conferência" })).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Conferência do texto registrada.");
  });

  it("toasts a refused comparison", async () => {
    vi.mocked(api.verifyLegalSource).mockRejectedValue(new ApiError(403, "Sem permissão no teste."));
    renderTax(<TaxSourcesSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Conferir texto…" }));
    fireEvent.change(screen.getByLabelText("Quem comparou com o texto oficial"), { target: { value: "Pessoa Teste" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar conferência" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.any(String), { description: "Sem permissão no teste." }));
  });

  it("explains a 403 on the list", async () => {
    vi.mocked(api.listLegalSources).mockRejectedValueOnce(forbidden());
    renderTax(<TaxSourcesSection />);
    expect(await screen.findByText(/Seu perfil não pode ver as fontes/)).toBeInTheDocument();
  });

  it("validates a new source and creates it unverified, with nulls for the optional fields", async () => {
    vi.mocked(api.createLegalSource).mockResolvedValue(unverifiedSource);
    renderTax(<TaxSourcesSection />);
    await screen.findByText("Fonte de teste A");

    fireEvent.click(screen.getByRole("button", { name: "Cadastrar fonte" }));
    for (const p of ["Código: letras, números e hífen.", "Informe o título.", "Informe a referência (artigo, inciso…).", "Informe o link oficial (https).",
      "Cole o trecho do texto oficial."]) expect(screen.getByText(p)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Código"), { target: { value: "FONTE TESTE" } });
    fireEvent.change(screen.getByLabelText("Link oficial"), { target: { value: "http://exemplo.test" } });
    expect(screen.getByText("Código: letras, números e hífen.")).toBeInTheDocument();
    expect(screen.getByText("Informe o link oficial (https).")).toBeInTheDocument();
    expect(api.createLegalSource).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Código"), { target: { value: "FONTE-C" } });
    fireEvent.change(screen.getByLabelText("Link oficial"), { target: { value: "https://exemplo.test/c" } });
    fireEvent.change(screen.getByLabelText("Título"), { target: { value: "Fonte de teste C" } });
    fireEvent.change(screen.getByLabelText("Referência"), { target: { value: "art. 3" } });
    fireEvent.change(screen.getByLabelText("Trecho do texto oficial"), { target: { value: "Trecho C" } });
    await choose("Tipo", "Decreto");
    fireEvent.click(screen.getByRole("button", { name: "Cadastrar fonte" }));

    await waitFor(() => expect(api.createLegalSource).toHaveBeenCalledWith({
      code: "FONTE-C", kind: "Decree", title: "Fonte de teste C", reference: "art. 3", url: "https://exemplo.test/c", excerpt: "Trecho C",
      publishedOn: null, notes: null,
    }));
    await waitFor(() => expect(screen.getByLabelText("Código")).toHaveValue(""));
    expect(toast.success).toHaveBeenCalledWith("Fonte cadastrada — ainda não conferida.");
  });

  it("sends the publication date and notes when given, and toasts a refusal", async () => {
    vi.mocked(api.createLegalSource).mockRejectedValue(new ApiError(409, "Código já existe no teste."));
    renderTax(<TaxSourcesSection />);
    await screen.findByText("Fonte de teste A");
    fireEvent.change(screen.getByLabelText("Código"), { target: { value: "FONTE-D" } });
    fireEvent.change(screen.getByLabelText("Link oficial"), { target: { value: "https://exemplo.test/d" } });
    fireEvent.change(screen.getByLabelText("Título"), { target: { value: "Fonte D" } });
    fireEvent.change(screen.getByLabelText("Referência"), { target: { value: "art. 4" } });
    fireEvent.change(screen.getByLabelText("Trecho do texto oficial"), { target: { value: "Trecho D" } });
    fireEvent.change(screen.getByLabelText("Publicação (opcional)"), { target: { value: "2025-01-16" } });
    fireEvent.change(screen.getByLabelText("Notas (opcional)"), { target: { value: " Nota " } });
    fireEvent.click(screen.getByRole("button", { name: "Cadastrar fonte" }));

    await waitFor(() => expect(api.createLegalSource).toHaveBeenCalledWith(expect.objectContaining({ kind: "ComplementaryLaw", publishedOn: "2025-01-16", notes: "Nota" })));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });
});

// ── Classifications ─────────────────────────────────────────────────────────

const draftCode: TaxClassificationCodeDto = {
  id: "c1", kind: "CClassTrib", code: "000001", description: "Código de teste um", source: "Tabela Teste", sourceVersion: "v1", validFrom: "2026-01-01",
  validTo: "2026-12-31", status: "Draft", professionalReview: "Pending", professionalReviewNotes: "Profissional Teste pediu revisão", notes: null,
  importBatchId: "b1", sourceDocumentId: null, createdByName: "Operador Teste", createdAt: "2026-10-28T10:00:00Z",
  statusChangedByName: "Operador Dois", statusChangedAt: "2026-10-28T11:00:00Z",
};
const activeCode: TaxClassificationCodeDto = {
  ...draftCode, id: "c2", code: "000002", description: "Código de teste dois", sourceVersion: null, validTo: null, status: "Active",
  professionalReview: "Approved", professionalReviewNotes: null, statusChangedByName: null, statusChangedAt: null,
};
const page = <T,>(items: T[], total = items.length): Page<T> => ({ items, total, skip: 0, take: 50 });

describe("TaxClassificationsSection", () => {
  beforeEach(() => vi.mocked(api.listPlatformClassifications).mockResolvedValue(page([draftCode, activeCode])));

  it("lists the catalogue with its provenance and filters it by search and status", async () => {
    renderTax(<TaxClassificationsSection />);

    expect(await screen.findByText("Código de teste um")).toBeInTheDocument();
    expect(api.listPlatformClassifications).toHaveBeenCalledWith({ kind: "CClassTrib", status: "", search: "", skip: 0, take: 50 });
    expect(screen.getByText(/Tabela Teste v1 · 01\/01\/2026 a 31\/12\/2026 · importado por Operador Teste · situação por Operador Dois em/)).toBeInTheDocument();
    expect(screen.getByText("Revisão: Profissional Teste pediu revisão")).toBeInTheDocument();
    expect(screen.getByText("Rascunho")).toBeInTheDocument();
    expect(screen.getByText("2 registro(s)")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Buscar"), { target: { value: "0001" } });
    await waitFor(() => expect(api.listPlatformClassifications).toHaveBeenLastCalledWith(expect.objectContaining({ search: "0001", skip: 0 })));
    await choose("Situação", "Ativo");
    await waitFor(() => expect(api.listPlatformClassifications).toHaveBeenLastCalledWith(expect.objectContaining({ status: "Active" })));
    await choose("Situação", "Todas");
    await waitFor(() => expect(api.listPlatformClassifications).toHaveBeenLastCalledWith(expect.objectContaining({ status: "" })));
  });

  it("pages through a long catalogue", async () => {
    vi.mocked(api.listPlatformClassifications).mockResolvedValue(page([draftCode], 120));
    renderTax(<TaxClassificationsSection />);
    await screen.findByText("Código de teste um");

    fireEvent.click(screen.getByLabelText("Go to next page"));
    await waitFor(() => expect(api.listPlatformClassifications).toHaveBeenLastCalledWith(expect.objectContaining({ skip: 50 })));
  });

  it("says when nothing matches, and explains a 403", async () => {
    vi.mocked(api.listPlatformClassifications).mockResolvedValueOnce(page([]));
    renderTax(<TaxClassificationsSection />);
    expect(await screen.findByText("Nenhum código com estes filtros.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.listPlatformClassifications).mockRejectedValueOnce(forbidden());
    renderTax(<TaxClassificationsSection />);
    expect(await screen.findByText(/Seu perfil não pode ver o catálogo/)).toBeInTheDocument();
  });

  it("activates and inactivates a code only with a reason", async () => {
    vi.mocked(api.changeClassificationStatus).mockResolvedValue(activeCode);
    renderTax(<TaxClassificationsSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Ativar" }));
    await confirmReason("Fonte oficial conferida", "Ativar");
    await waitFor(() => expect(api.changeClassificationStatus).toHaveBeenCalledWith("c1", "Active", "Fonte oficial conferida"));
    expect(toast.success).toHaveBeenCalledWith("Situação do código alterada.");

    fireEvent.click(screen.getByRole("button", { name: "Inativar" }));
    await confirmReason("Fora do recorte", "Inativar");
    await waitFor(() => expect(api.changeClassificationStatus).toHaveBeenLastCalledWith("c2", "Inactive", "Fora do recorte"));
  });

  it("records a received review of a code only with notes, and toggles the form", async () => {
    vi.mocked(api.recordClassificationReview).mockResolvedValue(draftCode);
    renderTax(<TaxClassificationsSection />);
    const toggles = await screen.findAllByRole("button", { name: "Registrar revisão recebida…" });
    fireEvent.click(toggles[0]);
    expect(screen.getByRole("button", { name: "Registrar" })).toBeDisabled();
    fireEvent.click(toggles[0]);
    expect(screen.queryByRole("button", { name: "Registrar" })).not.toBeInTheDocument();

    fireEvent.click(toggles[0]);
    fireEvent.click(screen.getByRole("combobox", { name: "Resultado" }));
    const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
    expect(options).not.toContain("Revisão profissional pendente");
    fireEvent.click(screen.getByRole("option", { name: "Revisão profissional aprovada" }));
    fireEvent.change(screen.getByLabelText("Profissional informado e o que disse"), { target: { value: " Profissional Teste: aprovado " } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(api.recordClassificationReview).toHaveBeenCalledWith("c1", "Approved", "Profissional Teste: aprovado"));
    await waitFor(() => expect(screen.queryByRole("button", { name: "Registrar" })).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Revisão registrada.");
  });

  it("toasts a refused review", async () => {
    vi.mocked(api.recordClassificationReview).mockRejectedValue(new ApiError(403, "Recusado no teste."));
    renderTax(<TaxClassificationsSection />);
    fireEvent.click((await screen.findAllByRole("button", { name: "Registrar revisão recebida…" }))[1]);
    fireEvent.change(screen.getByLabelText("Profissional informado e o que disse"), { target: { value: "Notas" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(api.recordClassificationReview).toHaveBeenCalledWith("c2", "ReviewReceived", "Notas"));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it("imports only well-formed lines from a named source, reporting malformed lines without guessing", async () => {
    vi.mocked(api.importClassifications).mockResolvedValue({ batchId: "b2", imported: 1, skipped: ["000002"] });
    vi.mocked(api.uploadDocument).mockResolvedValue({ ...storedFile, id: "doc-fonte" });
    renderTax(<TaxClassificationsSection />);
    await screen.findByText("Código de teste um");

    fireEvent.click(screen.getByRole("button", { name: "Importar 0 código(s)" }));
    expect(screen.getByText("Informe a fonte (ex.: tabela oficial publicada).")).toBeInTheDocument();
    expect(screen.getByText("Cole ao menos uma linha.")).toBeInTheDocument();

    const lines = screen.getByLabelText("Linhas: código;descrição;início;fim (opcional)");
    fireEvent.change(lines, { target: { value: "000003;Válido;2026-01-01\n000004;Data errada;01/01/2026" } });
    fireEvent.change(screen.getByLabelText("Fonte"), { target: { value: " Tabela Teste " } });
    expect(screen.getByText("Linha 2: use código;descrição;AAAA-MM-DD;AAAA-MM-DD(opcional).")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Importar 1 código(s)" }));
    expect(api.importClassifications).not.toHaveBeenCalled();

    fireEvent.change(lines, { target: { value: "000003;Válido;2026-01-01\n000002;Duplicado;2026-01-01;2026-12-31" } });
    fireEvent.change(screen.getByLabelText("Versão da fonte (opcional)"), { target: { value: "v2" } });
    const file = new File(["%PDF-1.4"], "tabela-teste.pdf", { type: "application/pdf" });
    fireEvent.change(document.getElementById("ci-file")!, { target: { files: [file] } });
    fireEvent.click(screen.getByRole("button", { name: "Importar 2 código(s)" }));

    await waitFor(() => expect(api.importClassifications).toHaveBeenCalledWith({
      kind: "CClassTrib", source: "Tabela Teste", sourceVersion: "v2", sourceDocumentId: "doc-fonte",
      items: [
        { code: "000003", description: "Válido", validFrom: "2026-01-01", validTo: null, notes: null },
        { code: "000002", description: "Duplicado", validFrom: "2026-01-01", validTo: "2026-12-31", notes: null },
      ],
    }));
    expect(api.uploadDocument).toHaveBeenCalledWith("classification-source", file, "Fonte do catálogo: Tabela Teste");
    expect(vi.mocked(api.uploadDocument).mock.invocationCallOrder[0]).toBeLessThan(vi.mocked(api.importClassifications).mock.invocationCallOrder[0]);
    expect(toast.success).toHaveBeenCalledWith("1 código(s) importado(s) como rascunho. Ignorados: 000002.");
    await waitFor(() => expect(lines).toHaveValue(""));
  });

  it("imports without a document or version, and toasts a refusal", async () => {
    vi.mocked(api.importClassifications)
      .mockResolvedValueOnce({ batchId: "b3", imported: 1, skipped: [] })
      .mockRejectedValueOnce(new ApiError(403, "Recusado no teste."));
    renderTax(<TaxClassificationsSection />);
    await screen.findByText("Código de teste um");
    fireEvent.change(screen.getByLabelText("Fonte"), { target: { value: "Tabela Teste" } });
    fireEvent.change(screen.getByLabelText("Linhas: código;descrição;início;fim (opcional)"), { target: { value: "000005;Outro;2026-01-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Importar 1 código(s)" }));

    await waitFor(() => expect(api.importClassifications).toHaveBeenCalledWith(expect.objectContaining({ sourceVersion: null, sourceDocumentId: null })));
    expect(api.uploadDocument).not.toHaveBeenCalled();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("1 código(s) importado(s) como rascunho."));

    fireEvent.change(screen.getByLabelText("Linhas: código;descrição;início;fim (opcional)"), { target: { value: "000006;Outro;2026-01-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Importar 1 código(s)" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });
});

// ── Obligations ─────────────────────────────────────────────────────────────

const obligations: TaxObligationDefinitionDto[] = [
  { id: "o1", code: "OB-TESTE-1", name: "Obrigação de teste ativa", description: "Descrição de teste", reference: "REF-NORMA-1", active: true, createdByName: "Operador Teste" },
  { id: "o2", code: "OB-TESTE-2", name: "Obrigação de teste inativa", description: "", reference: "REF-NORMA-2", active: false, createdByName: "Operador Teste" },
];

describe("TaxObligationsSection", () => {
  beforeEach(() => vi.mocked(api.listPlatformObligations).mockResolvedValue(obligations));

  it("lists the catalogue and toggles an obligation", async () => {
    vi.mocked(api.setObligationActive).mockResolvedValue(obligations[0]);
    renderTax(<TaxObligationsSection />);

    expect(await screen.findByText("REF-NORMA-1 · Descrição de teste · Operador Teste")).toBeInTheDocument();
    expect(screen.getByText("REF-NORMA-2 · Operador Teste")).toBeInTheDocument();
    expect(screen.getByText("Inativa")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Inativar" }));
    await waitFor(() => expect(api.setObligationActive).toHaveBeenCalledWith("o1", false));
    fireEvent.click(screen.getByRole("button", { name: "Reativar" }));
    await waitFor(() => expect(api.setObligationActive).toHaveBeenLastCalledWith("o2", true));
  });

  it("defines an obligation only with code, name and normative reference", async () => {
    vi.mocked(api.defineObligation).mockResolvedValue(obligations[0]);
    renderTax(<TaxObligationsSection />);
    await screen.findByText("Obrigação de teste ativa");
    const create = screen.getByRole("button", { name: "Cadastrar obrigação" });
    expect(create).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Código"), { target: { value: " OB-NOVA " } });
    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: "Obrigação nova de teste" } });
    expect(create).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Referência normativa"), { target: { value: "REF-NORMA-3" } });
    expect(create).toBeEnabled();
    fireEvent.click(create);

    await waitFor(() => expect(api.defineObligation).toHaveBeenCalledWith({ code: "OB-NOVA", name: "Obrigação nova de teste", description: null, reference: "REF-NORMA-3" }));
    await waitFor(() => expect(screen.getByLabelText("Código")).toHaveValue(""));
    expect(toast.success).toHaveBeenCalledWith("Obrigação cadastrada.");
  });

  it("sends the description when given, toasts a refusal and explains a 403 on the list", async () => {
    vi.mocked(api.defineObligation).mockRejectedValue(new ApiError(409, "Código repetido no teste."));
    renderTax(<TaxObligationsSection />);
    await screen.findByText("Obrigação de teste ativa");
    fireEvent.change(screen.getByLabelText("Código"), { target: { value: "OB-X" } });
    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: "X" } });
    fireEvent.change(screen.getByLabelText("Referência normativa"), { target: { value: "REF" } });
    fireEvent.change(screen.getByLabelText("Descrição (opcional)"), { target: { value: "Descrição X" } });
    fireEvent.click(screen.getByRole("button", { name: "Cadastrar obrigação" }));
    await waitFor(() => expect(api.defineObligation).toHaveBeenCalledWith(expect.objectContaining({ description: "Descrição X" })));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    cleanup();

    vi.mocked(api.listPlatformObligations).mockRejectedValueOnce(forbidden());
    renderTax(<TaxObligationsSection />);
    expect(await screen.findByText(/Seu perfil não pode ver as obrigações/)).toBeInTheDocument();
  });
});

// ── Retention ───────────────────────────────────────────────────────────────

const policy: TaxRetentionPolicyDto = {
  id: "rp1", tenantId: null, version: 2, evidenceType: null, retentionMonths: 60, startsAt: "EndOfUploadYear", enabled: true, allowTenantOverride: true,
  rationale: "Motivo de teste", reference: "REF-RET-1", configuredByName: "Operador Teste", configuredAt: "2026-10-28T10:00:00Z", supersededAt: null,
};

describe("TaxRetentionSection", () => {
  beforeEach(() => vi.mocked(api.listPlatformRetention).mockResolvedValue([
    policy,
    { ...policy, id: "rp0", version: 1, retentionMonths: 12, supersededAt: "2026-10-28T10:00:00Z" },
    { ...policy, id: "rp2", version: 1, evidenceType: "FiscalDocument", startsAt: "UploadDate", allowTenantOverride: false, reference: "REF-RET-2" },
  ]));

  it("lists only the policies in force", async () => {
    renderTax(<TaxRetentionSection />);

    const items = (await screen.findAllByRole("listitem")).map((li) => li.textContent);
    expect(items).toEqual([
      "Todos os tipos: 60 meses, do fim do ano do envio · v2 · empresa pode ajustar (REF-RET-1 — Operador Teste)",
      "Documento fiscal autorizado: 60 meses, da data do envio · v1 · fixa (REF-RET-2 — Operador Teste)",
    ]);
  });

  it("registers a default only with whole months between 1 and 600, a reason and a reference", async () => {
    vi.mocked(api.configurePlatformRetention).mockResolvedValue(policy);
    renderTax(<TaxRetentionSection />);
    await screen.findAllByRole("listitem");
    const save = screen.getByRole("button", { name: "Registrar padrão" });
    expect(save).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "Prazo de teste" } });
    fireEvent.change(screen.getByLabelText("Referência"), { target: { value: "REF-RET-3" } });
    for (const bad of ["", "0", "601", "1.5", "doze"]) {
      fireEvent.change(screen.getByLabelText("Meses"), { target: { value: bad } });
      expect(save).toBeDisabled();
    }
    fireEvent.change(screen.getByLabelText("Meses"), { target: { value: "600" } });
    expect(save).toBeEnabled();
    fireEvent.change(screen.getByLabelText("Meses"), { target: { value: "72" } });
    await choose("Tipo", "Documento fiscal autorizado");
    await choose("Conta a partir", "Da data do envio");
    fireEvent.click(screen.getByRole("checkbox", { name: "Empresas podem ajustar este padrão" }));
    fireEvent.click(save);

    await waitFor(() => expect(api.configurePlatformRetention).toHaveBeenCalledWith({
      evidenceType: "FiscalDocument", retentionMonths: 72, startsAt: "UploadDate", enabled: true, allowTenantOverride: true,
      rationale: "Prazo de teste", reference: "REF-RET-3",
    }));
    await waitFor(() => expect(screen.getByLabelText("Meses")).toHaveValue(""));
    expect(toast.success).toHaveBeenCalledWith("Padrão de retenção registrado (versão nova).");
  });

  it("sends a default for all types, toasts a refusal and explains a 403 on the list", async () => {
    vi.mocked(api.configurePlatformRetention).mockRejectedValue(new ApiError(403, "Recusado no teste."));
    renderTax(<TaxRetentionSection />);
    await screen.findAllByRole("listitem");
    fireEvent.change(screen.getByLabelText("Meses"), { target: { value: "60" } });
    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "Motivo" } });
    fireEvent.change(screen.getByLabelText("Referência"), { target: { value: "REF" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar padrão" }));
    await waitFor(() => expect(api.configurePlatformRetention).toHaveBeenCalledWith(expect.objectContaining({
      evidenceType: null, startsAt: "EndOfUploadYear", allowTenantOverride: false,
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    cleanup();

    vi.mocked(api.listPlatformRetention).mockRejectedValueOnce(forbidden());
    renderTax(<TaxRetentionSection />);
    expect(await screen.findByText(/Seu perfil não pode ver as políticas/)).toBeInTheDocument();
  });
});

// ── Documents ───────────────────────────────────────────────────────────────

describe("TaxDocumentsSection", () => {
  beforeEach(() => vi.mocked(api.listDocuments).mockResolvedValue([
    storedFile,
    { ...storedFile, id: "f2", originalFileName: "revogado-teste.pdf", status: "Revoked", description: null, sha256: "sha-teste-2" },
  ]));

  it("lists the private documents with hash and status, downloads through the API and filters by purpose", async () => {
    renderTax(<TaxDocumentsSection />);

    expect(await screen.findByText("fonte-teste.pdf")).toBeInTheDocument();
    expect(api.listDocuments).toHaveBeenCalledWith("");
    expect(screen.getByText("SHA-256 sha-teste-1")).toBeInTheDocument();
    expect(screen.getByText(/^2 KB · Operador Teste, .* · Documento de teste$/)).toBeInTheDocument();
    expect(screen.getByText("Em vigor")).toBeInTheDocument();
    expect(screen.getByText("Revogado")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Revogar" })).toHaveLength(1);

    fireEvent.click(screen.getAllByRole("button", { name: "Baixar" })[0]);
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/platform/tax/documents/f1/content", "fonte-teste.pdf", "Falha ao baixar"));

    await choose("Finalidade", "Revisão profissional");
    await waitFor(() => expect(api.listDocuments).toHaveBeenLastCalledWith("professional-review"));
    await choose("Finalidade", "Todas");
    await waitFor(() => expect(api.listDocuments).toHaveBeenLastCalledWith(""));
  });

  it("revokes a document only with a reason", async () => {
    vi.mocked(api.revokeDocument).mockResolvedValue({ ...storedFile, status: "Revoked" });
    renderTax(<TaxDocumentsSection />);
    fireEvent.click(await screen.findByRole("button", { name: "Revogar" }));
    await confirmReason("Substituído no teste", "Revogar");

    await waitFor(() => expect(api.revokeDocument).toHaveBeenCalledWith("f1", "Substituído no teste"));
    expect(toast.success).toHaveBeenCalledWith("Documento revogado (continua registrado).");
  });

  it("says when there is no document, and explains a 403", async () => {
    vi.mocked(api.listDocuments).mockResolvedValueOnce([]);
    renderTax(<TaxDocumentsSection />);
    expect(await screen.findByText("Nenhum documento.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.listDocuments).mockRejectedValueOnce(forbidden());
    renderTax(<TaxDocumentsSection />);
    expect(await screen.findByText(/Seu perfil não pode ver os documentos/)).toBeInTheDocument();
  });
});

// ── Refusals and audit ──────────────────────────────────────────────────────

describe("TaxAuditSection", () => {
  it("lists refusals with their code and the audit trail, filtered by action and paged", async () => {
    vi.mocked(api.getRefusals).mockResolvedValue([
      { action: "tax_refused", code: "codigo_desconhecido_teste", description: "Recusa de teste", actorName: "Operador Teste", at: "2026-10-28T10:00:00Z", tenantId: "t1" },
      { action: "tax_refused", code: null, description: "Recusa sem código", actorName: "Operador Teste", at: "2026-10-28T11:00:00Z", tenantId: null },
    ]);
    vi.mocked(api.getTaxAudit).mockResolvedValue({
      items: [
        { id: "a1", action: "tax_module_activated", severity: "Warning", entityType: null, entityId: null, description: "Ativação de teste",
          actorName: "Operador Teste", at: "2026-10-28T10:00:00Z", tenantId: null },
        { id: "a2", action: "tax_rule_reviewed", severity: "Info", entityType: null, entityId: null, description: "Revisão de teste",
          actorName: "Operador Teste", at: "2026-10-28T10:00:00Z", tenantId: "t1" },
      ],
      total: 120, skip: 0, take: 50,
    });
    renderTax(<TaxAuditSection />);

    expect(await screen.findByText(/^Recusa de teste · Operador Teste · .* · empresa t1$/)).toBeInTheDocument();
    expect(api.getRefusals).toHaveBeenCalledWith(50);
    expect(screen.getByText("codigo_desconhecido_teste")).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
    expect(screen.getAllByText("Não foi possível concluir")).toHaveLength(2);

    expect(await screen.findByText(/· Warning · plataforma$/)).toBeInTheDocument();
    expect(screen.getByText(/· Info · empresa t1$/)).toBeInTheDocument();
    expect(api.getTaxAudit).toHaveBeenCalledWith("", 0, 50);

    fireEvent.click(screen.getByLabelText("Go to next page"));
    await waitFor(() => expect(api.getTaxAudit).toHaveBeenLastCalledWith("", 50, 50));
    fireEvent.change(screen.getByLabelText("Ação (código exato, opcional)"), { target: { value: "tax_module_activated" } });
    await waitFor(() => expect(api.getTaxAudit).toHaveBeenLastCalledWith("tax_module_activated", 0, 50));
  });

  it("says when there are no refusals or events", async () => {
    vi.mocked(api.getRefusals).mockResolvedValue([]);
    vi.mocked(api.getTaxAudit).mockResolvedValue({ items: [], total: 0, skip: 0, take: 50 });
    renderTax(<TaxAuditSection />);

    expect(await screen.findByText("Nenhuma recusa registrada.")).toBeInTheDocument();
    expect(await screen.findByText("Nenhum evento.")).toBeInTheDocument();
  });

  it("explains failures of both lists", async () => {
    vi.mocked(api.getRefusals).mockRejectedValueOnce(forbidden());
    vi.mocked(api.getTaxAudit).mockRejectedValue(new Error("rede"));
    renderTax(<TaxAuditSection />);

    expect(await screen.findByText(/Seu perfil não pode ver as recusas/)).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível carregar a auditoria")).toBeInTheDocument();
  });
});
