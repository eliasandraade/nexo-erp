import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { LedgerDto, LedgerEntryDto } from "../api/financial.api";
import { selectTab } from "@/test/helpers/radixInteractions";
import FinanceiroPage from "./FinanceiroPage";

const api = vi.hoisted(() => ({ getLedger: vi.fn() }));
vi.mock("../api/financial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/financial.api")>()),
  ...api,
}));

const entry = (over: Partial<LedgerEntryDto>): LedgerEntryDto => ({
  id: "e", direction: "In", category: "Revenue", amount: 0, description: "", competenceDate: "2026-11-05T15:00:00Z",
  dueDate: "2026-11-05T15:00:00Z", paidAt: null, status: "Pending", storeId: null, referenceType: null,
  referenceId: null, reversesTransactionId: null, ...over,
});

const ledger: LedgerDto = {
  regime: "Accrual", from: "2026-11-01T03:00:00Z", to: "2026-12-01T03:00:00Z",
  totalIn: 12500, totalOut: 4300.25, net: 8199.75, result: 11199.75, truncated: false,
  byCategory: [
    { category: "Revenue", in: 12500, out: 0, net: 12500, count: 31 },
    { category: "Purchase", in: 0, out: 3000, net: -3000, count: 2 },
    { category: "OperatingExpense", in: 0, out: 1300.25, net: -1300.25, count: 1 },
  ],
  entries: [
    entry({ id: "e1", description: "Venda #1023", amount: 350, status: "Paid", referenceType: "Sale",
      competenceDate: "2026-11-05T15:00:00Z", paidAt: "2026-11-10T15:00:00Z" }),
    entry({ id: "e2", direction: "Out", category: "OperatingExpense", description: "Conta de energia", amount: 1300.25,
      status: "Pending", referenceType: "Recurrence", competenceDate: "2026-11-08T15:00:00Z" }),
    entry({ id: "e3", direction: "Out", category: "FinancialExpense", description: "Desconto concedido na venda #990",
      amount: 15, status: "Abated", referenceType: "Settlement", competenceDate: "2026-11-09T15:00:00Z" }),
    entry({ id: "e4", direction: "In", category: "OtherIncome", description: "Ajuste importado", amount: 42,
      status: "Overdue", referenceType: "LegacyImport", competenceDate: "2026-11-11T15:00:00Z" }),
  ],
};

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><FinanceiroPage /></QueryClientProvider>);
}

const metric = (label: string) => screen.getByText(label, { selector: "p" }).parentElement!.parentElement!;
const row = (text: string) => screen.getByText(text).closest("tr")!;

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 13, 10, 0));
  api.getLedger.mockResolvedValue(ledger);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("FinanceiroPage — livro por competência", () => {
  it("busca o mês corrente em horário local e mostra totais e resultado antes do CMV", async () => {
    renderPage();
    expect(await screen.findByText("Por categoria")).toBeInTheDocument();

    expect(api.getLedger).toHaveBeenCalledWith("accrual", new Date(2026, 10, 1), new Date(2026, 11, 1));
    expect(screen.getByLabelText("Mês")).toHaveValue("2026-11");

    expect(metric("Entradas")).toHaveTextContent("R$ 12.500,00");
    expect(metric("Saídas")).toHaveTextContent("R$ 4.300,25");
    const result = metric("Resultado (antes do CMV)");
    expect(result).toHaveTextContent("R$ 11.199,75");
    expect(result).toHaveTextContent(/Ainda não desconta o custo das mercadorias vendidas/);
    expect(result.querySelector(".text-destructive")).toBeNull();
  });

  it("detalha cada categoria com entradas, saídas e líquido", async () => {
    renderPage();
    await screen.findByText("Por categoria");

    const byCategory = within(screen.getAllByRole("table")[0]);
    const cols = (label: string) =>
      Array.from(byCategory.getByText(label).closest("tr")!.querySelectorAll("td")).map((td) => td.textContent?.replace(/\s/g, " "));
    expect(cols("Receita de vendas e serviços")).toEqual(["Receita de vendas e serviços", "R$ 12.500,00", "—", "R$ 12.500,00"]);
    expect(cols("Compras de mercadoria")).toEqual(["Compras de mercadoria", "—", "R$ 3.000,00", "-R$ 3.000,00"]);
    expect(cols("Despesas operacionais")).toEqual(["Despesas operacionais", "—", "R$ 1.300,25", "-R$ 1.300,25"]);
  });

  it("lista os lançamentos com data de competência, origem, situação e sinal", async () => {
    renderPage();
    await screen.findByText("Lançamentos");

    const venda = row("Venda #1023");
    expect(venda).toHaveTextContent("05/11/2026");
    expect(venda).toHaveTextContent("Venda");
    expect(venda).toHaveTextContent("Receita de vendas e serviços");
    expect(venda).toHaveTextContent("Quitado");
    expect(venda.lastElementChild).toHaveTextContent("+ R$ 350,00");
    expect(venda.lastElementChild).not.toHaveClass("text-destructive");

    const energia = row("Conta de energia");
    expect(energia).toHaveTextContent("08/11/2026");
    expect(energia).toHaveTextContent("Recorrência");
    expect(energia).toHaveTextContent("Em aberto");
    expect(energia.lastElementChild).toHaveTextContent("− R$ 1.300,25");
    expect(energia.lastElementChild).toHaveClass("text-destructive");

    expect(row("Desconto concedido na venda #990")).toHaveTextContent("Juros, multa ou desconto");
    expect(row("Desconto concedido na venda #990")).toHaveTextContent("Abatido (desconto)");
    // origem desconhecida aparece crua, sem quebrar
    expect(row("Ajuste importado")).toHaveTextContent("LegacyImport");
    expect(row("Ajuste importado")).toHaveTextContent("Vencido");
    expect(row("Ajuste importado")).toHaveTextContent("Outras entradas");
  });

  it("avisa quando os lançamentos foram cortados", async () => {
    api.getLedger.mockResolvedValue({ ...ledger, truncated: true });
    renderPage();
    expect(await screen.findByText(
      "Mostrando os 4 lançamentos mais recentes. Os totais acima cobrem o mês inteiro.")).toBeInTheDocument();
  });

  it("mês sem lançamentos mostra o vazio e zeros", async () => {
    api.getLedger.mockResolvedValue({ ...ledger, totalIn: 0, totalOut: 0, net: 0, result: 0, byCategory: [], entries: [] });
    renderPage();
    expect(await screen.findByText("Nenhum lançamento no mês")).toBeInTheDocument();
    expect(metric("Entradas")).toHaveTextContent("R$ 0,00");
    expect(screen.queryByText("Lançamentos")).not.toBeInTheDocument();
  });

  it("resultado negativo fica em destaque", async () => {
    api.getLedger.mockResolvedValue({ ...ledger, result: -850.1 });
    renderPage();
    await screen.findByText("Por categoria");
    const result = metric("Resultado (antes do CMV)");
    expect(result).toHaveTextContent("-R$ 850,10");
    expect(result.querySelector(".text-destructive")).not.toBeNull();
  });
});

describe("FinanceiroPage — regime de caixa e período", () => {
  it("no regime de caixa usa a data do pagamento e explica o resultado", async () => {
    renderPage();
    await screen.findByText("Por categoria");

    selectTab(screen.getByRole("tab", { name: "Caixa" }));
    await waitFor(() => expect(api.getLedger).toHaveBeenLastCalledWith("cash", new Date(2026, 10, 1), new Date(2026, 11, 1)));

    await waitFor(() => expect(screen.getByText("Resultado", { selector: "p" })).toBeInTheDocument());
    expect(metric("Resultado")).toHaveTextContent("Só o que foi quitado");
    // pago em 10/11: no caixa vale a data do pagamento
    expect(row("Venda #1023")).toHaveTextContent("10/11/2026");
    expect(row("Venda #1023")).not.toHaveTextContent("05/11/2026");
    // ainda não pago: cai na competência
    expect(row("Conta de energia")).toHaveTextContent("08/11/2026");
  });

  it("trocar o mês busca o intervalo daquele mês; apagar volta ao mês corrente", async () => {
    renderPage();
    await screen.findByText("Por categoria");

    fireEvent.change(screen.getByLabelText("Mês"), { target: { value: "2026-02" } });
    await waitFor(() => expect(api.getLedger).toHaveBeenLastCalledWith("accrual", new Date(2026, 1, 1), new Date(2026, 2, 1)));

    fireEvent.change(screen.getByLabelText("Mês"), { target: { value: "2025-12" } });
    await waitFor(() => expect(api.getLedger).toHaveBeenLastCalledWith("accrual", new Date(2025, 11, 1), new Date(2026, 0, 1)));

    fireEvent.change(screen.getByLabelText("Mês"), { target: { value: "" } });
    await waitFor(() => expect(api.getLedger).toHaveBeenLastCalledWith("accrual", new Date(2026, 10, 1), new Date(2026, 11, 1)));
  });

  it("mostra o erro e tenta de novo", async () => {
    api.getLedger.mockRejectedValueOnce(new Error("500"));
    renderPage();
    expect(await screen.findByText("Não foi possível carregar o financeiro")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Por categoria")).toBeInTheDocument();
    expect(api.getLedger).toHaveBeenCalledTimes(2);
  });
});
