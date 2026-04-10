import { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, AlertCircle, Warehouse, Info } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { useStockItems } from "../hooks/use-stock";
import { useProducts } from "@/modules/products/hooks/use-products";
import { useCategories } from "@/modules/products/hooks/use-products";
import { InventoryKpiCards } from "../components/InventoryKpiCards";
import { InventoryFilters } from "../components/InventoryFilters";
import { InventoryTable } from "../components/InventoryTable";
import { deriveStockStatus } from "../types";
import type { StockItemEnriched } from "../types";

const STALE_DAYS = 14;

export default function EstoquePage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");

  const { data: stockItems = [], isLoading: loadingStock, isError } = useStockItems();
  const { data: products = [] } = useProducts();
  const { data: categories = [] } = useCategories();

  // Build enriched list by joining stock items with product details
  const enriched = useMemo((): StockItemEnriched[] => {
    return stockItems.map((s) => {
      const product = products.find((p) => p.id === s.productId);
      const category = categories.find((c) => c.id === product?.categoryId);
      const minStock = product?.minStockQuantity ?? null;
      return {
        ...s,
        unit:             product?.unit ?? "",
        categoryName:     category?.name ?? "",
        minStockQuantity: minStock,
        status:           deriveStockStatus(s.availableQuantity, minStock),
      };
    });
  }, [stockItems, products, categories]);

  const filtered = useMemo(() => {
    return enriched.filter((i) => {
      const q = search.toLowerCase();
      const matchSearch =
        !q ||
        i.productCode.toLowerCase().includes(q) ||
        i.productName.toLowerCase().includes(q);
      const matchStatus = status === "all" || i.status === status;
      return matchSearch && matchStatus;
    });
  }, [enriched, search, status]);

  const belowMin = enriched.filter((i) => i.status === "low" || i.status === "zero").length;
  const noTurnover = useMemo(() => {
    const cutoff = Date.now() - STALE_DAYS * 24 * 60 * 60 * 1000;
    return enriched.filter(
      (i) => !i.lastMovementAt || new Date(i.lastMovementAt).getTime() < cutoff
    ).length;
  }, [enriched]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Estoque"
        description="Acompanhe saldos e movimentações de produtos com rastreio ativo."
        actions={
          <Button onClick={() => navigate("/estoque/ajustes")}>
            <Plus className="h-4 w-4 mr-1" /> Novo ajuste
          </Button>
        }
      />

      {!loadingStock && enriched.length > 0 && (
        <InventoryKpiCards
          totalProducts={enriched.length}
          belowMin={belowMin}
          noTurnover={noTurnover}
        />
      )}

      <SectionCard>
        <div className="space-y-4">
          <div className="flex items-center gap-2 text-xs text-muted-foreground bg-muted/40 rounded-md px-3 py-2">
            <Info className="h-3.5 w-3.5 shrink-0" />
            Exibindo apenas produtos com rastreio de estoque ativo.
          </div>

          <InventoryFilters
            search={search} onSearchChange={setSearch}
            status={status} onStatusChange={setStatus}
          />

          {loadingStock ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar estoque"
              description="Não foi possível carregar os dados de estoque. Tente novamente."
            />
          ) : filtered.length > 0 ? (
            <>
              <InventoryTable items={filtered} />
              <p className="text-xs text-muted-foreground pt-1">
                {filtered.length} item(ns) encontrado(s)
              </p>
            </>
          ) : (
            <EmptyState
              icon={Warehouse}
              title="Nenhum item encontrado"
              description={
                enriched.length === 0
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
