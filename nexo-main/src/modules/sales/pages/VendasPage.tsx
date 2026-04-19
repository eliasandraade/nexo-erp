import { useState, useMemo } from "react";
import { AlertCircle, Receipt } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { listSales } from "../api/sales.api";
import { saleToLegacy } from "../utils/saleAdapter";
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

  const { data: allSales = [], isLoading, isError } = useQuery({
    queryKey: ["sales"],
    queryFn: () => listSales().then((dtos) => dtos.map(saleToLegacy)),
    staleTime: 30_000,
  });

  const sales = useMemo(() => {
    return allSales.filter((sale) => {
      if (filters.search && filters.search.trim()) {
        const q = filters.search.trim().toLowerCase();
        const matchesId = sale.id.toLowerCase().includes(q);
        const matchesOperator = sale.operator.toLowerCase().includes(q);
        const matchesItem = sale.items.some(
          (i) =>
            i.description.toLowerCase().includes(q) ||
            i.code.toLowerCase().includes(q)
        );
        const matchesCustomer = sale.customerName
          ? sale.customerName.toLowerCase().includes(q)
          : false;
        if (!matchesId && !matchesOperator && !matchesItem && !matchesCustomer) return false;
      }

      if (filters.paymentMethod && filters.paymentMethod !== "all") {
        if (!sale.payments.some((p) => p.method === filters.paymentMethod)) return false;
      }

      if (filters.status && filters.status !== "all") {
        if (sale.status !== filters.status) return false;
      }

      return true;
    });
  }, [allSales, filters]);

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
