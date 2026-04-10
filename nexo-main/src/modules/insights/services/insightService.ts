import { reportService } from "@/modules/reports/services/reportService";
import { commissionService } from "@/modules/commissions/services/commissionService";
import type { ManagerialInsight, InsightFilters, InsightCategory, InsightSeverity } from "../types";

let insightSeq = 1;

function makeInsight(
  category: InsightCategory,
  severity: InsightSeverity,
  title: string,
  description: string,
  value?: string
): ManagerialInsight {
  return {
    id: `insight-${String(insightSeq++).padStart(4, "0")}`,
    category,
    severity,
    title,
    description,
    value,
    generatedAt: new Date().toISOString(),
  };
}

export const insightService = {
  async generateInsights(filters?: InsightFilters): Promise<ManagerialInsight[]> {
    insightSeq = 1;

    const [inventorySummary, operational, cash, commission, commissionBySeller] =
      await Promise.all([
        reportService.getInventorySummary(),
        reportService.getOperationalSummary(),
        reportService.getCashSummary(),
        reportService.getCommissionSummary(),
        commissionService.getCommissionSummaryBySeller(),
      ]);

    const insights: ManagerialInsight[] = [];

    // A) Zero stock — critical
    if (inventorySummary.zeroStockCount > 0) {
      insights.push(
        makeInsight(
          "inventory",
          "critical",
          "Produtos sem estoque",
          "Existem produtos com estoque zerado que podem impedir vendas.",
          `${inventorySummary.zeroStockCount} produto${inventorySummary.zeroStockCount > 1 ? "s" : ""}`
        )
      );
    }

    // B) Low stock — warning
    if (inventorySummary.lowStockCount > 0) {
      insights.push(
        makeInsight(
          "inventory",
          "warning",
          "Estoque baixo",
          "Alguns produtos estão com estoque abaixo do nível mínimo.",
          `${inventorySummary.lowStockCount} produto${inventorySummary.lowStockCount > 1 ? "s" : ""}`
        )
      );
    }

    // H) High stock — info
    if (inventorySummary.highStockCount > 0) {
      insights.push(
        makeInsight(
          "inventory",
          "info",
          "Estoque elevado",
          "Alguns produtos estão com estoque acima do nível esperado.",
          `${inventorySummary.highStockCount} produto${inventorySummary.highStockCount > 1 ? "s" : ""}`
        )
      );
    }

    // G) No open cash session — warning
    if (!cash.currentSession.isOpen) {
      insights.push(
        makeInsight(
          "cash",
          "warning",
          "Caixa não aberto",
          "Não há sessão de caixa ativa. O PDV não poderá finalizar vendas."
        )
      );
    }

    // C) Cash divergence — warning
    if (cash.closedSessionsWithDivergence > 0) {
      insights.push(
        makeInsight(
          "cash",
          "warning",
          "Divergências em sessões anteriores",
          "Sessões de caixa fechadas apresentaram diferença entre saldo esperado e contado.",
          `${cash.closedSessionsWithDivergence} sessão${cash.closedSessionsWithDivergence > 1 ? "ões" : ""}`
        )
      );
    }

    // D) High cancellation rate — warning
    if (operational.totalSales >= 5) {
      const rate = operational.cancelledCount / operational.totalSales;
      if (rate > 0.1) {
        insights.push(
          makeInsight(
            "sales",
            "warning",
            "Taxa de cancelamento elevada",
            "A proporção de vendas canceladas está acima de 10% do total.",
            `${Math.round(rate * 100)}% de cancelamentos`
          )
        );
      }
    }

    // E) Commission reversals — warning
    if (commission.totalReversed > 0) {
      insights.push(
        makeInsight(
          "commissions",
          "warning",
          "Comissões estornadas",
          "Existem comissões revertidas devido a cancelamentos de vendas.",
          commission.totalReversed.toLocaleString("pt-BR", {
            style: "currency",
            currency: "BRL",
          })
        )
      );
    }

    // F) Top seller — info
    if (commissionBySeller.length > 0) {
      const top = commissionBySeller[0];
      insights.push(
        makeInsight(
          "commissions",
          "info",
          "Melhor vendedor",
          `${top.operator} lidera em comissões ativas no período.`,
          top.activeCommission.toLocaleString("pt-BR", {
            style: "currency",
            currency: "BRL",
          })
        )
      );
    }

    // Apply filters
    let result = insights;
    if (filters) {
      if (filters.severity !== "all") {
        result = result.filter((i) => i.severity === filters.severity);
      }
      if (filters.category !== "all") {
        result = result.filter((i) => i.category === filters.category);
      }
    }

    return result;
  },

  async getSummaryStats(): Promise<{
    total: number;
    critical: number;
    warning: number;
    info: number;
  }> {
    const all = await insightService.generateInsights();
    return {
      total: all.length,
      critical: all.filter((i) => i.severity === "critical").length,
      warning: all.filter((i) => i.severity === "warning").length,
      info: all.filter((i) => i.severity === "info").length,
    };
  },
};
