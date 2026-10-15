import { apiClient } from "@/services/api-client";

export type LedgerRegime = "accrual" | "cash";

export type FinancialCategory =
  | "Revenue" | "RevenueDeduction" | "Purchase" | "OperatingExpense" | "Payroll"
  | "Commission" | "Tax" | "FinancialExpense" | "FinancialIncome" | "OtherIncome" | "OtherExpense";

export interface LedgerEntryDto {
  id: string;
  direction: "In" | "Out";
  category: FinancialCategory;
  amount: number;
  description: string;
  competenceDate: string;
  dueDate: string;
  paidAt: string | null;
  status: "Pending" | "Overdue" | "Paid" | "Cancelled";
  storeId: string | null;
  referenceType: string | null;
  referenceId: string | null;
  reversesTransactionId: string | null;
}

export interface LedgerCategoryTotalDto {
  category: FinancialCategory;
  in: number;
  out: number;
  net: number;
  count: number;
}

export interface LedgerDto {
  regime: "Accrual" | "Cash";
  from: string;
  to: string;
  totalIn: number;
  totalOut: number;
  net: number;
  /** Competência: net without compras de mercadoria (stock, not expense); caixa: = net. */
  result: number;
  byCategory: LedgerCategoryTotalDto[];
  entries: LedgerEntryDto[];
  truncated: boolean;
}

export const CATEGORY_LABELS: Record<FinancialCategory, string> = {
  Revenue: "Receita de vendas e serviços",
  RevenueDeduction: "Devoluções e estornos",
  Purchase: "Compras de mercadoria",
  OperatingExpense: "Despesas operacionais",
  Payroll: "Folha",
  Commission: "Comissões",
  Tax: "Impostos e taxas",
  FinancialExpense: "Despesas financeiras",
  FinancialIncome: "Receitas financeiras",
  OtherIncome: "Outras entradas",
  OtherExpense: "Outras saídas",
};

export const LEDGER_KEY = ["financial-ledger"] as const;

export function getLedger(regime: LedgerRegime, from: Date, to: Date, category?: FinancialCategory) {
  const params = new URLSearchParams({ regime, from: from.toISOString(), to: to.toISOString() });
  if (category) params.set("category", category);
  return apiClient.get<LedgerDto>(`/financial/ledger?${params.toString()}`);
}

// ── Títulos (contas a receber / a pagar) ─────────────────────────────────────

export type TitleStatusFilter = "open" | "overdue" | "settled" | "all";

export interface TitleDto {
  id: string;
  description: string;
  customerId: string | null;
  customerName: string | null;
  amount: number;
  dueDate: string;
  competenceDate: string;
  paidAt: string | null;
  status: "Pending" | "Overdue" | "Paid" | "Cancelled";
  daysOverdue: number;
  category: FinancialCategory;
  storeId: string | null;
  referenceType: string | null;
  referenceId: string | null;
  parentTransactionId: string | null;
}

export interface AgingDto {
  notDue: number;
  overdue1To30: number;
  overdue31To60: number;
  overdueOver60: number;
  totalOpen: number;
  openCount: number;
  overdueCount: number;
}

export interface TitlesDto {
  items: TitleDto[];
  aging: AgingDto;
  truncated: boolean;
}

export interface SettleRequest {
  amount: number;
  paidAt?: string;
  interest?: number;
  fine?: number;
  discount?: number;
}

export interface CreateInstallmentsRequest {
  transactionType: "Receivable" | "Payable";
  totalAmount: number;
  description: string;
  firstDueDate: string;
  installments: number;
  category?: FinancialCategory;
  customerId?: string | null;
}

export const RECEIVABLES_KEY = ["financial-receivables"] as const;

export function getReceivables(status: TitleStatusFilter, customerId?: string) {
  const params = new URLSearchParams({ status });
  if (customerId) params.set("customerId", customerId);
  return apiClient.get<TitlesDto>(`/financial/receivables?${params.toString()}`);
}

export const settleTransaction = (id: string, body: SettleRequest) =>
  apiClient.post<unknown>(`/financial/transactions/${id}/settle`, body);

export const createInstallments = (body: CreateInstallmentsRequest) =>
  apiClient.post<unknown>("/financial/transactions/installments", body);
