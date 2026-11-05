import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { ApiError } from "@/services/api-client";
import TaxCalculationsPage from "./TaxCalculationsPage";
import * as api from "../api/tax.api";

vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  listTaxCalculations: vi.fn(),
  createTaxCalculation: vi.fn(),
  calculateSale: vi.fn(),
  previewTaxCoverage: vi.fn(),
}));
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: (m: string) => toastError(m) } }));

const SALE = "0f8fad5b-d9cb-469f-a165-70867728950e";

// jsdom has no ResizeObserver; Radix's checkbox measures itself with one when it sits inside a <form>.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
// jsdom lacks what Radix selects use to scroll and capture the pointer.
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

/** Fills one valid line, so the operation can be checked or calculated. */
function fillLine() {
  fireEvent.change(screen.getByLabelText("Item"), { target: { value: "Camiseta" } });
  fireEvent.change(screen.getByLabelText("Valor da linha (R$)"), { target: { value: "100,00" } });
}

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/tributos/calculos"]}>
        <Routes>
          <Route path="/tributos/calculos" element={<TaxCalculationsPage />} />
          <Route path="/tributos/calculos/:id" element={<p>detalhe do cálculo</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("TaxCalculationsPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listTaxCalculations).mockResolvedValue({ items: [], total: 0, skip: 0, take: 25 });
  });
  afterEach(cleanup);

  it("sends the operation as the engine reads it — unknown taxes in the price go as null — and opens the result", async () => {
    vi.mocked(api.createTaxCalculation).mockResolvedValue({ id: "calc-9", reused: false } as api.TaxCalculationDto);
    show();

    fireEvent.change(screen.getByLabelText("Item"), { target: { value: "Camiseta" } });
    fireEvent.change(screen.getByLabelText("Valor da linha (R$)"), { target: { value: "1.000,00" } });
    fireEvent.change(screen.getByLabelText("Desconto incondicional (R$)"), { target: { value: "100" } });
    fireEvent.change(screen.getByLabelText("Desconto condicional (R$)"), { target: { value: "50" } });
    fireEvent.click(screen.getByRole("button", { name: "Calcular" }));

    await waitFor(() => expect(api.createTaxCalculation).toHaveBeenCalled());
    const body = vi.mocked(api.createTaxCalculation).mock.calls[0][0];
    expect(body.direction).toBe("Outbound");
    expect(body.lines[0]).toMatchObject({ description: "Camiseta", grossAmount: 1000, unconditionalDiscount: 100, conditionalDiscount: 50, excludedTaxes: null });
    expect(body.timing).toEqual({ supplyPattern: "SingleSupply" });
    expect(await screen.findByText("detalhe do cálculo")).toBeInTheDocument();
  });

  it("refuses to send an operation the server would refuse, saying why", async () => {
    show();

    fireEvent.click(screen.getByRole("button", { name: "Calcular" }));

    expect(await screen.findByText(/Linha 1: descreva o item\./)).toBeInTheDocument();
    expect(api.createTaxCalculation).not.toHaveBeenCalled();
  });

  it("points to the tax profile when there is none for the date", async () => {
    vi.mocked(api.createTaxCalculation).mockRejectedValue(new ApiError(422, "Não há perfil tributário vigente.", "tax_profile_missing"));
    show();

    fireEvent.change(screen.getByLabelText("Item"), { target: { value: "x" } });
    fireEvent.change(screen.getByLabelText("Valor da linha (R$)"), { target: { value: "10" } });
    fireEvent.click(screen.getByRole("button", { name: "Calcular" }));

    const link = await screen.findByRole("link", { name: /perfil/i });
    expect(link.getAttribute("href")).toContain("/tributos/perfil");
    expect(toastError).not.toHaveBeenCalled();
  });

  it("shows why an operation outside the engine's recorte was not calculated — never a zero", async () => {
    vi.mocked(api.calculateSale).mockRejectedValue(new ApiError(422, "Operação fora do recorte suportado.", "tax_operation_not_supported", undefined, [
      "Linha 1: Classificação insuficiente para o recorte: falta cClassTrib.", "Linha 2: Classificação insuficiente para o recorte: falta cClassTrib.",
    ]));
    show();

    fireEvent.change(screen.getByLabelText("Código da venda"), { target: { value: SALE } });
    fireEvent.click(screen.getByRole("button", { name: "Calcular venda" }));

    expect(await screen.findByText("Operação não suportada pela versão atual da engine.")).toBeInTheDocument();
    expect(screen.getByText(/o sistema não devolve zero para o que não modela/)).toBeInTheDocument();
    expect(screen.getAllByText(/falta cClassTrib/)).toHaveLength(2);
    expect(toastError).not.toHaveBeenCalled();
  });

  it("calculates a recorded sale only from a valid sale code", async () => {
    vi.mocked(api.calculateSale).mockResolvedValue({ id: "calc-sale", reused: true } as api.TaxCalculationDto);
    show();

    fireEvent.change(screen.getByLabelText("Código da venda"), { target: { value: "123" } });
    fireEvent.click(screen.getByRole("button", { name: "Calcular venda" }));
    expect(toastError).toHaveBeenCalled();
    expect(api.calculateSale).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Código da venda"), { target: { value: ` ${SALE} ` } });
    fireEvent.click(screen.getByRole("button", { name: "Calcular venda" }));
    await waitFor(() => expect(api.calculateSale).toHaveBeenCalledWith(SALE));
  });

  it("lists stored calculations with their source and open opportunities", async () => {
    vi.mocked(api.listTaxCalculations).mockResolvedValue({ items: [
      { id: "c1", referenceDate: "2026-06-15", source: "Sale", sourceId: SALE, direction: "Outbound", description: "Venda #12",
        totalBase: 840, totalTax: 8.4, openOpportunities: 1, createdAt: "2026-06-15T12:00:00Z" },
      { id: "c2", referenceDate: "2026-06-16", source: "Manual", sourceId: null, direction: "Inbound", description: null,
        totalBase: 100, totalTax: 1, openOpportunities: 0, createdAt: "2026-06-16T12:00:00Z" },
    ], total: 2, skip: 0, take: 25 });
    show();

    expect(await screen.findByRole("link", { name: "Venda #12" })).toHaveAttribute("href", "/tributos/calculos/c1");
    expect(screen.getByText("R$ 8,40")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Compra / entrada" })).toHaveAttribute("href", "/tributos/calculos/c2");
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("says so when nothing was calculated yet", async () => {
    show();
    expect(await screen.findByText("Nenhum cálculo ainda")).toBeInTheDocument();
  });

  it("filters the stored calculations by direction and pages through the server", async () => {
    vi.mocked(api.listTaxCalculations).mockResolvedValue({ items: [
      { id: "c1", referenceDate: "2026-06-15", source: "Manual", sourceId: null, direction: "Outbound", description: null,
        totalBase: 840, totalTax: 8.4, openOpportunities: 0, createdAt: "2026-06-15T12:00:00Z" },
    ], total: 60, skip: 0, take: 25 });
    show();

    await screen.findByRole("link", { name: "Venda / saída" });
    expect(api.listTaxCalculations).toHaveBeenLastCalledWith({ direction: "", skip: 0, take: 25 });
    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(api.listTaxCalculations).toHaveBeenLastCalledWith({ direction: "", skip: 25, take: 25 }));

    vi.mocked(api.listTaxCalculations).mockResolvedValue({ items: [], total: 0, skip: 0, take: 25 });
    fireEvent.click(screen.getByRole("combobox", { name: "Direção" }));
    fireEvent.click(await screen.findByRole("option", { name: "Compra / entrada" }));
    await waitFor(() => expect(api.listTaxCalculations).toHaveBeenLastCalledWith({ direction: "Inbound", skip: 0, take: 25 }));
    expect(await screen.findByText("Nenhum cálculo nesta direção")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("combobox", { name: "Direção" }));
    fireEvent.click(await screen.findByRole("option", { name: "Todas" }));
    await waitFor(() => expect(api.listTaxCalculations).toHaveBeenLastCalledWith({ direction: "", skip: 0, take: 25 }));
  });

  it("says why the stored calculations cannot be loaded", async () => {
    vi.mocked(api.listTaxCalculations).mockRejectedValue(new ApiError(403, "proibido"));
    show();
    expect(await screen.findByText("Seu perfil não pode ver os cálculos. Peça acesso à diretoria.")).toBeInTheDocument();
  });

  it("checks whether the operation is inside the recorte without calculating it", async () => {
    vi.mocked(api.previewTaxCoverage).mockResolvedValue({
      status: "Supported", policyVersion: "cobertura/1", findings: [], assumptions: [], supported: true,
    });
    show();

    const check = screen.getByRole("button", { name: "Conferir se está no recorte" });
    expect(check).toBeDisabled();
    fillLine();
    fireEvent.click(check);

    await waitFor(() => expect(api.previewTaxCoverage).toHaveBeenCalledWith(expect.objectContaining({
      direction: "Outbound", lines: [expect.objectContaining({ description: "Camiseta", grossAmount: 100 })],
    })));
    expect(await screen.findByText("Prévia do recorte: Suportada pela V1 (cobertura/1)")).toBeInTheDocument();
    expect(screen.getByText(/Dentro do recorte suportado\. Nada foi calculado nem registrado\./)).toBeInTheDocument();
    expect(api.createTaxCalculation).not.toHaveBeenCalled();
  });

  it("lists every finding of a preview outside the recorte, by line", async () => {
    vi.mocked(api.previewTaxCoverage).mockResolvedValue({
      status: "RequiresClassification", policyVersion: "cobertura/1", supported: false, assumptions: [],
      findings: [
        { status: "RequiresClassification", code: "missing-cclasstrib", message: "Falta cClassTrib.", lineIndex: 1 },
        { status: "Unsupported", code: "timing", message: "Fornecimento continuado não é modelado.", lineIndex: null },
      ],
    });
    show();

    fillLine();
    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));

    expect(await screen.findByText("Prévia do recorte: Falta classificação (cobertura/1)")).toBeInTheDocument();
    expect(screen.getByText(/ativo no catálogo\) — classifique o item e calcule de novo\. Nada foi calculado/)).toBeInTheDocument();
    expect(screen.getByText("Linha 1: Falta cClassTrib.")).toBeInTheDocument();
    expect(screen.getByText("Fornecimento continuado não é modelado.")).toBeInTheDocument();
  });

  it("names the coverage status and its advice when the server refuses an operation outside the recorte", async () => {
    vi.mocked(api.previewTaxCoverage)
      .mockResolvedValueOnce({ status: "Supported", policyVersion: "cobertura/1", findings: [], assumptions: [], supported: true })
      .mockRejectedValueOnce(new ApiError(422, "Operação fora do recorte (PendingRegulation).", "tax_operation_not_supported"));
    show();

    fillLine();
    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));
    await screen.findByText(/Prévia do recorte:/);
    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));

    expect(await screen.findByText("Operação não suportada pela versão atual da engine. — Aguardando regulamentação")).toBeInTheDocument();
    expect(screen.getByText("Depende de regulamentação ainda não refletida no catálogo.")).toBeInTheDocument();
    // Without itemised details, the server's own message is the reason.
    expect(screen.getByText("Operação fora do recorte (PendingRegulation).")).toBeInTheDocument();
  });

  it("clears the refusal once a check succeeds, and toasts any other failure", async () => {
    vi.mocked(api.previewTaxCoverage)
      .mockRejectedValueOnce(new ApiError(422, "Fora.", "tax_operation_not_supported", undefined, ["Linha 1: falta NCM."]))
      .mockResolvedValueOnce({ status: "Supported", policyVersion: "cobertura/1", findings: [], assumptions: [], supported: true })
      .mockRejectedValueOnce(new ApiError(500, "Erro interno."));
    show();

    fillLine();
    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));
    expect(await screen.findByText("Operação não suportada pela versão atual da engine.")).toBeInTheDocument();
    expect(screen.getByText("Linha 1: falta NCM.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));
    await screen.findByText(/Prévia do recorte:/);
    expect(screen.queryByText("Operação não suportada pela versão atual da engine.")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Conferir se está no recorte" }));
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("Não foi possível concluir"));
  });
});
