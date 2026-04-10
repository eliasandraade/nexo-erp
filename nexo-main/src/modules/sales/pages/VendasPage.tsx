import { useState } from "react";
import { AlertCircle, Receipt } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { salesHistoryService } from "../services/salesHistoryService";
import { SalesFilters } from "../components/SalesFilters";
import { SalesTable } from "../components/SalesTable";
import type { SaleListFilters } from "../types";

const defaultFilters: SaleListFilters = {
  search: "",
  paymentMethod: "all",
  status: "all",
};

export default function VendasPage() {
  const [filters, setFilters] = useState<SaleListFilters>(defaultFilters);

  // Filtering is handled by the service layer, consistent with future API integration.
  // Query key includes filters so TanStack Query caches per filter combination and
  // refetches when the filter state changes.
  const { data: sales = [], isLoading, isError } = useQuery({
    queryKey: ["sales", filters],
    queryFn: () => salesHistoryService.listSales(filters),
  });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Vendas"
        description="Consulte as vendas registradas no sistema."
      />

      <SectionCard>
        <div className="space-y-4">
          <SalesFilters filters={filters} onChange={setFilters} />

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 6 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar vendas"
              description="Não foi possível carregar o histórico de vendas. Tente novamente."
            />
          ) : sales.length > 0 ? (
            <>
              <SalesTable sales={sales} />
              <div className="pt-2 text-xs text-muted-foreground">
                {sales.length} venda(s) encontrada(s)
              </div>
            </>
          ) : (
            <EmptyState
              icon={Receipt}
              title="Nenhuma venda encontrada"
              description={
                filters.search || filters.paymentMethod !== "all" || filters.status !== "all"
                  ? "Nenhuma venda corresponde aos filtros selecionados."
                  : "As vendas registradas no PDV aparecerão aqui."
              }
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
