import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/services/api-client";
import { renderTax } from "../test/render";
import TaxCreditsPage, { allowedMovements } from "./TaxCreditsPage";
import * as api from "../api/tax.api";
import { downloadFile } from "@/services/pdf.api";
import type { TaxCreditDto } from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxCreditTotals: vi.fn(),
  listTaxCredits: vi.fn(),
  registerTaxCredit: vi.fn(),
  moveTaxCredit: vi.fn(),
  getSplitProvider: vi.fn(),
  listSplitExpectations: vi.fn(),
  getEvidenceStorage: vi.fn(),
  listEvidenceFiles: vi.fn(),
}));
const session = { role: "diretoria" as string };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: (m: string) => toastError(m) } }));

vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn().mockResolvedValue(undefined) }));

// jsdom has no ResizeObserver; Radix form controls measure themselves with one.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
// jsdom lacks what Radix selects use to scroll and capture the pointer.
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const KEY = "35261012345678000190550010000012341000012345";
const credit: TaxCreditDto = {
  id: "k1", tax: "Cbs", sourceDocumentType: "NF-e", sourceDocumentKey: KEY, supplierDocument: "12345678000190",
  acquiredOn: "2026-06-01", expiresOn: null, originalAmount: 100, potentialBasis: "insumo usado na atividade",
  stage: "ToBeAppropriated", lifecycle: "Appropriated", recognitionBasis: "nota idônea conferida", discardReason: null,
  balance: { potential: 90, toBeAppropriated: 30, appropriated: 60, used: 20, blocked: 10, available: 30 },
  entries: [
    { id: "x1", type: "Recognized", amount: 90, obligationReference: null, reason: "Crédito identificado", occurredOn: "2026-06-01", actorName: "Gerente", createdAt: "2026-06-01T12:00:00Z" },
    { id: "x2", type: "Used", amount: 20, obligationReference: "apuração 06/2026", reason: "Usado", occurredOn: "2026-07-01", actorName: "Gerente", createdAt: "2026-07-01T12:00:00Z" },
  ],
  createdAt: "2026-06-01T12:00:00Z",
} as TaxCreditDto;

const page = <T,>(items: T[]) => ({ items, total: items.length, skip: 0, take: 25 });

const show = (route = "/tributos/creditos") => renderTax(<TaxCreditsPage />, { path: "/tributos/creditos", route });

describe("TaxCreditsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getTaxCreditTotals).mockResolvedValue([{ tax: "Cbs", potential: 90, toBeAppropriated: 30, appropriated: 60, used: 20, blocked: 10, available: 30 }]);
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([credit]));
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: true, state: "ready", message: "Armazenamento privado disponível.", maxBytes: 20 * 1024 * 1024,
      acceptedExtensions: [".pdf"], scannerConfigured: false,
    });
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([]);
  });
  afterEach(cleanup);

  it("shows each credit's balance and its ledger", async () => {
    show();

    expect(await screen.findByText(KEY)).toBeInTheDocument();
    expect(screen.getByText("Estimativa: insumo usado na atividade")).toBeInTheDocument();
    expect(screen.getByText("Requisitos confirmados: nota idônea conferida")).toBeInTheDocument();
    expect(screen.getAllByText("R$ 30,00").length).toBeGreaterThan(0);
    expect(screen.getAllByText(/apuração 06\/2026/).length).toBeGreaterThan(0);
    expect(screen.getByText(/em 01\/07\/2026/)).toBeInTheDocument();
  });

  it("registers a potential credit only with a 44-digit key and an estimate within the tax stated", async () => {
    vi.mocked(api.registerTaxCredit).mockResolvedValue(credit);
    show();

    fireEvent.click(await screen.findByRole("button", { name: /Registrar crédito/ }));
    const dialog = screen.getByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Documento"), { target: { value: "NF-e" } });
    fireEvent.change(within(dialog).getByLabelText("Chave de acesso (opcional)"), { target: { value: "123" } });
    fireEvent.change(within(dialog).getByLabelText("Imposto destacado (R$)"), { target: { value: "100" } });
    expect(within(dialog).getByText(/Crédito potencial estimado\. Não representa validação de elegibilidade/)).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Crédito potencial estimado (R$)"), { target: { value: "120" } });
    fireEvent.change(within(dialog).getByLabelText("De onde vem a estimativa"), { target: { value: "insumo" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));

    expect(within(dialog).getByText("A chave de acesso tem 44 números.")).toBeInTheDocument();
    expect(within(dialog).getByText("O crédito potencial não passa do imposto destacado.")).toBeInTheDocument();
    expect(api.registerTaxCredit).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("Chave de acesso (opcional)"), { target: { value: KEY } });
    fireEvent.change(within(dialog).getByLabelText("Crédito potencial estimado (R$)"), { target: { value: "90" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(api.registerTaxCredit).toHaveBeenCalled());
    expect(vi.mocked(api.registerTaxCredit).mock.calls[0][0]).toMatchObject({ sourceDocumentKey: KEY, originalAmount: 100, potentialAmount: 90 });
  });

  it("explains a second use of the same credit in the same obligation", async () => {
    vi.mocked(api.moveTaxCredit).mockRejectedValue(new ApiError(409, "Este crédito já foi usado nesta obrigação.", "tax_credit_obligation_used"));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Movimentar" }));
    const dialog = screen.getByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText(/Valor/), { target: { value: "10" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(api.moveTaxCredit).toHaveBeenCalledWith("k1", expect.objectContaining({ amount: 10 })));
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("Este crédito já foi usado nessa obrigação."));
  });

  it("says plainly that no payment provider is integrated for the split", async () => {
    vi.mocked(api.getSplitProvider).mockResolvedValue({ configured: false, message: "Nenhum provedor integrado; registre à mão." });
    vi.mocked(api.listSplitExpectations).mockResolvedValue(page([]));
    show("/tributos/creditos?aba=split");

    expect(await screen.findByText("Nenhum provedor de pagamento integrado")).toBeInTheDocument();
    expect(screen.getByText("Nenhum provedor integrado; registre à mão.")).toBeInTheDocument();
    expect(await screen.findByText("Nenhuma retenção esperada")).toBeInTheDocument();
    expect(screen.getByText(/Expectativa gerencial de segregação\. Não comprova retenção ou recolhimento\./)).toBeInTheDocument();
  });

  it("says a potential credit is only an estimate", async () => {
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([{ ...credit, stage: "PotentialIdentified", lifecycle: "PotentialIdentified", recognitionBasis: null, entries: [] } as TaxCreditDto]));
    show();
    expect((await screen.findAllByText("Potencial estimado")).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/Não representa validação de elegibilidade, apropriação ou disponibilidade para utilização/).length).toBeGreaterThan(0);
  });

  it("says so when there is no credit yet", async () => {
    vi.mocked(api.listTaxCredits).mockResolvedValue(page<TaxCreditDto>([]));
    show();
    expect(await screen.findByText("Nenhum crédito registrado")).toBeInTheDocument();
  });
});

const withStage = (over: Partial<TaxCreditDto>): TaxCreditDto => ({ ...credit, ...over } as TaxCreditDto);
const balance = (over: Partial<TaxCreditDto["balance"]>): TaxCreditDto["balance"] =>
  ({ potential: 0, toBeAppropriated: 0, appropriated: 0, used: 0, blocked: 0, available: 0, ...over });

describe("allowedMovements", () => {
  const today = "2026-10-29";

  it("lets management review a potential credit, or discard it", () => {
    const potential = withStage({ stage: "PotentialIdentified" });
    expect(allowedMovements(potential, false, today)).toEqual(["StartRequirementsReview", "Discard"]);
    expect(allowedMovements(potential, true, today)).toEqual(["StartRequirementsReview", "Discard"]);
  });

  it("leaves confirming the requirements to the board", () => {
    const review = withStage({ stage: "RequirementsUnderReview" });
    expect(allowedMovements(review, true, today)).toEqual(["ConfirmToBeAppropriated", "Discard"]);
    expect(allowedMovements(review, false, today)).toEqual(["Discard"]);
  });

  it("offers each ledger movement only when the balance allows it, and only to the board", () => {
    const full = withStage({ stage: "ToBeAppropriated", expiresOn: "2026-10-01", balance: balance({ toBeAppropriated: 5, available: 10, blocked: 2 }) });
    expect(allowedMovements(full, true, today)).toEqual(["Appropriate", "Use", "Block", "Reverse", "Unblock", "Expire"]);
    expect(allowedMovements(full, false, today)).toEqual([]);

    expect(allowedMovements({ ...full, expiresOn: "2026-10-29" }, true, today)).not.toContain("Expire");
    expect(allowedMovements({ ...full, expiresOn: null }, true, today)).not.toContain("Expire");
    expect(allowedMovements({ ...full, balance: balance({ blocked: 2 }) }, true, today)).toEqual(["Unblock"]);
    expect(allowedMovements({ ...full, balance: balance({}) }, true, today)).toEqual([]);
  });

  it("offers nothing for a discarded credit", () => {
    expect(allowedMovements(withStage({ stage: "Discarded" }), true, today)).toEqual([]);
  });
});

describe("TaxCreditsPage — filters, movements, files and split", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    session.role = "diretoria";
    vi.mocked(api.getTaxCreditTotals).mockResolvedValue([{ tax: "Cbs", potential: 90, toBeAppropriated: 30, appropriated: 60, used: 20, blocked: 10, available: 30 }]);
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([credit]));
    vi.mocked(api.getEvidenceStorage).mockResolvedValue({
      available: true, state: "ready", message: "Armazenamento privado disponível.", maxBytes: 20 * 1024 * 1024,
      acceptedExtensions: [".pdf"], scannerConfigured: false,
    });
    vi.mocked(api.listEvidenceFiles).mockResolvedValue([]);
    vi.mocked(api.getSplitProvider).mockResolvedValue({ configured: true, message: "Provedor de teste conectado." });
  });
  afterEach(cleanup);

  const lastCredits = () => vi.mocked(api.listTaxCredits).mock.lastCall?.[0];
  async function pick(name: string, option: string) {
    fireEvent.click(screen.getByRole("combobox", { name }));
    fireEvent.click(await screen.findByRole("option", { name: option }));
  }

  it("filters the credits by tax and stage and pages through the server", async () => {
    vi.mocked(api.listTaxCredits).mockResolvedValue({ items: [credit], total: 45, skip: 0, take: 20 });
    show();

    await screen.findByText(KEY);
    expect(lastCredits()).toEqual({ tax: "", stage: "", skip: 0, take: 20 });
    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(lastCredits()).toEqual({ tax: "", stage: "", skip: 20, take: 20 }));

    await pick("Tributo", "IBS");
    await waitFor(() => expect(lastCredits()).toEqual({ tax: "Ibs", stage: "", skip: 0, take: 20 }));
    vi.mocked(api.listTaxCredits).mockResolvedValue(page<TaxCreditDto>([]));
    await pick("Etapa", "Requisitos em revisão");
    await waitFor(() => expect(lastCredits()).toEqual({ tax: "Ibs", stage: "RequirementsUnderReview", skip: 0, take: 20 }));
    expect(await screen.findByText("Nenhum crédito com estes filtros")).toBeInTheDocument();

    await pick("Tributo", "Todos");
    await pick("Etapa", "Todas");
    await waitFor(() => expect(lastCredits()).toEqual({ tax: "", stage: "", skip: 0, take: 20 }));
    expect(await screen.findByText("Nenhum crédito registrado")).toBeInTheDocument();
  });

  it("shows no pager when every credit fits in one page", async () => {
    show();
    await screen.findByText(KEY);
    expect(screen.queryByText(/registro\(s\)/)).not.toBeInTheDocument();
  });

  it("keeps the documents of each credit with the credit", async () => {
    show();

    expect(await screen.findByText("Documentos do crédito")).toBeInTheDocument();
    await waitFor(() => expect(api.listEvidenceFiles).toHaveBeenCalledWith("Credit", "k1"));
  });

  it("sends one idempotency key per dialog — a retry never writes the movement twice", async () => {
    vi.mocked(api.moveTaxCredit).mockRejectedValueOnce(new ApiError(503, "Indisponível.")).mockResolvedValue(credit);
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Movimentar" }));
    let dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("combobox", { name: "Movimento" })).toHaveTextContent("Apropriar");
    fireEvent.change(within(dialog).getByLabelText("Valor (R$)"), { target: { value: "10" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(api.moveTaxCredit).toHaveBeenCalledTimes(1));
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(api.moveTaxCredit).toHaveBeenCalledTimes(2));

    const [first, second] = vi.mocked(api.moveTaxCredit).mock.calls.map((c) => c[1]);
    expect(first).toMatchObject({ movement: "Appropriate", amount: 10 });
    expect(typeof first.idempotencyKey).toBe("string");
    expect(first.idempotencyKey).not.toBe("");
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    // A new dialog is a new request.
    fireEvent.click(screen.getByRole("button", { name: "Movimentar" }));
    dialog = screen.getByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Valor (R$)"), { target: { value: "5" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));
    await waitFor(() => expect(api.moveTaxCredit).toHaveBeenCalledTimes(3));
    expect(vi.mocked(api.moveTaxCredit).mock.calls[2][1].idempotencyKey).not.toBe(first.idempotencyKey);
  });

  it("uses a credit against an obligation, and writes off the whole balance on expiry", async () => {
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([withStage({ expiresOn: "2026-01-31" })]));
    vi.mocked(api.moveTaxCredit).mockResolvedValue(credit);
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Movimentar" }));
    const dialog = screen.getByRole("dialog");
    await pick("Movimento", "Usar contra imposto devido");
    fireEvent.change(within(dialog).getByLabelText("Valor (R$)"), { target: { value: "15" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));
    expect(within(dialog).getByText("Informe a obrigação (ex.: apuração 10/2026).")).toBeInTheDocument();
    await pick("Contra o imposto", "IBS");
    fireEvent.change(within(dialog).getByLabelText("Obrigação"), { target: { value: "Apuração 10/2026" } });
    fireEvent.change(within(dialog).getByLabelText("Data do uso (vazio = hoje)"), { target: { value: "2026-10-15" } });

    await pick("Movimento", "Dar como vencido");
    expect(within(dialog).getByText(/O vencimento dá baixa em todo o saldo disponível \(R\$ 30,00\)\. Não há vencimento parcial\./)).toBeInTheDocument();
    expect(within(dialog).queryByLabelText("Valor (R$)")).not.toBeInTheDocument();
    expect(within(dialog).getByText("Este movimento é registrado com a data de hoje.")).toBeInTheDocument();

    await pick("Movimento", "Bloquear");
    expect(within(dialog).getByLabelText("Motivo")).toBeInTheDocument();
    await pick("Movimento", "Usar contra imposto devido");
    expect(within(dialog).getByLabelText("Motivo (opcional)")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(api.moveTaxCredit).toHaveBeenCalledWith("k1", expect.objectContaining({
      movement: "Use", amount: 15, obligationTax: "Ibs", obligationReference: "Apuração 10/2026", onDate: "2026-10-15",
    })));
  });

  it("confirms the requirements of a credit under review, with the basis of the confirmation", async () => {
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([withStage({ stage: "RequirementsUnderReview", lifecycle: "RequirementsUnderReview", entries: [] })]));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Movimentar" }));
    const dialog = screen.getByRole("dialog");
    expect(within(dialog).getByRole("combobox", { name: "Movimento" })).toHaveTextContent("Confirmar requisitos (a apropriar)");
    expect(within(dialog).getByLabelText("Base da confirmação dos requisitos")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("tells a manager that ledger movements belong to the board, but lets them review a potential credit", async () => {
    session.role = "gerente";
    vi.mocked(api.listTaxCredits).mockResolvedValue(page([
      credit,
      withStage({ id: "k2", sourceDocumentKey: null, stage: "PotentialIdentified", lifecycle: "PotentialIdentified", recognitionBasis: null,
        discardReason: null, entries: [] }),
      withStage({ id: "k3", sourceDocumentKey: null, stage: "Discarded", lifecycle: "Discarded", discardReason: "nota inidônea", entries: [] }),
    ]));
    show();

    expect(await screen.findByText("Movimentos do razão: diretoria")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Movimentar" })).toHaveLength(1);
    expect(screen.getByText("Descartado: nota inidônea")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Movimentar" }));
    expect(within(screen.getByRole("dialog")).getByRole("combobox", { name: "Movimento" })).toHaveTextContent("Iniciar revisão dos requisitos");
  });

  it("says when there are no totals yet, and when credits or totals cannot be loaded", async () => {
    vi.mocked(api.getTaxCreditTotals).mockResolvedValue([]);
    show();
    expect(await screen.findByText("Nenhum crédito registrado ainda: os totais aparecem aqui por tributo.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.getTaxCreditTotals).mockRejectedValue(new ApiError(403, "proibido"));
    vi.mocked(api.listTaxCredits).mockRejectedValue(new Error("rede"));
    show();
    expect(await screen.findByText("Seu perfil não pode ver os totais de créditos. Peça acesso à diretoria.")).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível carregar os créditos")).toBeInTheDocument();
  });

  it("filters the split by status, tax and age, pages, and exports what is filtered", async () => {
    vi.mocked(api.listSplitExpectations).mockResolvedValue({ items: [{
      id: "x1", calculationId: "calc-1", tax: "Cbs", procedure: "Standard", expected: 7.56, actual: 0, difference: -7.56, status: "Pending",
      settlements: [], createdAt: "2026-06-15T12:00:00Z", operationDate: "2026-06-15", ageDays: 12,
    }], total: 30, skip: 0, take: 20 });
    show("/tributos/creditos?aba=split");

    expect(await screen.findByText("Provedor de pagamento conectado")).toBeInTheDocument();
    expect(await screen.findByText("12 dia(s) desde a operação")).toBeInTheDocument();
    const last = () => vi.mocked(api.listSplitExpectations).mock.lastCall?.[0];
    expect(last()).toEqual({ status: "", tax: "", minAgeDays: "", skip: 0, take: 20 });

    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(last()).toMatchObject({ skip: 20 }));
    await pick("Situação", "Aguardando retenção");
    await waitFor(() => expect(last()).toEqual({ status: "Pending", tax: "", minAgeDays: "", skip: 0, take: 20 }));
    await pick("Tributo", "IBS estadual");
    await waitFor(() => expect(last()).toMatchObject({ tax: "IbsState" }));
    const age = screen.getByLabelText("Em aberto há pelo menos (dias)");
    fireEvent.change(age, { target: { value: "7" } });
    await waitFor(() => expect(last()).toMatchObject({ minAgeDays: 7 }));
    fireEvent.change(age, { target: { value: "-3" } });
    await waitFor(() => expect(last()).toMatchObject({ minAgeDays: 0 }));
    fireEvent.change(age, { target: { value: "7" } });

    fireEvent.click(screen.getByRole("button", { name: "Exportar CSV" }));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith(
      api.splitCsvUrl({ status: "Pending", tax: "IbsState", minAgeDays: 7 }), "split.csv", "Falha ao baixar",
    ));
    expect(vi.mocked(downloadFile).mock.calls[0][0]).toMatch(/status=Pending/);
    expect(vi.mocked(downloadFile).mock.calls[0][0]).toMatch(/minAgeDays=7/);
    expect(vi.mocked(downloadFile).mock.calls[0][0]).not.toMatch(/skip|take/);

    vi.mocked(api.listSplitExpectations).mockResolvedValue({ items: [], total: 0, skip: 0, take: 20 });
    fireEvent.change(age, { target: { value: "abc" } });
    await waitFor(() => expect(last()).toMatchObject({ minAgeDays: "" }));
    expect(await screen.findByText("Nada com estes filtros")).toBeInTheDocument();
    await pick("Situação", "Todas");
    await pick("Tributo", "Todos");
    expect(await screen.findByText("Nenhuma retenção esperada")).toBeInTheDocument();
  });

  it("says when the expected retentions cannot be loaded, and switches back to the credits tab", async () => {
    vi.mocked(api.listSplitExpectations).mockRejectedValue(new Error("rede"));
    show("/tributos/creditos?aba=split");

    expect(await screen.findByText("Não foi possível carregar as retenções")).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Créditos" }));
    expect(await screen.findByText(KEY)).toBeInTheDocument();
    fireEvent.mouseDown(screen.getByRole("tab", { name: "Split payment" }));
    expect(await screen.findByText("Provedor de pagamento conectado")).toBeInTheDocument();
  });
});
