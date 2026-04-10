import { useState, useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ArrowLeft, AlertCircle, History, Search, PackageSearch } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { MovementTable } from "../components/MovementTable";
import { useStockItems } from "../hooks/use-stock";
import { useProductMovements } from "../hooks/use-stock";
import { MOVEMENT_TYPE_LABEL } from "../types";

export default function MovimentacoesPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const productId = searchParams.get("productId") ?? "";

  const [typeFilter, setTypeFilter] = useState("all");

  const { data: stockItems = [], isLoading: loadingStock } = useStockItems();
  const { data: movements = [], isLoading: loadingMovements, isError } = useProductMovements(
    productId || undefined
  );

  const selectedProduct = stockItems.find((s) => s.productId === productId);

  const [productSearch, setProductSearch] = useState("");
  const filteredProducts = useMemo(() => {
    if (!productSearch.trim()) return stockItems;
    const q = productSearch.toLowerCase();
    return stockItems.filter(
      (s) =>
        s.productName.toLowerCase().includes(q) ||
        s.productCode.toLowerCase().includes(q)
    );
  }, [stockItems, productSearch]);

  const filteredMovements = useMemo(() => {
    if (typeFilter === "all") return movements;
    return movements.filter((m) => m.movementType === typeFilter);
  }, [movements, typeFilter]);

  // Collect unique movement types present in the data
  const presentTypes = useMemo(() => {
    const types = new Set(movements.map((m) => m.movementType));
    return Array.from(types);
  }, [movements]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Movimentações de estoque"
        description="Consulte o histórico de entradas, saídas e ajustes por produto."
        actions={
          <Button variant="outline" onClick={() => navigate("/estoque")}>
            <ArrowLeft className="h-4 w-4 mr-1" /> Voltar
          </Button>
        }
      />

      <SectionCard title="Selecionar produto">
        <div className="space-y-3">
          <div className="relative max-w-md">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar por nome ou código..."
              value={productSearch}
              onChange={(e) => setProductSearch(e.target.value)}
              className="pl-9"
            />
          </div>
          {loadingStock ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-9 w-full" />)}
            </div>
          ) : (
            <div className="flex flex-wrap gap-2 max-h-40 overflow-y-auto">
              {filteredProducts.map((s) => (
                <button
                  key={s.productId}
                  type="button"
                  onClick={() => {
                    setSearchParams({ productId: s.productId });
                    setTypeFilter("all");
                  }}
                  className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm border transition-colors ${
                    productId === s.productId
                      ? "bg-primary text-primary-foreground border-primary"
                      : "bg-background border-border hover:bg-muted"
                  }`}
                >
                  <span className="font-mono text-xs opacity-70">{s.productCode}</span>
                  {s.productName}
                </button>
              ))}
              {filteredProducts.length === 0 && (
                <p className="text-sm text-muted-foreground py-2">
                  Nenhum produto encontrado.
                </p>
              )}
            </div>
          )}
        </div>
      </SectionCard>

      <SectionCard>
        {!productId ? (
          <EmptyState
            icon={PackageSearch}
            title="Selecione um produto"
            description="Escolha um produto acima para visualizar o histórico de movimentações."
          />
        ) : (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <p className="text-sm font-medium text-foreground">
                {selectedProduct
                  ? `${selectedProduct.productCode} — ${selectedProduct.productName}`
                  : "Produto selecionado"}
              </p>
              {presentTypes.length > 0 && (
                <Select value={typeFilter} onValueChange={setTypeFilter}>
                  <SelectTrigger className="w-[200px]">
                    <SelectValue placeholder="Filtrar por tipo" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Todos os tipos</SelectItem>
                    {presentTypes.map((t) => (
                      <SelectItem key={t} value={t}>
                        {MOVEMENT_TYPE_LABEL[t] ?? t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>

            {loadingMovements ? (
              <div className="space-y-3">
                {Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-12 w-full" />
                ))}
              </div>
            ) : isError ? (
              <EmptyState
                icon={AlertCircle}
                title="Erro ao carregar movimentações"
                description="Tente novamente mais tarde."
              />
            ) : filteredMovements.length > 0 ? (
              <>
                <MovementTable movements={filteredMovements} />
                <p className="text-xs text-muted-foreground pt-1">
                  {filteredMovements.length} movimentação(ões)
                </p>
              </>
            ) : (
              <EmptyState
                icon={History}
                title="Nenhuma movimentação encontrada"
                description={
                  typeFilter !== "all"
                    ? "Tente remover o filtro de tipo."
                    : "Este produto ainda não possui movimentações registradas."
                }
              />
            )}
          </div>
        )}
      </SectionCard>
    </div>
  );
}
