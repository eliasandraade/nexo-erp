import { useState, useEffect, useRef } from "react";
import { AlertCircle, Receipt } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { DataPagination } from "@/components/shared/DataPagination";
import { SalesFilters, type SalesServerFilters } from "../components/SalesFilters";
import { SalesTable } from "../components/SalesTable";
import { useSalesList } from "../hooks/useSalesList";

const defaultFilters: SalesServerFilters = {
  search:        "",
  paymentMethod: "all",
  status:        "all",
};

const PAGE_SIZE = 25;

export default function VendasPage() {
  const [filters, setFilters]   = useState<SalesServerFilters>(defaultFilters);
  const [page, setPage]         = useState(1);
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedSearch(filters.search);
      setPage(1);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [filters.search]);

  const handleFiltersChange = (next: SalesServerFilters) => {
    setFilters(next);
    if (next.paymentMethod !== filters.paymentMethod || next.status !== filters.status) {
      setPage(1);
    }
  };

  const { data, isLoading, isError } = useSalesList({
    page,
    pageSize: PAGE_SIZE,
    search:        debouncedSearch || undefined,
    status:        filters.status        !== "all" ? filters.status        : undefined,
    paymentMethod: filters.paymentMethod !== "all" ? filters.paymentMethod : undefined,
  });

  const sales      = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Vendas"
        description="Consulte as vendas registradas no sistema."
      />

      <SectionCard>
        <div className="space-y-4">
          <SalesFilters filters={filters} onChange={handleFiltersChange} />

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
              <div className="flex items-center justify-between pt-2">
                <p className="text-xs text-muted-foreground">
                  {totalCount} venda(s) encontrada(s)
                </p>
                <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />
              </div>
            </>
          ) : (
            <EmptyState
              icon={Receipt}
              title="Nenhuma venda encontrada"
              description={
                debouncedSearch || filters.paymentMethod !== "all" || filters.status !== "all"
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
