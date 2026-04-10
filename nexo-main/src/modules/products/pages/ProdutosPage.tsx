import { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Package, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductFilters } from "../components/ProductFilters";
import { ProductTable } from "../components/ProductTable";
import { useCategories, useProducts } from "../hooks/use-products";

export default function ProdutosPage() {
  const navigate = useNavigate();
  const [search, setSearch]       = useState("");
  const [categoryId, setCategoryId] = useState("all");
  const [status, setStatus]       = useState("all");
  const [unit, setUnit]           = useState("all");

  const { data: products = [], isLoading: loadingProducts, isError } = useProducts();
  const { data: categories = [] } = useCategories();

  const filtered = useMemo(() => {
    return products.filter((p) => {
      const q = search.toLowerCase();
      const matchesSearch =
        !q ||
        p.code.toLowerCase().includes(q) ||
        (p.barcode ?? "").toLowerCase().includes(q) ||
        p.name.toLowerCase().includes(q);
      const matchesCategory = categoryId === "all" || p.categoryId === categoryId;
      const matchesStatus =
        status === "all" ||
        (status === "active" && p.isActive) ||
        (status === "inactive" && !p.isActive);
      const matchesUnit = unit === "all" || p.unit === unit;
      return matchesSearch && matchesCategory && matchesStatus && matchesUnit;
    });
  }, [products, search, categoryId, status, unit]);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Produtos"
        description="Gerencie o cadastro e as informações comerciais dos produtos."
        actions={
          <Button onClick={() => navigate("/produtos/novo")}>
            <Plus className="h-4 w-4 mr-1" /> Novo produto
          </Button>
        }
      />

      <SectionCard>
        <div className="space-y-4">
          <ProductFilters
            search={search}           onSearchChange={setSearch}
            categoryId={categoryId}   onCategoryChange={setCategoryId}
            status={status}           onStatusChange={setStatus}
            unit={unit}               onUnitChange={setUnit}
            categories={categories}
          />

          {loadingProducts ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar produtos"
              description="Não foi possível carregar a lista de produtos. Tente novamente mais tarde."
            />
          ) : filtered.length > 0 ? (
            <>
              <ProductTable products={filtered} categories={categories} />
              <div className="flex items-center justify-between pt-2 text-xs text-muted-foreground">
                <span>{filtered.length} produto(s) encontrado(s)</span>
              </div>
            </>
          ) : (
            <EmptyState
              icon={Package}
              title="Nenhum produto encontrado"
              description="Tente ajustar os filtros ou adicione um novo produto."
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
