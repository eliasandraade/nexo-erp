export type InsightSeverity = "info" | "warning" | "critical";

export type InsightCategory =
  | "inventory"
  | "cash"
  | "sales"
  | "commissions"
  | "operations";

export interface ManagerialInsight {
  id: string;
  category: InsightCategory;
  severity: InsightSeverity;
  title: string;
  description: string;
  value?: string;
  generatedAt: string;
}

export interface InsightFilters {
  severity: InsightSeverity | "all";
  category: InsightCategory | "all";
}

export const insightCategoryLabels: Record<InsightCategory, string> = {
  inventory: "Estoque",
  cash: "Caixa",
  sales: "Vendas",
  commissions: "Comissões",
  operations: "Operações",
};

export const insightSeverityLabels: Record<InsightSeverity, string> = {
  info: "Info",
  warning: "Atenção",
  critical: "Crítico",
};
