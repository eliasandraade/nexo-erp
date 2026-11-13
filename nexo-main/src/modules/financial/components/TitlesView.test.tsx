import { type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { TitleDto, TitlesDto } from "../api/financial.api";
import { installRadixPolyfills, pickOption, selectTab } from "@/test/helpers/radixInteractions";
import { TitlesView } from "./TitlesView";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  getReceivables: vi.fn(),
  getPayables: vi.fn(),
  settleTransaction: vi.fn(),
  createInstallments: vi.fn(),
}));
vi.mock("../api/financial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/financial.api")>()),
  ...api,
}));

vi.mock("@/modules/customers/hooks/use-customers", () => ({
  useCustomers: () => ({ data: [{ id: "c1", name: "Maria Souza" }, { id: "c2", name: "Construtora Alfa" }] }),
}));
vi.mock("@/modules/suppliers/hooks/use-suppliers", () => ({
  useSuppliers: () => ({ data: [] }),
}));

/** Uma data local ao meio-dia, como a tela envia (dayToIso). */
const noonIso = (y: number, m: number, d: number) => new Date(y, m - 1, d, 12).toISOString();

function title(over: Partial<TitleDto>): TitleDto {
  return {
    id: "t", description: "", customerId: null, customerName: null, supplierId: null, supplierName: null,
    amount: 0, dueDate: "2026-11-20T15:00:00Z", competenceDate: "2026-11-01T15:00:00Z", paidAt: null,
    status: "Pending", daysOverdue: 0, category: "Revenue", storeId: null, referenceType: null,
    referenceId: null, parentTransactionId: null, ...over,
  };
}

const receivables: TitlesDto = {
  truncated: false,
  aging: { notDue: 350, overdue1To30: 1200, overdue31To60: 0, overdueOver60: 480, totalOpen: 2030, openCount: 3, overdueCount: 2 },
  items: [
    title({ id: "t1", description: "Venda #1023 — parcela 2/3", customerId: "c1", customerName: "Maria Souza",
      amount: 350, dueDate: "2026-11-20T15:00:00Z", status: "Pending", referenceType: "Sale" }),
    title({ id: "t2", description: "Serviço de instalação", customerId: "c2", customerName: "Construtora Alfa",
      amount: 1200, dueDate: "2026-10-20T15:00:00Z", status: "Overdue", daysOverdue: 24 }),
    title({ id: "t3", description: "Pacote 4 cortes", customerName: "João Lima", amount: 480,
      dueDate: "2026-08-10T15:00:00Z", status: "Overdue", daysOverdue: 95, referenceType: "SvcCustomerPackage" }),
    title({ id: "t4", description: "Venda balcão", customerName: null, amount: 90,
      dueDate: "2026-11-02T15:00:00Z", status: "Paid", paidAt: "2026-11-02T15:00:00Z" }),
  ],
};

function renderView(direction: "Receivable" | "Payable" = "Receivable") {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrap = (ui: ReactNode) => <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
  return render(wrap(<TitlesView direction={direction} />));
}

/** O valor exibido por um card de indicador, achado pelo rótulo. */
function metric(label: string) {
  return screen.getByText(label, { selector: "p" }).parentElement!.parentElement!;
}

function row(text: string) {
  return screen.getByText(text).closest("tr")!;
}

beforeEach(() => {
  installRadixPolyfills();
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(2026, 10, 13, 10, 0));
  api.getReceivables.mockResolvedValue(receivables);
  api.settleTransaction.mockResolvedValue({});
  api.createInstallments.mockResolvedValue({});
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("TitlesView — contas a receber", () => {
  it("lista os títulos em aberto com aging, vencimento, situação e valor", async () => {
    renderView();

    expect(await screen.findByText("Venda #1023 — parcela 2/3")).toBeInTheDocument();
    expect(api.getReceivables).toHaveBeenCalledWith("open");
    expect(screen.getByRole("heading", { name: "Contas a receber" })).toBeInTheDocument();

    expect(metric("A vencer")).toHaveTextContent("R$ 350,00");
    expect(metric("Vencido até 30 dias")).toHaveTextContent("R$ 1.200,00");
    expect(metric("Vencido 31 a 60 dias")).toHaveTextContent("R$ 0,00");
    expect(metric("Vencido há mais de 60 dias")).toHaveTextContent("R$ 480,00");
    expect(metric("Vencido há mais de 60 dias")).toHaveTextContent("Total em aberto: R$ 2.030,00");

    const venda = row("Venda #1023 — parcela 2/3");
    expect(venda).toHaveTextContent("20/11/2026");
    expect(venda).toHaveTextContent("Maria Souza");
    expect(venda).toHaveTextContent("A vencer");
    expect(venda).toHaveTextContent("R$ 350,00");
    expect(within(venda).getByRole("button", { name: "Receber" })).toBeInTheDocument();

    const vencido = row("Serviço de instalação");
    expect(vencido).toHaveTextContent("20/10/2026");
    expect(vencido).toHaveTextContent("Vencido");
    expect(vencido).toHaveTextContent("há 24 dias");
    expect(vencido).toHaveTextContent("R$ 1.200,00");

    // Recebido pelo Service: a baixa é lá, não aqui.
    const service = row("Pacote 4 cortes");
    expect(service).toHaveTextContent("Pelo Service");
    expect(within(service).queryByRole("button", { name: "Receber" })).not.toBeInTheDocument();

    // Já recebido: sem cliente vira "—" e não há ação.
    const pago = row("Venda balcão");
    expect(pago).toHaveTextContent("—");
    expect(pago).toHaveTextContent("Recebido");
    expect(within(pago).queryByRole("button")).not.toBeInTheDocument();
  });

  it("troca a situação pelas abas e busca de novo na API", async () => {
    renderView();
    await screen.findByText("Venda #1023 — parcela 2/3");

    api.getReceivables.mockResolvedValueOnce({ ...receivables, items: [receivables.items[1]] });
    selectTab(screen.getByRole("tab", { name: "Vencidos" }));
    await waitFor(() => expect(api.getReceivables).toHaveBeenLastCalledWith("overdue"));
    await waitFor(() => expect(screen.queryByText("Venda #1023 — parcela 2/3")).not.toBeInTheDocument());
    expect(screen.getByText("Serviço de instalação")).toBeInTheDocument();

    selectTab(screen.getByRole("tab", { name: "Recebidos" }));
    await waitFor(() => expect(api.getReceivables).toHaveBeenLastCalledWith("settled"));
    selectTab(screen.getByRole("tab", { name: "Todos" }));
    await waitFor(() => expect(api.getReceivables).toHaveBeenLastCalledWith("all"));
  });

  it("avisa quando a lista veio cortada", async () => {
    api.getReceivables.mockResolvedValue({ ...receivables, truncated: true });
    renderView();
    expect(await screen.findByText("Mostrando os primeiros 4 títulos.")).toBeInTheDocument();
  });

  it("mostra o vazio quando não há títulos", async () => {
    api.getReceivables.mockResolvedValue({
      ...receivables, items: [],
      aging: { notDue: 0, overdue1To30: 0, overdue31To60: 0, overdueOver60: 0, totalOpen: 0, openCount: 0, overdueCount: 0 },
    });
    renderView();
    expect(await screen.findByText("Nada por aqui")).toBeInTheDocument();
    expect(screen.getByText("Vendas a prazo e recebimentos lançados aparecem nesta lista.")).toBeInTheDocument();
    expect(metric("Vencido há mais de 60 dias")).toHaveTextContent("Total em aberto: R$ 0,00");
  });

  it("mostra o erro de carga e tenta de novo", async () => {
    api.getReceivables.mockRejectedValueOnce(new Error("falhou"));
    renderView();
    expect(await screen.findByText("Não foi possível carregar as contas a receber")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Venda #1023 — parcela 2/3")).toBeInTheDocument();
    expect(api.getReceivables).toHaveBeenCalledTimes(2);
  });
});

describe("TitlesView — baixa (receber)", () => {
  async function openSettle(description: string) {
    renderView();
    await screen.findByText(description);
    fireEvent.click(within(row(description)).getByRole("button", { name: "Receber" }));
    return screen.findByRole("dialog");
  }

  it("recebe parte do título com juros e multa e diz quanto fica em aberto", async () => {
    const dialog = await openSettle("Serviço de instalação");
    expect(within(dialog).getByText("Serviço de instalação — saldo R$ 1.200,00")).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Valor recebido")).toHaveValue("1200,00");
    expect(within(dialog).getByLabelText("Data")).toHaveValue("2026-11-13");
    expect(within(dialog).getByText("O título será quitado.")).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText("Valor recebido"), { target: { value: "500,00" } });
    fireEvent.change(within(dialog).getByLabelText("Juros"), { target: { value: "12,50" } });
    fireEvent.change(within(dialog).getByLabelText("Multa"), { target: { value: "24" } });
    expect(within(dialog).getByText("Fica em aberto: R$ 700,00 (mesmo vencimento).")).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(api.settleTransaction).toHaveBeenCalledTimes(1));
    expect(api.settleTransaction).toHaveBeenCalledWith("t2", {
      amount: 500, interest: 12.5, fine: 24, discount: 0, paidAt: noonIso(2026, 11, 13),
    });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Recebimento parcial registrado."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    // a lista é recarregada depois da baixa
    await waitFor(() => expect(api.getReceivables).toHaveBeenCalledTimes(2));
  });

  it("quita com desconto concedido e numa data anterior", async () => {
    const dialog = await openSettle("Serviço de instalação");

    fireEvent.change(within(dialog).getByLabelText("Valor recebido"), { target: { value: "1.150,00" } });
    fireEvent.change(within(dialog).getByLabelText("Desconto concedido"), { target: { value: "50" } });
    fireEvent.change(within(dialog).getByLabelText("Data"), { target: { value: "2026-11-10" } });
    expect(within(dialog).getByText("O título será quitado.")).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(api.settleTransaction).toHaveBeenCalledWith("t2", {
      amount: 1150, interest: 0, fine: 0, discount: 50, paidAt: noonIso(2026, 11, 10),
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Título recebido."));
  });

  it("não deixa confirmar valor acima do saldo, inválido, negativo ou zerado", async () => {
    const dialog = await openSettle("Serviço de instalação");
    const amount = within(dialog).getByLabelText("Valor recebido");
    const confirm = within(dialog).getByRole("button", { name: "Confirmar recebimento" });
    expect(confirm).toBeEnabled();

    fireEvent.change(amount, { target: { value: "1300" } });
    expect(within(dialog).getByText("O valor passa do saldo do título.")).toBeInTheDocument();
    expect(confirm).toBeDisabled();

    fireEvent.change(amount, { target: { value: "abc" } });
    expect(confirm).toBeDisabled();

    fireEvent.change(amount, { target: { value: "100" } });
    fireEvent.change(within(dialog).getByLabelText("Juros"), { target: { value: "-5" } });
    expect(confirm).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Juros"), { target: { value: "" } });
    expect(confirm).toBeEnabled();

    fireEvent.change(amount, { target: { value: "0" } });
    expect(confirm).toBeDisabled();

    fireEvent.change(amount, { target: { value: "100" } });
    fireEvent.change(within(dialog).getByLabelText("Data"), { target: { value: "" } });
    expect(confirm).toBeDisabled();

    fireEvent.click(confirm);
    expect(api.settleTransaction).not.toHaveBeenCalled();
  });

  it("mostra o erro da API e mantém o diálogo aberto", async () => {
    api.settleTransaction.mockRejectedValueOnce(new Error("Título já foi baixado por outro usuário."));
    const dialog = await openSettle("Venda #1023 — parcela 2/3");

    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Título já foi baixado por outro usuário."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it("usa a mensagem padrão quando o erro vem sem texto", async () => {
    api.settleTransaction.mockRejectedValueOnce(new Error(""));
    const dialog = await openSettle("Venda #1023 — parcela 2/3");
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível registrar a baixa."));
  });

  it("cancelar fecha sem registrar nada", async () => {
    const dialog = await openSettle("Venda #1023 — parcela 2/3");
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(api.settleTransaction).not.toHaveBeenCalled();
  });
});

describe("TitlesView — novo recebimento parcelado", () => {
  async function openNew() {
    renderView();
    await screen.findByText("Venda #1023 — parcela 2/3");
    fireEvent.click(screen.getByRole("button", { name: "Novo recebimento" }));
    return screen.findByRole("dialog");
  }

  it("lança em 3 parcelas para um cliente, a partir do 1º vencimento", async () => {
    const dialog = await openNew();
    const launch = within(dialog).getByRole("button", { name: "Lançar" });
    expect(launch).toBeDisabled();
    expect(within(dialog).getByLabelText("1º vencimento")).toHaveValue("2026-11-13");
    // contas a receber não têm categoria de despesa
    expect(within(dialog).queryByRole("combobox", { name: "Categoria" })).not.toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "  Consultoria de implantação  " } });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "3.000,00" } });
    fireEvent.change(within(dialog).getByLabelText("Parcelas"), { target: { value: "3" } });
    fireEvent.change(within(dialog).getByLabelText("1º vencimento"), { target: { value: "2026-12-10" } });
    await pickOption(within(dialog).getByRole("combobox", { name: "Cliente" }), "Construtora Alfa");
    expect(launch).toBeEnabled();

    fireEvent.click(launch);

    await waitFor(() => expect(api.createInstallments).toHaveBeenCalledWith({
      transactionType: "Receivable",
      totalAmount: 3000,
      description: "Consultoria de implantação",
      firstDueDate: noonIso(2026, 12, 10),
      installments: 3,
      customerId: "c2",
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("3 parcelas lançadas."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("lança à vista sem cliente", async () => {
    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "Aluguel de sala" } });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "800" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Lançar" }));

    await waitFor(() => expect(api.createInstallments).toHaveBeenCalledWith({
      transactionType: "Receivable", totalAmount: 800, description: "Aluguel de sala",
      firstDueDate: noonIso(2026, 11, 13), installments: 1, customerId: null,
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Recebimento lançado."));
  });

  it("valida descrição, número de parcelas e valor mínimo por parcela", async () => {
    const dialog = await openNew();
    const launch = within(dialog).getByRole("button", { name: "Lançar" });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "100" } });
    expect(launch).toBeDisabled(); // sem descrição

    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "   " } });
    expect(launch).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "Mensalidade" } });
    expect(launch).toBeEnabled();

    fireEvent.change(within(dialog).getByLabelText("Parcelas"), { target: { value: "61" } });
    expect(launch).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Parcelas"), { target: { value: "0" } });
    expect(launch).toBeDisabled();
    fireEvent.change(within(dialog).getByLabelText("Parcelas"), { target: { value: "3" } });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "0,02" } });
    expect(launch).toBeDisabled(); // menos de 1 centavo por parcela
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "0,03" } });
    expect(launch).toBeEnabled();
  });

  it("mostra o erro da API sem fechar e com mensagem padrão se vier vazia", async () => {
    api.createInstallments.mockRejectedValueOnce(new Error("Cliente inativo."));
    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText("Descrição"), { target: { value: "Consultoria" } });
    fireEvent.change(within(dialog).getByLabelText("Valor total"), { target: { value: "500" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Lançar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Cliente inativo."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    api.createInstallments.mockRejectedValueOnce(new Error(""));
    fireEvent.click(within(dialog).getByRole("button", { name: "Lançar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível lançar."));

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
