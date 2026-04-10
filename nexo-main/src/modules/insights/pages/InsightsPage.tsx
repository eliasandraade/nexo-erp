import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/shared/PageHeader";
import { EmptyState } from "@/components/shared/EmptyState";
import { InsightSummaryCards } from "../components/InsightSummaryCards";
import { InsightCard } from "../components/InsightCard";
import { InsightFilters } from "../components/InsightFilters";
import { insightService } from "../services/insightService";
import type { InsightFilters as InsightFiltersType } from "../types";

const defaultFilters: InsightFiltersType = {
  severity: "all",
  category: "all",
};

export default function InsightsPage() {
  const [filters, setFilters] = useState<InsightFiltersType>(defaultFilters);

  const { data: stats } = useQuery({
    queryKey: ["insights-stats"],
    queryFn: () => insightService.getSummaryStats(),
  });

  const { data: insights = [], isLoading } = useQuery({
    queryKey: ["insights", filters],
    queryFn: () => insightService.generateInsights(filters),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Insights"
        description="Análise automática baseada no estado atual do sistema."
      />

      <InsightSummaryCards
        total={stats?.total ?? 0}
        critical={stats?.critical ?? 0}
        warning={stats?.warning ?? 0}
        info={stats?.info ?? 0}
      />

      <InsightFilters filters={filters} onChange={setFilters} />

      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-20 w-full rounded-lg" />
          ))}
        </div>
      ) : insights.length === 0 ? (
        <EmptyState
          title="Nenhum insight encontrado"
          description="Nenhum dado relevante foi identificado com os filtros aplicados."
        />
      ) : (
        <div className="space-y-3">
          {insights.map((insight) => (
            <InsightCard key={insight.id} insight={insight} />
          ))}
        </div>
      )}
    </div>
  );
}
