import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ReportFilterBar } from "../components/ReportFilterBar";
import { ReportKpiCards } from "../components/ReportKpiCards";
import { SalesByOperatorTable } from "../components/SalesByOperatorTable";
import { TopProductsTable } from "../components/TopProductsTable";
import { reportService } from "../services/reportService";
import { formatCurrency } from "@/lib/formatters";
import type { ReportFilters } from "../types";

const defaultFilters: ReportFilters = {
  operator: "all",
  status: "all",
  paymentMethod: "all",
};

export default function RelatoriosPage() {
  const [filters, setFilters] = useState<ReportFilters>(defaultFilters);

  const { data: operators = [] } = useQuery({
    queryKey: ["report-operators"],
    queryFn: () => reportService.listOperators(),
  });

  const { data: operational, isLoading: loadingOp } = useQuery({
    queryKey: ["report-operational", filters],
    queryFn: () => reportService.getOperationalSummary(filters),
  });

  const { data: byOperator = [], isLoading: loadingByOp } = useQuery({
    queryKey: ["report-by-operator", filters],
    queryFn: () => reportService.getSalesByOperator(filters),
  });

  const { data: topProducts = [], isLoading: loadingTop } = useQuery({
    queryKey: ["report-top-products", filters],
    queryFn: () => reportService.getTopProducts(10, filters),
  });

  const { data: cancellations } = useQuery({
    queryKey: ["report-cancellations", filters],
    queryFn: () => reportService.getCancellationSummary(filters),
  });

  const { data: commission } = useQuery({
    queryKey: ["report-commission"],
    queryFn: () => reportService.getCommissionSummary(),
  });

  const { data: cash } = useQuery({
    queryKey: ["report-cash"],
    queryFn: () => reportService.getCashSummary(),
  });

  const { data: inventory } = useQuery({
    queryKey: ["report-inventory"],
    queryFn: () => reportService.getInventorySummary(),
  });

  return (
    <div className="space-y-6">
      <PageHeader title="Relatórios" />

      <ReportFilterBar
        filters={filters}
        operators={operators}
        onChange={setFilters}
      />

      {loadingOp ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6 gap-4">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-lg" />
          ))}
        </div>
      ) : (
        <ReportKpiCards
          totalSales={operational?.totalSales ?? 0}
          totalRevenue={operational?.totalRevenue ?? 0}
          averageTicket={operational?.averageTicket ?? 0}
          activeCommission={commission?.totalActive ?? 0}
          cancelledCount={operational?.cancelledCount ?? 0}
          stockAlerts={inventory?.totalAlertCount ?? 0}
        />
      )}

      <SectionCard title="Vendas por Operador">
        {loadingByOp ? (
          <Skeleton className="h-32 w-full" />
        ) : byOperator.length === 0 ? (
          <EmptyState
            title="Nenhuma venda encontrada"
            description="Ajuste os filtros para ver os dados por operador."
          />
        ) : (
          <SalesByOperatorTable rows={byOperator} />
        )}
      </SectionCard>

      <SectionCard title="Produtos mais vendidos">
        {loadingTop ? (
          <Skeleton className="h-32 w-full" />
        ) : topProducts.length === 0 ? (
          <EmptyState
            title="Nenhum produto encontrado"
            description="Ajuste os filtros para ver os produtos mais vendidos."
          />
        ) : (
          <TopProductsTable rows={topProducts} />
        )}
      </SectionCard>

      <div className="grid md:grid-cols-2 gap-6">
        <SectionCard title="Cancelamentos">
          {cancellations ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Vendas canceladas</dt>
                <dd className="font-semibold tabular-nums">
                  {cancellations.cancelledSalesCount}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Parc. canceladas</dt>
                <dd className="font-semibold tabular-nums">
                  {cancellations.partiallyCancelledCount}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Itens cancelados</dt>
                <dd className="font-semibold tabular-nums">
                  {cancellations.cancelledItemsCount}
                </dd>
              </div>
              <div className="flex justify-between border-t pt-2 mt-2">
                <dt className="text-muted-foreground">Valor estornado</dt>
                <dd className="font-semibold tabular-nums text-destructive">
                  {formatCurrency(cancellations.totalReversedValue)}
                </dd>
              </div>
            </dl>
          ) : (
            <Skeleton className="h-24 w-full" />
          )}
        </SectionCard>

        <SectionCard title="Caixa">
          {cash ? (
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Status</dt>
                <dd className="font-semibold">
                  {cash.currentSession.isOpen ? "Aberto" : "Fechado"}
                </dd>
              </div>
              {cash.currentSession.isOpen && cash.currentSession.operator && (
                <div className="flex justify-between">
                  <dt className="text-muted-foreground">Operador</dt>
                  <dd className="font-semibold">{cash.currentSession.operator}</dd>
                </div>
              )}
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Vendas em dinheiro</dt>
                <dd className="font-semibold tabular-nums">
                  {formatCurrency(cash.totalSalesThisSession)}
                </dd>
              </div>
              <div className="flex justify-between">
                <dt className="text-muted-foreground">Saldo esperado</dt>
                <dd className="font-semibold tabular-nums">
                  {formatCurrency(cash.currentSession.expectedBalance)}
                </dd>
              </div>
              {cash.closedSessionsWithDivergence > 0 && (
                <div className="flex justify-between border-t pt-2 mt-2">
                  <dt className="text-muted-foreground">Sessões com divergência</dt>
                  <dd className="font-semibold tabular-nums text-destructive">
                    {cash.closedSessionsWithDivergence}
                  </dd>
                </div>
              )}
            </dl>
          ) : (
            <Skeleton className="h-24 w-full" />
          )}
        </SectionCard>
      </div>
    </div>
  );
}
