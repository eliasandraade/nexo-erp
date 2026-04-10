import { useState } from "react";
import { AlertCircle, Percent } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { commissionService } from "../services/commissionService";
import { CommissionKpiCards } from "../components/CommissionKpiCards";
import { CommissionFilters } from "../components/CommissionFilters";
import { CommissionSellerSummaryTable } from "../components/CommissionSellerSummaryTable";
import { CommissionRecordsTable } from "../components/CommissionRecordsTable";
import type { CommissionFilters as CommissionFiltersType } from "../types";

const defaultFilters: CommissionFiltersType = {
  operator: "all",
  status: "all",
};

export default function ComissoesPage() {
  const [filters, setFilters] = useState<CommissionFiltersType>(defaultFilters);

  const { data: overall, isLoading: loadingOverall } = useQuery({
    queryKey: ["commissions-overall"],
    queryFn: () => commissionService.getCommissionSummaryOverall(),
  });

  const { data: operators = [] } = useQuery({
    queryKey: ["commission-operators"],
    queryFn: () => commissionService.listOperators(),
  });

  const { data: sellerSummaries = [], isLoading: loadingSummaries } = useQuery({
    queryKey: ["commissions-by-seller", filters],
    queryFn: () => commissionService.getCommissionSummaryBySeller(filters),
  });

  const {
    data: records = [],
    isLoading: loadingRecords,
    isError,
  } = useQuery({
    queryKey: ["commission-records", filters],
    queryFn: () => commissionService.listCommissionRecords(filters),
  });

  const isLoading = loadingOverall || loadingRecords;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Comissões"
        description="Acompanhe a comissão gerada por vendas e seus estornos."
      />

      {/* KPI Cards */}
      {loadingOverall ? (
        <div className="grid sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-lg" />
          ))}
        </div>
      ) : overall ? (
        <CommissionKpiCards
          totalActive={overall.totalActive}
          totalReversed={overall.totalReversed}
          sellersWithCommission={overall.sellersWithCommission}
          impactedSalesCount={overall.impactedSalesCount}
        />
      ) : null}

      {/* Seller Summary */}
      <SectionCard
        title="Resumo por vendedor"
        description="Comissão consolidada por operador no período."
      >
        {loadingSummaries ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        ) : sellerSummaries.length > 0 ? (
          <CommissionSellerSummaryTable summaries={sellerSummaries} />
        ) : (
          <EmptyState
            icon={Percent}
            title="Nenhum dado de comissão"
            description="As comissões serão calculadas automaticamente com base nas vendas."
          />
        )}
      </SectionCard>

      {/* Detailed Records */}
      <SectionCard title="Registros detalhados">
        <div className="space-y-4">
          <CommissionFilters
            filters={filters}
            operators={operators}
            onChange={setFilters}
          />

          {isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar comissões"
              description="Não foi possível carregar os registros de comissão. Tente novamente."
            />
          ) : isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 6 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : records.length > 0 ? (
            <>
              <CommissionRecordsTable records={records} />
              <div className="flex items-center justify-between pt-2 text-xs text-muted-foreground">
                <span>{records.length} registro(s) encontrado(s)</span>
                <span>
                  {records.filter((r) => r.status === "active").length} ativo(s) ·{" "}
                  {records.filter((r) => r.status === "reversed").length} estornado(s)
                </span>
              </div>
            </>
          ) : (
            <EmptyState
              icon={Percent}
              title="Nenhum registro encontrado"
              description={
                filters.operator !== "all" || filters.status !== "all"
                  ? "Nenhum registro corresponde aos filtros selecionados."
                  : "As comissões serão calculadas automaticamente com base nas vendas."
              }
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
