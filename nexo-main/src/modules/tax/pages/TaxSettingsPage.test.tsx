import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { renderTax } from "../test/render";
import TaxSettingsPage from "./TaxSettingsPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";
import { brazilTodayIso } from "../lib/forms";

const session = { role: "diretoria" as string, userId: "u-dir" };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getMateriality: vi.fn(),
  configureMateriality: vi.fn(),
  listRetentionPolicies: vi.fn(),
  configureRetention: vi.fn(),
  getRetentionPreview: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const v2: api.TaxMaterialityPolicyDto = {
  id: "m2", version: 2, threshold: 10000, currency: "BRL", effectiveFrom: "2026-07-01", effectiveTo: null, rationale: "Crescimento do porte",
  decisionReference: "ATA-12", configuredByName: "Diretora Exemplo", configuredAt: "2026-07-01T12:00:00Z",
};
const v1: api.TaxMaterialityPolicyDto = { ...v2, id: "m1", version: 1, threshold: 5000, effectiveFrom: "2026-01-01", effectiveTo: "2026-06-30", rationale: "Início", decisionReference: "ATA-3" };

const policy = (over: Partial<api.TaxRetentionPolicyDto>): api.TaxRetentionPolicyDto => ({
  id: "p1", tenantId: null, version: 1, evidenceType: null, retentionMonths: 120, startsAt: "EndOfUploadYear", enabled: true, allowTenantOverride: true,
  rationale: "Prazo decadencial", reference: "POL-PLAT-1", configuredByName: "Operador", configuredAt: "2026-01-01T12:00:00Z", supersededAt: null, ...over,
});
const platformAll = policy({});
const platformFiscalFixed = policy({ id: "p2", evidenceType: "FiscalDocument", retentionMonths: 60, allowTenantOverride: false, reference: "POL-PLAT-2" });
const tenantCommercial = policy({ id: "p3", tenantId: "t1", version: 2, evidenceType: "CommercialInstrument", retentionMonths: 84, enabled: false,
  startsAt: "UploadDate", reference: "NORMA-INT-4", configuredByName: "Diretora Exemplo" });
const superseded = policy({ id: "p0", tenantId: "t1", evidenceType: "PaymentRecord", reference: "ANTIGA", supersededAt: "2026-05-01T00:00:00Z" });

const preview: api.TaxRetentionPreviewDto = {
  asOf: "2026-10-29", automaticDisposal: false, filesConsidered: 12, onLegalHold: 2, withoutPolicy: 1,
  eligible: [{ fileId: "f9", originalFileName: "pedido-2014.pdf", evidenceType: "CommercialInstrument", uploadedAt: "2014-03-02T10:00:00Z",
    eligibleFrom: "2025-01-01", policyVersion: 1, sha256: "aa" }],
  notice: "Prévia apenas: nenhum arquivo é descartado automaticamente.",
};

const byId = (id: string) => document.getElementById(id)!;

describe("TaxSettingsPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    session.role = "diretoria";
    vi.mocked(api.getMateriality).mockResolvedValue({ inForce: v2, installationDefault: 2500, history: [v2, v1] });
    vi.mocked(api.listRetentionPolicies).mockResolvedValue([platformAll, platformFiscalFixed, tenantCommercial, superseded]);
    vi.mocked(api.getRetentionPreview).mockResolvedValue(preview);
  });
  afterEach(cleanup);

  it("shows the materiality in force and every version kept as history", async () => {
    renderTax(<TaxSettingsPage />);

    const inForce = await screen.findByText(/Em vigor:/);
    expect(inForce).toHaveTextContent("Em vigor: R$ 10.000,00 — versão 2, desde 01/07/2026 (ATA-12).");
    expect(screen.getByText("Versão 2: R$ 10.000,00 · 01/07/2026 em diante")).toBeInTheDocument();
    expect(screen.getByText("Versão 1: R$ 5.000,00 · 01/01/2026 a 30/06/2026")).toBeInTheDocument();
    expect(screen.getByText("Início · ATA-3")).toBeInTheDocument();
    expect(screen.getAllByText(/Diretora Exemplo em/).length).toBeGreaterThan(0);
  });

  it("falls back to the installation default, or says there is none", async () => {
    vi.mocked(api.getMateriality).mockResolvedValueOnce({ inForce: null, installationDefault: 2500, history: [] });
    renderTax(<TaxSettingsPage />);
    expect(await screen.findByText(/Vale o padrão da instalação:/)).toHaveTextContent("Sem versão própria da empresa. Vale o padrão da instalação: R$ 2.500,00.");
    cleanup();

    vi.mocked(api.getMateriality).mockResolvedValueOnce({ inForce: null, installationDefault: null, history: [] });
    renderTax(<TaxSettingsPage />);
    expect(await screen.findByText(/A instalação também não define um padrão\./)).toBeInTheDocument();
  });

  it("says why the materiality cannot be loaded", async () => {
    vi.mocked(api.getMateriality).mockRejectedValueOnce(new ApiError(403, "proibido"));
    renderTax(<TaxSettingsPage />);
    expect(await screen.findByText("Seu perfil não pode ver a materialidade. Peça acesso à diretoria.")).toBeInTheDocument();
  });

  it("validates a new materiality version and records it with its reason and reference", async () => {
    vi.mocked(api.configureMateriality).mockResolvedValue({ inForce: v2, installationDefault: null, history: [v2] });
    renderTax(<TaxSettingsPage />);

    await screen.findByLabelText("Valor (R$)");
    expect(byId("mat-from")).toHaveValue(brazilTodayIso());
    fireEvent.change(byId("mat-value"), { target: { value: "abc" } });
    fireEvent.change(byId("mat-from"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));
    expect(screen.getByText("Informe um valor maior que zero.")).toBeInTheDocument();
    expect(screen.getByText("Informe a data de início.")).toBeInTheDocument();
    expect(screen.getByText("Diga o motivo.")).toBeInTheDocument();
    expect(screen.getByText("Informe a referência da decisão (ata, e-mail, chamado…).")).toBeInTheDocument();
    expect(api.configureMateriality).not.toHaveBeenCalled();

    fireEvent.change(byId("mat-value"), { target: { value: "0" } });
    expect(screen.getByText("Informe um valor maior que zero.")).toBeInTheDocument();

    fireEvent.change(byId("mat-value"), { target: { value: "12.500,50" } });
    fireEvent.change(byId("mat-from"), { target: { value: "2026-11-01" } });
    fireEvent.change(byId("mat-ref"), { target: { value: " ATA-15 " } });
    fireEvent.change(byId("mat-why"), { target: { value: " Novo porte " } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));

    await waitFor(() => expect(api.configureMateriality).toHaveBeenCalledWith({
      threshold: 12500.5, currency: "BRL", effectiveFrom: "2026-11-01", effectiveTo: null, rationale: "Novo porte", decisionReference: "ATA-15",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Nova versão da materialidade registrada."));
    expect(byId("mat-value")).toHaveValue("");
    expect(api.getMateriality).toHaveBeenCalledTimes(2);
  });

  it("explains a refused materiality version", async () => {
    vi.mocked(api.configureMateriality).mockRejectedValue(new ApiError(422, "Já existe versão nesta data."));
    renderTax(<TaxSettingsPage />);

    await screen.findByLabelText("Valor (R$)");
    fireEvent.change(byId("mat-value"), { target: { value: "100" } });
    fireEvent.change(byId("mat-ref"), { target: { value: "ATA" } });
    fireEvent.change(byId("mat-why"), { target: { value: "x" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar nova versão" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Já existe versão nesta data." }));
  });

  it("lists only the retention policies in force, saying who set them and whether the company may adjust", async () => {
    renderTax(<TaxSettingsPage />);

    const table = await screen.findByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent("Plataforma · v1");
    expect(rows[0]).toHaveTextContent("Todos");
    expect(rows[0]).toHaveTextContent("120");
    expect(rows[0]).toHaveTextContent("Do fim do ano do envio");
    expect(rows[0]).toHaveTextContent("Ativa · empresa pode ajustar");
    expect(rows[1]).toHaveTextContent("Documento fiscal autorizado");
    expect(rows[1]).toHaveTextContent("Ativa · fixa");
    expect(rows[2]).toHaveTextContent("Empresa · v2");
    expect(rows[2]).toHaveTextContent("Da data do envio");
    expect(rows[2]).toHaveTextContent("Desligada");
    expect(rows[2]).not.toHaveTextContent("fixa");
    expect(rows[2]).toHaveTextContent("NORMA-INT-4 — Diretora Exemplo");
    expect(screen.queryByText(/ANTIGA/)).not.toBeInTheDocument();
    expect(screen.getByText(/Nenhum arquivo é apagado automaticamente\./)).toBeInTheDocument();
  });

  it("says no file is eligible for disposal when no policy is in force", async () => {
    vi.mocked(api.listRetentionPolicies).mockResolvedValueOnce([superseded]);
    renderTax(<TaxSettingsPage />);
    expect(await screen.findByText("Nenhuma política em vigor: nenhum arquivo é elegível para descarte.")).toBeInTheDocument();
  });

  it("says why the retention policies cannot be loaded", async () => {
    vi.mocked(api.listRetentionPolicies).mockRejectedValueOnce(new Error("rede"));
    renderTax(<TaxSettingsPage />);
    expect(await screen.findByText("Não foi possível carregar as políticas de retenção")).toBeInTheDocument();
  });

  it("refuses an adjustment where the platform default is fixed, and records one where it allows", async () => {
    vi.mocked(api.configureRetention).mockResolvedValue(tenantCommercial);
    renderTax(<TaxSettingsPage />);

    await screen.findByText("Ajuste da empresa");
    expect(screen.getByText("Padrão da plataforma: 120 meses.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("combobox", { name: "Tipo de evidência" }));
    fireEvent.click(await screen.findByRole("option", { name: "Documento fiscal autorizado" }));
    expect(screen.getByText("Padrão da plataforma: 60 meses.")).toBeInTheDocument();
    fireEvent.change(byId("ret-months"), { target: { value: "72" } });
    fireEvent.change(byId("ret-ref"), { target: { value: "NORMA-5" } });
    fireEvent.change(byId("ret-why"), { target: { value: "Prazo interno" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar ajuste" }));
    expect(screen.getByText("O padrão da plataforma para este tipo não permite ajuste da empresa.")).toBeInTheDocument();
    expect(api.configureRetention).not.toHaveBeenCalled();

    // Payment records have no platform default of their own: the "all types" default (adjustable) applies.
    fireEvent.click(screen.getByRole("combobox", { name: "Tipo de evidência" }));
    fireEvent.click(await screen.findByRole("option", { name: "Comprovante de pagamento" }));
    fireEvent.click(screen.getByRole("combobox", { name: "Conta a partir" }));
    fireEvent.click(await screen.findByRole("option", { name: "Da data do envio" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Política ativa" }));
    fireEvent.click(screen.getByRole("button", { name: "Registrar ajuste" }));

    await waitFor(() => expect(api.configureRetention).toHaveBeenCalledWith({
      evidenceType: "PaymentRecord", retentionMonths: 72, startsAt: "UploadDate", enabled: false, allowTenantOverride: false,
      rationale: "Prazo interno", reference: "NORMA-5",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Política de retenção registrada (versão nova)."));
    expect(byId("ret-months")).toHaveValue("");
  });

  it("blocks an adjustment without a platform default, and checks the months and the reasons", async () => {
    vi.mocked(api.listRetentionPolicies).mockResolvedValueOnce([tenantCommercial]);
    renderTax(<TaxSettingsPage />);

    await screen.findByText("Ajuste da empresa");
    fireEvent.change(byId("ret-months"), { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar ajuste" }));
    expect(screen.getByText("Não há padrão da plataforma para este tipo: a empresa não pode criar uma política sozinha.")).toBeInTheDocument();
    expect(screen.getByText("Meses: um número inteiro de 1 a 600.")).toBeInTheDocument();
    expect(screen.getAllByText("Diga o motivo.").length).toBeGreaterThan(0);
    expect(screen.getByText("Informe a referência (norma interna, parecer, ata…).")).toBeInTheDocument();
    fireEvent.change(byId("ret-months"), { target: { value: "1.5" } });
    expect(screen.getByText("Meses: um número inteiro de 1 a 600.")).toBeInTheDocument();
    expect(api.configureRetention).not.toHaveBeenCalled();
  });

  it("explains a refused retention adjustment", async () => {
    vi.mocked(api.configureRetention).mockRejectedValue(new ApiError(422, "Padrão fixo."));
    renderTax(<TaxSettingsPage />);

    await screen.findByText("Ajuste da empresa");
    fireEvent.change(byId("ret-months"), { target: { value: "36" } });
    fireEvent.change(byId("ret-ref"), { target: { value: "N" } });
    fireEvent.change(byId("ret-why"), { target: { value: "M" } });
    fireEvent.click(screen.getByRole("button", { name: "Registrar ajuste" }));
    await waitFor(() => expect(api.configureRetention).toHaveBeenCalledWith(expect.objectContaining({ evidenceType: null, retentionMonths: 36, enabled: true })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Padrão fixo." }));
  });

  it("previews the eligible files only on request and states nothing is deleted automatically", async () => {
    renderTax(<TaxSettingsPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Ver prévia do que seria elegível" }));
    expect(await screen.findByText("Prévia apenas: nenhum arquivo é descartado automaticamente.")).toBeInTheDocument();
    expect(api.getRetentionPreview).toHaveBeenCalledTimes(1);
    expect(screen.getByText("Em 29/10/2026: 12 arquivo(s) considerados · 2 em retenção legal · 1 sem política · 1 elegível(is).")).toBeInTheDocument();
    const item = screen.getByText("pedido-2014.pdf").closest("li")!;
    expect(item).toHaveTextContent("Instrumento comercial");
    expect(item).toHaveTextContent("elegível desde 01/01/2025 (política v1)");
    expect(screen.queryByRole("button", { name: /apagar|descartar|excluir/i })).not.toBeInTheDocument();
  });

  it("shows a preview without eligible files, and says when the preview cannot be loaded", async () => {
    vi.mocked(api.getRetentionPreview).mockResolvedValueOnce({ ...preview, eligible: [] });
    renderTax(<TaxSettingsPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Ver prévia do que seria elegível" }));
    expect(await screen.findByText(/0 elegível\(is\)\./)).toBeInTheDocument();
    expect(screen.queryByText("pedido-2014.pdf")).not.toBeInTheDocument();
    cleanup();

    vi.mocked(api.getRetentionPreview).mockRejectedValueOnce(new ApiError(404, "não"));
    renderTax(<TaxSettingsPage />);
    fireEvent.click(await screen.findByRole("button", { name: "Ver prévia do que seria elegível" }));
    expect(await screen.findByText("A prévia não encontrado(a)")).toBeInTheDocument();
  });

  it("shows a manager the policies and notes, never the forms or the preview", async () => {
    session.role = "gerente";
    renderTax(<TaxSettingsPage />);

    expect(await screen.findByText("Só a diretoria registra uma nova versão da materialidade.")).toBeInTheDocument();
    expect(await screen.findByText("Só a diretoria ajusta a retenção e vê a prévia.")).toBeInTheDocument();
    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar nova versão" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar ajuste" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Ver prévia do que seria elegível" })).not.toBeInTheDocument();
  });
});
