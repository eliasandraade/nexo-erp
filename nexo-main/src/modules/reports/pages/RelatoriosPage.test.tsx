import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import RelatoriosPage from "./RelatoriosPage";
import {
  exportSales, getCustomerReport, getInventoryReport, getSalesReport,
  type SalesReportDto,
} from "../api/reports.api";
import { formatCurrency } from "@/lib/formatters";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));
vi.mock("@/modules/auth/context/AuthContext", () => ({
  useAuth: () => ({ session: { modules: ["restaurante"], readOnlyModules: [] } }),
}));
vi.mock("../api/reports.api", () => ({
  REPORTS_KEY: ["reports"],
  getSalesReport: vi.fn(),
  getInventoryReport: vi.fn(),
  getCustomerReport: vi.fn(),
  exportSales: vi.fn(),
}));
// recharts needs layout measurements jsdom does not have: the chart only lists what it was given
vi.mock("../components/SalesByDayChart", () => ({
  default: ({ days }: { days: { date: string }[] }) => <div data-testid="sales-chart">{days.length} dias no gráfico</div>,
}));

const sales = (over: Partial<SalesReportDto> = {}): SalesReportDto => ({
  totalSales: 12,
  cancelledSales: 0,
  totalRevenue: 3400,
  averageTicket: 283.33,
  cancelledValue: 0,
  from: "2026-11-01",
  to: "2026-11-13",
  grossRevenue: 3400,
  returnedValue: 0,
  returnedSales: 0,
  discountValue: 120,
  days: [],
  topProducts: [],
  sellers: [],
  payments: [],
  ...over,
});

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}><MemoryRouter>{children}</MemoryRouter></QueryClientProvider>;
}

/** The metric card whose label is `label` (label and value live in the same card). */
const metric = (label: string) =>
  screen.getAllByText(label).find((el) => el.tagName === "P")!.closest("div.rounded-md") as HTMLElement;
const money = (n: number) => formatCurrency(n).replace(/\u00a0/g, " ");
const text = (el: HTMLElement) => (el.textContent ?? "").replace(/\u00a0/g, " ");

beforeEach(() => {
  // 13/11/2026 at 15:00 in São Paulo: "Este mês" is 01/11 to 13/11
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date("2026-11-13T18:00:00Z"));
  vi.mocked(getSalesReport).mockReset().mockResolvedValue(sales());
  vi.mocked(getInventoryReport).mockReset().mockResolvedValue({
    totalProducts: 240, zeroStockCount: 3, lowStockCount: 0, normalCount: 237, totalStockValue: 18500.5, alertCount: 3,
  });
  vi.mocked(getCustomerReport).mockReset().mockResolvedValue({
    totalCustomers: 87, newThisMonth: 5, withPurchases: 60, averagePurchaseValue: 152.4,
  });
  vi.mocked(exportSales).mockReset().mockResolvedValue(undefined);
  vi.mocked(toast.error).mockClear();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("RelatoriosPage", () => {
  it("asks the sales of the current month and shows revenue, returns, cancellations and discounts", async () => {
    vi.mocked(getSalesReport).mockResolvedValue(sales({
      totalSales: 12, totalRevenue: 3400, grossRevenue: 3600, returnedValue: 200, returnedSales: 1,
      cancelledSales: 2, cancelledValue: 450, averageTicket: 283.33, discountValue: 120,
    }));
    render(<RelatoriosPage />, { wrapper });

    expect(await screen.findByText("Ticket médio")).toBeInTheDocument();
    expect(getSalesReport).toHaveBeenCalledWith("2026-11-01", "2026-11-13");
    expect(screen.getByText("Período: 01/11/2026 a 13/11/2026")).toBeInTheDocument();

    expect(text(metric("Faturamento"))).toContain(money(3400));
    expect(text(metric("Faturamento"))).toContain(`${money(3600)} vendidos − ${money(200)} devolvidos`);
    expect(text(metric("Vendas"))).toContain("12");
    expect(text(metric("Vendas"))).toContain("mais 1 devolvida por inteiro");
    expect(text(metric("Ticket médio"))).toContain(money(283.33));
    expect(text(metric("Descontos dados"))).toContain(money(120));
    expect(screen.getByText(/2 vendas canceladas/).textContent?.replace(/\u00a0/g, " ")).toContain(
      `(${money(450)}), fora do faturamento`,
    );
    expect(text(screen.getByText(/em devoluções/))).toContain(`${money(200)} em devoluções`);
  });

  it("without returns nor cancellations says so and hides the notes", async () => {
    render(<RelatoriosPage />, { wrapper });
    expect(await screen.findByText("nenhuma devolução")).toBeInTheDocument();
    expect(screen.getByText("finalizadas no período")).toBeInTheDocument();
    expect(screen.queryByText(/fora do faturamento/)).not.toBeInTheDocument();
    expect(screen.queryByText(/em devoluções/)).not.toBeInTheDocument();
  });

  it("uses singular words for one cancelled sale and plural for several returned", async () => {
    vi.mocked(getSalesReport).mockResolvedValue(sales({ cancelledSales: 1, cancelledValue: 50, returnedSales: 3, returnedValue: 90, grossRevenue: 3490 }));
    render(<RelatoriosPage />, { wrapper });
    expect(await screen.findByText(/1 venda cancelada/)).toBeInTheDocument();
    expect(screen.getByText("mais 3 devolvidas por inteiro")).toBeInTheDocument();
  });

  it("lists only the days with sales, the best sellers, sellers and the share of each payment", async () => {
    vi.mocked(getSalesReport).mockResolvedValue(sales({
      totalRevenue: 1000,
      days: [
        { date: "2026-11-01", sales: 3, revenue: 600 },
        { date: "2026-11-02", sales: 0, revenue: 0 },
        { date: "2026-11-03", sales: 0, revenue: -40 },
        { date: "2026-11-04", sales: 2, revenue: 440 },
      ],
      topProducts: [
        { productId: "p1", productName: "Camiseta básica", quantity: 1.5, revenue: 700 },
        { productId: "p2", productName: "Boné", quantity: 4, revenue: 300 },
      ],
      sellers: [{ sellerName: "Ana", sales: 5, revenue: 1000 }],
      payments: [
        { method: "Pix", sales: 3, amount: 750 },
        { method: "Credit", sales: 1, amount: 250 },
        { method: "Voucher", sales: 1, amount: 0 },
      ],
    }));
    render(<RelatoriosPage />, { wrapper });

    expect(await screen.findByTestId("sales-chart")).toHaveTextContent("4 dias no gráfico");
    const dayTable = screen.getByText("Ver dia a dia").closest("details") as HTMLElement;
    const dayRows = within(dayTable).getAllByRole("row").slice(1).map((r) => text(r));
    expect(dayRows).toEqual([
      `01/113${money(600)}`,
      `03/110${money(-40)}`,
      `04/112${money(440)}`,
    ]);

    expect(screen.getByText("Camiseta básica")).toBeInTheDocument();
    expect(screen.getByText("1,5")).toBeInTheDocument();
    expect(screen.getByText("Ana")).toBeInTheDocument();

    const pix = screen.getByText("Pix").closest("li") as HTMLElement;
    expect(text(pix)).toContain(`3 vendas · ${money(750)} · 75%`);
    const credit = screen.getByText("Cartão de crédito").closest("li") as HTMLElement;
    expect(text(credit)).toContain(`1 venda · ${money(250)} · 25%`);
    expect((credit.querySelector(".bg-primary") as HTMLElement).style.width).toBe("25%");
    // an unknown method keeps its own name
    expect(text(screen.getByText("Voucher").closest("li") as HTMLElement)).toContain("· 0%");
  });

  it("an empty period has no chart and says there is nothing in each table", async () => {
    vi.mocked(getSalesReport).mockResolvedValue(sales({ totalSales: 0, totalRevenue: 0, averageTicket: 0 }));
    render(<RelatoriosPage />, { wrapper });
    expect(await screen.findByText("Nenhuma venda finalizada neste período.")).toBeInTheDocument();
    expect(screen.queryByTestId("sales-chart")).not.toBeInTheDocument();
    expect(screen.getByText("Sem produtos vendidos no período.")).toBeInTheDocument();
    expect(screen.getByText("Sem vendas no período.")).toBeInTheDocument();
    expect(screen.getByText("Sem pagamentos no período.")).toBeInTheDocument();
  });

  it("a period with only returns still shows the chart, and payments of zero total give 0%", async () => {
    vi.mocked(getSalesReport).mockResolvedValue(sales({
      totalSales: 0, returnedSales: 1, returnedValue: 80, totalRevenue: -80, grossRevenue: 0,
      payments: [{ method: "Cash", sales: 0, amount: 0 }],
    }));
    render(<RelatoriosPage />, { wrapper });
    expect(await screen.findByTestId("sales-chart")).toBeInTheDocument();
    expect(text(screen.getByText("Dinheiro").closest("li") as HTMLElement)).toContain("0 vendas");
    expect(text(screen.getByText("Dinheiro").closest("li") as HTMLElement)).toContain("· 0%");
  });

  it("shows stock and customer figures with warnings only when there is something to act on", async () => {
    render(<RelatoriosPage />, { wrapper });
    expect(await screen.findByText("Valor em estoque")).toBeInTheDocument();
    expect(text(metric("Valor em estoque"))).toContain(money(18500.5));
    expect(text(metric("Produtos ativos"))).toContain("240");
    expect(metric("Sem estoque").className).toContain("border-destructive");
    expect(metric("Abaixo do mínimo").className).not.toContain("border-warning");
    expect(text(metric("Clientes ativos"))).toContain("87");
    expect(text(metric("Novos neste mês"))).toContain("5");
    expect(text(metric("Já compraram"))).toContain("60");
    expect(text(metric("Compra média"))).toContain(money(152.4));
    expect(screen.getByRole("link", { name: "Ver estoque →" })).toHaveAttribute("href", "/estoque");
    // other reports: only the modules the company has
    expect(screen.getByRole("link", { name: /Restaurante/ })).toHaveAttribute("href", "/restaurante/relatorios");
    expect(screen.queryByRole("link", { name: /Obras/ })).not.toBeInTheDocument();
  });

  it("low stock is a warning", async () => {
    vi.mocked(getInventoryReport).mockResolvedValue({
      totalProducts: 10, zeroStockCount: 0, lowStockCount: 4, normalCount: 6, totalStockValue: 0, alertCount: 4,
    });
    render(<RelatoriosPage />, { wrapper });
    await screen.findByText("Abaixo do mínimo");
    expect(metric("Abaixo do mínimo").className).toContain("border-warning");
    expect(metric("Sem estoque").className).not.toContain("border-destructive");
  });

  it("each failed block offers a retry that reads it again", async () => {
    vi.mocked(getSalesReport).mockRejectedValueOnce(new Error("500"));
    vi.mocked(getInventoryReport).mockRejectedValueOnce(new Error("500"));
    vi.mocked(getCustomerReport).mockRejectedValueOnce(new Error("500"));
    render(<RelatoriosPage />, { wrapper });

    expect(await screen.findByText("Não foi possível montar o relatório de vendas")).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível ler o estoque")).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível ler os clientes")).toBeInTheDocument();

    const retry = within(screen.getByText("Não foi possível montar o relatório de vendas").closest("[role=alert]") as HTMLElement)
      .getByRole("button", { name: /Tentar novamente/ });
    fireEvent.click(retry);
    expect(await screen.findByText("Ticket médio")).toBeInTheDocument();
    expect(getSalesReport).toHaveBeenCalledTimes(2);

    fireEvent.click(within(screen.getByText("Não foi possível ler o estoque").closest("[role=alert]") as HTMLElement)
      .getByRole("button", { name: /Tentar novamente/ }));
    fireEvent.click(within(screen.getByText("Não foi possível ler os clientes").closest("[role=alert]") as HTMLElement)
      .getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Valor em estoque")).toBeInTheDocument();
    expect(await screen.findByText("Clientes ativos")).toBeInTheDocument();
  });

  it.each([
    ["Hoje", "2026-11-13", "2026-11-13", "Período: 13/11/2026"],
    ["7 dias", "2026-11-07", "2026-11-13", "Período: 07/11/2026 a 13/11/2026"],
    ["Mês passado", "2026-10-01", "2026-10-31", "Período: 01/10/2026 a 31/10/2026"],
  ])("the preset %s asks the sales from %s to %s", async (label, from, to, caption) => {
    render(<RelatoriosPage />, { wrapper });
    await screen.findByText("Ticket médio");
    fireEvent.click(screen.getByRole("radio", { name: label }));
    await waitFor(() => expect(getSalesReport).toHaveBeenLastCalledWith(from, to));
    expect(screen.getByText(caption)).toBeInTheDocument();
  });

  it("a custom period is validated before it is asked for", async () => {
    render(<RelatoriosPage />, { wrapper });
    await screen.findByText("Ticket médio");
    fireEvent.click(screen.getByRole("radio", { name: "Escolher datas" }));

    const from = screen.getByLabelText("De");
    const to = screen.getByLabelText("Até");
    const submit = screen.getByRole("button", { name: "Ver período" });

    fireEvent.change(to, { target: { value: "2026-10-01" } });
    expect(screen.getByText("A data final vem antes da inicial.")).toBeInTheDocument();
    expect(submit).toBeDisabled();

    fireEvent.change(from, { target: { value: "" } });
    expect(screen.getByText("Informe as duas datas.")).toBeInTheDocument();

    fireEvent.change(from, { target: { value: "2024-01-01" } });
    fireEvent.change(to, { target: { value: "2026-01-01" } });
    expect(screen.getByText("Escolha um período de até 366 dias.")).toBeInTheDocument();
    fireEvent.submit(submit.closest("form") as HTMLFormElement);
    expect(getSalesReport).not.toHaveBeenCalledWith("2024-01-01", "2026-01-01");

    fireEvent.change(from, { target: { value: "2026-09-10" } });
    fireEvent.change(to, { target: { value: "2026-09-20" } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);
    await waitFor(() => expect(getSalesReport).toHaveBeenLastCalledWith("2026-09-10", "2026-09-20"));
    expect(screen.getByText("Período: 10/09/2026 a 20/09/2026")).toBeInTheDocument();
  });

  it("exports the sales of the period shown and reports a failure", async () => {
    let finish!: () => void;
    vi.mocked(exportSales).mockImplementationOnce(() => new Promise<void>((r) => (finish = r)));
    render(<RelatoriosPage />, { wrapper });
    await screen.findByText("Ticket médio");

    fireEvent.click(screen.getByRole("button", { name: /Exportar planilha/ }));
    expect(await screen.findByRole("button", { name: /Exportando…/ })).toBeDisabled();
    expect(exportSales).toHaveBeenCalledWith("2026-11-01", "2026-11-13");
    finish();
    expect(await screen.findByRole("button", { name: /Exportar planilha/ })).toBeEnabled();

    vi.mocked(exportSales).mockRejectedValueOnce(new Error("Planilha grande demais"));
    fireEvent.click(screen.getByRole("button", { name: /Exportar planilha/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Planilha grande demais"));

    vi.mocked(exportSales).mockRejectedValueOnce("sem detalhes");
    fireEvent.click(screen.getByRole("button", { name: /Exportar planilha/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Falha ao exportar as vendas"));
  });
});
