import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { RecurrenceDto, TitleDto, TitlesDto } from "../api/financial.api";
import { installRadixPolyfills, pickOption } from "@/test/helpers/radixInteractions";
import ContasPagarPage from "./ContasPagarPage";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  getReceivables: vi.fn(),
  getPayables: vi.fn(),
  settleTransaction: vi.fn(),
  createInstallments: vi.fn(),
  listRecurrences: vi.fn(),
  createRecurrence: vi.fn(),
  deactivateRecurrence: vi.fn(),
}));
vi.mock("../api/financial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/financial.api")>()),
  ...api,
}));

vi.mock("@/modules/customers/hooks/use-customers", () => ({ useCustomers: () => ({ data: [] }) }));
vi.mock("@/modules/suppliers/hooks/use-suppliers", () => ({
  useSuppliers: () => ({
    data: [
      { id: "s1", name: "Companhia Energética do Ceará S.A.", tradeName: "Enel" },
      { id: "s2", name: "Escritório Contábil Silva", tradeName: "" },
    ],
  }),
}));

const noonIso = (y: number, m: number, d: number) => new Date(y, m - 1, d, 12).toISOString();

function title(over: Partial<TitleDto>): TitleDto {
  return {
    id: "t", description: "", customerId: null, customerName: null, supplierId: null, supplierName: null,
    amount: 0, dueDate: "2026-11-20T15:00:00Z", competenceDate: "2026-11-01T15:00:00Z", paidAt: null,
    status: "Pending", daysOverdue: 0, category: "OperatingExpense", storeId: null, referenceType: null,
    referenceId: null, parentTransactionId: null, ...over,
  };
}

const payables: TitlesDto = {
  truncated: false,
  aging: { notDue: 1290.4, overdue1To30: 0, overdue31To60: 640, overdueOver60: 0, totalOpen: 1930.4, openCount: 3, overdueCount: 1 },
  items: [
    title({ id: "p1", description: "Conta de energia — outubro", supplierId: "s1", supplierName: "Enel",
      amount: 410.4, dueDate: "2026-11-18T15:00:00Z" }),
    title({ id: "p2", description: "Compra de insumos da cozinha", supplierName: "Atacadão",
      amount: 880, referenceType: "RestExpense" }),
    title({ id: "p3", description: "Medição 3 — fundação", supplierName: "Concreto Forte",
      amount: 640, status: "Overdue", daysOverdue: 40, dueDate: "2026-10-04T15:00:00Z", referenceType: "FinancialMovement" }),
    title({ id: "p4", description: "Internet — setembro", supplierName: "Fibra Net", amount: 129.9, status: "Cancelled" }),
  ],
};

const recurrences: RecurrenceDto[] = [
  { id: "r1", transactionType: "Payable", category: "OperatingExpense", description: "Aluguel da loja", amount: 3500,
    dayOfMonth: 10, startMonth: "2026-01-01", endMonth: null, generatedThrough: "2026-12-01", isActive: true,
    supplierId: null, supplierName: "Imobiliária Centro", customerId: null, storeId: null },
  { id: "r2", transactionType: "Payable", category: "Tax", description: "Simples Nacional", amount: 1234.56,
    dayOfMonth: 20, startMonth: "2025-07-01", endMonth: null, generatedThrough: "2026-12-01", isActive: true,
    supplierId: null, supplierName: null, customerId: null, storeId: null },
];

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><ContasPagarPage /></QueryClientProvider>);
}

const row = (text: string) => screen.getByText(text).closest("tr")!;
const metric = (label: string) => screen.getByText(label, { selector: "p" }).parentElement!.parentElement!;

beforeEach(() => {
  installRadixPolyfills();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 13, 10, 0));
  api.getPayables.mockResolvedValue(payables);
  api.listRecurrences.mockResolvedValue(recurrences);
  api.settleTransaction.mockResolvedValue({});
  api.createInstallments.mockResolvedValue({});
  api.createRecurrence.mockResolvedValue({});
  api.deactivateRecurrence.mockResolvedValue({});
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("ContasPagarPage — títulos a pagar", () => {
  it("lista as contas com fornecedor, aging e quem dá baixa na origem", async () => {
    renderPage();
    expect(await screen.findByText("Conta de energia — outubro")).toBeInTheDocument();
    expect(api.getPayables).toHaveBeenCalledWith("open");
    expect(api.getReceivables).not.toHaveBeenCalled();
    expect(screen.getByRole("heading", { name: "Contas a pagar" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Pagos" })).toBeInTheDocument();

    expect(metric("A vencer")).toHaveTextContent("R$ 1.290,40");
    expect(metric("Vencido 31 a 60 dias")).toHaveTextContent("R$ 640,00");
    expect(metric("Vencido há mais de 60 dias")).toHaveTextContent("Total em aberto: R$ 1.930,40");

    const energia = row("Conta de energia — outubro");
    expect(energia).toHaveTextContent("18/11/2026");
    expect(energia).toHaveTextContent("Enel");
    expect(energia).toHaveTextContent("R$ 410,40");
    expect(within(energia).getByRole("button", { name: "Pagar" })).toBeInTheDocument();

    expect(row("Compra de insumos da cozinha")).toHaveTextContent("Pague na despesa do restaurante");
    expect(within(row("Compra de insumos da cozinha")).queryByRole("button")).not.toBeInTheDocument();

    const obra = row("Medição 3 — fundação");
    expect(obra).toHaveTextContent("Quitado na obra");
    expect(obra).toHaveTextContent("há 40 dias");

    const cancelada = row("Internet — setembro");
    expect(cancelada).toHaveTextContent("Cancelado");
    expect(within(cancelada).queryByRole("button")).not.toBeInTheDocument();
  });

  it("paga o título inteiro com desconto obtido", async () => {
    renderPage();
    await screen.findByText("Conta de energia — outubro");
    fireEvent.click(within(row("Conta de energia — outubro")).getByRole("button", { name: "Pagar" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Conta de energia — outubro — saldo R$ 410,40")).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText("Valor pago"), { target: { value: "400,40" } });
    fireEvent.change(within(dialog).getByLabelText("Desconto obtido"), { target: { value: "10" } });
    expect(within(dialog).getByText("O título será quitado.")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar pagamento" }));

    await waitFor(() => expect(api.settleTransaction).toHaveBeenCalledWith("p1", {
      amount: 400.4, interest: 0, fine: 0, discount: 10, paidAt: noonIso(2026, 11, 13),
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Título pago."));
  });

  it("pagamento parcial avisa o saldo restante", async () => {
    renderPage();
    await screen.findByText("Conta de energia — outubro");
    fireEvent.click(within(row("Conta de energia — outubro")).getByRole("button", { name: "Pagar" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Valor pago"), { target: { value: "200" } });
    expect(within(dialog).getByText("Fica em aberto: R$ 210,40 (mesmo vencimento).")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar pagamento" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Pagamento parcial registrado."));
  });

  it("lança uma conta a pagar com fornecedor e categoria", async () => {
    renderPage();
    await screen.findByText("Conta de energia — outubro");
    fireEvent.click(screen.getByRole("button", { name: "Nova conta a pagar" }));
    const dialog = await screen.findByRole("dialog");

    // Categoria padrão: despesas operacionais
    expect(within(dialog).getByRole("combobox", { name: "Categoria" })).toHaveTextContent("Despesas operacionais");
    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "Honorários contábeis" } });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "1.500" } });
    fireEvent.change(within(dialog).getByLabelText("1º vencimento"), { target: { value: "2026-11-30" } });
    // sem nome fantasia, vale a razão social
    await pickOption(within(dialog).getByRole("combobox", { name: "Fornecedor" }), "Escritório Contábil Silva");
    await pickOption(within(dialog).getByRole("combobox", { name: "Categoria" }), "Impostos e taxas");

    fireEvent.click(within(dialog).getByRole("button", { name: "Lançar" }));

    await waitFor(() => expect(api.createInstallments).toHaveBeenCalledWith({
      transactionType: "Payable", totalAmount: 1500, description: "Honorários contábeis",
      firstDueDate: noonIso(2026, 11, 30), installments: 1, category: "Tax", supplierId: "s2",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Conta lançada."));
  });

  it("mostra a mensagem do erro de carga das contas a pagar", async () => {
    api.getPayables.mockRejectedValue(new Error("503"));
    renderPage();
    expect(await screen.findByText("Não foi possível carregar as contas a pagar")).toBeInTheDocument();
  });

  it("mostra o vazio de contas a pagar", async () => {
    api.getPayables.mockResolvedValue({ ...payables, items: [] });
    renderPage();
    expect(await screen.findByText("Compras recebidas, despesas e recorrências aparecem nesta lista.")).toBeInTheDocument();
  });
});

describe("ContasPagarPage — despesas recorrentes", () => {
  it("lista as recorrências com categoria, mês de início, dia e valor", async () => {
    renderPage();
    const aluguel = (await screen.findByText("Aluguel da loja")).closest("tr")!;
    expect(aluguel).toHaveTextContent("Despesas operacionais · desde 01/2026");
    expect(aluguel).toHaveTextContent("Imobiliária Centro");
    expect(aluguel).toHaveTextContent("10");
    expect(aluguel).toHaveTextContent("R$ 3.500,00");

    const simples = row("Simples Nacional");
    expect(simples).toHaveTextContent("Impostos e taxas · desde 07/2025");
    expect(simples).toHaveTextContent("—");
    expect(simples).toHaveTextContent("R$ 1.234,56");
  });

  it("mostra o vazio quando não há recorrência", async () => {
    api.listRecurrences.mockResolvedValue([]);
    renderPage();
    expect(await screen.findByText("Nenhuma recorrência")).toBeInTheDocument();
  });

  it("encerra uma recorrência após confirmar", async () => {
    renderPage();
    await screen.findByText("Aluguel da loja");
    fireEvent.click(within(row("Aluguel da loja")).getByRole("button", { name: "Encerrar" }));

    const alert = await screen.findByRole("alertdialog");
    expect(within(alert).getByText("Encerrar “Aluguel da loja”?")).toBeInTheDocument();
    fireEvent.click(within(alert).getByRole("button", { name: "Encerrar recorrência" }));

    await waitFor(() => expect(api.deactivateRecurrence).toHaveBeenCalledTimes(1));
    expect(api.deactivateRecurrence.mock.calls[0][0]).toBe("r1");
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Recorrência encerrada. Os títulos já gerados continuam na lista."));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    await waitFor(() => expect(api.listRecurrences).toHaveBeenCalledTimes(2));
  });

  it("voltar não encerra; erro da API mantém a confirmação aberta", async () => {
    renderPage();
    await screen.findByText("Simples Nacional");
    fireEvent.click(within(row("Simples Nacional")).getByRole("button", { name: "Encerrar" }));
    let alert = await screen.findByRole("alertdialog");
    fireEvent.click(within(alert).getByRole("button", { name: "Voltar" }));
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
    expect(api.deactivateRecurrence).not.toHaveBeenCalled();

    api.deactivateRecurrence.mockRejectedValueOnce(new Error(""));
    fireEvent.click(within(row("Simples Nacional")).getByRole("button", { name: "Encerrar" }));
    alert = await screen.findByRole("alertdialog");
    fireEvent.click(within(alert).getByRole("button", { name: "Encerrar recorrência" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível encerrar."));
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });

  it("cria uma recorrência mensal com fornecedor e categoria", async () => {
    renderPage();
    await screen.findByText("Aluguel da loja");
    fireEvent.click(screen.getByRole("button", { name: "Nova recorrência" }));
    const dialog = await screen.findByRole("dialog");

    const create = within(dialog).getByRole("button", { name: "Criar recorrência" });
    expect(create).toBeDisabled();
    expect(within(dialog).getByLabelText("Dia do vencimento")).toHaveValue("10");
    expect(within(dialog).getByLabelText("A partir de")).toHaveValue("2026-11");

    fireEvent.change(within(dialog).getByLabelText("Descrição da recorrência"), { target: { value: " Energia da loja " } });
    fireEvent.change(within(dialog).getByLabelText("Valor mensal"), { target: { value: "420,90" } });
    fireEvent.change(within(dialog).getByLabelText("Dia do vencimento"), { target: { value: "5" } });
    fireEvent.change(within(dialog).getByLabelText("A partir de"), { target: { value: "2026-12" } });
    await pickOption(within(dialog).getByRole("combobox", { name: "Fornecedor da recorrência" }), "Enel");
    await pickOption(within(dialog).getByRole("combobox", { name: "Categoria da recorrência" }), "Outras saídas");
    expect(create).toBeEnabled();

    fireEvent.click(create);

    await waitFor(() => expect(api.createRecurrence).toHaveBeenCalledWith({
      transactionType: "Payable", description: "Energia da loja", amount: 420.9, dayOfMonth: 5,
      startMonth: "2026-12-01", category: "OtherExpense", supplierId: "s1",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(
      "Recorrência criada. Os títulos deste mês e do próximo já estão na lista."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    // a recorrência gera títulos: a lista de contas a pagar também é recarregada
    await waitFor(() => expect(api.getPayables).toHaveBeenCalledTimes(2));
    expect(api.listRecurrences).toHaveBeenCalledTimes(2);
  });

  it("valida valor, dia do mês e a janela do mês inicial", async () => {
    renderPage();
    await screen.findByText("Aluguel da loja");
    fireEvent.click(screen.getByRole("button", { name: "Nova recorrência" }));
    const dialog = await screen.findByRole("dialog");
    const create = within(dialog).getByRole("button", { name: "Criar recorrência" });
    const set = (label: string, value: string) => fireEvent.change(within(dialog).getByLabelText(label), { target: { value } });

    set("Descrição da recorrência", "Internet");
    set("Valor mensal", "0");
    expect(create).toBeDisabled();
    set("Valor mensal", "99,90");
    expect(create).toBeEnabled();

    for (const day of ["0", "32", "1.5", "abc", "100"]) {
      set("Dia do vencimento", day);
      expect(create).toBeDisabled();
    }
    set("Dia do vencimento", "31");
    expect(create).toBeEnabled();

    // até 12 meses para trás e 24 para frente
    set("A partir de", "2025-10");
    expect(create).toBeDisabled();
    set("A partir de", "2025-11");
    expect(create).toBeEnabled();
    set("A partir de", "2028-12");
    expect(create).toBeDisabled();
    set("A partir de", "2028-11");
    expect(create).toBeEnabled();
    set("A partir de", "11/2026");
    expect(create).toBeDisabled();
  });

  it("mostra o erro ao criar e mantém o diálogo; cancelar fecha", async () => {
    api.createRecurrence.mockRejectedValueOnce(new Error("Já existe uma recorrência com essa descrição."));
    renderPage();
    await screen.findByText("Aluguel da loja");
    fireEvent.click(screen.getByRole("button", { name: "Nova recorrência" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Descrição da recorrência"), { target: { value: "Aluguel da loja" } });
    fireEvent.change(within(dialog).getByLabelText("Valor mensal"), { target: { value: "3500" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar recorrência" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe uma recorrência com essa descrição."));
    expect(api.createRecurrence.mock.calls[0][0]).toMatchObject({ supplierId: null, category: "OperatingExpense", startMonth: "2026-11-01" });

    api.createRecurrence.mockRejectedValueOnce(new Error(""));
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar recorrência" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível criar a recorrência."));

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
