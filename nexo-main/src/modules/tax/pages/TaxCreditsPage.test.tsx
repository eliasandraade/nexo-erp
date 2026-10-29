import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/services/api-client";
import { renderTax } from "../test/render";
import TaxCreditsPage from "./TaxCreditsPage";
import * as api from "../api/tax.api";
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

// jsdom has no ResizeObserver; Radix form controls measure themselves with one.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

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
