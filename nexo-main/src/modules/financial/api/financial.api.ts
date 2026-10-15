import { apiClient } from "@/services/api-client";

export type LedgerRegime = "accrual" | "cash";

export type FinancialCategory =
  | "Revenue" | "RevenueDeduction" | "Purchase" | "OperatingExpense" | "Payroll"
  | "Commission" | "Tax" | "FinancialExpense" | "OtherIncome" | "OtherExpense";

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
  OtherIncome: "Outras entradas",
  OtherExpense: "Outras saídas",
};

export const LEDGER_KEY = ["financial-ledger"] as const;

export function getLedger(regime: LedgerRegime, from: Date, to: Date, category?: FinancialCategory) {
  const params = new URLSearchParams({ regime, from: from.toISOString(), to: to.toISOString() });
  if (category) params.set("category", category);
  return apiClient.get<LedgerDto>(`/financial/ledger?${params.toString()}`);
}
