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
}));
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: (m: string) => toastError(m) } }));

const SALE = "0f8fad5b-d9cb-469f-a165-70867728950e";

// jsdom has no ResizeObserver; Radix's checkbox measures itself with one when it sits inside a <form>.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

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

describe("TaxCalculationsPage", () => {
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
});
