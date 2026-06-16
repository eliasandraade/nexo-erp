import { useState, useRef, useEffect, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Warehouse, Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { Skeleton } from "@/components/ui/skeleton";
import { DataPagination } from "@/components/shared/DataPagination";
import { useStockPaged } from "../hooks/use-stock";
import { InventoryKpiCards } from "../components/InventoryKpiCards";
import { InventoryFilters } from "../components/InventoryFilters";
import { InventoryTable } from "../components/InventoryTable";
import { deriveStockStatus } from "../types";
import type { StockItemEnriched } from "../types";

export default function EstoquePage() {
  const navigate = useNavigate();
  const [search, setSearch]           = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [status, setStatus]           = useState("all");
  const [page, setPage]               = useState(1);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search]);

  const { data, isLoading, isError, refetch } = useStockPaged({
    page,
    pageSize: 50,
    search:  debouncedSearch || undefined,
    status:  status !== "all" ? status : undefined,
  });

  // Add derived status field so InventoryTable gets StockItemEnriched shape
  const items = useMemo((): StockItemEnriched[] =>
    (data?.items ?? []).map(item => ({
      ...item,
      categoryName: item.categoryName ?? "",
      status: deriveStockStatus(item.availableQuantity, item.minStockQuantity),
    })),
    [data?.items]
  );

  const totalPages = data?.totalPages ?? 1;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Ingredientes"
        description="Insumos e ingredientes usados nos pratos. Controle de estoque e histórico de preços."
        actions={
          <Button onClick={() => navigate("/produtos/novo?tipo=ingrediente")}>
            <Plus className="h-4 w-4 mr-1" /> Novo ingrediente
          </Button>
        }
      />

      {!isLoading && (data?.totalCount ?? 0) > 0 && (
        <InventoryKpiCards
          totalProducts={data?.totalCount ?? 0}
          belowMin={data?.belowMinCount ?? 0}
          noTurnover={data?.noTurnoverCount ?? 0}
        />
      )}

      <SectionCard>
        <div className="space-y-4">
          <div className="flex items-center gap-2 text-xs text-muted-foreground bg-muted/40 rounded-md px-3 py-2">
            <Info className="h-3.5 w-3.5 shrink-0" />
            Exibindo apenas produtos com rastreio de estoque ativo.
          </div>

          <InventoryFilters
            search={search} onSearchChange={(v) => { setSearch(v); }}
            status={status} onStatusChange={(v) => { setStatus(v); setPage(1); }}
          />

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <ErrorState
              title="Não foi possível carregar o estoque"
              description="Não conseguimos buscar os dados agora. Tente novamente em instantes."
              onRetry={() => refetch()}
            />
          ) : items.length > 0 ? (
            <>
              <InventoryTable items={items} />
              <div className="flex items-center justify-between pt-1">
                <p className="text-xs text-muted-foreground">
                  {data?.totalCount ?? 0} item(ns) encontrado(s)
                </p>
                <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />
              </div>
            </>
          ) : (
            <EmptyState
              icon={Warehouse}
              title="Nenhum item encontrado"
              description={
                (data?.totalCount ?? 0) === 0
                  ? "Nenhum produto com rastreio de estoque ativo."
                  : "Tente ajustar os filtros."
              }
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
