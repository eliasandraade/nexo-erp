import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { CashFlowBucketDto, CashFlowDto, FinancialAccountDto } from "../api/financial.api";
import { installRadixPolyfills, pickOption, selectTab } from "@/test/helpers/radixInteractions";
import FluxoCaixaPage from "./FluxoCaixaPage";

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock("sonner", () => ({ toast }));

const api = vi.hoisted(() => ({
  getCashFlow: vi.fn(),
  listAccounts: vi.fn(),
  setOpeningBalance: vi.fn(),
}));
vi.mock("../api/financial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/financial.api")>()),
  ...api,
}));

// O gráfico (recharts) não mede nada no jsdom: basta saber que recebeu os períodos.
vi.mock("../components/CashFlowChart", () => ({
  default: ({ buckets }: { buckets: CashFlowBucketDto[] }) => <div data-testid="grafico">{buckets.length} períodos no gráfico</div>,
}));

const bucket = (over: Partial<CashFlowBucketDto>): CashFlowBucketDto => ({
  start: "2026-11-13", end: "2026-11-13", realizedIn: 0, realizedOut: 0, projectedIn: 0, projectedOut: 0, balance: 0, ...over,
});

const flow: CashFlowDto = {
  from: "2026-11-13", to: "2026-12-13", granularity: "Day",
  openingBalance: 10000, openingBalanceConfigured: true,
  overdueIn: 300, overdueOut: 0, closingBalance: 11250.5,
  buckets: [
    bucket({ start: "2026-11-13", end: "2026-11-13", realizedIn: 1500, realizedOut: 200, balance: 11300 }),
    bucket({ start: "2026-11-14", end: "2026-11-14", projectedIn: 450.5, projectedOut: 500, balance: 11250.5 }),
  ],
};

const account = (over: Partial<FinancialAccountDto>): FinancialAccountDto => ({
  id: "a", code: "1", name: "", accountType: "Cash", isActive: true, openingBalance: 0, openingBalanceDate: null, ...over,
});
const accounts: FinancialAccountDto[] = [
  account({ id: "a1", code: "1.01", name: "Caixa da loja", accountType: "Cash" }),
  account({ id: "a2", code: "1.02", name: "Banco do Brasil", accountType: "Bank", openingBalance: 5230.75, openingBalanceDate: "2026-10-31" }),
  account({ id: "a3", code: "1.10", name: "Clientes a receber", accountType: "Receivable" }),
  account({ id: "a4", code: "1.03", name: "Conta antiga", accountType: "Bank", isActive: false }),
];

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><FluxoCaixaPage /></QueryClientProvider>);
}

const metric = (label: string) => screen.getByText(label, { selector: "p" }).parentElement!.parentElement!;
const cells = (period: string) =>
  Array.from(screen.getByText(period, { selector: "td" }).closest("tr")!.querySelectorAll("td")).map((td) => td.textContent);

function today(y: number, m: number, d: number) {
  vi.setSystemTime(new Date(y, m - 1, d, 10, 0));
}

beforeEach(() => {
  installRadixPolyfills();
  vi.useFakeTimers({ toFake: ["Date"] });
  today(2026, 11, 13);
  api.getCashFlow.mockResolvedValue(flow);
  api.listAccounts.mockResolvedValue(accounts);
  api.setOpeningBalance.mockResolvedValue({});
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

describe("FluxoCaixaPage — saldo e totais", () => {
  it("pede os próximos 30 dias por dia e soma entradas e saídas realizadas e previstas", async () => {
    renderPage();
    expect(await screen.findByText("Por período")).toBeInTheDocument();
    expect(api.getCashFlow).toHaveBeenCalledWith("2026-11-13", "2026-12-13", "day");

    expect(metric("Saldo no início")).toHaveTextContent("R$ 10.000,00");
    // 1.500 recebido + 450,50 a receber
    expect(metric("Entradas")).toHaveTextContent("R$ 1.950,50");
    // 200 pago + 500 a pagar
    expect(metric("Saídas")).toHaveTextContent("R$ 700,00");
    expect(metric("Saldo no fim")).toHaveTextContent("R$ 11.250,50");
    expect(screen.queryByText(/Sem saldo inicial/)).not.toBeInTheDocument();
    expect(screen.getByText("Vencidos e não pagos (fora do saldo): a receber R$ 300,00 · a pagar R$ 0,00", { exact: false }))
      .toBeInTheDocument();

    expect(cells("13/11")).toEqual(["13/11", "R$ 1.500,00", "R$ 200,00", "—", "—", "R$ 11.300,00"].map((s) => s.replace(" ", " ")));
    expect(cells("14/11")).toEqual(["14/11", "—", "—", "R$ 450,50", "R$ 500,00", "R$ 11.250,50"].map((s) => s.replace(" ", " ")));
    expect(await screen.findByTestId("grafico")).toHaveTextContent("2 períodos no gráfico");
  });

  it("destaca saldo negativo e avisa quando não há saldo inicial", async () => {
    api.getCashFlow.mockResolvedValue({
      ...flow, openingBalance: 0, openingBalanceConfigured: false, overdueIn: 0, overdueOut: 0, closingBalance: -320,
      buckets: [bucket({ start: "2026-11-16", end: "2026-11-22", realizedOut: 320, balance: -320 })],
    });
    renderPage();
    expect(await screen.findByText(/Sem saldo inicial, o saldo parte de zero/)).toBeInTheDocument();
    expect(metric("Saldo no fim")).toHaveTextContent("-R$ 320,00");
    expect(metric("Saldo no fim").querySelector(".text-destructive")).not.toBeNull();
    expect(screen.queryByText(/Vencidos e não pagos/)).not.toBeInTheDocument();

    // período de uma semana aparece como intervalo; saldo negativo em vermelho
    const row = screen.getByText("16/11 a 22/11").closest("tr")!;
    expect(row.lastElementChild).toHaveClass("text-destructive");
    expect(row.lastElementChild).toHaveTextContent("-R$ 320,00");
  });

  it("só mostra os vencidos a pagar quando não há a receber", async () => {
    api.getCashFlow.mockResolvedValue({ ...flow, overdueIn: 0, overdueOut: 75.3 });
    renderPage();
    expect(await screen.findByText(/a receber R\$\s0,00 · a pagar R\$\s75,30/)).toBeInTheDocument();
  });

  it("troca o período: mês atual por dia e próximos 90 dias por semana", async () => {
    renderPage();
    await screen.findByText("Por período");

    selectTab(screen.getByRole("tab", { name: "Mês atual" }));
    await waitFor(() => expect(api.getCashFlow).toHaveBeenLastCalledWith("2026-11-01", "2026-11-30", "day"));

    selectTab(screen.getByRole("tab", { name: "Próximos 90 dias" }));
    await waitFor(() => expect(api.getCashFlow).toHaveBeenLastCalledWith("2026-11-13", "2027-02-11", "week"));
  });

  it("o mês atual termina no último dia certo, inclusive em fevereiro bissexto", async () => {
    today(2028, 2, 10);
    renderPage();
    await screen.findByText("Por período");
    expect(api.getCashFlow).toHaveBeenLastCalledWith("2028-02-10", "2028-03-11", "day");
    selectTab(screen.getByRole("tab", { name: "Mês atual" }));
    await waitFor(() => expect(api.getCashFlow).toHaveBeenLastCalledWith("2028-02-01", "2028-02-29", "day"));
  });

  it("mostra o erro do cálculo e tenta de novo", async () => {
    api.getCashFlow.mockRejectedValueOnce(new Error("timeout"));
    renderPage();
    expect(await screen.findByText("Não foi possível calcular o fluxo de caixa")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Tentar novamente/ }));
    expect(await screen.findByText("Por período")).toBeInTheDocument();
    expect(api.getCashFlow).toHaveBeenCalledTimes(2);
  });
});

describe("FluxoCaixaPage — saldo inicial", () => {
  async function openDialog() {
    renderPage();
    await screen.findByText("Por período");
    fireEvent.click(screen.getByRole("button", { name: "Saldo inicial" }));
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect(within(dialog).getByRole("combobox", { name: "Conta" })).toHaveTextContent("Caixa da loja"));
    return dialog;
  }

  it("só oferece caixa e banco ativos e usa a data de corte já existente", async () => {
    const dialog = await openDialog();
    expect(within(dialog).getByLabelText("No fim do dia")).toHaveValue("2026-10-31");
    expect(within(dialog).getByLabelText("No fim do dia")).toHaveAttribute("max", "2026-11-13");

    fireEvent.click(within(dialog).getByRole("combobox", { name: "Conta" }));
    const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
    expect(options).toEqual(["Caixa da loja", "Banco do Brasil — atual R$ 5.230,75"]);
  });

  it("salva o saldo da conta escolhida na data de corte e recalcula o fluxo", async () => {
    const dialog = await openDialog();
    const save = within(dialog).getByRole("button", { name: "Salvar" });
    expect(save).toBeDisabled();

    fireEvent.change(within(dialog).getByLabelText("Valor"), { target: { value: "2.500,00" } });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() => expect(api.setOpeningBalance).toHaveBeenCalledWith("a1", 2500, "2026-10-31"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Saldo inicial salvo."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await waitFor(() => expect(api.getCashFlow).toHaveBeenCalledTimes(2));
  });

  it("ao trocar de conta traz o saldo já informado e aceita outra data", async () => {
    const dialog = await openDialog();
    await pickOption(within(dialog).getByRole("combobox", { name: "Conta" }), /Banco do Brasil/);
    expect(within(dialog).getByLabelText("Valor")).toHaveValue("5230,75");

    fireEvent.change(within(dialog).getByLabelText("No fim do dia"), { target: { value: "2026-11-12" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));

    await waitFor(() => expect(api.setOpeningBalance).toHaveBeenCalledWith("a2", 5230.75, "2026-11-12"));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("voltar para uma conta sem saldo limpa o valor e bloqueia o salvar", async () => {
    const dialog = await openDialog();
    await pickOption(within(dialog).getByRole("combobox", { name: "Conta" }), /Banco do Brasil/);
    await pickOption(within(dialog).getByRole("combobox", { name: "Conta" }), "Caixa da loja");
    expect(within(dialog).getByLabelText("Valor")).toHaveValue("");
    expect(within(dialog).getByRole("button", { name: "Salvar" })).toBeDisabled();

    fireEvent.change(within(dialog).getByLabelText("Valor"), { target: { value: "abc" } });
    expect(within(dialog).getByRole("button", { name: "Salvar" })).toBeDisabled();
  });

  it("sem data de corte em nenhuma conta, propõe o fim de ontem", async () => {
    api.listAccounts.mockResolvedValue([account({ id: "a1", name: "Caixa da loja" })]);
    const dialog = await openDialog();
    expect(within(dialog).getByLabelText("No fim do dia")).toHaveValue("2026-11-12");
  });

  it("sem conta de caixa ou banco não deixa salvar", async () => {
    api.listAccounts.mockResolvedValue([accounts[2], accounts[3]]);
    renderPage();
    await screen.findByText("Por período");
    fireEvent.click(screen.getByRole("button", { name: "Saldo inicial" }));
    const dialog = await screen.findByRole("dialog");
    await waitFor(() => expect(api.listAccounts).toHaveBeenCalled());
    expect(within(dialog).getByRole("combobox", { name: "Conta" })).toHaveTextContent("Sem conta de caixa ou banco");
    fireEvent.change(within(dialog).getByLabelText("Valor"), { target: { value: "100" } });
    expect(within(dialog).getByRole("button", { name: "Salvar" })).toBeDisabled();
  });

  it("mostra o erro ao salvar e mantém o diálogo; cancelar fecha", async () => {
    api.setOpeningBalance.mockRejectedValueOnce(new Error("Data posterior ao primeiro lançamento."));
    const dialog = await openDialog();
    fireEvent.change(within(dialog).getByLabelText("Valor"), { target: { value: "100" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Data posterior ao primeiro lançamento."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    api.setOpeningBalance.mockRejectedValueOnce(new Error(""));
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível salvar."));

    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
});
